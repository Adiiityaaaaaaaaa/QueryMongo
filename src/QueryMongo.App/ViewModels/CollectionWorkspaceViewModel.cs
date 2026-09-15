using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MongoDB.Bson;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The main pane for one collection: the query bar, the document list, indexes and
/// the explain summary.
/// </summary>
public sealed partial class CollectionWorkspaceViewModel : ObservableObject
{
    /// <summary>Shown so the aggregation tab is never a blank box.</summary>
    private const string StarterPipeline = """
        [
          { "$match": {} }
        ]
        """;

    private readonly QueryService _queries;
    private readonly CatalogService _catalog;

    // Lets a new query cancel the one still in flight, so fast typing on the query
    // bar cannot leave a slow earlier find to overwrite newer results.
    private CancellationTokenSource? _inFlight;

    public CollectionWorkspaceViewModel(
        string database,
        string collection,
        QueryService queries,
        CatalogService catalog)
    {
        Database = database;
        Collection = collection;
        _queries = queries;
        _catalog = catalog;

        // Partial properties cannot carry initializers, so the defaults live here.
        Filter = "{}";
        Projection = "";
        Sort = "";
        Limit = 50;
        ResultSummary = "";
        StatsSummary = "";
        PipelineText = StarterPipeline;
    }

    public string Database { get; }
    public string Collection { get; }
    public string Namespace => $"{Database}.{Collection}";

    [ObservableProperty] public partial string Filter { get; set; }
    [ObservableProperty] public partial string Projection { get; set; }
    [ObservableProperty] public partial string Sort { get; set; }
    [ObservableProperty] public partial int Limit { get; set; }
    [ObservableProperty] public partial int Skip { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial bool CanGoBack { get; set; }

    [ObservableProperty] public partial string StatsSummary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExplainDescription))]
    public partial ExplainSummary? Explain { get; set; }

    [ObservableProperty] public partial bool ShowCollectionScanWarning { get; set; }

    [ObservableProperty] public partial string PipelineText { get; set; }
    [ObservableProperty] public partial string? PipelineError { get; set; }

    /// <summary>The explain tab body: plan shape and the numbers that explain the cost.</summary>
    public string ExplainDescription => Explain is null
        ? "Run a query to see its execution plan."
        : string.Join(Environment.NewLine,
            $"Stage:              {Explain.Stage}",
            $"Index:              {Explain.IndexName ?? "(none — no index used)"}",
            $"Documents examined: {Explain.DocumentsExamined:N0}",
            $"Keys examined:      {Explain.KeysExamined:N0}",
            $"Documents returned: {Explain.DocumentsReturned:N0}",
            $"Execution time:     {Explain.ExecutionTime.TotalMilliseconds:N0} ms",
            "",
            BsonJson.ToPrettyJson(Explain.Raw));

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];
    public ObservableCollection<IndexInfo> Indexes { get; } = [];
    public ObservableCollection<DocumentViewModel> PipelineResults { get; } = [];

    public async Task InitializeAsync()
    {
        await RunQueryAsync().ConfigureAwait(true);
        await LoadStatsAsync().ConfigureAwait(true);
        await LoadIndexesAsync().ConfigureAwait(true);
    }

    private QuerySpec CurrentSpec => new()
    {
        Filter = Filter,
        Projection = Projection,
        Sort = Sort,
        Skip = Skip,
        Limit = Limit
    };

    [RelayCommand]
    private async Task RunQueryAsync()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _inFlight, cts);
        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var page = await _queries.FindAsync(Database, Collection, CurrentSpec, cts.Token)
                .ConfigureAwait(true);

            Documents.Clear();
            var index = Skip;
            foreach (var doc in page.Documents)
                Documents.Add(new DocumentViewModel(doc, ++index));

            HasMore = page.HasMore;
            CanGoBack = Skip > 0;
            ResultSummary = Documents.Count == 0
                ? "No documents match this query."
                : $"{Skip + 1}–{Skip + Documents.Count}{(page.HasMore ? "" : " of " + (Skip + Documents.Count))}";

            await UpdateExplainAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query; the newer one owns the UI state.
        }
        catch (Exception e)
        {
            // QueryInputException (bad JSON in the query bar) and driver errors both
            // land here; the message is what the user needs either way.
            ErrorMessage = e.Message;
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts)) IsBusy = false;
        }
    }

    private async Task UpdateExplainAsync(CancellationToken ct)
    {
        try
        {
            Explain = await _queries.ExplainAsync(Database, Collection, CurrentSpec, ct)
                .ConfigureAwait(true);

            // Compass flags this because a COLLSCAN on a large collection is almost
            // always the reason a query feels slow.
            ShowCollectionScanWarning = Explain.IsCollectionScan && Explain.DocumentsExamined > 1000;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Explain = null;
            ShowCollectionScanWarning = false;
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        Skip += Limit;
        await RunQueryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        Skip = Math.Max(0, Skip - Limit);
        await RunQueryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetQueryAsync()
    {
        Filter = "{}";
        Projection = "";
        Sort = "";
        Skip = 0;
        Limit = 50;
        await RunQueryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadStatsAsync()
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

    [RelayCommand]
    private async Task LoadIndexesAsync()
    {
        try
        {
            var indexes = await _catalog.ListIndexesAsync(Database, Collection).ConfigureAwait(true);

            Indexes.Clear();
            foreach (var index in indexes) Indexes.Add(index);
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
    }

    [RelayCommand]
    private async Task RunPipelineAsync()
    {
        PipelineError = null;

        var parsed = BsonJson.ParsePipeline(PipelineText);
        if (!parsed.IsValid)
        {
            PipelineError = parsed.Error;
            return;
        }

        IsBusy = true;
        try
        {
            var results = await _queries
                .AggregateAsync(Database, Collection, parsed.Value!, previewLimit: 50)
                .ConfigureAwait(true);

            PipelineResults.Clear();
            var i = 0;
            foreach (var doc in results) PipelineResults.Add(new DocumentViewModel(doc, ++i));

            if (results.Count == 0) PipelineError = "The pipeline returned no documents.";
        }
        catch (Exception e)
        {
            PipelineError = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// One row in the document list.
///
/// The pretty-printed JSON is built lazily: 50 rows a page is cheap, but a single
/// document can be 16 MB and formatting every one up front would stall the list on
/// collections with large documents.
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject
{
    private string? _json;

    public DocumentViewModel(BsonDocument document, int ordinal)
    {
        Document = document;
        Ordinal = ordinal;
        Preview = BsonJson.ToPreview(document);
    }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public BsonDocument Document { get; }

    public int Ordinal { get; }

    public string Preview { get; }

    public string Json => _json ??= BsonJson.ToPrettyJson(Document);

    public string IdDescription =>
        Document.TryGetValue("_id", out var id) ? id.ToString() ?? "" : "(no _id)";
}
