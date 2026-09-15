namespace QueryMongo.Core.Connections;

/// <summary>
/// A saved connection. <see cref="ConnectionString"/> is kept in memory only; the
/// store persists it encrypted (see <see cref="FileConnectionStore"/>) because it
/// usually carries credentials.
/// </summary>
public sealed record ConnectionProfile
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }
    public DateTimeOffset? LastUsedUtc { get; init; }
    public bool IsFavorite { get; init; }

    /// <summary>Set when the deployment is only reachable through a bastion host.</summary>
    public SshOptions? Ssh { get; init; }

    public bool UsesSsh => Ssh is { IsConfigured: true };

    public static ConnectionProfile Create(string name, string connectionString) => new()
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(name) ? DeriveName(connectionString) : name.Trim(),
        ConnectionString = connectionString.Trim()
    };

    /// <summary>
    /// Builds a display name from the host, the way Compass labels an unnamed connection.
    /// Never echoes credentials.
    /// </summary>
    public static string DeriveName(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "New connection";
        try
        {
            var url = new MongoDB.Driver.MongoUrl(connectionString);
            var host = url.Servers.FirstOrDefault()?.Host;
            return string.IsNullOrEmpty(host) ? "New connection" : host;
        }
        catch
        {
            return "New connection";
        }
    }

    /// <summary>The connection string with any password replaced, safe to log or display.</summary>
    public string RedactedConnectionString => Redact(ConnectionString);

    public static string Redact(string connectionString)
    {
        // mongodb://user:secret@host -> mongodb://user:***@host
        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return connectionString;

        var credStart = schemeEnd + 3;
        var at = connectionString.IndexOf('@', credStart);
        if (at < 0) return connectionString;

        var colon = connectionString.IndexOf(':', credStart);
        if (colon < 0 || colon > at) return connectionString;

        return string.Concat(connectionString.AsSpan(0, colon + 1), "***", connectionString.AsSpan(at));
    }
}
