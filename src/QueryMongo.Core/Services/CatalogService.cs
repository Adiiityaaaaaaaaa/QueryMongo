using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>Reads the deployment's catalog: databases, collections, indexes and stats.</summary>
public sealed class CatalogService(MongoSession session)
{
    private readonly MongoSession _session = session;

    public async Task<IReadOnlyList<DatabaseInfo>> ListDatabasesAsync(CancellationToken ct = default)
    {
        using var cursor = await _session.Client.ListDatabasesAsync(cancellationToken: ct).ConfigureAwait(false);
        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return docs.Select(DatabaseInfo.FromBson)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(
        string database, CancellationToken ct = default)
    {
        var db = _session.Client.GetDatabase(database);

        // nameOnly is not used: the type field distinguishes views and time series,
        // and the payload is small enough that one round trip is cheaper than two.
        using var cursor = await db.ListCollectionsAsync(cancellationToken: ct).ConfigureAwait(false);
        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return docs.Select(doc => new CollectionInfo(
                database,
                doc.GetValue("name", BsonString.Empty).AsString,
                ReadKind(doc)))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CollectionKind ReadKind(BsonDocument doc)
    {
        if (doc.Contains("options") && doc["options"].AsBsonDocument.Contains("timeseries"))
            return CollectionKind.TimeSeries;

        return doc.GetValue("type", BsonString.Empty).AsString switch
        {
            "view" => CollectionKind.View,
            "timeseries" => CollectionKind.TimeSeries,
            _ => CollectionKind.Collection
        };
    }

    /// <summary>
    /// Collection counters via <c>$collStats</c>. Views have no storage stats, so the
    /// caller gets zeroes rather than an error.
    /// </summary>
    public async Task<CollectionStats> GetStatsAsync(
        string database, string collection, CancellationToken ct = default)
    {
        var db = _session.Client.GetDatabase(database);

        var pipeline = new[]
        {
            new BsonDocument("$collStats", new BsonDocument
            {
                { "storageStats", new BsonDocument() }
            })
        };

        try
        {
            using var cursor = await db.GetCollection<BsonDocument>(collection)
                .AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct)
                .ConfigureAwait(false);

            var result = await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (result is null || !result.Contains("storageStats")) return Empty;

            var s = result["storageStats"].AsBsonDocument;
            return new CollectionStats(
                DocumentCount: s.GetValue("count", BsonInt64.Create(0L)).ToInt64(),
                StorageSizeBytes: s.GetValue("storageSize", BsonInt64.Create(0L)).ToInt64(),
                TotalIndexSizeBytes: s.GetValue("totalIndexSize", BsonInt64.Create(0L)).ToInt64(),
                IndexCount: s.GetValue("nindexes", BsonInt32.Create(0)).ToInt32(),
                AverageDocumentSizeBytes: s.GetValue("avgObjSize", BsonDouble.Create(0.0)).ToDouble());
        }
        catch (MongoCommandException)
        {
            // $collStats is unsupported on views and on some hosted tiers.
            return Empty;
        }
    }

    private static CollectionStats Empty => new(0, 0, 0, 0, 0);

    public async Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(
        string database, string collection, CancellationToken ct = default)
    {
        var coll = _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

        using var cursor = await coll.Indexes.ListAsync(ct).ConfigureAwait(false);
        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        var sizes = await GetIndexSizesAsync(database, collection, ct).ConfigureAwait(false);

        return docs.Select(doc =>
        {
            var name = doc.GetValue("name", BsonString.Empty).AsString;
            return new IndexInfo(
                Name: name,
                Key: doc.GetValue("key", new BsonDocument()).AsBsonDocument,
                IsUnique: doc.GetValue("unique", BsonBoolean.False).ToBoolean(),
                IsSparse: doc.GetValue("sparse", BsonBoolean.False).ToBoolean(),
                IsTtl: doc.Contains("expireAfterSeconds"),
                SizeBytes: sizes.TryGetValue(name, out var size) ? size : null);
        }).ToList();
    }

    private async Task<Dictionary<string, long>> GetIndexSizesAsync(
        string database, string collection, CancellationToken ct)
    {
        try
        {
            var stats = await _session.Client.GetDatabase(database)
                .RunCommandAsync<BsonDocument>(
                    new BsonDocument { { "collStats", collection } }, cancellationToken: ct)
                .ConfigureAwait(false);

            if (!stats.Contains("indexSizes")) return [];

            return stats["indexSizes"].AsBsonDocument.Elements
                .ToDictionary(e => e.Name, e => e.Value.ToInt64());
        }
        catch (MongoCommandException)
        {
            return [];
        }
    }
}
