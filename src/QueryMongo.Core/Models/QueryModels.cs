using MongoDB.Bson;

namespace QueryMongo.Core.Models;

/// <summary>
/// The contents of the query bar: the same five inputs Compass exposes.
/// Each field holds raw Extended-JSON text so the UI can round-trip exactly what
/// the user typed, including invalid drafts.
/// </summary>
public sealed record QuerySpec
{
    public string Filter { get; init; } = "{}";
    public string Projection { get; init; } = "";
    public string Sort { get; init; } = "";
    public string Collation { get; init; } = "";
    public int Skip { get; init; }
    public int Limit { get; init; } = 50;

    /// <summary>
    /// An index to force, either as a key document or as an index name in quotes. Compass
    /// exposes this as "Index Hint" in the expanded query options.
    /// </summary>
    public string Hint { get; init; } = "";

    /// <summary>
    /// How long the server may spend on the query before giving up. Zero leaves the
    /// server's own default in place rather than imposing one.
    /// </summary>
    public int MaxTimeMs { get; init; }

    public static QuerySpec Default => new();
}

/// <summary>A single page of documents plus the cursor state needed to fetch the next.</summary>
public sealed record DocumentPage(
    IReadOnlyList<BsonDocument> Documents,
    int Skip,
    bool HasMore);

/// <summary>Outcome of <c>explain</c>, reduced to what the UI shows.</summary>
public sealed record ExplainSummary(
    string Stage,
    long DocumentsExamined,
    long KeysExamined,
    long DocumentsReturned,
    TimeSpan ExecutionTime,
    string? IndexName,
    BsonDocument Raw)
{
    /// <summary>True when the server scanned the whole collection — the warning Compass surfaces.</summary>
    public bool IsCollectionScan => Stage.Equals("COLLSCAN", StringComparison.OrdinalIgnoreCase);
}
