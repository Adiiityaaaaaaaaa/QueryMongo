using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The database list for one connection, the workspace Compass opens when a connection
/// itself is clicked: a sortable table of every database with its size and counts.
/// </summary>
public sealed partial class DatabasesWorkspaceViewModel : WorkspaceTabViewModel
{
    private readonly CatalogService _catalog;
    private readonly List<DatabaseRowViewModel> _all = [];

    public DatabasesWorkspaceViewModel(
        Guid connectionId, string connectionName, string? colorCode, CatalogService catalog)
        : base(connectionId, connectionName, colorCode)
    {
        _catalog = catalog;
        Filter = "";
    }

    public override WorkspaceKind Kind => WorkspaceKind.Databases;
    public override string IconGlyph => "Server";
    public override string Title => ConnectionName ?? "Databases";

    public override IReadOnlyList<(string Label, string Value)> Tooltip =>
        [("Connection", ConnectionName ?? "")];

    public ObservableCollection<DatabaseRowViewModel> Databases { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public bool IsEmpty => !IsLoading && Databases.Count == 0;

    protected override Task ActivateAsync() => RefreshAsync();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var databases = await _catalog.ListDatabasesWithStatsAsync().ConfigureAwait(true);

            _all.Clear();
            _all.AddRange(databases.Select(d => new DatabaseRowViewModel(d)));

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

    /// <summary>The trail above the table: just the connection, which is where we are.</summary>
    public IReadOnlyList<Controls.Crumb> Trail => [new Controls.Crumb(ConnectionName ?? "")];

    /// <summary>Opens a database's collection list, the way clicking its name does.</summary>
    public void OpenDatabase(string database)
    {
        if (Host is not { } host || Connection is not { } connection) return;

        host.OpenCollections(connection, database);
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

        Databases.Clear();

        foreach (var row in _all)
            if (term.Length == 0 || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                Databases.Add(row);

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>One row of the database table.</summary>
public sealed class DatabaseRowViewModel(DatabaseInfo info)
{
    public DatabaseInfo Info { get; } = info;

    public string Name => Info.Name;

    // A dash means "the server did not tell us", which is what Compass shows too. It is
    // deliberately not zero, because zero is a real and different answer.
    public string StorageSize => Info.StorageSizeBytes is { } b ? ByteSize.Format(b) : "–";
    public string DataSize => Info.DataSizeBytes is { } b ? ByteSize.Format(b) : "–";
    public string Collections => Info.CollectionCount is { } c ? ByteSize.CompactNumber(c) : "–";
    public string Indexes => Info.IndexCount is { } i ? ByteSize.CompactNumber(i) : "–";
}
