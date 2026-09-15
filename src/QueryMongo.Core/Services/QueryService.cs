using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>Raised when the query bar holds text the server would reject.</summary>
public sealed class QueryInputException(string field, string error)
    : Exception($"{field}: {error}")
{
    public string Field { get; } = field;
}

/// <summary>Runs finds, counts, explains and aggregations for one collection.</summary>
public sealed partial class QueryService(MongoSession session)
{
    private readonly MongoSession _session = session;

    private IMongoCollection<BsonDocument> Collection(string database, string collection) =>
        _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

    /// <summary>
    /// Runs one page of a find.
    ///
    /// Fetches one document beyond the page size so the caller learns whether a next
    /// page exists without paying for a separate countDocuments, which on a large
    /// collection costs a full scan.
    /// </summary>
    public async Task<DocumentPage> FindAsync(
        string database, string collection, QuerySpec spec, CancellationToken ct = default)
    {
        var options = new FindOptions<BsonDocument>
        {
            Projection = Parse(spec.Projection, "Projection"),
            Sort = Parse(spec.Sort, "Sort"),
            Skip = spec.Skip,
            Limit = spec.Limit + 1,
            BatchSize = Math.Min(spec.Limit + 1, 101),
            Collation = ParseCollation(spec.Collation),
            Hint = ParseHint(spec.Hint),
            MaxTime = spec.MaxTimeMs > 0 ? TimeSpan.FromMilliseconds(spec.MaxTimeMs) : null
        };

        var filter = Parse(spec.Filter, "Filter") ?? new BsonDocument();

        using var cursor = await Collection(database, collection)
            .FindAsync(filter, options, ct)
            .ConfigureAwait(false);

        var documents = await cursor.ToListAsync(ct).ConfigureAwait(false);

        var hasMore = documents.Count > spec.Limit;
        if (hasMore) documents.RemoveAt(documents.Count - 1);

        return new DocumentPage(documents, spec.Skip, hasMore);
    }

    /// <summary>
    /// Counts documents matching the filter. Uses the metadata count for an empty
    /// filter, which is O(1), and a real count only when a filter is present.
    /// </summary>
    public async Task<long> CountAsync(
        string database, string collection, string filter, CancellationToken ct = default)
    {
        var parsed = Parse(filter, "Filter") ?? new BsonDocument();
        var coll = Collection(database, collection);

        return parsed.ElementCount == 0
            ? await coll.EstimatedDocumentCountAsync(cancellationToken: ct).ConfigureAwait(false)
            : await coll.CountDocumentsAsync(parsed, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Explains the find, so the UI can warn about collection scans.</summary>
    public async Task<ExplainSummary> ExplainAsync(
        string database, string collection, QuerySpec spec, CancellationToken ct = default)
    {
        var find = new BsonDocument
        {
            { "find", collection },
            { "filter", Parse(spec.Filter, "Filter") ?? new BsonDocument() },
            { "limit", spec.Limit }
        };

        if (Parse(spec.Sort, "Sort") is { ElementCount: > 0 } sort)
            find["sort"] = sort;
        if (Parse(spec.Projection, "Projection") is { ElementCount: > 0 } projection)
            find["projection"] = projection;
        if (spec.Skip > 0)
            find["skip"] = spec.Skip;

        var command = new BsonDocument
        {
            { "explain", find },
            { "verbosity", "executionStats" }
        };

        var sw = Stopwatch.StartNew();
        var result = await _session.Client.GetDatabase(database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: ct)
            .ConfigureAwait(false);
        sw.Stop();

        return Summarize(result, sw.Elapsed);
    }

    private static ExplainSummary Summarize(BsonDocument explain, TimeSpan wallClock)
    {
        var stats = explain.GetValue("executionStats", new BsonDocument()).AsBsonDocument;

        var winning = explain.GetValue("queryPlanner", new BsonDocument()).AsBsonDocument
            .GetValue("winningPlan", new BsonDocument()).AsBsonDocument;

        // The winning plan is a tree; the leaf names the access method that matters.
        var leaf = winning;
        while (leaf.Contains("inputStage")) leaf = leaf["inputStage"].AsBsonDocument;

        var serverMillis = stats.GetValue("executionTimeMillis", BsonInt32.Create(0)).ToInt32();

        return new ExplainSummary(
            Stage: leaf.GetValue("stage", BsonString.Empty).AsString,
            DocumentsExamined: stats.GetValue("totalDocsExamined", BsonInt64.Create(0L)).ToInt64(),
            KeysExamined: stats.GetValue("totalKeysExamined", BsonInt64.Create(0L)).ToInt64(),
            DocumentsReturned: stats.GetValue("nReturned", BsonInt64.Create(0L)).ToInt64(),
            ExecutionTime: serverMillis > 0 ? TimeSpan.FromMilliseconds(serverMillis) : wallClock,
            IndexName: leaf.TryGetValue("indexName", out var name) ? name.AsString : null,
            Raw: explain);
    }

    /// <summary>
    /// Runs an aggregation pipeline. <paramref name="previewLimit"/> caps what is read
    /// back so a pipeline with no limit stage cannot pull an entire collection into
    /// the UI.
    /// </summary>
    public async Task<IReadOnlyList<BsonDocument>> AggregateAsync(
        string database,
        string collection,
        IReadOnlyList<BsonDocument> pipeline,
        int previewLimit = 50,
        CancellationToken ct = default)
    {
        var options = new AggregateOptions
        {
            BatchSize = Math.Min(previewLimit, 101),
            AllowDiskUse = true
        };

        using var cursor = await Collection(database, collection)
            .AggregateAsync<BsonDocument>(pipeline.ToList(), options, ct)
            .ConfigureAwait(false);

        var results = new List<BsonDocument>(Math.Min(previewLimit, 256));
        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                results.Add(doc);
                if (results.Count >= previewLimit) return results;
            }
        }

        return results;
    }

    public Task<ReplaceOneResult> ReplaceAsync(
        string database, string collection, BsonValue id, BsonDocument replacement,
        CancellationToken ct = default) =>
        Collection(database, collection).ReplaceOneAsync(
            new BsonDocument("_id", id), replacement, cancellationToken: ct);

    public Task InsertAsync(
        string database, string collection, BsonDocument document, CancellationToken ct = default) =>
        Collection(database, collection).InsertOneAsync(document, cancellationToken: ct);

    public Task<DeleteResult> DeleteAsync(
        string database, string collection, BsonValue id, CancellationToken ct = default) =>
        Collection(database, collection).DeleteOneAsync(new BsonDocument("_id", id), ct);

    private static BsonDocument? Parse(string? text, string field)
    {
        var result = BsonJson.ParseDocument(text);
        if (!result.IsValid) throw new QueryInputException(field, result.Error!);
        return result.Value is { ElementCount: 0 } ? null : result.Value;
    }

    /// <summary>
    /// A hint is either an index key document or an index name. A bare name is accepted
    /// with or without quotes, since both are natural to type.
    /// </summary>
    private static BsonValue? ParseHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();

        if (trimmed.StartsWith('{')) return Parse(trimmed, "Index Hint");

        return new BsonString(trimmed.Trim('"', '\''));
    }

    private static Collation? ParseCollation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parsed = BsonJson.ParseDocument(text);
        if (!parsed.IsValid) throw new QueryInputException("Collation", parsed.Error!);
        if (parsed.Value is not { ElementCount: > 0 } doc) return null;

        var locale = doc.GetValue("locale", BsonString.Create("simple")).AsString;
        return new Collation(locale);
    }
}
