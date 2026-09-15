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

    /// <summary>
    /// The same listing with the <c>dbStats</c> counters the database table shows.
    ///
    /// This is one command per database, so it is kept off the plain listing the sidebar
    /// uses and only paid for when a database list is actually on screen. A database that
    /// refuses the command keeps its counters null rather than failing the listing.
    /// </summary>
    public async Task<IReadOnlyList<DatabaseInfo>> ListDatabasesWithStatsAsync(
        CancellationToken ct = default)
    {
        var databases = await ListDatabasesAsync(ct).ConfigureAwait(false);
        var results = new List<DatabaseInfo>(databases.Count);

        foreach (var database in databases)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stats = await _session.Client.GetDatabase(database.Name)
                    .RunCommandAsync<BsonDocument>(
                        new BsonDocument { { "dbStats", 1 } }, cancellationToken: ct)
                    .ConfigureAwait(false);

                results.Add(database.WithStats(stats));
            }
            catch (MongoException)
            {
                results.Add(database);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(
        string database, bool withCounts = true, CancellationToken ct = default)
    {
        var db = _session.Client.GetDatabase(database);

        // nameOnly is not used: the type field distinguishes views and time series,
        // and the payload is small enough that one round trip is cheaper than two.
        using var cursor = await db.ListCollectionsAsync(cancellationToken: ct).ConfigureAwait(false);
        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        var counts = withCounts
            ? await GetCollectionCountsAsync(database, ct).ConfigureAwait(false)
            : new Dictionary<string, CollectionStats>();

        return docs.Select(doc =>
            {
                var name = doc.GetValue("name", BsonString.Empty).AsString;
                var options = doc.GetValue("options", new BsonDocument()).AsBsonDocument;
                var kind = ReadKind(doc);

                var info = new CollectionInfo(database, name, kind)
                {
                    Properties = ReadProperties(kind, options),
                    ViewOn = options.Contains("viewOn") ? options["viewOn"].AsString : null
                };

                return counts.TryGetValue(name, out var stats)
                    ? info with
                    {
                        DocumentCount = stats.DocumentCount,
                        StorageSizeBytes = stats.StorageSizeBytes,
                        DataSizeBytes = stats.DataSizeBytes,
                        FreeStorageSizeBytes = stats.FreeStorageSizeBytes,
                        TotalIndexSizeBytes = stats.TotalIndexSizeBytes,
                        AverageDocumentSizeBytes = stats.AverageDocumentSizeBytes,
                        IndexCount = stats.IndexCount
                    }
                    : info;
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Counters for every collection in a database, used to annotate the sidebar.
    ///
    /// $collStats reads metadata rather than scanning, so this is cheap per collection;
    /// a collection that refuses it (a view, or one the user cannot read) is skipped
    /// rather than failing the whole listing.
    /// </summary>
    private async Task<Dictionary<string, CollectionStats>> GetCollectionCountsAsync(
        string database, CancellationToken ct)
    {
        var db = _session.Client.GetDatabase(database);

        List<string> names;
        try
        {
            using var cursor = await db.ListCollectionNamesAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            names = await cursor.ToListAsync(ct).ConfigureAwait(false);
        }
        catch (MongoException)
        {
            return [];
        }

        var results = new Dictionary<string, CollectionStats>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                results[name] = await GetStatsAsync(database, name, ct).ConfigureAwait(false);
            }
            catch (MongoException)
            {
                // Views and restricted collections have no storage stats.
            }
        }

        return results;
    }

    /// <summary>
    /// The badges the collection list shows, read off the collection's creation options
    /// the way Compass reads them.
    /// </summary>
    private static List<string> ReadProperties(CollectionKind kind, BsonDocument options)
    {
        var properties = new List<string>();

        if (kind == CollectionKind.View) properties.Add("view");
        if (kind == CollectionKind.TimeSeries) properties.Add("timeseries");

        if (options.GetValue("capped", BsonBoolean.False).ToBoolean()) properties.Add("capped");
        if (options.Contains("clusteredIndex")) properties.Add("clustered");
        if (options.Contains("collation")) properties.Add("collation");
        if (options.Contains("encryptedFields")) properties.Add("fle2");

        return properties;
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
                AverageDocumentSizeBytes: s.GetValue("avgObjSize", BsonDouble.Create(0.0)).ToDouble())
            {
                DataSizeBytes = s.GetValue("size", BsonInt64.Create(0L)).ToInt64(),
                FreeStorageSizeBytes = s.Contains("freeStorageSize")
                    ? s["freeStorageSize"].ToInt64()
                    : null
            };
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
