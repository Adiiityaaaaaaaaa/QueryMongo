using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>One field row in the Schema tab, with its type mix ready to render.</summary>
public sealed class SchemaFieldViewModel(SchemaField field)
{
    public SchemaField Field { get; } = field;

    public string Path => Field.Path;
    public string TypeDescription => Field.TypeDescription;
    public string PresenceDescription => Field.PresenceDescription;
    public bool IsSparse => Field.IsSparse;

    /// <summary>Width of the presence bar, as a percentage of the row.</summary>
    public double PresencePercent => Field.Presence * 100;

    public string DominantType => Field.Types.Count > 0 ? Field.Types[0].TypeName : "—";

    public string Examples => Field.Examples.Count == 0
        ? ""
        : string.Join("  ·  ", Field.Examples);

    /// <summary>A short note when a field holds more than one type, which usually signals a bug.</summary>
    public string? MixedTypeWarning => Field.Types.Count > 1
        ? $"{Field.Types.Count} different types"
        : null;

    public bool HasMixedTypes => Field.Types.Count > 1;
}

/// <summary>
/// The Schema tab: samples documents and reports the fields found, their types and
/// how often each appears.
/// </summary>
public sealed partial class SchemaViewModel : ObservableObject
{
    private readonly SchemaService _schema;
    private readonly string _database;
    private readonly string _collection;

    public SchemaViewModel(string database, string collection, SchemaService schema)
    {
        _database = database;
        _collection = collection;
        _schema = schema;

        Filter = "{}";
        SampleSize = 1000;
        Summary = "Analyze the collection to infer its schema.";
    }

    public ObservableCollection<SchemaFieldViewModel> Fields { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; }
    [ObservableProperty] public partial int SampleSize { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial bool HasAnalyzed { get; set; }

    [RelayCommand]
    public async Task AnalyzeAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var parsed = BsonJson.ParseDocument(Filter);
            if (!parsed.IsValid)
            {
                ErrorMessage = parsed.Error;
                return;
            }

            var report = await _schema
                .AnalyzeAsync(_database, _collection, parsed.Value, SampleSize)
                .ConfigureAwait(true);

            Fields.Clear();
            foreach (var field in report.Fields)
                Fields.Add(new SchemaFieldViewModel(field));

            var mixed = report.Fields.Count(f => f.Types.Count > 1);
            var sparse = report.Fields.Count(f => f.IsSparse);

            Summary = report.SampleSize == 0
                ? "No documents matched, so there is nothing to analyze."
                : $"{report.Fields.Count} field(s) across {report.SampleSize:N0} sampled document(s) · " +
                  $"{sparse} not in every document · {mixed} with mixed types";

            HasAnalyzed = true;
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
}
