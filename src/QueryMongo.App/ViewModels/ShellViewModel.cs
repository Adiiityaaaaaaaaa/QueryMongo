using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// Owns the connection, the database tree and the open collection tabs. Everything
/// the window shows hangs off this one object.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionStore _store;
    private readonly QueryHistoryStore _history;

    private MongoSession? _session;
    private AdminService? _admin;
    private CatalogService? _catalog;

    /// <summary>All databases, before the sidebar filter is applied.</summary>
    private readonly List<DatabaseNodeViewModel> _allDatabases = [];

    public ShellViewModel(IConnectionStore store, QueryHistoryStore history)
    {
        _store = store;
        _history = history;

        ConnectionString = "mongodb://localhost:27017";
        ServerDescription = "";
        SidebarFilter = "";
    }

    // ---- connection ------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string ConnectionString { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string ServerDescription { get; set; }

    /// <summary>Host name of the open connection, shown in the sidebar header.</summary>
    [ObservableProperty] public partial string ConnectionName { get; set; } = "Not connected";

    public bool IsDisconnected => !IsConnected;

    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];

    // ---- sidebar ---------------------------------------------------------

    public ObservableCollection<DatabaseNodeViewModel> Databases { get; } = [];

    [ObservableProperty] public partial string SidebarFilter { get; set; }

    /// <summary>
    /// SSH settings from the connect form, applied to the next connection. The form
    /// owns the fields; the shell only carries them into the profile.
    /// </summary>
    public SshOptions? PendingSsh { get; set; }

    // ---- tabs ------------------------------------------------------------

    public ObservableCollection<CollectionTabViewModel> Tabs { get; } = [];

    [ObservableProperty] public partial CollectionTabViewModel? ActiveTab { get; set; }

    [ObservableProperty] public partial PerformanceViewModel? Performance { get; set; }

    [ObservableProperty] public partial bool IsPerformanceOpen { get; set; }

    // ---- connecting ------------------------------------------------------

    public async Task LoadSavedConnectionsAsync()
    {
        SavedConnections.Clear();
        foreach (var profile in await _store.LoadAsync().ConfigureAwait(true))
            SavedConnections.Add(profile);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var profile = ConnectionProfile.Create(name: "", ConnectionString) with { Ssh = PendingSsh };
            var session = await MongoSession.ConnectAsync(profile).ConfigureAwait(true);

            _session?.Dispose();
            _session = session;
            _admin = new AdminService(session);
            _catalog = new CatalogService(session);

            Performance = new PerformanceViewModel(_admin);

            ServerDescription = $"MongoDB {session.ServerVersion} · {session.Topology}"
                                + (session.IsTunnelled ? " · via SSH" : "");
            ConnectionName = session.Profile.Name;
            IsConnected = true;

            await _store.SaveAsync(session.Profile).ConfigureAwait(true);
            await LoadSavedConnectionsAsync().ConfigureAwait(true);
            await RefreshDatabasesAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            // The driver's own message already distinguishes auth from network from TLS.
            ErrorMessage = e.Message;
            IsConnected = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        Performance?.Dispose();
        Performance = null;

        _session?.Dispose();
        _session = null;
        _admin = null;
        _catalog = null;

        Tabs.Clear();
        ActiveTab = null;
        _allDatabases.Clear();
        Databases.Clear();
        ServerDescription = "";
        ConnectionName = "Not connected";
        IsConnected = false;
    }

    [RelayCommand]
    public async Task RefreshDatabasesAsync()
    {
        if (_catalog is null) return;

        IsBusy = true;
        try
        {
            var databases = await _catalog.ListDatabasesAsync().ConfigureAwait(true);

            _allDatabases.Clear();
            foreach (var db in databases)
                _allDatabases.Add(new DatabaseNodeViewModel(db, _catalog));

            ApplySidebarFilter();
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSidebarFilterChanged(string value) => ApplySidebarFilter();

    /// <summary>
    /// Filters the sidebar by database or collection name. A database stays visible
    /// when one of its already-loaded collections matches, and auto-expands to show it.
    /// </summary>
    private void ApplySidebarFilter()
    {
        Databases.Clear();

        var term = SidebarFilter.Trim();

        foreach (var db in _allDatabases)
        {
            if (term.Length == 0)
            {
                db.ClearCollectionFilter();
                Databases.Add(db);
                continue;
            }

            var nameMatches = db.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            var childMatches = db.ApplyCollectionFilter(term);

            if (nameMatches || childMatches)
            {
                if (childMatches) db.IsExpanded = true;
                Databases.Add(db);
            }
        }
    }

    // ---- tabs ------------------------------------------------------------

    /// <summary>
    /// Opens a collection. An already-open collection is focused rather than opened
    /// twice, which is how Compass behaves.
    /// </summary>
    public async Task OpenCollectionAsync(string database, string collection, CollectionKind kind)
    {
        if (_session is null) return;

        var existing = Tabs.FirstOrDefault(t => t.Database == database && t.Collection == collection);
        if (existing is not null)
        {
            ActiveTab = existing;
            IsPerformanceOpen = false;
            return;
        }

        var tab = new CollectionTabViewModel(database, collection, kind, _session, _history);
        Tabs.Add(tab);
        ActiveTab = tab;
        IsPerformanceOpen = false;

        await tab.InitializeAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CloseTab(CollectionTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (ReferenceEquals(ActiveTab, tab))
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    [RelayCommand]
    private void CloseAllTabs()
    {
        Tabs.Clear();
        ActiveTab = null;
    }

    [RelayCommand]
    private void ShowPerformance()
    {
        IsPerformanceOpen = true;
        Performance?.SampleCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowCollections() => IsPerformanceOpen = false;

    // ---- database and collection management ------------------------------

    /// <summary>Creates a database by creating its first collection. Returns an error, or null.</summary>
    public async Task<string?> CreateDatabaseAsync(
        string database, string collection, NewCollectionOptions options)
    {
        if (_admin is null) return "Not connected.";
        if (string.IsNullOrWhiteSpace(database)) return "A database needs a name.";
        if (string.IsNullOrWhiteSpace(collection)) return "A database needs at least one collection.";

        try
        {
            await _admin.CreateDatabaseAsync(database.Trim(), collection.Trim(), options)
                .ConfigureAwait(true);
            await RefreshDatabasesAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> CreateCollectionAsync(
        string database, string collection, NewCollectionOptions options)
    {
        if (_admin is null) return "Not connected.";
        if (string.IsNullOrWhiteSpace(collection)) return "A collection needs a name.";

        try
        {
            await _admin.CreateCollectionAsync(database, collection.Trim(), options).ConfigureAwait(true);
            await RefreshNodeAsync(database).ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> DropDatabaseAsync(string database)
    {
        if (_admin is null) return "Not connected.";

        try
        {
            await _admin.DropDatabaseAsync(database).ConfigureAwait(true);

            foreach (var tab in Tabs.Where(t => t.Database == database).ToList())
                CloseTab(tab);

            await RefreshDatabasesAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> DropCollectionAsync(string database, string collection)
    {
        if (_admin is null) return "Not connected.";

        try
        {
            await _admin.DropCollectionAsync(database, collection).ConfigureAwait(true);

            if (Tabs.FirstOrDefault(t => t.Database == database && t.Collection == collection) is { } tab)
                CloseTab(tab);

            await RefreshNodeAsync(database).ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> RenameCollectionAsync(string database, string from, string to)
    {
        if (_admin is null) return "Not connected.";
        if (string.IsNullOrWhiteSpace(to)) return "The new name cannot be empty.";

        try
        {
            await _admin.RenameCollectionAsync(database, from, to.Trim()).ConfigureAwait(true);

            if (Tabs.FirstOrDefault(t => t.Database == database && t.Collection == from) is { } tab)
                CloseTab(tab);

            await RefreshNodeAsync(database).ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Empties a collection, keeping its indexes and validation rules.</summary>
    public async Task<string?> ClearCollectionAsync(string database, string collection)
    {
        if (_admin is null) return "Not connected.";

        try
        {
            var deleted = await _admin.DeleteAllDocumentsAsync(database, collection).ConfigureAwait(true);

            if (Tabs.FirstOrDefault(t => t.Database == database && t.Collection == collection) is { } tab)
                await tab.Documents.RunQueryAsync().ConfigureAwait(true);

            ErrorMessage = null;
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private async Task RefreshNodeAsync(string database)
    {
        var node = _allDatabases.FirstOrDefault(d => d.Name == database);
        if (node is null)
        {
            await RefreshDatabasesAsync().ConfigureAwait(true);
            return;
        }

        await node.ReloadAsync().ConfigureAwait(true);
        ApplySidebarFilter();
    }

    // ---- saved connections ----------------------------------------------

    public void UseSavedConnection(ConnectionProfile profile) => ConnectionString = profile.ConnectionString;

    [RelayCommand]
    private async Task DeleteSavedConnectionAsync(ConnectionProfile profile)
    {
        await _store.DeleteAsync(profile.Id).ConfigureAwait(true);
        await LoadSavedConnectionsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ConnectionProfile profile)
    {
        await _store.SaveAsync(profile with { IsFavorite = !profile.IsFavorite }).ConfigureAwait(true);
        await LoadSavedConnectionsAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        Performance?.Dispose();
        _session?.Dispose();
        _session = null;
    }
}
