using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MongoDB.Bson;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

public enum DocumentViewMode { List, Table, Json }

/// <summary>
/// The Documents tab: query bar, results in three view modes, paging, and per-document
/// edit, clone and delete.
/// </summary>
public sealed partial class DocumentsViewModel : ObservableObject
{
    private readonly QueryService _queries;
    private readonly QueryHistoryStore _history;
    private readonly string _database;
    private readonly string _collection;

    // Lets a new query cancel the one still in flight, so a slow earlier find cannot
    // overwrite newer results.
    private CancellationTokenSource? _inFlight;

    public DocumentsViewModel(
        string database, string collection, QueryService queries, QueryHistoryStore history)
    {
        _database = database;
        _collection = collection;
        _queries = queries;
        _history = history;

        // Compass starts with an empty filter rather than a literal "{}", so the
        // placeholder shows through and a fresh query bar reads as untouched.
        Filter = "";
        Projection = "";
        Sort = "";
        Collation = "";
        Hint = "";
        Limit = 50;
        ResultSummary = "";
        RawJson = "";
    }

    public string Namespace => $"{_database}.{_collection}";

    // ---- query bar -------------------------------------------------------

    [ObservableProperty] public partial string Filter { get; set; }
    [ObservableProperty] public partial string Projection { get; set; }
    [ObservableProperty] public partial string Sort { get; set; }
    [ObservableProperty] public partial string Collation { get; set; }
    [ObservableProperty] public partial int Limit { get; set; }
    [ObservableProperty] public partial int Skip { get; set; }

    /// <summary>An index to force, as a key document or an index name.</summary>
    [ObservableProperty] public partial string Hint { get; set; }

    /// <summary>Server-side time limit for the query; zero leaves the server default.</summary>
    [ObservableProperty] public partial int MaxTimeMs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OptionsCaretGlyph))]
    public partial bool IsQueryBarExpanded { get; set; }

    /// <summary>The options toggle points down when closed and up when open.</summary>
    public string OptionsCaretGlyph => IsQueryBarExpanded ? "CaretUp" : "CaretDown";

    // ---- results ---------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode), nameof(IsTableMode), nameof(IsJsonMode))]
    public partial DocumentViewMode ViewMode { get; set; }

    public bool IsListMode => ViewMode == DocumentViewMode.List;
    public bool IsTableMode => ViewMode == DocumentViewMode.Table;
    public bool IsJsonMode => ViewMode == DocumentViewMode.Json;

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial long MatchingCount { get; set; }

    /// <summary>The whole page as a JSON array, for the JSON view mode.</summary>
    [ObservableProperty] public partial string RawJson { get; set; }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>Column order for the table view, derived from the documents on this page.</summary>
    public ObservableCollection<string> Columns { get; } = [];

    public ObservableCollection<SavedQuery> History { get; } = [];

    public QuerySpec CurrentSpec => new()
    {
        Filter = Filter,
        Projection = Projection,
        Sort = Sort,
        Collation = Collation,
        Skip = Skip,
        Limit = Limit,
        Hint = Hint,
        MaxTimeMs = MaxTimeMs
    };

    public async Task InitializeAsync()
    {
        await RunQueryAsync().ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);
    }

    // ---- running the query ----------------------------------------------

    [RelayCommand]
    public async Task RunQueryAsync()
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
        StatusMessage = null;

        try
        {
            var spec = CurrentSpec;

            var page = await _queries.FindAsync(_database, _collection, spec, cts.Token)
                .ConfigureAwait(true);

            Populate(page);

            MatchingCount = await _queries
                .CountAsync(_database, _collection, Filter, cts.Token)
                .ConfigureAwait(true);

            ResultSummary = Documents.Count == 0
                ? "No documents match this query."
                : $"{Skip + 1}–{Skip + Documents.Count} of {MatchingCount:N0}";

            await _history.RecordAsync(Namespace, spec, cts.Token).ConfigureAwait(true);
            await LoadHistoryAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query; that one owns the UI state now.
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts)) IsBusy = false;
        }
    }

    private void Populate(DocumentPage page)
    {
        Documents.Clear();

        var ordinal = Skip;
        foreach (var doc in page.Documents)
            Documents.Add(new DocumentViewModel(doc, ++ordinal));

        RebuildColumns();

        HasMore = page.HasMore;
        CanGoBack = Skip > 0;

        var builder = new StringBuilder("[");
        for (var i = 0; i < Documents.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.AppendLine();
            builder.Append("  ").Append(BsonJson.ToCompactJson(Documents[i].Document));
        }
        builder.AppendLine();
        builder.Append(']');
        RawJson = builder.ToString();
    }

    /// <summary>
    /// Builds the table columns from every document on the page, preserving first-seen
    /// order with _id first, so rows stay aligned even with heterogeneous documents.
    /// </summary>
    private void RebuildColumns()
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vm in Documents)
            foreach (var name in vm.Document.Names)
                if (seen.Add(name)) ordered.Add(name);

        // _id is the row identity; Compass always shows it leftmost.
        if (ordered.Remove("_id")) ordered.Insert(0, "_id");

        Columns.Clear();
        foreach (var column in ordered) Columns.Add(column);

        foreach (var vm in Documents) vm.BuildCells(ordered);
    }

    // ---- paging ----------------------------------------------------------

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
        Filter = "";
        Projection = "";
        Sort = "";
        Collation = "";
        Hint = "";
        MaxTimeMs = 0;
        Skip = 0;
        Limit = 50;
        await RunQueryAsync().ConfigureAwait(true);
    }

    // ---- view modes ------------------------------------------------------

    [RelayCommand] private void ShowList() => ViewMode = DocumentViewMode.List;
    [RelayCommand] private void ShowTable() => ViewMode = DocumentViewMode.Table;
    [RelayCommand] private void ShowJson() => ViewMode = DocumentViewMode.Json;

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var doc in Documents) doc.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var doc in Documents) doc.IsExpanded = false;
    }

    // ---- per-document editing -------------------------------------------

    [RelayCommand]
    private static void BeginEdit(DocumentViewModel document) => document.BeginEdit();

    [RelayCommand]
    private static void CancelEdit(DocumentViewModel document) => document.CancelEdit();

    [RelayCommand]
    private async Task SaveEditAsync(DocumentViewModel document)
    {
        if (document.TryReadEditor() is not { } replacement) return;
        if (document.Id is not { } id) return;

        try
        {
            var result = await _queries
                .ReplaceAsync(_database, _collection, id, replacement)
                .ConfigureAwait(true);

            document.IsEditing = false;
            StatusMessage = result.ModifiedCount > 0
                ? "Document updated."
                : "No change — the document already matched.";

            await RunQueryAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            document.EditError = e.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync(DocumentViewModel document)
    {
        if (document.Id is not { } id) return;

        try
        {
            await _queries.DeleteAsync(_database, _collection, id).ConfigureAwait(true);
            StatusMessage = "Document deleted.";
            await RunQueryAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
    }

    /// <summary>
    /// Inserts a copy without its <c>_id</c>, so the server assigns a fresh one.
    /// </summary>
    [RelayCommand]
    private async Task CloneDocumentAsync(DocumentViewModel document)
    {
        try
        {
            var copy = (BsonDocument)document.Document.DeepClone();
            copy.Remove("_id");

            await _queries.InsertAsync(_database, _collection, copy).ConfigureAwait(true);
            StatusMessage = "Document cloned.";
            await RunQueryAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
        }
    }

    /// <summary>Called by the insert dialog once the user confirms.</summary>
    public async Task<string?> InsertAsync(string json)
    {
        var parsed = BsonJson.ParseDocument(json);
        if (!parsed.IsValid) return parsed.Error;
        if (parsed.Value is not { ElementCount: > 0 } doc) return "A document cannot be empty.";

        try
        {
            await _queries.InsertAsync(_database, _collection, doc).ConfigureAwait(true);
            StatusMessage = "Document inserted.";
            await RunQueryAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Called by the bulk-update dialog. Returns an error, or null on success.</summary>
    public async Task<string?> UpdateManyAsync(string update)
    {
        try
        {
            var modified = await _queries
                .UpdateManyAsync(_database, _collection, Filter, update)
                .ConfigureAwait(true);

            StatusMessage = $"{modified:N0} document(s) updated.";
            await RunQueryAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Deletes every document matching the current filter.</summary>
    public async Task<string?> DeleteManyAsync()
    {
        try
        {
            var deleted = await _queries
                .DeleteManyAsync(_database, _collection, Filter)
                .ConfigureAwait(true);

            StatusMessage = $"{deleted:N0} document(s) deleted.";
            await RunQueryAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // ---- history and favorites ------------------------------------------

    private async Task LoadHistoryAsync()
    {
        try
        {
            var entries = await _history.LoadAsync(Namespace).ConfigureAwait(true);

            History.Clear();
            foreach (var entry in entries) History.Add(entry);
        }
        catch (IOException)
        {
            // History is a convenience; a locked file must not break querying.
        }
    }

    public void ApplySavedQuery(SavedQuery saved)
    {
        Filter = saved.Spec.Filter;
        Projection = saved.Spec.Projection;
        Sort = saved.Spec.Sort;
        Collation = saved.Spec.Collation;
        Limit = saved.Spec.Limit;
        Skip = 0;
    }

    public async Task SaveFavoriteAsync(string name)
    {
        await _history.SaveFavoriteAsync(Namespace, CurrentSpec, name).ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteSavedQueryAsync(SavedQuery saved)
    {
        await _history.DeleteAsync(saved.Id).ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _history.ClearHistoryAsync(Namespace).ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);
    }

    /// <summary>Generated driver code for the current query.</summary>
    public string ExportQuery(TargetLanguage language) =>
        ExportToLanguage.Query(language, _database, _collection, CurrentSpec);
}
