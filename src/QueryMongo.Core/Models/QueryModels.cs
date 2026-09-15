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
