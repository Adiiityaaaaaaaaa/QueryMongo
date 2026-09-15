using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>Everything the index creation dialog can set.</summary>
public sealed record IndexDefinition
{
    /// <summary>Key spec, e.g. <c>{ "year": 1, "rating": -1 }</c>.</summary>
    public required BsonDocument Keys { get; init; }

    public string? Name { get; init; }
    public bool Unique { get; init; }
    public bool Sparse { get; init; }
    public bool Background { get; init; }

    /// <summary>TTL in seconds; only valid on a single date field.</summary>
    public int? ExpireAfterSeconds { get; init; }

    /// <summary>Partial filter expression, so the index covers only matching documents.</summary>
    public BsonDocument? PartialFilter { get; init; }

    public string? CollationLocale { get; init; }
}

/// <summary>Creating, dropping and measuring indexes.</summary>
public sealed class IndexService(MongoSession session)
{
    private readonly MongoSession _session = session;

    private IMongoCollection<BsonDocument> Collection(string database, string collection) =>
        _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

    public async Task<string> CreateAsync(
        string database, string collection, IndexDefinition definition, CancellationToken ct = default)
    {
        if (definition.Keys.ElementCount == 0)
            throw new ArgumentException("An index needs at least one key.", nameof(definition));

        var options = new CreateIndexOptions<BsonDocument>
        {
            Name = string.IsNullOrWhiteSpace(definition.Name) ? null : definition.Name.Trim(),
            Unique = definition.Unique ? true : null,
            Sparse = definition.Sparse ? true : null,
            Background = definition.Background ? true : null,
            ExpireAfter = definition.ExpireAfterSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : null,
            Collation = string.IsNullOrWhiteSpace(definition.CollationLocale)
                ? null
                : new Collation(definition.CollationLocale)
        };

        if (definition.PartialFilter is { ElementCount: > 0 } partial)
            options.PartialFilterExpression = partial;

        var model = new CreateIndexModel<BsonDocument>(definition.Keys, options);

        return await Collection(database, collection).Indexes
            .CreateOneAsync(model, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public Task DropAsync(string database, string collection, string indexName, CancellationToken ct = default)
    {
        if (indexName == "_id_")
            throw new InvalidOperationException("The _id index cannot be dropped.");

        return Collection(database, collection).Indexes.DropOneAsync(indexName, ct);
    }

    /// <summary>
    /// Per-index access counts from <c>$indexStats</c>. Compass shows these so you can
    /// spot indexes nothing queries, which cost writes and storage for nothing.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, long>> GetUsageAsync(
        string database, string collection, CancellationToken ct = default)
    {
        try
        {
            var pipeline = new[] { new BsonDocument("$indexStats", new BsonDocument()) };

            using var cursor = await Collection(database, collection)
                .AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct)
                .ConfigureAwait(false);

            var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

            return docs.ToDictionary(
                d => d.GetValue("name", BsonString.Empty).AsString,
                d => d.GetValue("accesses", new BsonDocument()).AsBsonDocument
                      .GetValue("ops", BsonInt64.Create(0L)).ToInt64());
        }
        catch (MongoCommandException)
        {
            // $indexStats is unavailable on some hosted tiers and on views.
            return new Dictionary<string, long>();
        }
    }

    /// <summary>Indexes with their usage counts merged in, ready for the Indexes tab.</summary>
    public async Task<IReadOnlyList<IndexDetail>> ListDetailedAsync(
        string database, string collection, CancellationToken ct = default)
    {
        var catalog = new CatalogService(_session);

        var indexes = await catalog.ListIndexesAsync(database, collection, ct).ConfigureAwait(false);
        var usage = await GetUsageAsync(database, collection, ct).ConfigureAwait(false);

        return indexes
            .Select(i => new IndexDetail(i, usage.TryGetValue(i.Name, out var ops) ? ops : null))
            .ToList();
    }
}

/// <summary>An index plus how often the server has used it.</summary>
public sealed record IndexDetail(IndexInfo Index, long? Operations)
{
    public string Name => Index.Name;
    public string KeyDescription => Index.KeyDescription;
    public string SizeDescription => Index.SizeBytes is { } b ? ByteSize.Format(b) : "—";
    public string UsageDescription => Operations is { } ops ? $"{ops:N0} ops" : "not tracked";

    /// <summary>The _id index always exists and cannot be removed.</summary>
    public bool CanDrop => Index.Name != "_id_";

    public string Properties
    {
        get
        {
            var parts = new List<string>();
            if (Index.IsUnique) parts.Add("unique");
            if (Index.IsSparse) parts.Add("sparse");
            if (Index.IsTtl) parts.Add("TTL");
            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }
}
