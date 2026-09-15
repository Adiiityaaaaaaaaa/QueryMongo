using MongoDB.Bson;

namespace QueryMongo.Core.Models;

/// <summary>A database as listed by <c>listDatabases</c>.</summary>
public sealed record DatabaseInfo(string Name, long SizeOnDisk, bool Empty)
{
    public static DatabaseInfo FromBson(BsonDocument doc) => new(
        doc.GetValue("name", BsonString.Empty).AsString,
        doc.GetValue("sizeOnDisk", BsonInt64.Create(0L)).ToInt64(),
        doc.GetValue("empty", BsonBoolean.False).ToBoolean());

    /// <summary>
    /// Counters from <c>dbStats</c>, which the database list shows in its columns.
    /// Null means "not measured", which is different from zero.
    /// </summary>
    public long? StorageSizeBytes { get; init; }

    public long? DataSizeBytes { get; init; }

    public int? CollectionCount { get; init; }

    public int? IndexCount { get; init; }

    /// <summary>Copies the <c>dbStats</c> counters onto the listing entry.</summary>
    public DatabaseInfo WithStats(BsonDocument stats) => this with
    {
        StorageSizeBytes = stats.GetValue("storageSize", BsonInt64.Create(0L)).ToInt64(),
        DataSizeBytes = stats.GetValue("dataSize", BsonInt64.Create(0L)).ToInt64(),
        CollectionCount = stats.GetValue("collections", BsonInt32.Create(0)).ToInt32(),
        IndexCount = stats.GetValue("indexes", BsonInt32.Create(0)).ToInt32()
    };
}

public enum CollectionKind { Collection, View, TimeSeries }

/// <summary>A collection or view inside a database.</summary>
public sealed record CollectionInfo(string Database, string Name, CollectionKind Kind)
{
    public string Namespace => $"{Database}.{Name}";

    /// <summary>
    /// Counters from listCollections with storage stats, when the server supplied them.
    /// Null means "not measured", which is different from zero.
    /// </summary>
    public long? DocumentCount { get; init; }

    public long? StorageSizeBytes { get; init; }

    public long? DataSizeBytes { get; init; }

    public long? FreeStorageSizeBytes { get; init; }

    public long? TotalIndexSizeBytes { get; init; }

    public double? AverageDocumentSizeBytes { get; init; }

    public int? IndexCount { get; init; }

    /// <summary>
    /// The badges the collection list shows: capped, clustered, collation, view,
    /// timeseries, and Queryable Encryption. Compass reads these off the collection's
    /// creation options rather than from stats.
    /// </summary>
    public IReadOnlyList<string> Properties { get; init; } = [];

    /// <summary>For a view, the collection it reads from.</summary>
    public string? ViewOn { get; init; }

    /// <summary>Views have no storage of their own, so their size columns stay blank.</summary>
    public bool HasStorage => Kind != CollectionKind.View;
}

/// <summary>Counters shown on the collection header. All values are best-effort.</summary>
public sealed record CollectionStats(
    long DocumentCount,
    long StorageSizeBytes,
    long TotalIndexSizeBytes,
    int IndexCount,
    double AverageDocumentSizeBytes)
{
    /// <summary>Uncompressed size of the documents, as opposed to what they take on disk.</summary>
    public long DataSizeBytes { get; init; }

    /// <summary>
    /// Storage the collection has allocated but is not using. Null when the server did
    /// not report it, which is why the storage tooltip only splits total/used/free when
    /// there is a real number to split by.
    /// </summary>
    public long? FreeStorageSizeBytes { get; init; }
}

public sealed record IndexInfo(
    string Name,
    BsonDocument Key,
    bool IsUnique,
    bool IsSparse,
    bool IsTtl,
    long? SizeBytes)
{
    /// <summary>Renders the key as Compass does: <c>field ↑, other ↓</c>.</summary>
    public string KeyDescription => string.Join(", ", Key.Elements.Select(e =>
        e.Value switch
        {
            BsonInt32 i when i.Value == 1 => $"{e.Name} ↑",
            BsonInt32 i when i.Value == -1 => $"{e.Name} ↓",
            _ => $"{e.Name} ({e.Value})"
        }));
}
