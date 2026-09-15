using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>How often one BSON type appears for a given field.</summary>
public sealed record SchemaTypeShare(string TypeName, int Count, double Fraction);

/// <summary>
/// One field in the sampled schema. <paramref name="Path"/> is dotted, so a field
/// inside an array of subdocuments reads as <c>cast.name</c>.
/// </summary>
public sealed record SchemaField(
    string Path,
    int Present,
    int SampleSize,
    IReadOnlyList<SchemaTypeShare> Types,
    IReadOnlyList<string> Examples)
{
    /// <summary>Share of sampled documents that carry this field at all.</summary>
    public double Presence => SampleSize == 0 ? 0 : (double)Present / SampleSize;

    /// <summary>Compass highlights fields that only some documents have.</summary>
    public bool IsSparse => Presence < 0.999;

    public string PresenceDescription => $"{Presence:P0} of documents";

    public string TypeDescription => string.Join(", ", Types.Select(t => $"{t.TypeName} {t.Fraction:P0}"));
}

public sealed record SchemaReport(int SampleSize, IReadOnlyList<SchemaField> Fields);

/// <summary>
/// Infers a collection's shape by sampling documents, the way Compass's Schema tab
/// does. MongoDB is schemaless, so this is always an estimate over a sample rather
/// than a declared schema.
/// </summary>
public sealed class SchemaService(MongoSession session)
{
    private readonly MongoSession _session = session;

    /// <summary>Fields nested deeper than this are not expanded, to bound the work.</summary>
    private const int MaxDepth = 4;

    private const int MaxExamples = 3;

    public async Task<SchemaReport> AnalyzeAsync(
        string database,
        string collection,
        BsonDocument? filter = null,
        int sampleSize = 1000,
        CancellationToken ct = default)
    {
        var coll = _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

        // $sample pushes sampling to the server, so a 10M-document collection costs
        // about the same as a small one.
        var pipeline = new List<BsonDocument>();
        if (filter is { ElementCount: > 0 })
            pipeline.Add(new BsonDocument("$match", filter));
        pipeline.Add(new BsonDocument("$sample", new BsonDocument("size", sampleSize)));

        using var cursor = await coll
            .AggregateAsync<BsonDocument>(pipeline, new AggregateOptions { AllowDiskUse = true }, ct)
            .ConfigureAwait(false);

        var accumulator = new Dictionary<string, FieldAccumulator>(StringComparer.Ordinal);
        var sampled = 0;

        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                sampled++;
                Walk(doc, prefix: "", depth: 0, accumulator);
            }
        }

        var fields = accumulator
            .Select(kvp => kvp.Value.ToField(kvp.Key, sampled))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        return new SchemaReport(sampled, fields);
    }

    private static void Walk(
        BsonDocument doc, string prefix, int depth, Dictionary<string, FieldAccumulator> into)
    {
        foreach (var element in doc.Elements)
        {
            var path = prefix.Length == 0 ? element.Name : $"{prefix}.{element.Name}";

            if (!into.TryGetValue(path, out var acc))
                into[path] = acc = new FieldAccumulator();

            acc.Observe(element.Value);

            if (depth >= MaxDepth) continue;

            switch (element.Value)
            {
                case BsonDocument nested:
                    Walk(nested, path, depth + 1, into);
                    break;

                case BsonArray array:
                    // Array elements fold into the same path, which is how Compass
                    // reports "cast.name" for an array of subdocuments.
                    foreach (var item in array)
                        if (item is BsonDocument itemDoc)
                            Walk(itemDoc, path, depth + 1, into);
                    break;
            }
        }
    }

    private sealed class FieldAccumulator
    {
        private readonly Dictionary<string, int> _types = new(StringComparer.Ordinal);
        private readonly List<string> _examples = [];
        private int _present;

        public void Observe(BsonValue value)
        {
            _present++;

            var typeName = Describe(value);
            _types[typeName] = _types.GetValueOrDefault(typeName) + 1;

            if (_examples.Count < MaxExamples && value is not (BsonDocument or BsonArray))
            {
                var text = value.ToString() ?? "";
                if (text.Length > 60) text = string.Concat(text.AsSpan(0, 60), "…");
                if (!_examples.Contains(text)) _examples.Add(text);
            }
        }

        public SchemaField ToField(string path, int sampleSize)
        {
            var types = _types
                .OrderByDescending(t => t.Value)
                .Select(t => new SchemaTypeShare(
                    t.Key, t.Value, _present == 0 ? 0 : (double)t.Value / _present))
                .ToList();

            return new SchemaField(path, _present, sampleSize, types, _examples);
        }

        private static string Describe(BsonValue value) => value.BsonType switch
        {
            BsonType.Double or BsonType.Int32 or BsonType.Int64 or BsonType.Decimal128 => "Number",
            BsonType.String => "String",
            BsonType.Document => "Document",
            BsonType.Array => "Array",
            BsonType.Boolean => "Boolean",
            BsonType.DateTime => "Date",
            BsonType.ObjectId => "ObjectId",
            BsonType.Null => "Null",
            BsonType.Binary => "Binary",
            BsonType.RegularExpression => "Regex",
            _ => value.BsonType.ToString()
        };
    }
}
