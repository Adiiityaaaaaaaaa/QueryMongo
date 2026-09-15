using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Json;

namespace QueryMongo.Core.Services;

/// <summary>
/// The write half of <see cref="QueryService"/>: bulk updates and deletes, plus the
/// aggregation-side operations that change data.
/// </summary>
public sealed partial class QueryService
{
    /// <summary>Stages that write to a collection rather than just returning documents.</summary>
    private static readonly string[] WritingStages = ["$out", "$merge"];

    /// <summary>
    /// True when running this pipeline would modify the deployment. The UI confirms
    /// before running one, because $out replaces a whole collection.
    /// </summary>
    public static bool PipelineWrites(IReadOnlyList<BsonDocument> pipeline) =>
        pipeline.Any(stage => stage.Names.Any(n => WritingStages.Contains(n)));

    /// <summary>Names the collection a writing pipeline targets, for the confirmation prompt.</summary>
    public static string? PipelineTarget(IReadOnlyList<BsonDocument> pipeline)
    {
        foreach (var stage in pipeline)
        {
            if (stage.TryGetValue("$out", out var outValue))
            {
                return outValue switch
                {
                    BsonString s => s.Value,
                    BsonDocument d => d.GetValue("coll", BsonString.Empty).AsString,
                    _ => null
                };
            }

            if (stage.TryGetValue("$merge", out var mergeValue))
            {
                return mergeValue switch
                {
                    BsonString s => s.Value,
                    BsonDocument d => d.GetValue("into", BsonString.Empty) is BsonString into
                        ? into.Value
                        : d.GetValue("into", new BsonDocument()).AsBsonDocument
                           .GetValue("coll", BsonString.Empty).AsString,
                    _ => null
                };
            }
        }

        return null;
    }

    /// <summary>Applies an update document to every match, as Compass's bulk update does.</summary>
    public async Task<long> UpdateManyAsync(
        string database, string collection, string filter, string update, CancellationToken ct = default)
    {
        var parsedFilter = ParseRequired(filter, "Filter");
        var parsedUpdate = ParseRequired(update, "Update");

        if (parsedUpdate.ElementCount == 0)
            throw new QueryInputException("Update", "The update document is empty.");

        // A bare replacement document here would silently wipe fields, so require an
        // update operator, the same guard the server applies to updateMany.
        if (!parsedUpdate.Names.Any(n => n.StartsWith('$')))
            throw new QueryInputException("Update",
                "An update must use operators such as $set or $unset.");

        var result = await Collection(database, collection)
            .UpdateManyAsync(parsedFilter, parsedUpdate, cancellationToken: ct)
            .ConfigureAwait(false);

        return result.ModifiedCount;
    }

    /// <summary>Deletes every document matching the filter.</summary>
    public async Task<long> DeleteManyAsync(
        string database, string collection, string filter, CancellationToken ct = default)
    {
        var parsed = ParseRequired(filter, "Filter");

        var result = await Collection(database, collection)
            .DeleteManyAsync(parsed, ct)
            .ConfigureAwait(false);

        return result.DeletedCount;
    }

    /// <summary>Explains an aggregation, so the pipeline builder can show its cost.</summary>
    public async Task<BsonDocument> ExplainAggregateAsync(
        string database,
        string collection,
        IReadOnlyList<BsonDocument> pipeline,
        CancellationToken ct = default)
    {
        var command = new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "aggregate", collection },
                    { "pipeline", new BsonArray(pipeline) },
                    { "cursor", new BsonDocument() }
                }
            },
            { "verbosity", "executionStats" }
        };

        return await _session.Client.GetDatabase(database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a pipeline for its side effect ($out or $merge), returning nothing. The
    /// cursor must still be drained for the server to execute the write.
    /// </summary>
    public async Task RunWritingPipelineAsync(
        string database,
        string collection,
        IReadOnlyList<BsonDocument> pipeline,
        CancellationToken ct = default)
    {
        using var cursor = await Collection(database, collection)
            .AggregateAsync<BsonDocument>(
                pipeline.ToList(), new AggregateOptions { AllowDiskUse = true }, ct)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
        {
            // $out and $merge return no documents; draining the cursor commits the write.
        }
    }

    /// <summary>Runs a pipeline up to and including <paramref name="stageCount"/> stages.</summary>
    public Task<IReadOnlyList<BsonDocument>> PreviewStagesAsync(
        string database,
        string collection,
        IReadOnlyList<BsonDocument> pipeline,
        int stageCount,
        int previewLimit = 10,
        CancellationToken ct = default)
    {
        var prefix = pipeline.Take(stageCount).ToList();

        // Previewing a writing stage would actually write, so it is dropped from the
        // preview and only runs when the user explicitly executes the pipeline.
        if (prefix.Count > 0 && PipelineWrites([prefix[^1]])) prefix.RemoveAt(prefix.Count - 1);

        return prefix.Count == 0
            ? Task.FromResult<IReadOnlyList<BsonDocument>>([])
            : AggregateAsync(database, collection, prefix, previewLimit, ct);
    }

    private static BsonDocument ParseRequired(string? text, string field)
    {
        var parsed = BsonJson.ParseDocument(text);
        if (!parsed.IsValid) throw new QueryInputException(field, parsed.Error!);
        return parsed.Value ?? new BsonDocument();
    }
}
