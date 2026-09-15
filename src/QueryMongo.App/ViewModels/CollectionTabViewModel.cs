using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;
using QueryMongo.Core.Services;
using QueryMongo.Core.Shell;

namespace QueryMongo.App.ViewModels;

/// <summary>Identifies a pane by position in the tab strip.</summary>
public enum CollectionPane
{
    Documents = 0,
    Aggregations = 1,
    Schema = 2,
    Explain = 3,
    Indexes = 4,
    SearchIndexes = 5,
    Validation = 6,
    Map = 7,
    Shell = 8
}

/// <summary>
/// One open collection and the panes it shows. Each pane loads on first visit rather
/// than up front, so opening a tab costs a single query.
/// </summary>
public sealed partial class CollectionTabViewModel : WorkspaceTabViewModel
{
    private readonly CatalogService _catalog;
    private readonly QueryService _queries;
    private readonly HashSet<CollectionPane> _loaded = [];

    public CollectionTabViewModel(
        Guid connectionId,
        string connectionName,
        string? colorCode,
        string database,
        string collection,
        CollectionKind kind,
        MongoSession session,
        QueryHistoryStore history)
        : base(connectionId, connectionName, colorCode)
    {
        Database = database;
        Collection = collection;
        CollectionType = kind;

        _queries = new QueryService(session);
        _catalog = new CatalogService(session);

        Documents = new DocumentsViewModel(database, collection, _queries, history);
        Aggregation = new AggregationViewModel(database, collection, _queries);
        Schema = new SchemaViewModel(database, collection, new SchemaService(session));
        Explain = new ExplainViewModel(database, collection, _queries);
        Indexes = new IndexesViewModel(database, collection, new IndexService(session));
        SearchIndexes = new SearchIndexesViewModel(database, collection, new SearchIndexService(session));
        Validation = new ValidationViewModel(database, collection, new ValidationService(session));
        Map = new MapViewModel(database, collection, new GeoService(session));
        MongoShell = new MongoShellViewModel(new ShellService(session, database));

        Transfer = new TransferService(session);
        StatsSummary = "";
    }

    public string Database { get; }
    public string Collection { get; }

    /// <summary>Whether this is a plain collection, a view, or a time series collection.</summary>
    public CollectionKind CollectionType { get; }

    public override WorkspaceKind Kind => WorkspaceKind.Collection;

    public override string Title => Collection;

    /// <summary>Compass gives views and time series collections their own tab glyphs.</summary>
    public override string IconGlyph => CollectionType switch
    {
        CollectionKind.View => "Visibility",
        CollectionKind.TimeSeries => "TimeSeries",
        _ => "Folder"
    };

    public override IReadOnlyList<(string Label, string Value)> Tooltip =>
    [
        ("Connection", ConnectionName ?? ""),
        ("Database", Database),
        (CollectionType == CollectionKind.View ? "View" : "Collection", Collection)
    ];

    public string Namespace => $"{Database}.{Collection}";

    /// <summary>
    /// Two servers can hold the same namespace, so the tab tooltip and header name the
    /// connection as well.
    /// </summary>
    public string QualifiedNamespace => $"{ConnectionName} · {Database}.{Collection}";

    /// <summary>Views are read-only, so write-facing actions are hidden for them.</summary>
    public bool IsEditable => CollectionType != CollectionKind.View;

    /// <summary>A view cannot be written to, so its header says so.</summary>
    public bool IsReadOnly => CollectionType != CollectionKind.Collection;

    /// <summary>The badge beside the breadcrumb, for anything that is not a plain collection.</summary>
    public string KindLabel => CollectionType switch
    {
        CollectionKind.View => "view",
        CollectionKind.TimeSeries => "timeseries",
        _ => ""
    };

    /// <summary>Connection, database, then this collection.</summary>
    public IReadOnlyList<Controls.Crumb> Trail =>
    [
        new Controls.Crumb(ConnectionName ?? "", OpenDatabases),
        new Controls.Crumb(Database, OpenCollections),
        new Controls.Crumb(Collection)
    ];

    private void OpenDatabases()
    {
        if (Host is not { } host || Connection is not { } connection) return;

        host.OpenDatabasesCommand.Execute(connection);
    }

    private void OpenCollections()
    {
        if (Host is not { } host || Connection is not { } connection) return;

        host.OpenCollections(connection, Database);
    }

    /// <summary>
    /// A pipeline that has been built up in the aggregation pane exists nowhere else, so
    /// the tab holding it will not quietly be replaced by the next thing opened.
    /// </summary>
    public override bool CanBeReplaced =>
        Aggregation.Stages.Count == 0
        || Aggregation.Stages.All(s => string.IsNullOrWhiteSpace(s.Body));

    public DocumentsViewModel Documents { get; }
    public AggregationViewModel Aggregation { get; }
    public SchemaViewModel Schema { get; }
    public ExplainViewModel Explain { get; }
    public IndexesViewModel Indexes { get; }
    public SearchIndexesViewModel SearchIndexes { get; }
    public ValidationViewModel Validation { get; }
    public MapViewModel Map { get; }
    public MongoShellViewModel MongoShell { get; }

    internal TransferService Transfer { get; }

    [ObservableProperty] public partial string StatsSummary { get; set; }

    [ObservableProperty] public partial int SelectedPane { get; set; }

    [ObservableProperty] public partial bool ShowCollectionScanWarning { get; set; }

    protected override async Task ActivateAsync() => await InitializeAsync().ConfigureAwait(true);

    public async Task InitializeAsync()
    {
        await Documents.InitializeAsync().ConfigureAwait(true);
        await LoadStatsAsync().ConfigureAwait(true);
        await RefreshScanWarningAsync().ConfigureAwait(true);
    }

    /// <summary>Loads a pane's data the first time it is shown.</summary>
    public async Task OnPaneSelectedAsync(int index)
    {
        SelectedPane = index;

        var pane = (CollectionPane)index;

        // Explain always re-runs: it reports on whatever the Documents tab holds now.
        if (pane == CollectionPane.Explain)
        {
            await Explain.RefreshAsync(Documents.CurrentSpec).ConfigureAwait(true);
            return;
        }

        if (!_loaded.Add(pane)) return;

        switch (pane)
        {
            case CollectionPane.Schema:
                await Schema.AnalyzeAsync().ConfigureAwait(true);
                break;
            case CollectionPane.Indexes:
                await Indexes.RefreshAsync().ConfigureAwait(true);
                break;
            case CollectionPane.SearchIndexes:
                await SearchIndexes.RefreshAsync().ConfigureAwait(true);
                break;
            case CollectionPane.Validation:
                await Validation.LoadAsync().ConfigureAwait(true);
                break;
            case CollectionPane.Map:
                await Map.LoadAsync().ConfigureAwait(true);
                break;
        }
    }

    [RelayCommand]
    public async Task LoadStatsAsync()
    {
        try
        {
            var stats = await _catalog.GetStatsAsync(Database, Collection).ConfigureAwait(true);

            StatsSummary =
                $"{stats.DocumentCount:N0} documents · {ByteSize.Format(stats.StorageSizeBytes)} storage · " +
                $"{stats.IndexCount} indexes ({ByteSize.Format(stats.TotalIndexSizeBytes)}) · " +
                $"avg {ByteSize.Format((long)stats.AverageDocumentSizeBytes)}/doc";
        }
        catch (Exception e)
        {
            StatsSummary = e.Message;
        }
    }

    /// <summary>
    /// Cheap check behind the banner over the results list. The full plan lives on the
    /// Explain pane; this only needs to know whether the server scanned.
    /// </summary>
    [RelayCommand]
    public async Task RefreshScanWarningAsync()
    {
        try
        {
            var summary = await _queries
                .ExplainAsync(Database, Collection, Documents.CurrentSpec)
                .ConfigureAwait(true);

            ShowCollectionScanWarning = summary.IsCollectionScan && summary.DocumentsExamined > 1000;
        }
        catch (Exception)
        {
            // Explain is advisory; a server that refuses it must not break the tab.
            ShowCollectionScanWarning = false;
        }
    }
}
