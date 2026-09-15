using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Mongo;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// One open collection, holding the panes Compass shows per collection. Each pane
/// loads on first visit rather than up front, so opening a tab costs one query.
/// </summary>
public sealed partial class CollectionTabViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly QueryService _queries;

    private ExplainSummary? _explain;
    private bool _schemaLoaded;
    private bool _indexesLoaded;
    private bool _validationLoaded;

    public CollectionTabViewModel(
        string database,
        string collection,
        CollectionKind kind,
        MongoSession session,
        QueryHistoryStore history)
    {
        Database = database;
        Collection = collection;
        Kind = kind;

        _queries = new QueryService(session);
        _catalog = new CatalogService(session);

        Documents = new DocumentsViewModel(database, collection, _queries, history);
        Aggregation = new AggregationViewModel(database, collection, _queries);
        Schema = new SchemaViewModel(database, collection, new SchemaService(session));
        Indexes = new IndexesViewModel(database, collection, new IndexService(session));
        Validation = new ValidationViewModel(database, collection, new ValidationService(session));
        Transfer = new TransferService(session);

        StatsSummary = "";
    }

    public string Database { get; }
    public string Collection { get; }
    public CollectionKind Kind { get; }

    public string Namespace => $"{Database}.{Collection}";

    /// <summary>Views are read-only, so write-facing actions are hidden for them.</summary>
    public bool IsEditable => Kind != CollectionKind.View;

    public DocumentsViewModel Documents { get; }
    public AggregationViewModel Aggregation { get; }
    public SchemaViewModel Schema { get; }
    public IndexesViewModel Indexes { get; }
    public ValidationViewModel Validation { get; }

    internal TransferService Transfer { get; }

    [ObservableProperty] public partial string StatsSummary { get; set; }

    [ObservableProperty] public partial int SelectedPane { get; set; }

    [ObservableProperty] public partial bool ShowCollectionScanWarning { get; set; }

    public async Task InitializeAsync()
    {
        await Documents.InitializeAsync().ConfigureAwait(true);
        await LoadStatsAsync().ConfigureAwait(true);
        await RefreshExplainAsync().ConfigureAwait(true);
    }

    /// <summary>Loads a pane's data the first time it is shown.</summary>
    public async Task OnPaneSelectedAsync(int index)
    {
        SelectedPane = index;

        switch (index)
        {
            case 2 when !_schemaLoaded:
                _schemaLoaded = true;
                await Schema.AnalyzeAsync().ConfigureAwait(true);
                break;

            case 3:
                await RefreshExplainAsync().ConfigureAwait(true);
                break;

            case 4 when !_indexesLoaded:
                _indexesLoaded = true;
                await Indexes.RefreshAsync().ConfigureAwait(true);
                break;

            case 5 when !_validationLoaded:
                _validationLoaded = true;
                await Validation.LoadAsync().ConfigureAwait(true);
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

    /// <summary>The Explain pane reports on the Documents query, so it has no query of its own.</summary>
    public string ExplainDescription => _explain is null
        ? "Run a query on the Documents tab to see its execution plan."
        : string.Join(Environment.NewLine,
            $"Stage:              {_explain.Stage}",
            $"Index:              {_explain.IndexName ?? "(none — no index used)"}",
            $"Documents examined: {_explain.DocumentsExamined:N0}",
            $"Keys examined:      {_explain.KeysExamined:N0}",
            $"Documents returned: {_explain.DocumentsReturned:N0}",
            $"Execution time:     {_explain.ExecutionTime.TotalMilliseconds:N0} ms",
            "",
            BsonJson.ToPrettyJson(_explain.Raw));

    [RelayCommand]
    public async Task RefreshExplainAsync()
    {
        try
        {
            _explain = await _queries
                .ExplainAsync(Database, Collection, Documents.CurrentSpec)
                .ConfigureAwait(true);

            // A COLLSCAN over a large collection is almost always why a query feels slow.
            ShowCollectionScanWarning = _explain.IsCollectionScan && _explain.DocumentsExamined > 1000;
        }
        catch (Exception)
        {
            // Explain is advisory; a server that refuses it must not break the tab.
            _explain = null;
            ShowCollectionScanWarning = false;
        }

        OnPropertyChanged(nameof(ExplainDescription));
    }
}
