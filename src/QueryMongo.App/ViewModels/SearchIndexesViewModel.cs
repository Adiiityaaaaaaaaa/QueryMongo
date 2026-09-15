using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// Atlas Search indexes. These commands only exist on Atlas, so the pane says so
/// plainly on other deployments rather than showing a raw command error.
/// </summary>
public sealed partial class SearchIndexesViewModel : ObservableObject
{
    private readonly SearchIndexService _service;
    private readonly string _database;
    private readonly string _collection;

    public SearchIndexesViewModel(string database, string collection, SearchIndexService service)
    {
        _database = database;
        _collection = collection;
        _service = service;

        NewIndexName = "default";
        NewIndexDefinition = SearchIndexService.DynamicMappingTemplate;
        NewIndexType = "search";
    }

    public ObservableCollection<SearchIndexInfo> Indexes { get; } = [];

    public static IReadOnlyList<string> Types { get; } = ["search", "vectorSearch"];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsSupported { get; set; } = true;
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "";

    [ObservableProperty] public partial string NewIndexName { get; set; }
    [ObservableProperty] public partial string NewIndexDefinition { get; set; }
    [ObservableProperty] public partial string NewIndexType { get; set; }

    /// <summary>Swapping the type swaps in a template that actually fits it.</summary>
    partial void OnNewIndexTypeChanged(string value)
    {
        var isDefault = NewIndexDefinition.Trim() == SearchIndexService.DynamicMappingTemplate.Trim()
                        || NewIndexDefinition.Trim() == SearchIndexService.VectorTemplate.Trim();

        if (!isDefault) return;

        NewIndexDefinition = value == "vectorSearch"
            ? SearchIndexService.VectorTemplate
            : SearchIndexService.DynamicMappingTemplate;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var found = await _service.ListAsync(_database, _collection).ConfigureAwait(true);

            Indexes.Clear();
            foreach (var index in found) Indexes.Add(index);

            IsSupported = true;
            Summary = found.Count == 0
                ? "No search indexes on this collection."
                : $"{found.Count} search index(es)";
        }
        catch (Exception e)
        {
            // Every non-Atlas deployment lands here; it is the expected case, not a fault.
            IsSupported = false;
            Summary = "Search indexes are an Atlas feature and are not available on this deployment.";
            ErrorMessage = e.Message;
            Indexes.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Creates the index described by the form. Returns an error, or null.</summary>
    public async Task<string?> CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewIndexName)) return "A search index needs a name.";

        var parsed = BsonJson.ParseDocument(NewIndexDefinition);
        if (!parsed.IsValid) return parsed.Error;
        if (parsed.Value is not { ElementCount: > 0 } definition)
            return "The definition cannot be empty.";

        try
        {
            await _service
                .CreateAsync(_database, _collection, NewIndexName.Trim(), definition, NewIndexType)
                .ConfigureAwait(true);

            // Atlas builds asynchronously, so the index is listed before it is usable.
            StatusMessage = $"Index \"{NewIndexName}\" requested. Atlas builds it in the background.";
            await RefreshAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> DropAsync(SearchIndexInfo index)
    {
        try
        {
            await _service.DropAsync(_database, _collection, index.Name).ConfigureAwait(true);
            StatusMessage = $"Index \"{index.Name}\" dropped.";
            await RefreshAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public async Task<string?> UpdateAsync(SearchIndexInfo index, string definitionJson)
    {
        var parsed = BsonJson.ParseDocument(definitionJson);
        if (!parsed.IsValid) return parsed.Error;
        if (parsed.Value is not { ElementCount: > 0 } definition) return "The definition cannot be empty.";

        try
        {
            await _service.UpdateAsync(_database, _collection, index.Name, definition).ConfigureAwait(true);
            StatusMessage = $"Index \"{index.Name}\" updated.";
            await RefreshAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
