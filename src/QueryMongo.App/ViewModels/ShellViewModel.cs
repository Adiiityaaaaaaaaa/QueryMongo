using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;
using QueryMongo.Core.Shell;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// Owns every connection and every open workspace tab.
///
/// Several connections can be live at once, and a tab is not necessarily a collection:
/// the welcome screen, a server's database list, a shell and the performance charts are
/// all workspaces that sit in the same tab strip.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionStore _store;
    private readonly QueryHistoryStore _history;

    /// <summary>Every connection, before the sidebar filter is applied.</summary>
    private readonly List<ConnectionViewModel> _all = [];

    public ShellViewModel(IConnectionStore store, QueryHistoryStore history)
    {
        _store = store;
        _history = history;
        SidebarFilter = "";

        SavedQueries = new SavedQueriesViewModel(history);
    }

    /// <summary>Connections currently shown in the sidebar.</summary>
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTab))]
    public partial WorkspaceTabViewModel? ActiveTab { get; set; }

    public bool HasActiveTab => ActiveTab is not null;

    public SavedQueriesViewModel SavedQueries { get; }

    [ObservableProperty] public partial string SidebarFilter { get; set; }

    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public bool HasConnections => _all.Count > 0;

    /// <summary>Finds an open connection by id, or null once it has been forgotten.</summary>
    public ConnectionViewModel? FindConnection(Guid? id) =>
        id is { } value ? _all.FirstOrDefault(c => c.Id == value) : null;

    public int ConnectionCount => _all.Count;

    /// <summary>
    /// The count beside the CONNECTIONS heading. Blank rather than "(0)" when there are
    /// none, so an empty sidebar is not shouting a zero at you.
    /// </summary>
    public string ConnectionCountLabel => _all.Count == 0 ? "" : $"({_all.Count})";

    public int ActiveConnectionCount => _all.Count(c => c.IsConnected);

    // ---- loading ---------------------------------------------------------

    /// <summary>Loads saved connections into the sidebar. None are opened automatically.</summary>
    public async Task LoadConnectionsAsync()
    {
        foreach (var existing in _all) existing.Dispose();
        _all.Clear();

        foreach (var profile in await _store.LoadAsync().ConfigureAwait(true))
            _all.Add(Track(profile));

        ApplyFilter();
        NotifyConnectionCounts();

        // Compass opens on the welcome workspace rather than an empty window.
        if (Tabs.Count == 0) OpenWelcome();
    }

    private ConnectionViewModel Track(ConnectionProfile profile)
    {
        var connection = new ConnectionViewModel(profile, _store);

        // Closing a connection must not leave its tabs pointing at a dead session.
        connection.Disconnected += (_, _) => CloseTabsFor(connection);

        return connection;
    }

    private void NotifyConnectionCounts()
    {
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(ConnectionCount));
        OnPropertyChanged(nameof(ConnectionCountLabel));
        OnPropertyChanged(nameof(ActiveConnectionCount));
    }

    /// <summary>
    /// Adds a connection from the form and opens it. An existing profile with the same
    /// URI is reused rather than duplicated.
    /// </summary>
    public async Task<string?> AddConnectionAsync(
        string uri, string? name, SshOptions? ssh, string? colorCode = null)
    {
        if (string.IsNullOrWhiteSpace(uri)) return "A connection needs a URI.";

        var existing = _all.FirstOrDefault(c =>
            string.Equals(c.Profile.ConnectionString, uri.Trim(), StringComparison.Ordinal));

        var connection = existing;

        if (connection is null)
        {
            var profile = ConnectionProfile.Create(name ?? "", uri) with
            {
                Ssh = ssh,
                ColorCode = colorCode
            };

            await _store.SaveAsync(profile).ConfigureAwait(true);

            connection = Track(profile);
            _all.Add(connection);

            ApplyFilter();
            NotifyConnectionCounts();
        }

        await connection.ConnectAsync().ConfigureAwait(true);
        NotifyConnectionCounts();

        if (connection.State == ConnectionState.Failed) return connection.ErrorMessage;

        // Connecting lands on the server's database list, as it does in Compass.
        OpenDatabases(connection);
        return null;
    }

    [RelayCommand]
    private async Task ConnectAsync(ConnectionViewModel connection)
    {
        await connection.ConnectAsync().ConfigureAwait(true);
        NotifyConnectionCounts();

        if (connection.IsConnected) OpenDatabases(connection);
    }

    [RelayCommand]
    private void Disconnect(ConnectionViewModel connection)
    {
        connection.Disconnect();
        NotifyConnectionCounts();
    }

    [RelayCommand]
    private async Task ForgetConnectionAsync(ConnectionViewModel connection)
    {
        connection.Disconnect();

        await _store.DeleteAsync(connection.Id).ConfigureAwait(true);

        _all.Remove(connection);
        connection.Dispose();

        ApplyFilter();
        NotifyConnectionCounts();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ConnectionViewModel connection)
    {
        await UpdateProfileAsync(
            connection, connection.Profile with { IsFavorite = !connection.Profile.IsFavorite })
            .ConfigureAwait(true);
    }

    /// <summary>Renames a saved connection so a deployment is recognisable in the list.</summary>
    public async Task RenameConnectionAsync(ConnectionViewModel connection, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        await UpdateProfileAsync(connection, connection.Profile with { Name = name.Trim() })
            .ConfigureAwait(true);
    }

    /// <summary>Tags a connection with a colour, which tints its sidebar rows and tabs.</summary>
    public async Task SetConnectionColorAsync(ConnectionViewModel connection, string? colorCode)
    {
        await UpdateProfileAsync(connection, connection.Profile with { ColorCode = colorCode })
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Saves an edited profile in place. The connection object is kept, so its open
    /// session, expanded databases and tabs all survive an edit.
    /// </summary>
    private async Task UpdateProfileAsync(ConnectionViewModel connection, ConnectionProfile updated)
    {
        await _store.SaveAsync(updated).ConfigureAwait(true);

        connection.ApplyProfile(updated);
        ApplyFilter();
    }

    // ---- sidebar filter --------------------------------------------------

    partial void OnSidebarFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = SidebarFilter.Trim();

        Connections.Clear();

        // Favourites first, then alphabetically, which is the order Compass lists them in.
        foreach (var connection in _all
                     .OrderByDescending(c => c.Profile.IsFavorite)
                     .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            if (connection.ApplyFilter(term))
                Connections.Add(connection);
    }

    [RelayCommand]
    private void CollapseAllConnections()
    {
        foreach (var connection in _all) connection.Collapse();
    }

    // ---- opening workspaces ----------------------------------------------

    /// <summary>
    /// Puts a workspace on screen, following Compass's rules: an identical workspace that
    /// is already open is selected rather than duplicated; otherwise the active tab is
    /// replaced, unless it belongs to a different connection or has edits worth keeping,
    /// in which case a new tab opens next to it.
    /// </summary>
    private void Open(WorkspaceTabViewModel workspace, bool inNewTab = false)
    {
        workspace.Host = this;

        if (!inNewTab)
        {
            // The active tab gets first refusal, so re-opening what is already on screen
            // changes nothing at all.
            var candidates = ActiveTab is { } active
                ? new[] { active }.Concat(Tabs)
                : Tabs;

            if (candidates.FirstOrDefault(t => IsSameWorkspace(t, workspace)) is { } match)
            {
                workspace.Close();
                Activate(match);
                return;
            }
        }

        var replaceable =
            !inNewTab &&
            ActiveTab is { } current &&
            current.CanBeReplaced &&
            // Tabs are never replaced across connections: losing another server's tab
            // because you clicked something on this one would be surprising.
            !(current.ConnectionId is not null
              && workspace.ConnectionId is not null
              && current.ConnectionId != workspace.ConnectionId);

        if (replaceable && ActiveTab is { } toReplace)
        {
            var at = Tabs.IndexOf(toReplace);

            Tabs[at] = workspace;
            toReplace.Close();
        }
        else
        {
            // A new tab opens immediately after the active one rather than at the end.
            var at = ActiveTab is { } anchor ? Tabs.IndexOf(anchor) + 1 : Tabs.Count;
            Tabs.Insert(at, workspace);
        }

        Activate(workspace);
    }

    private void Activate(WorkspaceTabViewModel workspace)
    {
        // The strip draws the selected tab from the tab's own flag, so only one tab may
        // carry it at a time.
        foreach (var tab in Tabs) tab.IsSelected = ReferenceEquals(tab, workspace);

        ActiveTab = workspace;
        _ = workspace.EnsureActivatedAsync();
    }

    /// <summary>
    /// Whether two workspaces address the same thing. Identity is the connection plus
    /// whatever the workspace is scoped to, not the tab object.
    /// </summary>
    private static bool IsSameWorkspace(WorkspaceTabViewModel a, WorkspaceTabViewModel b)
    {
        if (a.Kind != b.Kind || a.ConnectionId != b.ConnectionId) return false;

        return (a, b) switch
        {
            (CollectionsWorkspaceViewModel x, CollectionsWorkspaceViewModel y) =>
                x.Database == y.Database,
            (CollectionTabViewModel x, CollectionTabViewModel y) =>
                x.Database == y.Database && x.Collection == y.Collection,
            _ => true
        };
    }

    [RelayCommand]
    public void OpenWelcome() => Open(new WelcomeWorkspaceViewModel());

    [RelayCommand]
    public void OpenMyQueries() => Open(new MyQueriesWorkspaceViewModel(SavedQueries));

    [RelayCommand]
    public void OpenDatabases(ConnectionViewModel connection)
    {
        if (connection.Session is not { } session) return;

        Open(new DatabasesWorkspaceViewModel(
            connection.Id, connection.Name, connection.ColorCode, new CatalogService(session)));
    }

    public void OpenCollections(ConnectionViewModel connection, string database, bool inNewTab = false)
    {
        if (connection.Session is not { } session) return;

        Open(
            new CollectionsWorkspaceViewModel(
                connection.Id, connection.Name, connection.ColorCode, database,
                new CatalogService(session)),
            inNewTab);
    }

    public void OpenCollection(
        ConnectionViewModel connection,
        string database,
        string collection,
        CollectionKind kind,
        bool inNewTab = false)
    {
        if (connection.Session is not { } session) return;

        Open(
            new CollectionTabViewModel(
                connection.Id, connection.Name, connection.ColorCode,
                database, collection, kind, session, _history),
            inNewTab);
    }

    [RelayCommand]
    public void OpenShell(ConnectionViewModel connection)
    {
        if (connection.Session is not { } session) return;

        Open(new ShellWorkspaceViewModel(
            connection.Id,
            connection.Name,
            connection.ColorCode,
            new MongoShellViewModel(new ShellService(session))));
    }

    [RelayCommand]
    public void OpenPerformance(ConnectionViewModel connection)
    {
        if (connection.Admin is not { } admin) return;

        Open(new PerformanceWorkspaceViewModel(
            connection.Id, connection.Name, connection.ColorCode, new PerformanceViewModel(admin)));
    }

    // ---- tab strip -------------------------------------------------------

    [RelayCommand]
    public void SelectTab(WorkspaceTabViewModel tab)
    {
        if (!Tabs.Contains(tab)) return;
        Activate(tab);
    }

    [RelayCommand]
    private void CloseTab(WorkspaceTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        Tabs.RemoveAt(index);
        tab.Close();

        if (!ReferenceEquals(ActiveTab, tab)) return;

        if (Tabs.Count == 0)
        {
            ActiveTab = null;
            return;
        }

        Activate(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    [RelayCommand]
    private void CloseOtherTabs(WorkspaceTabViewModel tab)
    {
        foreach (var other in Tabs.Where(t => !ReferenceEquals(t, tab)).ToList())
        {
            Tabs.Remove(other);
            other.Close();
        }

        Activate(tab);
    }

    /// <summary>Moves a tab in the strip, for drag-to-reorder.</summary>
    public void MoveTab(int from, int to)
    {
        if (from == to) return;
        if (from < 0 || from >= Tabs.Count) return;
        if (to < 0 || to >= Tabs.Count) return;

        Tabs.Move(from, to);
    }

    [RelayCommand]
    private void SelectNextTab() => Step(1);

    [RelayCommand]
    private void SelectPreviousTab() => Step(-1);

    private void Step(int delta)
    {
        if (Tabs.Count == 0 || ActiveTab is null) return;

        var index = Tabs.IndexOf(ActiveTab);
        if (index < 0) return;

        // Wraps, so ctrl-tab keeps cycling rather than stopping at the last tab.
        var next = (index + delta + Tabs.Count) % Tabs.Count;
        Activate(Tabs[next]);
    }

    private void CloseTabsFor(ConnectionViewModel connection)
    {
        foreach (var tab in Tabs.Where(t => t.ConnectionId == connection.Id).ToList())
            CloseTab(tab);

        NotifyConnectionCounts();
    }

    // ---- database and collection management ------------------------------

    public static async Task<string?> CreateDatabaseAsync(
        ConnectionViewModel connection, string database, string collection, NewCollectionOptions options)
    {
        if (connection.Admin is not { } admin) return "That connection is not open.";
        if (string.IsNullOrWhiteSpace(database)) return "A database needs a name.";
        if (string.IsNullOrWhiteSpace(collection)) return "A database needs at least one collection.";

        try
        {
            await admin.CreateDatabaseAsync(database.Trim(), collection.Trim(), options)
                .ConfigureAwait(true);
            await connection.RefreshDatabasesAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static async Task<string?> CreateCollectionAsync(
        ConnectionViewModel connection, string database, string collection, NewCollectionOptions options)
    {
        if (connection.Admin is not { } admin) return "That connection is not open.";
        if (string.IsNullOrWhiteSpace(collection)) return "A collection needs a name.";

        try
        {
            await admin.CreateCollectionAsync(database, collection.Trim(), options).ConfigureAwait(true);
            await connection.RefreshDatabaseAsync(database).ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> DropDatabaseAsync(ConnectionViewModel connection, string database)
    {
        if (connection.Admin is not { } admin) return "That connection is not open.";

        try
        {
            await admin.DropDatabaseAsync(database).ConfigureAwait(true);

            CloseTabsWhere(t => t.ConnectionId == connection.Id && DatabaseOf(t) == database);

            await connection.RefreshDatabasesAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> DropCollectionAsync(
        ConnectionViewModel connection, string database, string collection)
    {
        if (connection.Admin is not { } admin) return "That connection is not open.";

        try
        {
            await admin.DropCollectionAsync(database, collection).ConfigureAwait(true);

            CloseTabsWhere(t => t is CollectionTabViewModel tab
                                && tab.ConnectionId == connection.Id
                                && tab.Database == database
                                && tab.Collection == collection);

            await connection.RefreshDatabaseAsync(database).ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> RenameCollectionAsync(
        ConnectionViewModel connection, string database, string from, string to)
    {
        if (connection.Admin is not { } admin) return "That connection is not open.";
        if (string.IsNullOrWhiteSpace(to)) return "The new name cannot be empty.";

        try
        {
            await admin.RenameCollectionAsync(database, from, to.Trim()).ConfigureAwait(true);

            CloseTabsWhere(t => t is CollectionTabViewModel tab
                                && tab.ConnectionId == connection.Id
                                && tab.Database == database
                                && tab.Collection == from);

            await connection.RefreshDatabaseAsync(database).ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> ClearCollectionAsync(
        ConnectionViewModel connection, string database, string collection)
    {
        if (connection.Admin is not { } admin) return "That connection is not open.";

        try
        {
            await admin.DeleteAllDocumentsAsync(database, collection).ConfigureAwait(true);

            if (Tabs.OfType<CollectionTabViewModel>().FirstOrDefault(t =>
                    t.ConnectionId == connection.Id
                    && t.Database == database
                    && t.Collection == collection) is { } tab)
                await tab.Documents.RunQueryAsync().ConfigureAwait(true);

            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>The database a workspace is scoped to, or null when it is not scoped to one.</summary>
    private static string? DatabaseOf(WorkspaceTabViewModel tab) => tab switch
    {
        CollectionsWorkspaceViewModel collections => collections.Database,
        CollectionTabViewModel collection => collection.Database,
        _ => null
    };

    private void CloseTabsWhere(Func<WorkspaceTabViewModel, bool> predicate)
    {
        foreach (var tab in Tabs.Where(predicate).ToList()) CloseTab(tab);
    }

    public void Dispose()
    {
        foreach (var tab in Tabs) tab.Close();
        Tabs.Clear();

        foreach (var connection in _all) connection.Dispose();
        _all.Clear();
    }
}
