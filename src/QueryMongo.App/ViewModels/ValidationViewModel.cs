using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The Validation tab: edits a collection's JSON-schema rules and previews which
/// existing documents would pass or fail them.
/// </summary>
public sealed partial class ValidationViewModel : ObservableObject
{
    private readonly ValidationService _validation;
    private readonly string _database;
    private readonly string _collection;

    /// <summary>Offered when a collection has no rules yet, so the editor is not blank.</summary>
    private const string StarterSchema = """
        {
          "$jsonSchema": {
            "bsonType": "object",
            "required": [],
            "properties": {}
          }
        }
        """;

    public ValidationViewModel(string database, string collection, ValidationService validation)
    {
        _database = database;
        _collection = collection;
        _validation = validation;

        RulesText = StarterSchema;
        Level = "strict";
        Action = "error";
    }

    public ObservableCollection<DocumentViewModel> Passing { get; } = [];
    public ObservableCollection<DocumentViewModel> Failing { get; } = [];

    [ObservableProperty] public partial string RulesText { get; set; }
    [ObservableProperty] public partial string Level { get; set; }
    [ObservableProperty] public partial string Action { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial bool HasRules { get; set; }
    [ObservableProperty] public partial string PreviewSummary { get; set; } = "";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var rules = await _validation.GetAsync(_database, _collection).ConfigureAwait(true);

            HasRules = rules.HasRules;
            RulesText = rules.HasRules ? BsonJson.ToPrettyJson(rules.Validator) : StarterSchema;
            Level = rules.Level;
            Action = rules.Action;

            if (rules.HasRules) await PreviewAsync().ConfigureAwait(true);
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

    /// <summary>
    /// Shows which existing documents the rules accept and reject. Applying rules does
    /// not re-check existing documents, so this is the only way to see the impact.
    /// </summary>
    [RelayCommand]
    public async Task PreviewAsync()
    {
        var parsed = BsonJson.ParseDocument(RulesText);
        if (!parsed.IsValid)
        {
            ErrorMessage = parsed.Error;
            return;
        }

        if (parsed.Value is not { ElementCount: > 0 } validator)
        {
            PreviewSummary = "No rules to preview.";
            Passing.Clear();
            Failing.Clear();
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var passing = await _validation
                .PreviewPassingAsync(_database, _collection, validator).ConfigureAwait(true);
            var failing = await _validation
                .PreviewFailingAsync(_database, _collection, validator).ConfigureAwait(true);

            Passing.Clear();
            var i = 0;
            foreach (var doc in passing) Passing.Add(new DocumentViewModel(doc, ++i));

            Failing.Clear();
            i = 0;
            foreach (var doc in failing) Failing.Add(new DocumentViewModel(doc, ++i));

            PreviewSummary = failing.Count == 0
                ? "No sampled documents fail these rules."
                : $"{failing.Count} sampled document(s) would be rejected.";
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

    /// <summary>Applies the rules. Returns an error, or null on success.</summary>
    public async Task<string?> ApplyAsync()
    {
        var parsed = BsonJson.ParseDocument(RulesText);
        if (!parsed.IsValid) return parsed.Error;

        try
        {
            await _validation
                .SetAsync(_database, _collection,
                    new ValidationRules(parsed.Value ?? new MongoDB.Bson.BsonDocument(), Level, Action))
                .ConfigureAwait(true);

            StatusMessage = "Validation rules applied.";
            await LoadAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Removes validation from the collection entirely.</summary>
    public async Task<string?> ClearAsync()
    {
        try
        {
            await _validation.ClearAsync(_database, _collection).ConfigureAwait(true);
            StatusMessage = "Validation removed.";
            await LoadAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
