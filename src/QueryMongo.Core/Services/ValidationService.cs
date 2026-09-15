using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>What the server rejects, and how loudly, when a write breaks the rules.</summary>
public sealed record ValidationRules(BsonDocument Validator, string Level, string Action)
{
    /// <summary>"off", "moderate" or "strict".</summary>
    public static readonly string[] Levels = ["off", "moderate", "strict"];

    /// <summary>"error" rejects the write; "warn" only logs it.</summary>
    public static readonly string[] Actions = ["error", "warn"];

    public bool HasRules => Validator.ElementCount > 0;

    public static ValidationRules Empty => new(new BsonDocument(), "off", "error");
}

/// <summary>One document that does not satisfy the current rules.</summary>
public sealed record ValidationFailure(BsonDocument Document);

/// <summary>
/// Reads and writes a collection's JSON-schema validation, matching Compass's
/// Validation tab.
/// </summary>
public sealed class ValidationService(MongoSession session)
{
    private readonly MongoSession _session = session;

    public async Task<ValidationRules> GetAsync(
        string database, string collection, CancellationToken ct = default)
    {
        var db = _session.Client.GetDatabase(database);

        var filter = new BsonDocument("name", collection);
        using var cursor = await db
            .ListCollectionsAsync(new ListCollectionsOptions { Filter = filter }, ct)
            .ConfigureAwait(false);

        var info = await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (info is null || !info.Contains("options")) return ValidationRules.Empty;

        var options = info["options"].AsBsonDocument;
        if (!options.Contains("validator")) return ValidationRules.Empty;

        return new ValidationRules(
            options["validator"].AsBsonDocument,
            options.GetValue("validationLevel", BsonString.Create("strict")).AsString,
            options.GetValue("validationAction", BsonString.Create("error")).AsString);
    }

    /// <summary>
    /// Applies rules through <c>collMod</c>. Existing documents are not re-checked;
    /// the rules govern subsequent writes, which is why the tab also offers a preview
    /// of what currently fails.
    /// </summary>
    public async Task SetAsync(
        string database, string collection, ValidationRules rules, CancellationToken ct = default)
    {
        var command = new BsonDocument
        {
            { "collMod", collection },
            { "validator", rules.Validator },
            { "validationLevel", rules.Level },
            { "validationAction", rules.Action }
        };

        await _session.Client.GetDatabase(database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <summary>Removes validation entirely.</summary>
    public Task ClearAsync(string database, string collection, CancellationToken ct = default) =>
        SetAsync(database, collection, ValidationRules.Empty, ct);

    /// <summary>
    /// Sample documents that would fail the given rules, so the rules can be checked
    /// before they are applied.
    /// </summary>
    public async Task<IReadOnlyList<BsonDocument>> PreviewFailingAsync(
        string database,
        string collection,
        BsonDocument validator,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (validator.ElementCount == 0) return [];

        // $nor of the validator matches exactly the documents it would reject.
        var filter = new BsonDocument("$nor", new BsonArray { validator });

        var coll = _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

        using var cursor = await coll
            .FindAsync(filter, new FindOptions<BsonDocument> { Limit = limit }, ct)
            .ConfigureAwait(false);

        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Documents currently matching the rules, for the passing preview.</summary>
    public async Task<IReadOnlyList<BsonDocument>> PreviewPassingAsync(
        string database,
        string collection,
        BsonDocument validator,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (validator.ElementCount == 0) return [];

        var coll = _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

        using var cursor = await coll
            .FindAsync(validator, new FindOptions<BsonDocument> { Limit = limit }, ct)
            .ConfigureAwait(false);

        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }
}
