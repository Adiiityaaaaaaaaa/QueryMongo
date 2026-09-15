using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MongoDB.Bson;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// One stage in the pipeline builder. Holds the operator and its body separately so
/// the UI can offer an operator picker and still round-trip hand-edited text.
/// </summary>
public sealed partial class PipelineStageViewModel : ObservableObject
{
    /// <summary>The stages Compass offers in its stage picker.</summary>
    public static IReadOnlyList<string> Operators { get; } =
    [
        "$match", "$group", "$project", "$sort", "$limit", "$skip", "$unwind",
        "$lookup", "$addFields", "$set", "$unset", "$replaceRoot", "$count",
        "$facet", "$bucket", "$bucketAuto", "$sortByCount", "$sample",
        "$geoNear", "$graphLookup", "$redact", "$densify", "$fill",
        "$setWindowFields", "$documents", "$unionWith", "$out", "$merge"
    ];

    /// <summary>Starter bodies so a newly added stage is not an empty box.</summary>
    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        ["$match"] = "{\n  \n}",
        ["$group"] = "{\n  _id: null,\n  count: { $sum: 1 }\n}",
        ["$project"] = "{\n  \n}",
        ["$sort"] = "{\n  \n}",
        ["$limit"] = "10",
        ["$skip"] = "0",
        ["$unwind"] = "\"$field\"",
        ["$count"] = "\"total\"",
        ["$sample"] = "{ size: 10 }",
        ["$lookup"] = "{\n  from: \"\",\n  localField: \"\",\n  foreignField: \"\",\n  as: \"\"\n}",
        ["$unset"] = "\"field\"",
        ["$out"] = "\"targetCollection\"",
        ["$merge"] = "{ into: \"targetCollection\" }"
    };

    public PipelineStageViewModel(string op)
    {
        Operator = op;
        Body = Templates.TryGetValue(op, out var template) ? template : "{\n  \n}";
        IsEnabled = true;
        IsExpanded = true;
        PreviewSummary = "";
    }

    [ObservableProperty] public partial string Operator { get; set; }
    [ObservableProperty] public partial string Body { get; set; }

    /// <summary>Disabled stages stay in the list but are skipped when the pipeline runs.</summary>
    [ObservableProperty] public partial bool IsEnabled { get; set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string PreviewSummary { get; set; }

    public ObservableCollection<DocumentViewModel> Preview { get; } = [];

    /// <summary>True for stages that write to a collection rather than returning documents.</summary>
    public bool IsWritingStage => Operator is "$out" or "$merge";

    /// <summary>
    /// Builds the stage document. Returns null and sets <see cref="Error"/> when the
    /// body is not valid for this operator.
    /// </summary>
    public BsonDocument? TryBuild()
    {
        var text = $"{{ \"{Operator}\": {Body} }}";

        var parsed = BsonJson.ParseDocument(text);
        if (!parsed.IsValid)
        {
            Error = parsed.Error;
            return null;
        }

        Error = null;
        return parsed.Value;
    }
}

/// <summary>
/// The Aggregations tab: a stage list with per-stage previews, matching Compass's
/// pipeline builder.
/// </summary>
public sealed partial class AggregationViewModel : ObservableObject
{
    private readonly QueryService _queries;
    private readonly string _database;
    private readonly string _collection;

    private CancellationTokenSource? _inFlight;

    public AggregationViewModel(string database, string collection, QueryService queries)
    {
        _database = database;
        _collection = collection;
        _queries = queries;

        TextPipeline = "[\n  { \"$match\": {} }\n]";
        ResultSummary = "";

        Stages.Add(new PipelineStageViewModel("$match"));
    }

    public ObservableCollection<PipelineStageViewModel> Stages { get; } = [];

    public ObservableCollection<DocumentViewModel> Results { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; }

    /// <summary>Per-stage previews are helpful but cost a round trip per stage.</summary>
    [ObservableProperty] public partial bool AutoPreview { get; set; }

    /// <summary>Raw text editing of the whole pipeline, as an alternative to the stage list.</summary>
    [ObservableProperty] public partial bool IsTextMode { get; set; }

    [ObservableProperty] public partial string TextPipeline { get; set; }

    [ObservableProperty] public partial int SampleSize { get; set; } = 20;

    /// <summary>Set when the pipeline ends in $out or $merge, so the UI can warn first.</summary>
    [ObservableProperty] public partial bool WritesToCollection { get; set; }

    [ObservableProperty] public partial string? WriteTarget { get; set; }

    // ---- building the pipeline ------------------------------------------

    /// <summary>
    /// Collects the enabled stages. Returns null when any stage is invalid, leaving the
    /// per-stage error visible so the user can see which one.
    /// </summary>
    public List<BsonDocument>? TryBuildPipeline()
    {
        if (IsTextMode)
        {
            var parsed = BsonJson.ParsePipeline(TextPipeline);
            if (!parsed.IsValid)
            {
                ErrorMessage = parsed.Error;
                return null;
            }
            return parsed.Value;
        }

        var stages = new List<BsonDocument>();
        var failed = false;

        foreach (var stage in Stages.Where(s => s.IsEnabled))
        {
            var built = stage.TryBuild();
            if (built is null) failed = true;
            else stages.Add(built);
        }

        if (failed)
        {
            ErrorMessage = "One or more stages are not valid.";
            return null;
        }

        return stages;
    }

    private void UpdateWriteWarning(IReadOnlyList<BsonDocument> pipeline)
    {
        WritesToCollection = QueryService.PipelineWrites(pipeline);
        WriteTarget = WritesToCollection ? QueryService.PipelineTarget(pipeline) : null;
    }

    // ---- stage list editing ---------------------------------------------

    [RelayCommand]
    private void AddStage(string? op)
    {
        Stages.Add(new PipelineStageViewModel(
            string.IsNullOrWhiteSpace(op) ? "$match" : op));
    }

    [RelayCommand]
    private void RemoveStage(PipelineStageViewModel stage) => Stages.Remove(stage);

    [RelayCommand]
    private void MoveStageUp(PipelineStageViewModel stage)
    {
        var index = Stages.IndexOf(stage);
        if (index > 0) Stages.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveStageDown(PipelineStageViewModel stage)
    {
        var index = Stages.IndexOf(stage);
        if (index >= 0 && index < Stages.Count - 1) Stages.Move(index, index + 1);
    }

    [RelayCommand]
    private void DuplicateStage(PipelineStageViewModel stage)
    {
        var copy = new PipelineStageViewModel(stage.Operator) { Body = stage.Body };
        Stages.Insert(Stages.IndexOf(stage) + 1, copy);
    }

    [RelayCommand]
    private void ClearPipeline()
    {
        Stages.Clear();
        Results.Clear();
        Stages.Add(new PipelineStageViewModel("$match"));
        ResultSummary = "";
        ErrorMessage = null;
    }

    /// <summary>Switching to text mode carries the stage list across, and back again.</summary>
    [RelayCommand]
    private void ToggleTextMode()
    {
        if (!IsTextMode)
        {
            var stages = TryBuildPipeline();
            if (stages is not null)
            {
                TextPipeline = "[" + Environment.NewLine
                    + string.Join("," + Environment.NewLine,
                        stages.Select(s => "  " + BsonJson.ToCompactJson(s)))
                    + Environment.NewLine + "]";
            }
            IsTextMode = true;
            return;
        }

        var parsed = BsonJson.ParsePipeline(TextPipeline);
        if (parsed.IsValid && parsed.Value is { Count: > 0 } list)
        {
            Stages.Clear();
            foreach (var stage in list)
            {
                var name = stage.Names.FirstOrDefault() ?? "$match";
                Stages.Add(new PipelineStageViewModel(name)
                {
                    Body = BsonJson.ToPrettyJson(stage).Trim() is var _ && stage.TryGetValue(name, out var body)
                        ? RenderBody(body)
                        : "{}"
                });
            }
        }

        IsTextMode = false;
    }

    private static string RenderBody(BsonValue value) => value switch
    {
        BsonDocument doc => BsonJson.ToPrettyJson(doc),
        _ => value.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
        {
            OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson
        })
    };

    // ---- running ---------------------------------------------------------

    [RelayCommand]
    private async Task RunAsync()
    {
        var pipeline = TryBuildPipeline();
        if (pipeline is null) return;

        UpdateWriteWarning(pipeline);

        // A writing pipeline must be run explicitly through RunWriteAsync, so an
        // accidental Run cannot replace a collection.
        if (WritesToCollection)
        {
            StatusMessage = $"This pipeline writes to \"{WriteTarget}\". Use Run and save to execute it.";
            return;
        }

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
            var results = await _queries
                .AggregateAsync(_database, _collection, pipeline, SampleSize, cts.Token)
                .ConfigureAwait(true);

            Results.Clear();
            var i = 0;
            foreach (var doc in results) Results.Add(new DocumentViewModel(doc, ++i));

            ResultSummary = results.Count == 0
                ? "The pipeline returned no documents."
                : $"{results.Count} document(s)" + (results.Count >= SampleSize ? " (preview limit reached)" : "");

            if (AutoPreview) await RefreshStagePreviewsAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
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

    /// <summary>Executes a $out or $merge pipeline after the user has confirmed it.</summary>
    public async Task<string?> RunWriteAsync()
    {
        var pipeline = TryBuildPipeline();
        if (pipeline is null) return "The pipeline is not valid.";

        IsBusy = true;
        try
        {
            await _queries.RunWritingPipelineAsync(_database, _collection, pipeline)
                .ConfigureAwait(true);

            StatusMessage = $"Pipeline written to \"{WriteTarget}\".";
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fills each stage's preview with the output of the pipeline up to and including
    /// that stage, which is how Compass shows where a pipeline goes wrong.
    /// </summary>
    private async Task RefreshStagePreviewsAsync(CancellationToken ct)
    {
        var enabled = Stages.Where(s => s.IsEnabled).ToList();
        var pipeline = TryBuildPipeline();
        if (pipeline is null) return;

        for (var i = 0; i < enabled.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var stage = enabled[i];

            if (stage.IsWritingStage)
            {
                stage.PreviewSummary = "Writing stage — not previewed.";
                stage.Preview.Clear();
                continue;
            }

            try
            {
                var docs = await _queries
                    .PreviewStagesAsync(_database, _collection, pipeline, i + 1, 5, ct)
                    .ConfigureAwait(true);

                stage.Preview.Clear();
                var n = 0;
                foreach (var doc in docs) stage.Preview.Add(new DocumentViewModel(doc, ++n));

                stage.PreviewSummary = docs.Count == 0 ? "No documents at this stage." : $"{docs.Count} shown";
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                stage.PreviewSummary = e.Message;
                stage.Preview.Clear();
            }
        }
    }

    [RelayCommand]
    private async Task PreviewStagesAsync()
    {
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource();
            await RefreshStagePreviewsAsync(cts.Token).ConfigureAwait(true);
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

    [RelayCommand]
    private async Task ExplainAsync()
    {
        var pipeline = TryBuildPipeline();
        if (pipeline is null) return;

        IsBusy = true;
        try
        {
            var explain = await _queries
                .ExplainAggregateAsync(_database, _collection, pipeline)
                .ConfigureAwait(true);

            Results.Clear();
            Results.Add(new DocumentViewModel(explain, 1));
            ResultSummary = "Explain output";
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

    /// <summary>Generated driver code for the current pipeline.</summary>
    public string ExportPipeline(TargetLanguage language)
    {
        var pipeline = TryBuildPipeline() ?? [];
        return ExportToLanguage.Pipeline(language, _database, _collection, pipeline);
    }
}
