using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Services;
using QueryMongo.Core.Mongo;

namespace QueryMongo.App.ViewModels;

public enum ConnectionState { Disconnected, Connecting, Connected, Failed }

/// <summary>
/// One connection in the sidebar. Several can be live at once, each with its own
/// session, databases and services, so collections from different deployments can be
/// open side by side.
/// </summary>
public sealed partial class ConnectionViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionStore _store;

    /// <summary>All databases, before the sidebar filter.</summary>
    private readonly List<DatabaseNodeViewModel> _allDatabases = [];

    private MongoSession? _session;
    private string _filter = "";

    public ConnectionViewModel(ConnectionProfile profile, IConnectionStore store)
    {
        Profile = profile;
        _store = store;
        Name = profile.Name;
    }

    public ConnectionProfile Profile { get; private set; }

    /// <summary>Stable across reconnects, so open tabs keep pointing at this connection.</summary>
    public Guid Id => Profile.Id;

    public MongoSession? Session => _session;

    public AdminService? Admin { get; private set; }

    private CatalogService? _catalog;

    [ObservableProperty] public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsBusy), nameof(StatusMarker),
        nameof(CanExpand))]
    public partial ConnectionState State { get; set; }

    [ObservableProperty] public partial string Detail { get; set; } = "";

    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaretGlyph))]
    public partial bool IsExpanded { get; set; }

    /// <summary>The tree caret, pointing down when the branch is open.</summary>
    public string CaretGlyph => IsExpanded ? "CaretDown" : "CaretRight";

    [ObservableProperty] public partial bool IsSelected { get; set; }

    public ObservableCollection<DatabaseNodeViewModel> Databases { get; } = [];

    public bool IsConnected => State == ConnectionState.Connected;
    public bool IsBusy => State == ConnectionState.Connecting;
    public bool CanExpand => State == ConnectionState.Connected;

    public string RedactedUri => Profile.RedactedConnectionString;

    /// <summary>
    /// The icon the row draws, chosen the way Compass chooses it: a star for a favourite,
    /// a laptop for a local server, otherwise the generic server.
    /// </summary>
    public string IconGlyph =>
        Profile.IsFavorite ? "Favorite" : IsLocalhost(Profile.ConnectionString) ? "Laptop" : "Server";

    private static bool IsLocalhost(string connectionString)
    {
        try
        {
            var host = new MongoDB.Driver.MongoUrl(connectionString).Servers.FirstOrDefault()?.Host;

            return host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The small badge drawn over the bottom-right of the connection icon. Disconnected
    /// carries no badge at all, so a dormant connection is quiet rather than marked.
    /// </summary>
    public string StatusMarker => State switch
    {
        ConnectionState.Connected => "connected",
        ConnectionState.Connecting => "connecting",
        ConnectionState.Failed => "failed",
        _ => "none"
    };

    /// <summary>The connection's colour tag, or null when it has none.</summary>
    public string? ColorCode =>
        ConnectionColors.IsCustom(Profile.ColorCode) ? Profile.ColorCode : null;

    public bool HasColor => ColorCode is not null;

    /// <summary>Raised so the shell can close tabs belonging to this connection.</summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Takes an edited profile without tearing the connection down, so renaming or
    /// recolouring a live connection does not drop its session or its open tabs.
    /// </summary>
    public void ApplyProfile(ConnectionProfile profile)
    {
        Profile = profile;
        Name = profile.Name;

        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(ColorCode));
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(RedactedUri));
    }

    /// <summary>Folds the connection and everything under it, for "collapse all".</summary>
    public void Collapse()
    {
        IsExpanded = false;

        foreach (var database in _allDatabases) database.IsExpanded = false;
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (State is ConnectionState.Connected or ConnectionState.Connecting) return;

        State = ConnectionState.Connecting;
        ErrorMessage = null;
        Detail = "Connecting…";

        try
        {
            var session = await MongoSession.ConnectAsync(Profile).ConfigureAwait(true);

            _session = session;
            Admin = new AdminService(session);
            _catalog = new CatalogService(session);

            // The profile carries a refreshed LastUsedUtc, so keep the returned one.
            Profile = session.Profile;
            Name = Profile.Name;

            Detail = $"MongoDB {session.ServerVersion} · {session.Topology}"
                     + (session.IsTunnelled ? " · via SSH" : "");

            State = ConnectionState.Connected;

            await _store.SaveAsync(Profile).ConfigureAwait(true);
            await RefreshDatabasesAsync().ConfigureAwait(true);

            IsExpanded = true;
        }
        catch (Exception e)
        {
            // The driver's own message already distinguishes auth from network from TLS.
            ErrorMessage = e.Message;
            Detail = "Could not connect";
            State = ConnectionState.Failed;

            _session?.Dispose();
            _session = null;
            Admin = null;
            _catalog = null;
        }
    }

    [RelayCommand]
    public void Disconnect()
    {
        if (State == ConnectionState.Disconnected) return;

        _session?.Dispose();
        _session = null;
        Admin = null;
        _catalog = null;

        _allDatabases.Clear();
        Databases.Clear();

        IsExpanded = false;
        Detail = "";
        ErrorMessage = null;
        State = ConnectionState.Disconnected;

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task RefreshDatabasesAsync()
    {
        if (_catalog is null) return;

        try
        {
            var databases = await _catalog.ListDatabasesAsync().ConfigureAwait(true);

            _allDatabases.Clear();
            foreach (var db in databases)
                _allDatabases.Add(new DatabaseNodeViewModel(db, _catalog));

            Project();
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
    }

    /// <summary>Reloads one database's collections after a create, drop or rename.</summary>
    public async Task RefreshDatabaseAsync(string database)
    {
        var node = _allDatabases.FirstOrDefault(d => d.Name == database);

        if (node is null)
        {
            await RefreshDatabasesAsync().ConfigureAwait(true);
            return;
        }

        await node.ReloadAsync().ConfigureAwait(true);
        Project();
    }

    /// <summary>
    /// Narrows the tree to matching databases and collections. Returns true when this
    /// connection has anything to show, so the sidebar can hide the rest.
    /// </summary>
    public bool ApplyFilter(string term)
    {
        _filter = term;
        Project();

        if (term.Length == 0) return true;

        return Name.Contains(term, StringComparison.OrdinalIgnoreCase) || Databases.Count > 0;
    }

    private void Project()
    {
        Databases.Clear();

        foreach (var db in _allDatabases)
        {
            if (_filter.Length == 0)
            {
                db.ClearCollectionFilter();
                Databases.Add(db);
                continue;
            }

            var nameMatches = db.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase);
            var childMatches = db.ApplyCollectionFilter(_filter);

            if (nameMatches || childMatches)
            {
                if (childMatches) db.IsExpanded = true;
                Databases.Add(db);
            }
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
