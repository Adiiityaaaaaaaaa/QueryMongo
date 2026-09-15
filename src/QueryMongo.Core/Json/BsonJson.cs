using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace QueryMongo.Core.Json;

/// <summary>Result of parsing one query-bar input.</summary>
public readonly record struct ParseResult<T>(T? Value, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>Factories for <see cref="ParseResult{T}"/>, kept off the generic type itself.</summary>
public static class ParseResult
{
    public static ParseResult<T> Ok<T>(T value) => new(value, null);
    public static ParseResult<T> Fail<T>(string error) => new(default, error);
}

/// <summary>
/// Parsing and formatting for the Extended JSON the user types and the documents we
/// show back. The driver's reader already accepts shell-style input (unquoted keys,
/// single quotes, <c>ObjectId(...)</c>), so the query bar takes the same text a
/// <c>mongosh</c> user would write.
/// </summary>
public static class BsonJson
{
    private static readonly JsonWriterSettings Pretty = new()
    {
        Indent = true,
        IndentChars = "  ",
        OutputMode = JsonOutputMode.RelaxedExtendedJson
    };

    private static readonly JsonWriterSettings Compact = new()
    {
        Indent = false,
        OutputMode = JsonOutputMode.RelaxedExtendedJson
    };

    /// <summary>
    /// Parses a document input. Blank text yields an empty document, which is what an
    /// empty filter, projection or sort box should mean.
    /// </summary>
    public static ParseResult<BsonDocument> ParseDocument(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ParseResult.Ok(new BsonDocument());

        try
        {
            return ParseResult.Ok(BsonDocument.Parse(text));
        }
        catch (Exception e) when (IsParseFailure(e))
        {
            return ParseResult.Fail<BsonDocument>(Describe(e));
        }
    }

    /// <summary>Parses an aggregation pipeline: a JSON array of stage documents.</summary>
    public static ParseResult<List<BsonDocument>> ParsePipeline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ParseResult.Ok(new List<BsonDocument>());

        try
        {
            // Wrapping in an object lets the same reader handle the array form.
            var wrapper = BsonDocument.Parse($"{{ \"pipeline\": {text} }}");
            var array = wrapper["pipeline"].AsBsonArray;

            var stages = new List<BsonDocument>(array.Count);
            foreach (var stage in array)
            {
                if (stage is not BsonDocument doc)
                    return ParseResult.Fail<List<BsonDocument>>("Every pipeline stage must be a document.");
                stages.Add(doc);
            }

            return ParseResult.Ok(stages);
        }
        catch (Exception e) when (IsParseFailure(e))
        {
            return ParseResult.Fail<List<BsonDocument>>(Describe(e));
        }
    }

    public static string ToPrettyJson(BsonDocument document) => document.ToJson(Pretty);

    public static string ToCompactJson(BsonDocument document) => document.ToJson(Compact);

    /// <summary>
    /// A one-line preview for collapsed rows, truncated so a 16 MB document cannot
    /// stall the list.
    /// </summary>
    public static string ToPreview(BsonDocument document, int maxLength = 200)
    {
        var json = ToCompactJson(document);
        return json.Length <= maxLength ? json : string.Concat(json.AsSpan(0, maxLength), "…");
    }

    /// <summary>
    /// The driver signals bad JSON through several exception types, and truncated
    /// input surfaces as InvalidOperationException from the reader rather than
    /// FormatException. Parsing is pure and bounded, so anything short of a process
    /// failure is reported as a parse error instead of escaping to the UI.
    /// </summary>
    private static bool IsParseFailure(Exception e) =>
        e is not (OutOfMemoryException or StackOverflowException or OperationCanceledException);

    private static string Describe(Exception e) =>
        e.Message.Replace("\r", " ").Replace("\n", " ").Trim();
}
