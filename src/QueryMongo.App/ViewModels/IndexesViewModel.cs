using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The Indexes tab: lists indexes with size and usage, and creates or drops them.
/// </summary>
public sealed partial class IndexesViewModel : ObservableObject
{
    private readonly IndexService _indexes;
    private readonly string _database;
    private readonly string _collection;

    public IndexesViewModel(string database, string collection, IndexService indexes)
    {
        _database = database;
        _collection = collection;
        _indexes = indexes;

        NewIndexKeys = "{\n  \n}";
        NewIndexName = "";
        NewIndexPartialFilter = "";
        NewIndexCollation = "";
    }

    public ObservableCollection<IndexDetail> Indexes { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "";

    // ---- create dialog state --------------------------------------------

    [ObservableProperty] public partial string NewIndexKeys { get; set; }
    [ObservableProperty] public partial string NewIndexName { get; set; }
    [ObservableProperty] public partial bool NewIndexUnique { get; set; }
    [ObservableProperty] public partial bool NewIndexSparse { get; set; }
    [ObservableProperty] public partial bool NewIndexBackground { get; set; }
    [ObservableProperty] public partial bool NewIndexHasTtl { get; set; }
    [ObservableProperty] public partial int NewIndexTtlSeconds { get; set; } = 3600;
    [ObservableProperty] public partial string NewIndexPartialFilter { get; set; }
    [ObservableProperty] public partial string NewIndexCollation { get; set; }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var details = await _indexes.ListDetailedAsync(_database, _collection).ConfigureAwait(true);

            Indexes.Clear();
            foreach (var detail in details) Indexes.Add(detail);

            var totalBytes = details.Sum(d => d.Index.SizeBytes ?? 0);
            var unused = details.Count(d => d.Operations == 0 && d.Name != "_id_");

            Summary = $"{details.Count} index(es) · {QueryMongo.Core.ByteSize.Format(totalBytes)}"
                      + (unused > 0 ? $" · {unused} never used since the server started" : "");
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

    /// <summary>Creates the index described by the dialog fields. Returns an error, or null.</summary>
    public async Task<string?> CreateAsync()
    {
        var keys = BsonJson.ParseDocument(NewIndexKeys);
        if (!keys.IsValid) return keys.Error;
        if (keys.Value is not { ElementCount: > 0 } keyDoc)
            return "An index needs at least one key, for example { field: 1 }.";

        var partial = BsonJson.ParseDocument(NewIndexPartialFilter);
        if (!partial.IsValid) return $"Partial filter: {partial.Error}";

        var definition = new IndexDefinition
        {
            Keys = keyDoc,
            Name = NewIndexName,
            Unique = NewIndexUnique,
            Sparse = NewIndexSparse,
            Background = NewIndexBackground,
            ExpireAfterSeconds = NewIndexHasTtl ? NewIndexTtlSeconds : null,
            PartialFilter = partial.Value,
            CollationLocale = string.IsNullOrWhiteSpace(NewIndexCollation) ? null : NewIndexCollation.Trim()
        };

        try
        {
            var name = await _indexes.CreateAsync(_database, _collection, definition).ConfigureAwait(true);
            StatusMessage = $"Index \"{name}\" created.";
            ResetCreateForm();
            await RefreshAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private void ResetCreateForm()
    {
        NewIndexKeys = "{\n  \n}";
        NewIndexName = "";
        NewIndexUnique = false;
        NewIndexSparse = false;
        NewIndexBackground = false;
        NewIndexHasTtl = false;
        NewIndexTtlSeconds = 3600;
        NewIndexPartialFilter = "";
        NewIndexCollation = "";
    }

    /// <summary>Drops an index. The caller confirms first; this does not prompt.</summary>
    public async Task<string?> DropAsync(IndexDetail detail)
    {
        try
        {
            await _indexes.DropAsync(_database, _collection, detail.Name).ConfigureAwait(true);
            StatusMessage = $"Index \"{detail.Name}\" dropped.";
            await RefreshAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
