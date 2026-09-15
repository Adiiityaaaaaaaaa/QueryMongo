using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>Options offered when creating a collection.</summary>
public sealed record NewCollectionOptions
{
    public bool Capped { get; init; }
    public long? MaxSizeBytes { get; init; }
    public long? MaxDocuments { get; init; }

    /// <summary>Set to make a time-series collection; names the time field.</summary>
    public string? TimeField { get; init; }

    public string? MetaField { get; init; }
    public string? CollationLocale { get; init; }
}

/// <summary>A point-in-time sample of server counters, for the Performance tab.</summary>
public sealed record ServerMetrics(
    DateTimeOffset Taken,
    long Insert, long Query, long Update, long Delete, long GetMore, long Command,
    int CurrentConnections, int AvailableConnections,
    long NetworkInBytes, long NetworkOutBytes,
    long ResidentMemoryMb, long VirtualMemoryMb,
    TimeSpan Uptime)
{
    public long TotalOperations => Insert + Query + Update + Delete + GetMore + Command;
}

/// <summary>Per-second rates between two <see cref="ServerMetrics"/> samples.</summary>
public sealed record ServerRates(
    double Insert, double Query, double Update, double Delete, double Command,
    double NetworkInPerSecond, double NetworkOutPerSecond)
{
    public static ServerRates Between(ServerMetrics previous, ServerMetrics current)
    {
        var seconds = (current.Taken - previous.Taken).TotalSeconds;
        if (seconds <= 0) return Zero;

        double Rate(long a, long b) => Math.Max(0, b - a) / seconds;

        return new ServerRates(
            Rate(previous.Insert, current.Insert),
            Rate(previous.Query, current.Query),
            Rate(previous.Update, current.Update),
            Rate(previous.Delete, current.Delete),
            Rate(previous.Command, current.Command),
            Rate(previous.NetworkInBytes, current.NetworkInBytes),
            Rate(previous.NetworkOutBytes, current.NetworkOutBytes));
    }

    public static ServerRates Zero => new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>A running operation, as shown by Compass's "Current operations" list.</summary>
public sealed record CurrentOperation(
    long OperationId, string Namespace, string Type, int SecondsRunning, string Description);

/// <summary>
/// Deployment-level actions: creating and dropping databases and collections, and
/// reading server health.
/// </summary>
public sealed class AdminService(MongoSession session)
{
    private readonly MongoSession _session = session;

    /// <summary>
    /// MongoDB has no "create database" command: a database exists once it holds a
    /// collection, so this creates the first collection in it.
    /// </summary>
    public Task CreateDatabaseAsync(
        string database, string firstCollection, NewCollectionOptions? options = null,
        CancellationToken ct = default) =>
        CreateCollectionAsync(database, firstCollection, options, ct);

    public Task DropDatabaseAsync(string database, CancellationToken ct = default) =>
        _session.Client.DropDatabaseAsync(database, ct);

    public async Task CreateCollectionAsync(
        string database, string collection, NewCollectionOptions? options = null,
        CancellationToken ct = default)
    {
        var settings = new CreateCollectionOptions<BsonDocument>();

        if (options is not null)
        {
            if (options.Capped)
            {
                settings.Capped = true;
                settings.MaxSize = options.MaxSizeBytes;
                settings.MaxDocuments = options.MaxDocuments;
            }

            if (!string.IsNullOrWhiteSpace(options.CollationLocale))
                settings.Collation = new Collation(options.CollationLocale);

            if (!string.IsNullOrWhiteSpace(options.TimeField))
            {
                settings.TimeSeriesOptions = new TimeSeriesOptions(
                    timeField: options.TimeField,
                    metaField: string.IsNullOrWhiteSpace(options.MetaField) ? null : options.MetaField);
            }
        }

        await _session.Client.GetDatabase(database)
            .CreateCollectionAsync(collection, settings, ct)
            .ConfigureAwait(false);
    }

    public Task DropCollectionAsync(string database, string collection, CancellationToken ct = default) =>
        _session.Client.GetDatabase(database).DropCollectionAsync(collection, ct);

    public async Task RenameCollectionAsync(
        string database, string from, string to, CancellationToken ct = default) =>
        await _session.Client.GetDatabase(database)
            .RenameCollectionAsync(from, to, cancellationToken: ct)
            .ConfigureAwait(false);

    /// <summary>Empties a collection without dropping its indexes or validation rules.</summary>
    public async Task<long> DeleteAllDocumentsAsync(
        string database, string collection, CancellationToken ct = default)
    {
        var result = await _session.Client.GetDatabase(database)
            .GetCollection<BsonDocument>(collection)
            .DeleteManyAsync(new BsonDocument(), ct)
            .ConfigureAwait(false);

        return result.DeletedCount;
    }

    public async Task<ServerMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var status = await _session.Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("serverStatus", 1), cancellationToken: ct)
            .ConfigureAwait(false);

        var opcounters = status.GetValue("opcounters", new BsonDocument()).AsBsonDocument;
        var connections = status.GetValue("connections", new BsonDocument()).AsBsonDocument;
        var network = status.GetValue("network", new BsonDocument()).AsBsonDocument;
        var mem = status.GetValue("mem", new BsonDocument()).AsBsonDocument;

        long Counter(BsonDocument d, string name) => d.GetValue(name, BsonInt64.Create(0L)).ToInt64();

        return new ServerMetrics(
            Taken: DateTimeOffset.UtcNow,
            Insert: Counter(opcounters, "insert"),
            Query: Counter(opcounters, "query"),
            Update: Counter(opcounters, "update"),
            Delete: Counter(opcounters, "delete"),
            GetMore: Counter(opcounters, "getmore"),
            Command: Counter(opcounters, "command"),
            CurrentConnections: (int)Counter(connections, "current"),
            AvailableConnections: (int)Counter(connections, "available"),
            NetworkInBytes: Counter(network, "bytesIn"),
            NetworkOutBytes: Counter(network, "bytesOut"),
            ResidentMemoryMb: Counter(mem, "resident"),
            VirtualMemoryMb: Counter(mem, "virtual"),
            Uptime: TimeSpan.FromSeconds(status.GetValue("uptime", BsonDouble.Create(0.0)).ToDouble()));
    }

    /// <summary>Operations the server is running right now, slowest first.</summary>
    public async Task<IReadOnlyList<CurrentOperation>> GetCurrentOperationsAsync(
        bool includeIdle = false, CancellationToken ct = default)
    {
        var command = new BsonDocument
        {
            { "currentOp", 1 },
            { "$all", includeIdle }
        };

        try
        {
            var result = await _session.Client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(command, cancellationToken: ct)
                .ConfigureAwait(false);

            if (!result.Contains("inprog")) return [];

            return result["inprog"].AsBsonArray
                .OfType<BsonDocument>()
                .Select(op => new CurrentOperation(
                    op.GetValue("opid", BsonInt64.Create(0L)).ToInt64(),
                    op.GetValue("ns", BsonString.Empty).AsString,
                    op.GetValue("op", BsonString.Empty).AsString,
                    op.GetValue("secs_running", BsonInt32.Create(0)).ToInt32(),
                    op.GetValue("desc", BsonString.Empty).AsString))
                .OrderByDescending(o => o.SecondsRunning)
                .ToList();
        }
        catch (MongoCommandException)
        {
            // Requires cluster-wide privileges the user may not hold.
            return [];
        }
    }

    /// <summary>Kills a long-running operation by id.</summary>
    public async Task KillOperationAsync(long operationId, CancellationToken ct = default)
    {
        var command = new BsonDocument { { "killOp", 1 }, { "op", operationId } };

        await _session.Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(command, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}
