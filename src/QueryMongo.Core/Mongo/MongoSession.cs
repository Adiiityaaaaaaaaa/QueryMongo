using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Compression;
using MongoDB.Driver.Core.Configuration;
using QueryMongo.Core.Connections;

namespace QueryMongo.Core.Mongo;

/// <summary>
/// One live connection to a deployment.
///
/// A session owns exactly one <see cref="IMongoClient"/> and is shared for the
/// lifetime of the connection: the driver keeps its own connection pool and
/// topology monitor per client, so creating a client per query would be both slow
/// and a socket leak.
/// </summary>
public sealed class MongoSession : IDisposable
{
    private readonly IMongoClient _client;
    private readonly SshTunnel? _tunnel;
    private bool _disposed;

    private MongoSession(
        IMongoClient client, SshTunnel? tunnel, ConnectionProfile profile,
        string serverVersion, string topology)
    {
        _client = client;
        _tunnel = tunnel;
        Profile = profile;
        ServerVersion = serverVersion;
        Topology = topology;
    }

    /// <summary>True when traffic is going through an SSH bastion.</summary>
    public bool IsTunnelled => _tunnel is not null;

    public ConnectionProfile Profile { get; }
    public string ServerVersion { get; }
    public string Topology { get; }

    public IMongoClient Client => _disposed ? throw new ObjectDisposedException(nameof(MongoSession)) : _client;

    /// <summary>
    /// Opens a client and confirms the deployment answers before returning, so the
    /// caller never hands the UI a session that cannot be used.
    /// </summary>
    public static async Task<MongoSession> ConnectAsync(
        ConnectionProfile profile,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        SshTunnel? tunnel = null;
        var connectionString = profile.ConnectionString;

        if (profile.Ssh is { IsConfigured: true } ssh)
        {
            // The tunnel must be up before the driver resolves the host, so it is
            // opened first and torn down with the session.
            tunnel = SshTunnel.Open(ssh);
            connectionString = tunnel.Rewrite(connectionString);
        }

        var settings = BuildSettings(connectionString, timeout ?? TimeSpan.FromSeconds(10));
        var client = new MongoClient(settings);

        try
        {
            var hello = await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct)
                .ConfigureAwait(false);

            var buildInfo = await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: ct)
                .ConfigureAwait(false);

            return new MongoSession(
                client,
                tunnel,
                profile with { LastUsedUtc = DateTimeOffset.UtcNow },
                buildInfo.GetValue("version", BsonString.Empty).AsString,
                DescribeTopology(hello));
        }
        catch
        {
            client.Dispose();
            tunnel?.Dispose();
            throw;
        }
    }

    private static MongoClientSettings BuildSettings(string connectionString, TimeSpan timeout)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);

        settings.ApplicationName = "QueryMongo";

        // Fail fast: an unreachable host should surface an error in seconds, not
        // sit on the driver's 30s default while the UI shows a spinner.
        settings.ServerSelectionTimeout = timeout;
        settings.ConnectTimeout = timeout;

        // Wire compression cuts transfer size substantially on document-heavy reads.
        // The driver negotiates down to whatever the server supports.
        if (settings.Compressors is not { Count: > 0 })
        {
            settings.Compressors =
            [
                new CompressorConfiguration(CompressorType.ZStandard),
                new CompressorConfiguration(CompressorType.Snappy),
                new CompressorConfiguration(CompressorType.Zlib)
            ];
        }

        return settings;
    }

    private static string DescribeTopology(BsonDocument hello)
    {
        if (hello.Contains("setName")) return $"Replica set ({hello["setName"]})";
        if (hello.GetValue("msg", BsonString.Empty).AsString == "isdbgrid") return "Sharded cluster";
        return "Standalone";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _client.Dispose();
        _tunnel?.Dispose();
    }
}
