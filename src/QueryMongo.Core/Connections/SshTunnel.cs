using Renci.SshNet;

namespace QueryMongo.Core.Connections;

/// <summary>How to reach a deployment through a bastion host.</summary>
public sealed record SshOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 22;
    public required string Username { get; init; }

    /// <summary>Used when no <see cref="PrivateKeyPath"/> is given.</summary>
    public string? Password { get; init; }

    public string? PrivateKeyPath { get; init; }
    public string? PrivateKeyPassphrase { get; init; }

    /// <summary>The MongoDB host as seen from the bastion, not from this machine.</summary>
    public string RemoteHost { get; init; } = "127.0.0.1";
    public int RemotePort { get; init; } = 27017;

    /// <summary>0 lets the OS pick a free local port, which avoids collisions.</summary>
    public int LocalPort { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username);
}

/// <summary>
/// An SSH local port forward held open for the life of a connection.
///
/// The driver connects to <see cref="LocalEndpoint"/> on this machine, and the bastion
/// forwards to the real deployment. Disposing closes the forward and the SSH session,
/// so the tunnel's lifetime is tied to the connection that opened it.
/// </summary>
public sealed class SshTunnel : IDisposable
{
    private readonly SshClient _client;
    private readonly ForwardedPortLocal _port;
    private bool _disposed;

    private SshTunnel(SshClient client, ForwardedPortLocal port)
    {
        _client = client;
        _port = port;
        LocalEndpoint = $"{port.BoundHost}:{port.BoundPort}";
    }

    /// <summary>Host and port the driver should connect to on this machine.</summary>
    public string LocalEndpoint { get; }

    public static SshTunnel Open(SshOptions options)
    {
        var connection = BuildConnectionInfo(options);
        var client = new SshClient(connection);

        try
        {
            client.Connect();

            // Binding to loopback keeps the forwarded port off the network.
            var port = new ForwardedPortLocal(
                "127.0.0.1", (uint)options.LocalPort,
                options.RemoteHost, (uint)options.RemotePort);

            client.AddForwardedPort(port);
            port.Start();

            return new SshTunnel(client, port);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static ConnectionInfo BuildConnectionInfo(SshOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            if (!File.Exists(options.PrivateKeyPath))
                throw new FileNotFoundException("Private key not found.", options.PrivateKeyPath);

            var key = string.IsNullOrEmpty(options.PrivateKeyPassphrase)
                ? new PrivateKeyFile(options.PrivateKeyPath)
                : new PrivateKeyFile(options.PrivateKeyPath, options.PrivateKeyPassphrase);

            return new ConnectionInfo(
                options.Host, options.Port, options.Username,
                new PrivateKeyAuthenticationMethod(options.Username, key));
        }

        if (string.IsNullOrEmpty(options.Password))
            throw new InvalidOperationException("SSH needs either a password or a private key.");

        return new ConnectionInfo(
            options.Host, options.Port, options.Username,
            new PasswordAuthenticationMethod(options.Username, options.Password));
    }

    /// <summary>
    /// Rewrites a connection string to point at the local end of the tunnel, keeping
    /// credentials, options and the database intact.
    /// </summary>
    public string Rewrite(string connectionString)
    {
        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return $"mongodb://{LocalEndpoint}";

        var afterScheme = schemeEnd + 3;

        // Keep any user:password@ prefix.
        var at = connectionString.IndexOf('@', afterScheme);
        var credentials = at >= 0 ? connectionString[afterScheme..(at + 1)] : "";
        var hostStart = at >= 0 ? at + 1 : afterScheme;

        // The host list ends at the first '/' or '?'.
        var hostEnd = connectionString.IndexOfAny(['/', '?'], hostStart);
        var tail = hostEnd >= 0 ? connectionString[hostEnd..] : "";

        // An SRV URI resolves to a host list, which a fixed tunnel cannot represent.
        var scheme = connectionString[..afterScheme].Replace("mongodb+srv://", "mongodb://");

        return $"{scheme}{credentials}{LocalEndpoint}{tail}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_port.IsStarted) _port.Stop();
            _port.Dispose();
        }
        catch (Exception)
        {
            // The tunnel is going away regardless; a failure to stop it cleanly is
            // not worth propagating during teardown.
        }

        _client.Dispose();
    }
}
