using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Json;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// One stage card in the plan tree. Depth drives the indent, so the tree reads
/// top-down from the stage that produced the final results.
/// </summary>
public sealed class ExplainNodeViewModel(ExplainNode node, int depth)
{
    public ExplainNode Node { get; } = node;
    public int Depth { get; } = depth;

    public string Stage => Node.Stage;
    public string Description => Node.Description;
    public string? Advice => Node.Advice;
    public bool HasAdvice => Node.Advice is not null;
    public bool IsProblem => Node.IsProblem;

    public double Indent => Depth * 24;

    public string Returned => $"{Node.Returned:N0} returned";
    public string Examined => Node.Examined > 0 ? $"{Node.Examined:N0} examined" : "";
    public bool ShowExamined => Node.Examined > 0;

    public string Elapsed => Node.Elapsed.TotalMilliseconds >= 1
        ? $"{Node.Elapsed.TotalMilliseconds:N0} ms"
        : "< 1 ms";

    public string IndexDescription => Node.IndexName is null
        ? ""
        : $"{Node.IndexName}  ({Node.KeyDescription})";

    public bool HasIndex => Node.IndexName is not null;

    public string Json => BsonJson.ToPrettyJson(Node.Raw);
}

/// <summary>The Explain pane: a visual plan tree plus the totals and the raw output.</summary>
public sealed partial class ExplainViewModel : ObservableObject
{
    private readonly QueryService _queries;
    private readonly string _database;
    private readonly string _collection;

    public ExplainViewModel(string database, string collection, QueryService queries)
    {
        _database = database;
        _collection = collection;
        _queries = queries;

        Summary = "Run a query on the Documents tab, then refresh to see its plan.";
        RawJson = "";
    }

    public ObservableCollection<ExplainNodeViewModel> Nodes { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string RawJson { get; set; }

    [ObservableProperty] public partial string Returned { get; set; } = "—";
    [ObservableProperty] public partial string DocsExamined { get; set; } = "—";
    [ObservableProperty] public partial string KeysExamined { get; set; } = "—";
    [ObservableProperty] public partial string ExecutionTime { get; set; } = "—";

    [ObservableProperty] public partial bool IsInefficient { get; set; }
    [ObservableProperty] public partial string? InefficiencyMessage { get; set; }
    [ObservableProperty] public partial bool HasPlan { get; set; }

    /// <summary>Explains the spec the Documents pane currently holds.</summary>
    public async Task RefreshAsync(QuerySpec spec)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var summary = await _queries.ExplainAsync(_database, _collection, spec).ConfigureAwait(true);
            var plan = ExplainPlan.Parse(summary.Raw);

            Nodes.Clear();
            if (plan.Root is not null) Add(plan.Root, 0);

            Returned = $"{plan.TotalReturned:N0}";
            DocsExamined = $"{plan.TotalDocsExamined:N0}";
            KeysExamined = $"{plan.TotalKeysExamined:N0}";
            ExecutionTime = plan.ExecutionTime.TotalMilliseconds >= 1
                ? $"{plan.ExecutionTime.TotalMilliseconds:N0} ms"
                : "< 1 ms";

            IsInefficient = plan.IsInefficient;
            InefficiencyMessage = plan.IsInefficient
                ? $"The server examined {plan.ExaminedPerReturned:N1} documents for every one returned. " +
                  "An index on the filtered fields would cut that."
                : null;

            RawJson = BsonJson.ToPrettyJson(plan.Raw);
            HasPlan = plan.Root is not null;

            Summary = plan.Root is null
                ? "The server returned no execution plan."
                : $"{Nodes.Count} stage(s) · {plan.Namespace}";
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
            Nodes.Clear();
            HasPlan = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Add(ExplainNode node, int depth)
    {
        Nodes.Add(new ExplainNodeViewModel(node, depth));

        foreach (var child in node.Children) Add(child, depth + 1);
    }
}
