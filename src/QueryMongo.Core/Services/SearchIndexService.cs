using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;
using QueryMongo.Core.Json;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>An Atlas Search or Vector Search index.</summary>
public sealed record SearchIndexInfo(
    string Name,
    string Type,
    string Status,
    bool Queryable,
    BsonDocument Definition)
{
    public string DefinitionJson => BsonJson.ToPrettyJson(Definition);

    /// <summary>Atlas builds indexes asynchronously, so a new index is not immediately usable.</summary>
    public string StatusDescription => Queryable ? $"{Status} (queryable)" : Status;
}

/// <summary>
/// Atlas Search index management.
///
/// These commands only exist on Atlas: a self-hosted or local deployment rejects them.
/// <see cref="IsSupportedAsync"/> lets the UI say so plainly instead of surfacing a
/// raw command error.
/// </summary>
public sealed class SearchIndexService(MongoSession session)
{
    private readonly MongoSession _session = session;

    private IMongoCollection<BsonDocument> Collection(string database, string collection) =>
        _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

    /// <summary>True when the deployment supports search indexes at all.</summary>
    public async Task<bool> IsSupportedAsync(
        string database, string collection, CancellationToken ct = default)
    {
        try
        {
            await ListAsync(database, collection, ct).ConfigureAwait(false);
            return true;
        }
        catch (MongoCommandException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SearchIndexInfo>> ListAsync(
        string database, string collection, CancellationToken ct = default)
    {
        var pipeline = new[] { new BsonDocument("$listSearchIndexes", new BsonDocument()) };

        using var cursor = await Collection(database, collection)
            .AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct)
            .ConfigureAwait(false);

        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return docs.Select(d => new SearchIndexInfo(
            d.GetValue("name", BsonString.Empty).AsString,
            d.GetValue("type", BsonString.Create("search")).AsString,
            d.GetValue("status", BsonString.Empty).AsString,
            d.GetValue("queryable", BsonBoolean.False).ToBoolean(),
            d.GetValue("latestDefinition", new BsonDocument()).AsBsonDocument)).ToList();
    }

    public async Task<string> CreateAsync(
        string database,
        string collection,
        string name,
        BsonDocument definition,
        string type = "search",
        CancellationToken ct = default)
    {
        var model = new CreateSearchIndexModel(name, MapType(type), definition);

        return await Collection(database, collection).SearchIndexes
            .CreateOneAsync(model, ct)
            .ConfigureAwait(false);
    }

    public Task UpdateAsync(
        string database, string collection, string name, BsonDocument definition,
        CancellationToken ct = default) =>
        Collection(database, collection).SearchIndexes.UpdateAsync(name, definition, ct);

    public Task DropAsync(
        string database, string collection, string name, CancellationToken ct = default) =>
        Collection(database, collection).SearchIndexes.DropOneAsync(name, ct);

    private static SearchIndexType MapType(string type) =>
        type.Equals("vectorSearch", StringComparison.OrdinalIgnoreCase)
            ? SearchIndexType.VectorSearch
            : SearchIndexType.Search;

    /// <summary>A default definition that indexes every field, as Atlas's wizard offers.</summary>
    public const string DynamicMappingTemplate = """
        {
          "mappings": {
            "dynamic": true
          }
        }
        """;

    /// <summary>A starting point for a vector index; dimensions must match the embedding model.</summary>
    public const string VectorTemplate = """
        {
          "fields": [
            {
              "type": "vector",
              "path": "embedding",
              "numDimensions": 1536,
              "similarity": "cosine"
            }
          ]
        }
        """;
}
