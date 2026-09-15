using MongoDB.Bson;

namespace QueryMongo.Core.Models;

/// <summary>
/// One node in the execution plan, with the numbers Compass shows on each stage card.
/// </summary>
public sealed record ExplainNode(
    string Stage,
    long Returned,
    long Examined,
    TimeSpan Elapsed,
    string? IndexName,
    BsonDocument? KeyPattern,
    BsonDocument Raw,
    IReadOnlyList<ExplainNode> Children)
{
    /// <summary>Stages that read every document rather than using an index.</summary>
    public bool IsScan => Stage.Equals("COLLSCAN", StringComparison.OrdinalIgnoreCase);

    /// <summary>A sort the server performed in memory, which is a common cause of slowness.</summary>
    public bool IsBlockingSort => Stage.Equals("SORT", StringComparison.OrdinalIgnoreCase);

    public bool IsProblem => IsScan || IsBlockingSort;

    /// <summary>What the stage does, in one line, for the card subtitle.</summary>
    public string Description => Stage switch
    {
        "COLLSCAN" => "Reads every document in the collection",
        "IXSCAN" => $"Scans the index {IndexName}",
        "FETCH" => "Loads the full documents for the matched index keys",
        "SORT" => "Sorts results in memory",
        "SORT_KEY_GENERATOR" => "Prepares sort keys",
        "LIMIT" => "Stops after the requested number of documents",
        "SKIP" => "Discards the leading documents",
        "PROJECTION_SIMPLE" or "PROJECTION_COVERED" or "PROJECTION_DEFAULT" => "Shapes the returned fields",
        "GROUP" => "Groups documents",
        "COUNT" or "COUNT_SCAN" => "Counts without loading documents",
        "DISTINCT_SCAN" => "Reads distinct index keys only",
        "IDHACK" => "Looks up directly by _id",
        "FETCH_AND_LIMIT" => "Loads documents up to the limit",
        _ => Stage
    };

    /// <summary>The hint shown on a problem stage.</summary>
    public string? Advice => Stage switch
    {
        "COLLSCAN" => "Add an index covering the filtered fields.",
        "SORT" => "Add an index whose key order matches the sort to avoid an in-memory sort.",
        _ => null
    };

    public string KeyDescription => KeyPattern is null
        ? ""
        : string.Join(", ", KeyPattern.Elements.Select(e =>
            e.Value is BsonInt32 { Value: -1 } ? $"{e.Name} ↓" : $"{e.Name} ↑"));
}

/// <summary>The plan plus the totals that sit above it.</summary>
public sealed record ExplainPlan(
    ExplainNode? Root,
    long TotalReturned,
    long TotalDocsExamined,
    long TotalKeysExamined,
    TimeSpan ExecutionTime,
    string Namespace,
    BsonDocument Raw)
{
    /// <summary>
    /// Documents examined per document returned. Anything far above 1 means the server
    /// is doing much more work than the result justifies, which is Compass's main signal.
    /// </summary>
    public double ExaminedPerReturned =>
        TotalReturned == 0 ? TotalDocsExamined : (double)TotalDocsExamined / TotalReturned;

    public bool IsInefficient => TotalReturned > 0 && ExaminedPerReturned > 10;

    /// <summary>Every node, flattened, so the UI can list problems without walking the tree.</summary>
    public IEnumerable<ExplainNode> AllNodes => Root is null ? [] : Flatten(Root);

    private static IEnumerable<ExplainNode> Flatten(ExplainNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    /// <summary>
    /// Builds the tree from raw explain output.
    ///
    /// The server nests the plan through <c>inputStage</c>, <c>inputStages</c> (for a
    /// merge across shards or an OR) and <c>shards</c>, so all three are followed.
    /// </summary>
    public static ExplainPlan Parse(BsonDocument explain)
    {
        var stats = explain.GetValue("executionStats", new BsonDocument()).AsBsonDocument;

        var planner = explain.GetValue("queryPlanner", new BsonDocument()).AsBsonDocument;
        var ns = planner.GetValue("namespace", BsonString.Empty).AsString;

        // executionStats carries the timings; queryPlanner alone has only the shape.
        var executionRoot = stats.GetValue("executionStages", BsonNull.Value);
        var plannerRoot = planner.GetValue("winningPlan", BsonNull.Value);

        var source = executionRoot as BsonDocument ?? plannerRoot as BsonDocument;

        var millis = stats.GetValue("executionTimeMillis", BsonInt32.Create(0)).ToInt32();

        return new ExplainPlan(
            Root: source is null ? null : BuildNode(source),
            TotalReturned: stats.GetValue("nReturned", BsonInt64.Create(0L)).ToInt64(),
            TotalDocsExamined: stats.GetValue("totalDocsExamined", BsonInt64.Create(0L)).ToInt64(),
            TotalKeysExamined: stats.GetValue("totalKeysExamined", BsonInt64.Create(0L)).ToInt64(),
            ExecutionTime: TimeSpan.FromMilliseconds(millis),
            Namespace: ns,
            Raw: explain);
    }

    private static ExplainNode BuildNode(BsonDocument doc)
    {
        var children = new List<ExplainNode>();

        if (doc.TryGetValue("inputStage", out var single) && single is BsonDocument singleDoc)
            children.Add(BuildNode(singleDoc));

        if (doc.TryGetValue("inputStages", out var many) && many is BsonArray manyArray)
            children.AddRange(manyArray.OfType<BsonDocument>().Select(BuildNode));

        // A sharded plan nests each shard's stages under "shards".
        if (doc.TryGetValue("shards", out var shards) && shards is BsonArray shardArray)
        {
            foreach (var shard in shardArray.OfType<BsonDocument>())
            {
                var shardRoot = shard.GetValue("executionStages", BsonNull.Value) as BsonDocument
                                ?? shard.GetValue("winningPlan", BsonNull.Value) as BsonDocument;
                if (shardRoot is not null) children.Add(BuildNode(shardRoot));
            }
        }

        // queryPlan appears in newer explain output for the winning plan's shape.
        if (children.Count == 0
            && doc.TryGetValue("queryPlan", out var queryPlan)
            && queryPlan is BsonDocument queryPlanDoc)
        {
            children.Add(BuildNode(queryPlanDoc));
        }

        return new ExplainNode(
            Stage: doc.GetValue("stage", BsonString.Create("UNKNOWN")).AsString,
            Returned: doc.GetValue("nReturned", BsonInt64.Create(0L)).ToInt64(),
            Examined: doc.GetValue("docsExamined", doc.GetValue("keysExamined", BsonInt64.Create(0L))).ToInt64(),
            Elapsed: TimeSpan.FromMilliseconds(
                doc.GetValue("executionTimeMillisEstimate", BsonInt64.Create(0L)).ToInt64()),
            IndexName: doc.TryGetValue("indexName", out var name) ? name.AsString : null,
            KeyPattern: doc.TryGetValue("keyPattern", out var key) && key is BsonDocument keyDoc
                ? keyDoc
                : null,
            Raw: doc,
            Children: children);
    }
}
