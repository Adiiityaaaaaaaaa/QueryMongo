using System.Globalization;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

public enum TransferFormat { Json, Csv }

public sealed record TransferProgress(long Processed, long? Total)
{
    public double? Fraction => Total is > 0 ? (double)Processed / Total : null;
}

public sealed record ImportResult(long Inserted, long Failed, IReadOnlyList<string> Errors);

/// <summary>
/// Import and export of collection data, matching Compass's Export Collection and
/// Import Data commands.
/// </summary>
public sealed class TransferService(MongoSession session)
{
    private readonly MongoSession _session = session;

    /// <summary>Documents are written in batches so memory does not scale with collection size.</summary>
    private const int BatchSize = 1000;

    private IMongoCollection<BsonDocument> Collection(string database, string collection) =>
        _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

    /// <summary>
    /// Streams query results to a file. The cursor is consumed as it is written, so
    /// exporting a collection larger than memory works.
    /// </summary>
    public async Task<long> ExportAsync(
        string database,
        string collection,
        QuerySpec spec,
        string path,
        TransferFormat format,
        IReadOnlyList<string>? csvFields = null,
        IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        var filter = BsonJson.ParseDocument(spec.Filter).Value ?? new BsonDocument();

        var options = new FindOptions<BsonDocument>
        {
            Projection = Nullable(spec.Projection),
            Sort = Nullable(spec.Sort),
            Skip = spec.Skip > 0 ? spec.Skip : null,
            // A limit of 0 in the query bar means "no limit" for an export.
            Limit = spec.Limit > 0 ? spec.Limit : null,
            BatchSize = BatchSize
        };

        using var cursor = await Collection(database, collection)
            .FindAsync(filter, options, ct).ConfigureAwait(false);

        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));

        long written = 0;

        if (format == TransferFormat.Json)
        {
            await writer.WriteLineAsync("[").ConfigureAwait(false);

            while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
            {
                foreach (var doc in cursor.Current)
                {
                    if (written > 0) await writer.WriteLineAsync(",").ConfigureAwait(false);
                    await writer.WriteAsync(BsonJson.ToCompactJson(doc)).ConfigureAwait(false);
                    written++;
                }

                progress?.Report(new TransferProgress(written, null));
            }

            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync("]").ConfigureAwait(false);
        }
        else
        {
            var fields = csvFields?.ToList();
            var headerWritten = false;

            while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
            {
                foreach (var doc in cursor.Current)
                {
                    // Without an explicit field list, the first document defines the columns.
                    fields ??= doc.Names.ToList();

                    if (!headerWritten)
                    {
                        await writer.WriteLineAsync(string.Join(",", fields.Select(EscapeCsv)))
                            .ConfigureAwait(false);
                        headerWritten = true;
                    }

                    var cells = fields.Select(f => EscapeCsv(ReadPath(doc, f)));
                    await writer.WriteLineAsync(string.Join(",", cells)).ConfigureAwait(false);
                    written++;
                }

                progress?.Report(new TransferProgress(written, null));
            }
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        return written;

        BsonDocument? Nullable(string text) =>
            BsonJson.ParseDocument(text).Value is { ElementCount: > 0 } d ? d : null;
    }

    /// <summary>
    /// Loads documents from a file. Accepts a JSON array, newline-delimited JSON, or
    /// CSV. Bad rows are collected rather than aborting the run, so one malformed
    /// line does not lose the whole import.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        string database,
        string collection,
        string path,
        TransferFormat format,
        bool stopOnError = false,
        IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        var coll = Collection(database, collection);
        var errors = new List<string>();
        long inserted = 0, failed = 0;

        var batch = new List<BsonDocument>(BatchSize);

        async Task FlushAsync()
        {
            if (batch.Count == 0) return;

            try
            {
                await coll.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct)
                    .ConfigureAwait(false);
                inserted += batch.Count;
            }
            catch (MongoBulkWriteException<BsonDocument> e)
            {
                inserted += e.Result.InsertedCount;
                failed += e.WriteErrors.Count;
                errors.AddRange(e.WriteErrors.Take(10).Select(w => w.Message));
                if (stopOnError) throw;
            }

            batch.Clear();
            progress?.Report(new TransferProgress(inserted + failed, null));
        }

        foreach (var doc in ReadDocuments(path, format, errors))
        {
            ct.ThrowIfCancellationRequested();

            batch.Add(doc);
            if (batch.Count >= BatchSize) await FlushAsync().ConfigureAwait(false);
        }

        await FlushAsync().ConfigureAwait(false);

        failed += errors.Count(e => e.StartsWith("Line ", StringComparison.Ordinal));
        return new ImportResult(inserted, failed, errors);
    }

    private static IEnumerable<BsonDocument> ReadDocuments(
        string path, TransferFormat format, List<string> errors)
    {
        if (format == TransferFormat.Csv)
        {
            foreach (var doc in ReadCsv(path, errors)) yield return doc;
            yield break;
        }

        var text = File.ReadAllText(path).TrimStart();

        // A JSON array is one value; NDJSON is one document per line.
        if (text.StartsWith('['))
        {
            var parsed = BsonJson.ParsePipeline(text);
            if (!parsed.IsValid)
            {
                errors.Add($"File is not a valid JSON array: {parsed.Error}");
                yield break;
            }

            foreach (var doc in parsed.Value!) yield return doc;
            yield break;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parsed = BsonJson.ParseDocument(line);
            if (parsed.IsValid && parsed.Value is { } doc) yield return doc;
            else errors.Add($"Line {lineNumber}: {parsed.Error}");
        }
    }

    private static IEnumerable<BsonDocument> ReadCsv(string path, List<string> errors)
    {
        string[]? header = null;
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = SplitCsv(line);

            if (header is null)
            {
                header = cells;
                continue;
            }

            if (cells.Length != header.Length)
            {
                errors.Add($"Line {lineNumber}: expected {header.Length} columns, found {cells.Length}");
                continue;
            }

            var doc = new BsonDocument();
            for (var i = 0; i < header.Length; i++)
                doc[header[i]] = Infer(cells[i]);

            yield return doc;
        }
    }

    /// <summary>
    /// CSV carries no types, so values are inferred the way Compass does by default:
    /// numbers and booleans become typed values, everything else stays a string.
    /// </summary>
    private static BsonValue Infer(string cell)
    {
        if (string.IsNullOrEmpty(cell)) return BsonNull.Value;

        if (int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return new BsonInt32(i);

        if (long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return new BsonInt64(l);

        if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return new BsonDouble(d);

        if (bool.TryParse(cell, out var b)) return BsonBoolean.Create(b);

        return new BsonString(cell);
    }

    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted cell is a literal quote.
                    if (i + 1 < line.Length && line[i + 1] == '"') { builder.Append('"'); i++; }
                    else inQuotes = false;
                }
                else builder.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { cells.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(c);
        }

        cells.Add(builder.ToString());
        return [.. cells];
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    /// <summary>Reads a dotted path so nested fields can be exported as flat columns.</summary>
    private static string ReadPath(BsonDocument doc, string path)
    {
        BsonValue current = doc;

        foreach (var segment in path.Split('.'))
        {
            if (current is not BsonDocument d || !d.TryGetValue(segment, out var next)) return "";
            current = next;
        }

        return current switch
        {
            BsonNull => "",
            // A bare string goes in unquoted; the CSV writer quotes it if needed.
            BsonString s => s.Value,
            BsonDocument or BsonArray => BsonJson.ValueToJson(current),
            _ => current.ToString() ?? ""
        };
    }
}
