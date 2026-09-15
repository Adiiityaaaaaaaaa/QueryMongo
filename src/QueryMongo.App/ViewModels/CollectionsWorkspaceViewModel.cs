using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The collection list for one database: the workspace Compass opens when a database is
/// clicked, showing each collection's badges, sizes and counts.
/// </summary>
public sealed partial class CollectionsWorkspaceViewModel : WorkspaceTabViewModel
{
    private readonly CatalogService _catalog;
    private readonly List<CollectionRowViewModel> _all = [];

    public CollectionsWorkspaceViewModel(
        Guid connectionId,
        string connectionName,
        string? colorCode,
        string database,
        CatalogService catalog)
        : base(connectionId, connectionName, colorCode)
    {
        Database = database;
        _catalog = catalog;
        Filter = "";
    }

    public string Database { get; }

    public override WorkspaceKind Kind => WorkspaceKind.Collections;
    public override string IconGlyph => "Database";
    public override string Title => Database;

    public override IReadOnlyList<(string Label, string Value)> Tooltip =>
        [("Connection", ConnectionName ?? ""), ("Database", Database)];

    public ObservableCollection<CollectionRowViewModel> Collections { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public bool IsEmpty => !IsLoading && Collections.Count == 0;

    protected override Task ActivateAsync() => RefreshAsync();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var collections = await _catalog.ListCollectionsAsync(Database).ConfigureAwait(true);

            _all.Clear();
            _all.AddRange(collections.Select(c => new CollectionRowViewModel(c)));

            ApplyFilter();
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Connection, then the database we are looking at.</summary>
    public IReadOnlyList<Controls.Crumb> Trail =>
    [
        new Controls.Crumb(ConnectionName ?? "", OpenDatabases),
        new Controls.Crumb(Database)
    ];

    private void OpenDatabases()
    {
        if (Host is not { } host || Connection is not { } connection) return;

        host.OpenDatabasesCommand.Execute(connection);
    }

    /// <summary>Opens a collection on a tab, the way clicking its name does.</summary>
    public void OpenCollection(CollectionRowViewModel row)
    {
        if (Host is not { } host || Connection is not { } connection) return;

        host.OpenCollection(connection, Database, row.Name, row.Kind);
    }

    public void OpenShell()
    {
        if (Host is not { } host || Connection is not { } connection) return;

        host.OpenShellCommand.Execute(connection);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = Filter.Trim();

        Collections.Clear();

        foreach (var row in _all)
            if (term.Length == 0 || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                Collections.Add(row);

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>One row of the collection table.</summary>
public sealed class CollectionRowViewModel(CollectionInfo info)
{
    public CollectionInfo Info { get; } = info;

    public string Name => Info.Name;
    public string Database => Info.Database;
    public CollectionKind Kind => Info.Kind;

    public IReadOnlyList<CollectionBadgeViewModel> Badges { get; } =
        [.. info.Properties.Select(p => new CollectionBadgeViewModel(p, info))];

    public bool HasBadges => Badges.Count > 0;

    // Views own no storage, so their size columns stay blank rather than reading zero.
    public string StorageSize => Cell(Info.HasStorage ? Info.StorageSizeBytes : null);
    public string DataSize => Cell(Info.HasStorage ? Info.DataSizeBytes : null);
    public string IndexSize => Cell(Info.HasStorage ? Info.TotalIndexSizeBytes : null);

    public string AverageDocumentSize => Info is { HasStorage: true, AverageDocumentSizeBytes: { } avg }
        && Kind != CollectionKind.TimeSeries
            ? ByteSize.Format((long)avg)
            : "–";

    public string Documents => Info is { HasStorage: true, DocumentCount: { } count }
        && Kind != CollectionKind.TimeSeries
            ? ByteSize.CompactNumber(count)
            : "–";

    public string Indexes => Info is { HasStorage: true, IndexCount: { } indexes }
        ? ByteSize.CompactNumber(indexes)
        : "–";

    /// <summary>
    /// The storage tooltip splits total, used and free, but only when the server reported
    /// free space; otherwise there is nothing to split by and showing zeroes would lie.
    /// </summary>
    public string? StorageTooltip => Info is { StorageSizeBytes: { } total, FreeStorageSizeBytes: { } free }
        ? $"Storage Size: {ByteSize.Format(total)} (total allocated)\n" +
          $"Used: {ByteSize.Format(total - free)}\n" +
          $"Free: {ByteSize.Format(free)}"
        : null;

    private static string Cell(long? bytes) => bytes is { } b ? ByteSize.Format(b) : "–";
}

/// <summary>A property badge on a collection row.</summary>
public sealed class CollectionBadgeViewModel
{
    public CollectionBadgeViewModel(string property, CollectionInfo info)
    {
        Property = property;

        (Label, Glyph) = property switch
        {
            "view" => ("view", "Visibility"),
            "timeseries" => ("timeseries", "TimeSeries"),
            "fle2" => ("Queryable Encryption", "Key"),
            _ => (property, null)
        };

        Hint = property == "view" && info.ViewOn is { } source ? $"Derived from {source}" : null;
    }

    public string Property { get; }
    public string Label { get; }
    public string? Glyph { get; }
    public string? Hint { get; }

    public bool HasGlyph => Glyph is not null;
}
