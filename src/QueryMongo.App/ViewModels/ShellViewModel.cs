using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Mongo;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// Owns the connection lifecycle and the database tree. Everything the window shows
/// hangs off this one object.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionStore _store;
    private MongoSession? _session;

    public ShellViewModel(IConnectionStore store)
    {
        _store = store;
        ConnectionString = "mongodb://localhost:27017";
        ServerDescription = "";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ConnectionString { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string ServerDescription { get; set; }

    [ObservableProperty]
    public partial CollectionWorkspaceViewModel? ActiveCollection { get; set; }

    public bool IsDisconnected => !IsConnected;

    public ObservableCollection<DatabaseNodeViewModel> Databases { get; } = [];

    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];

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
            var profile = ConnectionProfile.Create(name: "", ConnectionString);
            var session = await MongoSession.ConnectAsync(profile).ConfigureAwait(true);

            _session?.Dispose();
            _session = session;

            ServerDescription = $"MongoDB {session.ServerVersion} · {session.Topology}";
            IsConnected = true;

            await _store.SaveAsync(session.Profile).ConfigureAwait(true);
            await LoadSavedConnectionsAsync().ConfigureAwait(true);
            await RefreshDatabasesAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            // Surfaced in the connect view; the driver's own message already
            // distinguishes auth from network from TLS failures.
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
        _session?.Dispose();
        _session = null;

        Databases.Clear();
        ActiveCollection = null;
        ServerDescription = "";
        IsConnected = false;
    }

    [RelayCommand]
    private async Task RefreshDatabasesAsync()
    {
        if (_session is null) return;

        IsBusy = true;
        try
        {
            var catalog = new CatalogService(_session);
            var databases = await catalog.ListDatabasesAsync().ConfigureAwait(true);

            Databases.Clear();
            foreach (var db in databases)
                Databases.Add(new DatabaseNodeViewModel(db, catalog));
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

    /// <summary>Opens a collection in the main pane, replacing whatever was open.</summary>
    public void OpenCollection(string database, string collection)
    {
        if (_session is null) return;

        ActiveCollection = new CollectionWorkspaceViewModel(
            database, collection, new QueryService(_session), new CatalogService(_session));

        _ = ActiveCollection.InitializeAsync();
    }

    public void UseSavedConnection(ConnectionProfile profile) => ConnectionString = profile.ConnectionString;

    [RelayCommand]
    private async Task DeleteSavedConnectionAsync(ConnectionProfile profile)
    {
        await _store.DeleteAsync(profile.Id).ConfigureAwait(true);
        await LoadSavedConnectionsAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
