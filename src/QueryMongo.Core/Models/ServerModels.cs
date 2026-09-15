using MongoDB.Bson;

namespace QueryMongo.Core.Models;

/// <summary>A database as listed by <c>listDatabases</c>.</summary>
public sealed record DatabaseInfo(string Name, long SizeOnDisk, bool Empty)
{
    public static DatabaseInfo FromBson(BsonDocument doc) => new(
        doc.GetValue("name", BsonString.Empty).AsString,
        doc.GetValue("sizeOnDisk", BsonInt64.Create(0L)).ToInt64(),
        doc.GetValue("empty", BsonBoolean.False).ToBoolean());
}

public enum CollectionKind { Collection, View, TimeSeries }

/// <summary>A collection or view inside a database.</summary>
public sealed record CollectionInfo(string Database, string Name, CollectionKind Kind)
{
    public string Namespace => $"{Database}.{Name}";
}

/// <summary>Counters shown on the collection header. All values are best-effort.</summary>
public sealed record CollectionStats(
    long DocumentCount,
    long StorageSizeBytes,
    long TotalIndexSizeBytes,
    int IndexCount,
    double AverageDocumentSizeBytes);

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
