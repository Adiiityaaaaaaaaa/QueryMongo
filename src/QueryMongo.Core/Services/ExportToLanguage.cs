using System.Text;
using MongoDB.Bson;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;

namespace QueryMongo.Core.Services;

public enum TargetLanguage { MongoShell, CSharp, Python, JavaScript, Java, Ruby, Go, Php, Rust }

/// <summary>
/// Turns the query bar or a pipeline into driver code, matching Compass's
/// "Export to language" command.
/// </summary>
public static class ExportToLanguage
{
    public static IReadOnlyList<TargetLanguage> All { get; } = Enum.GetValues<TargetLanguage>();

    public static string DisplayName(TargetLanguage language) => language switch
    {
        TargetLanguage.MongoShell => "Mongo Shell",
        TargetLanguage.CSharp => "C#",
        TargetLanguage.JavaScript => "Node.js",
        _ => language.ToString()
    };

    public static string Query(TargetLanguage language, string database, string collection, QuerySpec spec)
    {
        var filter = Compact(spec.Filter) ?? "{}";
        var projection = Compact(spec.Projection);
        var sort = Compact(spec.Sort);
        var b = new StringBuilder();

        switch (language)
        {
            case TargetLanguage.CSharp:
                b.AppendLine("var collection = client");
                b.AppendLine($"    .GetDatabase(\"{database}\")");
                b.AppendLine($"    .GetCollection<BsonDocument>(\"{collection}\");");
                b.AppendLine();
                b.AppendLine($"var filter = BsonDocument.Parse(@\"{Escape(filter)}\");");
                b.AppendLine();
                b.AppendLine("var options = new FindOptions<BsonDocument>");
                b.AppendLine("{");
                if (projection is not null)
                    b.AppendLine($"    Projection = BsonDocument.Parse(@\"{Escape(projection)}\"),");
                if (sort is not null)
                    b.AppendLine($"    Sort = BsonDocument.Parse(@\"{Escape(sort)}\"),");
                if (spec.Skip > 0) b.AppendLine($"    Skip = {spec.Skip},");
                if (spec.Limit > 0) b.AppendLine($"    Limit = {spec.Limit},");
                b.AppendLine("};");
                b.AppendLine();
                b.AppendLine("using var cursor = await collection.FindAsync(filter, options);");
                b.Append("var results = await cursor.ToListAsync();");
                break;

            case TargetLanguage.Python:
                b.AppendLine($"collection = client[\"{database}\"][\"{collection}\"]");
                b.AppendLine();
                b.Append($"cursor = collection.find({PyJson(filter)}");
                if (projection is not null) b.Append($", {PyJson(projection)}");
                b.AppendLine(")");
                if (sort is not null) b.AppendLine($"cursor = cursor.sort(list({PyJson(sort)}.items()))");
                if (spec.Skip > 0) b.AppendLine($"cursor = cursor.skip({spec.Skip})");
                if (spec.Limit > 0) b.AppendLine($"cursor = cursor.limit({spec.Limit})");
                b.Append("results = list(cursor)");
                break;

            case TargetLanguage.JavaScript:
                b.AppendLine($"const collection = client.db(\"{database}\").collection(\"{collection}\");");
                b.AppendLine();
                b.Append($"const results = await collection.find({filter}");
                if (projection is not null) b.Append($", {{ projection: {projection} }}");
                b.AppendLine(")");
                if (sort is not null) b.AppendLine($"  .sort({sort})");
                if (spec.Skip > 0) b.AppendLine($"  .skip({spec.Skip})");
                if (spec.Limit > 0) b.AppendLine($"  .limit({spec.Limit})");
                b.Append("  .toArray();");
                break;

            case TargetLanguage.Java:
                b.AppendLine("MongoCollection<Document> collection = client");
                b.AppendLine($"    .getDatabase(\"{database}\")");
                b.AppendLine($"    .getCollection(\"{collection}\");");
                b.AppendLine();
                b.Append($"FindIterable<Document> results = collection.find(Document.parse(\"{Inline(filter)}\"))");
                if (projection is not null)
                {
                    b.AppendLine();
                    b.Append($"    .projection(Document.parse(\"{Inline(projection)}\"))");
                }
                if (sort is not null)
                {
                    b.AppendLine();
                    b.Append($"    .sort(Document.parse(\"{Inline(sort)}\"))");
                }
                if (spec.Skip > 0) { b.AppendLine(); b.Append($"    .skip({spec.Skip})"); }
                if (spec.Limit > 0) { b.AppendLine(); b.Append($"    .limit({spec.Limit})"); }
                b.Append(';');
                break;

            case TargetLanguage.Ruby:
                b.AppendLine($"collection = client.use(\"{database}\")[:{collection}]");
                b.AppendLine();
                b.Append($"results = collection.find({filter})");
                if (projection is not null) b.Append($".projection({projection})");
                if (sort is not null) b.Append($".sort({sort})");
                if (spec.Skip > 0) b.Append($".skip({spec.Skip})");
                if (spec.Limit > 0) b.Append($".limit({spec.Limit})");
                break;

            case TargetLanguage.Go:
                b.AppendLine($"collection := client.Database(\"{database}\").Collection(\"{collection}\")");
                b.AppendLine();
                b.AppendLine("opts := options.Find()");
                if (projection is not null) b.AppendLine($"// projection: {Inline(projection)}");
                if (sort is not null) b.AppendLine($"// sort: {Inline(sort)}");
                if (spec.Skip > 0) b.AppendLine($"opts.SetSkip({spec.Skip})");
                if (spec.Limit > 0) b.AppendLine($"opts.SetLimit({spec.Limit})");
                b.AppendLine();
                b.AppendLine($"// filter: {Inline(filter)}");
                b.Append("cursor, err := collection.Find(ctx, bson.M{}, opts)");
                break;

            case TargetLanguage.Php:
                b.AppendLine($"$collection = $client->selectCollection('{database}', '{collection}');");
                b.AppendLine();
                b.AppendLine("$options = [");
                if (projection is not null) b.AppendLine($"    // projection: {Inline(projection)}");
                if (sort is not null) b.AppendLine($"    // sort: {Inline(sort)}");
                if (spec.Skip > 0) b.AppendLine($"    'skip' => {spec.Skip},");
                if (spec.Limit > 0) b.AppendLine($"    'limit' => {spec.Limit},");
                b.AppendLine("];");
                b.AppendLine();
                b.Append($"$cursor = $collection->find([], $options); // filter: {Inline(filter)}");
                break;

            case TargetLanguage.Rust:
                b.AppendLine($"let collection = client.database(\"{database}\")");
                b.AppendLine($"    .collection::<Document>(\"{collection}\");");
                b.AppendLine();
                b.AppendLine("let options = FindOptions::builder()");
                if (sort is not null) b.AppendLine($"    // sort: {Inline(sort)}");
                if (spec.Skip > 0) b.AppendLine($"    .skip({spec.Skip}u64)");
                if (spec.Limit > 0) b.AppendLine($"    .limit({spec.Limit}i64)");
                b.AppendLine("    .build();");
                b.AppendLine();
                b.Append($"let cursor = collection.find(doc! {{}}, options).await?; // filter: {Inline(filter)}");
                break;

            default:
                b.Append($"db.getSiblingDB(\"{database}\").{collection}.find({filter}");
                if (projection is not null) b.Append($", {projection}");
                b.Append(')');
                if (sort is not null) b.Append($".sort({sort})");
                if (spec.Skip > 0) b.Append($".skip({spec.Skip})");
                if (spec.Limit > 0) b.Append($".limit({spec.Limit})");
                break;
        }

        return b.ToString();
    }

    public static string Pipeline(
        TargetLanguage language, string database, string collection, IReadOnlyList<BsonDocument> stages)
    {
        var nl = Environment.NewLine;
        var json = "[" + nl
                   + string.Join("," + nl, stages.Select(s => "  " + BsonJson.ToCompactJson(s)))
                   + nl + "]";

        var b = new StringBuilder();

        switch (language)
        {
            case TargetLanguage.CSharp:
                b.AppendLine("var collection = client");
                b.AppendLine($"    .GetDatabase(\"{database}\")");
                b.AppendLine($"    .GetCollection<BsonDocument>(\"{collection}\");");
                b.AppendLine();
                b.AppendLine("var pipeline = new[]");
                b.AppendLine("{");
                foreach (var stage in stages)
                    b.AppendLine($"    BsonDocument.Parse(@\"{Escape(BsonJson.ToCompactJson(stage))}\"),");
                b.AppendLine("};");
                b.AppendLine();
                b.AppendLine("using var cursor = await collection.AggregateAsync<BsonDocument>(pipeline);");
                b.Append("var results = await cursor.ToListAsync();");
                break;

            case TargetLanguage.Python:
                b.AppendLine($"collection = client[\"{database}\"][\"{collection}\"]");
                b.AppendLine();
                b.Append($"results = list(collection.aggregate({PyJson(json)}))");
                break;

            case TargetLanguage.JavaScript:
                b.AppendLine($"const collection = client.db(\"{database}\").collection(\"{collection}\");");
                b.AppendLine();
                b.Append($"const results = await collection.aggregate({json}).toArray();");
                break;

            case TargetLanguage.Java:
                b.AppendLine("MongoCollection<Document> collection = client");
                b.AppendLine($"    .getDatabase(\"{database}\")");
                b.AppendLine($"    .getCollection(\"{collection}\");");
                b.AppendLine();
                b.AppendLine("List<Document> pipeline = Arrays.asList(");
                b.AppendLine(string.Join("," + nl, stages.Select(
                    st => $"    Document.parse(\"{Inline(BsonJson.ToCompactJson(st))}\")")));
                b.AppendLine(");");
                b.AppendLine();
                b.Append("AggregateIterable<Document> results = collection.aggregate(pipeline);");
                break;

            case TargetLanguage.Ruby:
                b.AppendLine($"collection = client.use(\"{database}\")[:{collection}]");
                b.AppendLine();
                b.Append($"results = collection.aggregate({json})");
                break;

            case TargetLanguage.Go:
                b.AppendLine($"collection := client.Database(\"{database}\").Collection(\"{collection}\")");
                b.AppendLine();
                b.AppendLine("// pipeline:");
                b.AppendLine(json);
                b.Append("cursor, err := collection.Aggregate(ctx, mongo.Pipeline{})");
                break;

            case TargetLanguage.Php:
                b.AppendLine($"$collection = $client->selectCollection('{database}', '{collection}');");
                b.AppendLine();
                b.AppendLine("// pipeline:");
                b.AppendLine(json);
                b.Append("$cursor = $collection->aggregate([]);");
                break;

            case TargetLanguage.Rust:
                b.AppendLine($"let collection = client.database(\"{database}\")");
                b.AppendLine($"    .collection::<Document>(\"{collection}\");");
                b.AppendLine();
                b.AppendLine("// pipeline:");
                b.AppendLine(json);
                b.Append("let cursor = collection.aggregate(vec![], None).await?;");
                break;

            default:
                b.Append($"db.getSiblingDB(\"{database}\").{collection}.aggregate({json})");
                break;
        }

        return b.ToString();
    }

    /// <summary>Returns null for an empty input so callers can omit the clause entirely.</summary>
    private static string? Compact(string? text)
    {
        var parsed = BsonJson.ParseDocument(text);
        if (!parsed.IsValid || parsed.Value is not { ElementCount: > 0 } doc) return null;
        return BsonJson.ToCompactJson(doc);
    }

    private static string Escape(string json) => json.Replace("\"", "\"\"");

    private static string Inline(string json) => json.Replace("\"", "\\\"");

    /// <summary>Python has no JSON literals; its true/false/null are spelled differently.</summary>
    private static string PyJson(string json) => json
        .Replace(": true", ": True")
        .Replace(": false", ": False")
        .Replace(": null", ": None");
}
