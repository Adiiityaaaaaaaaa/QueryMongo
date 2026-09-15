using System.Text.Json;
using System.Text.Json.Serialization;
using QueryMongo.Core.Models;

namespace QueryMongo.Core.Connections;

/// <summary>A query the user ran or saved, as listed in Compass's query bar history.</summary>
public sealed record SavedQuery
{
    public required Guid Id { get; init; }
    public required string Namespace { get; init; }
    public required QuerySpec Spec { get; init; }
    public required DateTimeOffset LastUsedUtc { get; init; }

    /// <summary>Set when the user named and saved it; unnamed entries are history.</summary>
    public string? Name { get; init; }

    public bool IsFavorite => Name is not null;

    /// <summary>One-line summary for the history dropdown.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { Spec.Filter };
            if (!string.IsNullOrWhiteSpace(Spec.Sort)) parts.Add($"sort {Spec.Sort}");
            if (!string.IsNullOrWhiteSpace(Spec.Projection)) parts.Add($"project {Spec.Projection}");

            var text = string.Join("  ·  ", parts);
            return text.Length <= 110 ? text : string.Concat(text.AsSpan(0, 110), "…");
        }
    }

    public static SavedQuery FromSpec(string ns, QuerySpec spec, string? name = null) => new()
    {
        Id = Guid.NewGuid(),
        Namespace = ns,
        Spec = spec,
        LastUsedUtc = DateTimeOffset.UtcNow,
        Name = name
    };
}

/// <summary>
/// Recent and saved queries, kept in
/// <c>%LOCALAPPDATA%\QueryMongo\queries.json</c>.
///
/// Unlike connection strings these hold no credentials, so they are stored as plain
/// JSON — readable and editable by hand, which is useful for sharing a query.
/// </summary>
public sealed class QueryHistoryStore : IDisposable
{
    /// <summary>History is capped so the file cannot grow without bound. Favorites never expire.</summary>
    private const int MaxHistoryPerNamespace = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QueryHistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QueryMongo", "queries.json");
    }

    public async Task<IReadOnlyList<SavedQuery>> LoadAsync(string? ns = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);

            return all
                .Where(q => ns is null || q.Namespace == ns)
                .OrderByDescending(q => q.IsFavorite)
                .ThenByDescending(q => q.LastUsedUtc)
                .ToList();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Records a run. An identical filter/sort/projection replaces its earlier entry
    /// rather than stacking duplicates, which is what makes the history usable.
    /// </summary>
    public async Task RecordAsync(string ns, QuerySpec spec, CancellationToken ct = default)
    {
        // An empty query is not worth remembering.
        if (IsBlank(spec)) return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);

            all.RemoveAll(q => !q.IsFavorite && q.Namespace == ns && SameShape(q.Spec, spec));
            all.Add(SavedQuery.FromSpec(ns, spec));

            Trim(all, ns);
            await WriteAsync(all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveFavoriteAsync(string ns, QuerySpec spec, string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            all.Add(SavedQuery.FromSpec(ns, spec, name.Trim()));
            await WriteAsync(all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            if (all.RemoveAll(q => q.Id == id) > 0) await WriteAsync(all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearHistoryAsync(string? ns = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            all.RemoveAll(q => !q.IsFavorite && (ns is null || q.Namespace == ns));
            await WriteAsync(all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();

    private static bool IsBlank(QuerySpec spec) =>
        (string.IsNullOrWhiteSpace(spec.Filter) || spec.Filter.Trim() is "{}" )
        && string.IsNullOrWhiteSpace(spec.Sort)
        && string.IsNullOrWhiteSpace(spec.Projection);

    private static bool SameShape(QuerySpec a, QuerySpec b) =>
        Normalize(a.Filter) == Normalize(b.Filter)
        && Normalize(a.Sort) == Normalize(b.Sort)
        && Normalize(a.Projection) == Normalize(b.Projection);

    /// <summary>Whitespace differences should not create a second history entry.</summary>
    private static string Normalize(string? text) =>
        string.Concat((text ?? "").Where(c => !char.IsWhiteSpace(c)));

    private static void Trim(List<SavedQuery> all, string ns)
    {
        var history = all
            .Where(q => !q.IsFavorite && q.Namespace == ns)
            .OrderByDescending(q => q.LastUsedUtc)
            .Skip(MaxHistoryPerNamespace)
            .Select(q => q.Id)
            .ToHashSet();

        if (history.Count > 0) all.RemoveAll(q => history.Contains(q.Id));
    }

    private async Task<List<SavedQuery>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return [];

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<SavedQuery>>(stream, JsonOptions, ct)
                       .ConfigureAwait(false) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAsync(List<SavedQuery> all, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, all, JsonOptions, ct).ConfigureAwait(false);

        File.Move(temp, _path, overwrite: true);
    }
}
