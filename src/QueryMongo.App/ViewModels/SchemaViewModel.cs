using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>One bar in a field's distribution chart.</summary>
public sealed class ChartBarViewModel(HistogramBucket bucket, double maxFraction)
{
    public string Label { get; } = bucket.Label;
    public int Count { get; } = bucket.Count;

    /// <summary>Bar height as a share of the tallest bar, so the chart fills its box.</summary>
    public double HeightFraction { get; } = maxFraction <= 0 ? 0 : bucket.Fraction / maxFraction;

    /// <summary>Pixel height against the fixed 64px chart area.</summary>
    public double BarHeight => Math.Max(HeightFraction * 64, bucket.Count > 0 ? 2 : 0);

    public string Tooltip { get; } = $"{bucket.Label}: {bucket.Count:N0} ({bucket.Fraction:P1})";
}

/// <summary>One field row in the Schema tab, with its type mix and distribution chart.</summary>
public sealed class SchemaFieldViewModel
{
    public SchemaFieldViewModel(SchemaField field)
    {
        Field = field;

        var distribution = SchemaCharts.Build(field.Path, field.Values);
        Distribution = distribution;

        var max = distribution.Buckets.Count == 0 ? 0 : distribution.Buckets.Max(b => b.Fraction);
        Bars = distribution.Buckets.Select(b => new ChartBarViewModel(b, max)).ToList();
    }

    public SchemaField Field { get; }
    public FieldDistribution Distribution { get; }
    public IReadOnlyList<ChartBarViewModel> Bars { get; }

    public string Path => Field.Path;
    public string TypeDescription => Field.TypeDescription;
    public string PresenceDescription => Field.PresenceDescription;
    public bool IsSparse => Field.IsSparse;
    public double PresencePercent => Field.Presence * 100;

    public string DominantType => Field.Types.Count > 0 ? Field.Types[0].TypeName : "—";

    public string DistributionSummary => Distribution.Summary;

    public bool HasChart => Bars.Count > 0;

    public string Examples => Field.Examples.Count == 0
        ? ""
        : string.Join("  ·  ", Field.Examples);

    /// <summary>A field holding more than one type is usually a bug worth surfacing.</summary>
    public bool HasMixedTypes => Field.Types.Count > 1;

    public string? MixedTypeWarning => HasMixedTypes
        ? $"{Field.Types.Count} types: {Field.TypeDescription}"
        : null;
}

/// <summary>
/// The Schema tab: samples documents and reports the fields found, their types, how
/// often each appears, and the distribution of their values.
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
