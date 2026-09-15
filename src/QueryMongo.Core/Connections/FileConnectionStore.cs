using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryMongo.Core.Connections;

/// <summary>
/// Persists connections to <c>%LOCALAPPDATA%\QueryMongo\connections.json</c>.
///
/// Connection strings are encrypted with DPAPI scoped to the current user, so the
/// file is unreadable by other accounts on the machine and unusable if copied off it.
/// This matches what a native app can offer without asking for a master password;
/// it is not a substitute for a secrets manager on a shared machine.
/// </summary>
public sealed class FileConnectionStore : IConnectionStore, IDisposable
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("QueryMongo.v1.connections");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileConnectionStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QueryMongo",
            "connections.json");
    }

    private sealed record Entry(Guid Id, string Name, string Secret, DateTimeOffset? LastUsedUtc, bool IsFavorite);

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return (await ReadAsync(ct).ConfigureAwait(false))
                .Select(TryDecrypt)
                .OfType<ConnectionProfile>()
                .OrderByDescending(p => p.IsFavorite)
                .ThenByDescending(p => p.LastUsedUtc ?? DateTimeOffset.MinValue)
                .ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            entries.RemoveAll(e => e.Id == profile.Id);
            entries.Add(new Entry(
                profile.Id,
                profile.Name,
                Protect(profile.ConnectionString),
                profile.LastUsedUtc,
                profile.IsFavorite));
            await WriteAsync(entries, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(ct).ConfigureAwait(false);
            if (entries.RemoveAll(e => e.Id == id) > 0)
                await WriteAsync(entries, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<Entry>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<Entry>>(stream, JsonOptions, ct)
                       .ConfigureAwait(false) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt file must not block the app from starting.
            return [];
        }
    }

    private async Task WriteAsync(List<Entry> entries, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Write to a temp file and move, so a crash mid-write cannot truncate the list.
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, ct).ConfigureAwait(false);

        File.Move(temp, _path, overwrite: true);
    }

    public void Dispose() => _gate.Dispose();

    private static string Protect(string plaintext) =>
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser));

    private static ConnectionProfile? TryDecrypt(Entry entry)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(entry.Secret), Entropy, DataProtectionScope.CurrentUser);

            return new ConnectionProfile
            {
                Id = entry.Id,
                Name = entry.Name,
                ConnectionString = Encoding.UTF8.GetString(bytes),
                LastUsedUtc = entry.LastUsedUtc,
                IsFavorite = entry.IsFavorite
            };
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Written by a different user or machine — drop it rather than fail the load.
            return null;
        }
    }
}
