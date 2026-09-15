using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Json;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Shell;

/// <summary>Result of one shell command.</summary>
public sealed record ShellResult(string Output, bool IsError = false)
{
    public static ShellResult Error(string message) => new(message, true);
}

/// <summary>
/// A command interpreter over the driver, covering the shell surface people actually
/// type: <c>show</c>, <c>use</c>, and the common collection and database methods.
///
/// This is not mongosh. mongosh is a Node.js runtime, so expressions, variables and
/// scripting are out of scope; what is here maps each command onto a driver call.
/// The help text says so, and unsupported methods name themselves rather than
/// failing silently.
/// </summary>
public sealed class ShellService(MongoSession session, string initialDatabase = "test")
{
    private readonly MongoSession _session = session;

    /// <summary>The database <c>db</c> currently refers to, changed by <c>use</c>.</summary>
    public string CurrentDatabase { get; private set; } = initialDatabase;

    public async Task<ShellResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        ShellCommand command;
        try
        {
            command = ShellParser.Parse(input);
        }
        catch (Exception e)
        {
            return ShellResult.Error($"Could not parse: {e.Message}");
        }

        try
        {
            return command.Kind switch
            {
                ShellCommandKind.Empty => new ShellResult(""),
                ShellCommandKind.Help => new ShellResult(HelpText),
                ShellCommandKind.ShowDatabases => await ShowDatabasesAsync(ct).ConfigureAwait(false),
                ShellCommandKind.ShowCollections => await ShowCollectionsAsync(ct).ConfigureAwait(false),
                ShellCommandKind.Use => Use(command.Target),
                ShellCommandKind.Database => await RunDatabaseAsync(command, ct).ConfigureAwait(false),
                ShellCommandKind.Collection => await RunCollectionAsync(command, ct).ConfigureAwait(false),
                _ => ShellResult.Error($"Unrecognised command. Type help for what is supported.")
            };
        }
        catch (MongoException e)
        {
            return ShellResult.Error(e.Message);
        }
        catch (Exception e)
        {
            return ShellResult.Error($"{e.GetType().Name}: {e.Message}");
        }
    }

    private ShellResult Use(string? database)
    {
        if (string.IsNullOrWhiteSpace(database)) return ShellResult.Error("use needs a database name.");

        CurrentDatabase = database.Trim();
        return new ShellResult($"switched to db {CurrentDatabase}");
    }

    private async Task<ShellResult> ShowDatabasesAsync(CancellationToken ct)
    {
        using var cursor = await _session.Client.ListDatabasesAsync(cancellationToken: ct)
            .ConfigureAwait(false);
        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        var builder = new StringBuilder();
        foreach (var doc in docs.OrderBy(d => d.GetValue("name", BsonString.Empty).AsString))
        {
            var name = doc.GetValue("name", BsonString.Empty).AsString;
            var size = doc.GetValue("sizeOnDisk", BsonInt64.Create(0L)).ToInt64();
            builder.AppendLine($"{name,-32} {ByteSize.Format(size)}");
        }

        return new ShellResult(builder.ToString().TrimEnd());
    }

    private async Task<ShellResult> ShowCollectionsAsync(CancellationToken ct)
    {
        var db = _session.Client.GetDatabase(CurrentDatabase);

        using var cursor = await db.ListCollectionNamesAsync(cancellationToken: ct).ConfigureAwait(false);
        var names = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return new ShellResult(string.Join(Environment.NewLine, names.OrderBy(n => n, StringComparer.Ordinal)));
    }

    private async Task<ShellResult> RunDatabaseAsync(ShellCommand command, CancellationToken ct)
    {
        var call = command.Calls[0];
        var db = _session.Client.GetDatabase(CurrentDatabase);

        switch (call.Method)
        {
            case "runCommand" or "adminCommand":
            {
                if (ParseDocument(call, 0) is not { } doc)
                    return ShellResult.Error($"{call.Method} needs a command document.");

                var target = call.Method == "adminCommand"
                    ? _session.Client.GetDatabase("admin")
                    : db;

                var result = await target.RunCommandAsync<BsonDocument>(doc, cancellationToken: ct)
                    .ConfigureAwait(false);

                return new ShellResult(BsonJson.ToPrettyJson(result));
            }

            case "getCollectionNames":
                return await ShowCollectionsAsync(ct).ConfigureAwait(false);

            case "getName":
                return new ShellResult(CurrentDatabase);

            case "stats":
            {
                var stats = await db
                    .RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: ct)
                    .ConfigureAwait(false);
                return new ShellResult(BsonJson.ToPrettyJson(stats));
            }

            case "dropDatabase":
                await _session.Client.DropDatabaseAsync(CurrentDatabase, ct).ConfigureAwait(false);
                return new ShellResult($"dropped {CurrentDatabase}");

            case "createCollection":
            {
                if (call.Arguments.Count == 0) return ShellResult.Error("createCollection needs a name.");
                var name = Unquote(call.Arguments[0]);
                await db.CreateCollectionAsync(name, cancellationToken: ct).ConfigureAwait(false);
                return new ShellResult($"created {name}");
            }

            default:
                return ShellResult.Error($"db.{call.Method}() is not supported. Type help for the list.");
        }
    }

    private async Task<ShellResult> RunCollectionAsync(ShellCommand command, CancellationToken ct)
    {
        var collection = _session.Client
            .GetDatabase(CurrentDatabase)
            .GetCollection<BsonDocument>(command.Collection!);

        var call = command.Calls[0];

        // Anything after the first call is a cursor modifier: .sort(), .limit(), .skip().
        var modifiers = command.Calls.Skip(1).ToList();

        switch (call.Method)
        {
            case "find" or "findOne":
            {
                var filter = ParseDocument(call, 0) ?? new BsonDocument();
                var projection = ParseDocument(call, 1);

                var options = new FindOptions<BsonDocument>
                {
                    Projection = projection,
                    Limit = call.Method == "findOne" ? 1 : DefaultShellLimit
                };

                foreach (var modifier in modifiers)
                {
                    switch (modifier.Method)
                    {
                        case "sort":
                            options.Sort = ParseDocument(modifier, 0);
                            break;
                        case "limit" when modifier.Arguments.Count > 0:
                            options.Limit = ParseInt(modifier.Arguments[0]);
                            break;
                        case "skip" when modifier.Arguments.Count > 0:
                            options.Skip = ParseInt(modifier.Arguments[0]);
                            break;
                        case "count":
                        {
                            var counted = await collection.CountDocumentsAsync(filter, cancellationToken: ct)
                                .ConfigureAwait(false);
                            return new ShellResult(counted.ToString());
                        }
                    }
                }

                using var cursor = await collection.FindAsync(filter, options, ct).ConfigureAwait(false);
                var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

                return new ShellResult(FormatDocuments(docs, options.Limit));
            }

            case "countDocuments" or "count":
            {
                var filter = ParseDocument(call, 0) ?? new BsonDocument();
                var counted = await collection.CountDocumentsAsync(filter, cancellationToken: ct)
                    .ConfigureAwait(false);
                return new ShellResult(counted.ToString());
            }

            case "estimatedDocumentCount":
            {
                var counted = await collection.EstimatedDocumentCountAsync(cancellationToken: ct)
                    .ConfigureAwait(false);
                return new ShellResult(counted.ToString());
            }

            case "distinct":
            {
                if (call.Arguments.Count == 0) return ShellResult.Error("distinct needs a field name.");

                var field = Unquote(call.Arguments[0]);
                var filter = ParseDocument(call, 1) ?? new BsonDocument();

                using var cursor = await collection
                    .DistinctAsync<BsonValue>(field, filter, cancellationToken: ct)
                    .ConfigureAwait(false);
                var values = await cursor.ToListAsync(ct).ConfigureAwait(false);

                return new ShellResult("[ " + string.Join(", ", values.Select(BsonJson.ValueToJson)) + " ]");
            }

            case "insertOne":
            {
                if (ParseDocument(call, 0) is not { } doc)
                    return ShellResult.Error("insertOne needs a document.");

                await collection.InsertOneAsync(doc, cancellationToken: ct).ConfigureAwait(false);
                return new ShellResult($"{{ acknowledged: true, insertedId: {BsonJson.ValueToJson(doc.GetValue("_id", BsonNull.Value))} }}");
            }

            case "insertMany":
            {
                if (call.Arguments.Count == 0) return ShellResult.Error("insertMany needs an array.");

                var parsed = BsonJson.ParsePipeline(call.Arguments[0]);
                if (!parsed.IsValid) return ShellResult.Error(parsed.Error!);

                await collection.InsertManyAsync(parsed.Value!, cancellationToken: ct).ConfigureAwait(false);
                return new ShellResult($"{{ acknowledged: true, insertedCount: {parsed.Value!.Count} }}");
            }

            case "updateOne" or "updateMany":
            {
                var filter = ParseDocument(call, 0);
                var update = ParseDocument(call, 1);
                if (filter is null || update is null)
                    return ShellResult.Error($"{call.Method} needs a filter and an update.");

                var result = call.Method == "updateOne"
                    ? await collection.UpdateOneAsync(filter, update, cancellationToken: ct).ConfigureAwait(false)
                    : await collection.UpdateManyAsync(filter, update, cancellationToken: ct).ConfigureAwait(false);

                return new ShellResult(
                    $"{{ matchedCount: {result.MatchedCount}, modifiedCount: {result.ModifiedCount} }}");
            }

            case "replaceOne":
            {
                var filter = ParseDocument(call, 0);
                var replacement = ParseDocument(call, 1);
                if (filter is null || replacement is null)
                    return ShellResult.Error("replaceOne needs a filter and a replacement.");

                var result = await collection.ReplaceOneAsync(filter, replacement, cancellationToken: ct)
                    .ConfigureAwait(false);

                return new ShellResult(
                    $"{{ matchedCount: {result.MatchedCount}, modifiedCount: {result.ModifiedCount} }}");
            }

            case "deleteOne" or "deleteMany":
            {
                var filter = ParseDocument(call, 0) ?? new BsonDocument();

                var result = call.Method == "deleteOne"
                    ? await collection.DeleteOneAsync(filter, ct).ConfigureAwait(false)
                    : await collection.DeleteManyAsync(filter, ct).ConfigureAwait(false);

                return new ShellResult($"{{ acknowledged: true, deletedCount: {result.DeletedCount} }}");
            }

            case "aggregate":
            {
                if (call.Arguments.Count == 0) return ShellResult.Error("aggregate needs a pipeline array.");

                var parsed = BsonJson.ParsePipeline(call.Arguments[0]);
                if (!parsed.IsValid) return ShellResult.Error(parsed.Error!);

                using var cursor = await collection
                    .AggregateAsync<BsonDocument>(parsed.Value!,
                        new AggregateOptions { AllowDiskUse = true }, ct)
                    .ConfigureAwait(false);

                var docs = new List<BsonDocument>();
                while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        docs.Add(doc);
                        if (docs.Count >= DefaultShellLimit) goto done;
                    }
                }
                done:

                return new ShellResult(FormatDocuments(docs, DefaultShellLimit));
            }

            case "getIndexes" or "getIndexKeys":
            {
                using var cursor = await collection.Indexes.ListAsync(ct).ConfigureAwait(false);
                var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);
                return new ShellResult(FormatDocuments(docs, docs.Count));
            }

            case "createIndex":
            {
                if (ParseDocument(call, 0) is not { } keys)
                    return ShellResult.Error("createIndex needs a key document.");

                var name = await collection.Indexes
                    .CreateOneAsync(new CreateIndexModel<BsonDocument>(keys), cancellationToken: ct)
                    .ConfigureAwait(false);

                return new ShellResult(name);
            }

            case "dropIndex":
            {
                if (call.Arguments.Count == 0) return ShellResult.Error("dropIndex needs an index name.");

                await collection.Indexes.DropOneAsync(Unquote(call.Arguments[0]), ct).ConfigureAwait(false);
                return new ShellResult("{ ok: 1 }");
            }

            case "drop":
                await _session.Client.GetDatabase(CurrentDatabase)
                    .DropCollectionAsync(command.Collection, ct).ConfigureAwait(false);
                return new ShellResult($"dropped {command.Collection}");

            case "stats":
            {
                var stats = await _session.Client.GetDatabase(CurrentDatabase)
                    .RunCommandAsync<BsonDocument>(
                        new BsonDocument { { "collStats", command.Collection } }, cancellationToken: ct)
                    .ConfigureAwait(false);

                return new ShellResult(BsonJson.ToPrettyJson(stats));
            }

            default:
                return ShellResult.Error(
                    $"db.{command.Collection}.{call.Method}() is not supported. Type help for the list.");
        }
    }

    /// <summary>The shell caps output the way mongosh does, so a bare find cannot dump a collection.</summary>
    private const int DefaultShellLimit = 20;

    private static string FormatDocuments(List<BsonDocument> docs, int? limit)
    {
        if (docs.Count == 0) return "(no documents)";

        var builder = new StringBuilder();
        foreach (var doc in docs)
        {
            builder.AppendLine(BsonJson.ToPrettyJson(doc));
        }

        if (limit is { } max && docs.Count >= max)
            builder.AppendLine($"Type \"it\" in Compass; here output stops at {max} documents.");

        return builder.ToString().TrimEnd();
    }

    private static BsonDocument? ParseDocument(ShellCall call, int index)
    {
        if (call.Arguments.Count <= index) return null;

        var parsed = BsonJson.ParseDocument(call.Arguments[index]);
        return parsed.IsValid ? parsed.Value : null;
    }

    private static int ParseInt(string text) =>
        int.TryParse(text.Trim(), out var value) ? value : 0;

    private static string Unquote(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == trimmed[^1] && trimmed[0] is '"' or '\'')
            return trimmed[1..^1];
        return trimmed;
    }

    public const string HelpText = """
        QueryMongo shell — runs commands against the driver.

        This is not mongosh: there is no JavaScript engine, so variables, loops and
        expressions are not available. Each command maps onto a driver call.

          show dbs                        list databases
          show collections                list collections in the current database
          use <database>                  switch the database db refers to

          db.runCommand({ ... })          run a database command
          db.adminCommand({ ... })        run a command against admin
          db.stats()                      database statistics
          db.createCollection("name")     create a collection
          db.getCollectionNames()         list collection names

          db.<c>.find({ ... }, { ... })   query; chain .sort() .limit() .skip() .count()
          db.<c>.findOne({ ... })         first matching document
          db.<c>.countDocuments({ ... })  exact count
          db.<c>.estimatedDocumentCount() fast metadata count
          db.<c>.distinct("field")        distinct values
          db.<c>.aggregate([ ... ])       run a pipeline
          db.<c>.insertOne({ ... })       insert one document
          db.<c>.insertMany([ ... ])      insert several
          db.<c>.updateOne(f, u)          update the first match
          db.<c>.updateMany(f, u)         update every match
          db.<c>.replaceOne(f, r)         replace the first match
          db.<c>.deleteOne(f)             delete the first match
          db.<c>.deleteMany(f)            delete every match
          db.<c>.getIndexes()             list indexes
          db.<c>.createIndex({ ... })     create an index
          db.<c>.dropIndex("name")        drop an index
          db.<c>.stats()                  collection statistics
          db.<c>.drop()                   drop the collection

        Filters accept shell syntax: unquoted keys and single quotes are fine.
        Output is capped at 20 documents.
        """;
}
