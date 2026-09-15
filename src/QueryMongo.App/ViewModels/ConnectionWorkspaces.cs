using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>A shell opened against a whole connection rather than one collection.</summary>
public sealed class ShellWorkspaceViewModel : WorkspaceTabViewModel
{
    public ShellWorkspaceViewModel(
        Guid connectionId, string connectionName, string? colorCode, MongoShellViewModel shell)
        : base(connectionId, connectionName, colorCode) => Shell = shell;

    public MongoShellViewModel Shell { get; }

    public override WorkspaceKind Kind => WorkspaceKind.Shell;
    public override string IconGlyph => "Shell";

    // Compass labels the shell after the connection it is attached to, because several
    // shells against different servers can be open at once.
    public override string Title =>
        string.IsNullOrEmpty(ConnectionName) ? "MongoDB Shell" : $"mongosh: {ConnectionName}";

    public override IReadOnlyList<(string Label, string Value)> Tooltip =>
        string.IsNullOrEmpty(ConnectionName) ? [] : [("mongosh", ConnectionName)];
}

/// <summary>The live server metrics for one connection.</summary>
public sealed class PerformanceWorkspaceViewModel : WorkspaceTabViewModel
{
    public PerformanceWorkspaceViewModel(
        Guid connectionId, string connectionName, string? colorCode, PerformanceViewModel performance)
        : base(connectionId, connectionName, colorCode) => Performance = performance;

    public PerformanceViewModel Performance { get; }

    public override WorkspaceKind Kind => WorkspaceKind.Performance;
    public override string IconGlyph => "Gauge";
    public override string Title => $"Performance: {ConnectionName}";

    public override IReadOnlyList<(string Label, string Value)> Tooltip =>
        [("Performance", ConnectionName ?? "")];

    protected override Task ActivateAsync()
    {
        Performance.SampleCommand.Execute(null);
        return Task.CompletedTask;
    }

    public override void Close() => Performance.Dispose();
}

/// <summary>
/// Saved queries and favourites across every connection, which is what Compass's
/// "My Queries" workspace lists.
/// </summary>
public sealed partial class SavedQueriesViewModel : ObservableObject
{
    private readonly QueryHistoryStore _history;
    private readonly List<SavedQuery> _all = [];

    public SavedQueriesViewModel(QueryHistoryStore history)
    {
        _history = history;
        Filter = "";
    }

    public ObservableCollection<SavedQuery> Queries { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && Queries.Count == 0;

    /// <summary>Raised when a saved query is opened, so the shell can put it on a tab.</summary>
    public event EventHandler<SavedQuery>? Opened;

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var saved = await _history.LoadAsync().ConfigureAwait(true);

            _all.Clear();
            // Favourites first, then the rest by how recently they were used, which is the
            // order Compass lists them in.
            _all.AddRange(saved
                .OrderByDescending(q => q.IsFavorite)
                .ThenByDescending(q => q.LastUsedUtc));

            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private void Open(SavedQuery query) => Opened?.Invoke(this, query);

    [RelayCommand]
    private async Task DeleteAsync(SavedQuery query)
    {
        await _history.DeleteAsync(query.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = Filter.Trim();

        Queries.Clear();

        foreach (var query in _all)
            if (term.Length == 0
                || query.Namespace.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (query.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                Queries.Add(query);

        OnPropertyChanged(nameof(IsEmpty));
    }
}
