using CommunityToolkit.Mvvm.ComponentModel;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The kinds of thing a tab can hold. Compass calls these workspaces: a tab is not
/// necessarily a collection, it can equally be the welcome screen, a server's database
/// list, a shell, or the performance charts.
/// </summary>
public enum WorkspaceKind
{
    Welcome,
    MyQueries,
    Databases,
    Collections,
    Collection,
    Shell,
    Performance
}

/// <summary>
/// One tab in the workspace strip.
///
/// Every tab carries the connection it belongs to, because several deployments can be
/// open at once and a tab has to keep naming its own server after other tabs move around.
/// </summary>
public abstract partial class WorkspaceTabViewModel : ObservableObject
{
    protected WorkspaceTabViewModel(
        Guid? connectionId, string? connectionName, string? colorCode = null)
    {
        ConnectionId = connectionId;
        ConnectionName = connectionName;
        ColorCode = colorCode;
    }

    /// <summary>Identity for the tab strip; two tabs on the same namespace are still distinct.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public abstract WorkspaceKind Kind { get; }

    /// <summary>A LeafyGreen glyph name, the same one Compass gives this workspace.</summary>
    public abstract string IconGlyph { get; }

    /// <summary>What the tab is labelled with.</summary>
    public abstract string Title { get; }

    /// <summary>Null for the workspaces that do not belong to a server, such as Welcome.</summary>
    public Guid? ConnectionId { get; }

    public string? ConnectionName { get; }

    /// <summary>
    /// The connection's colour tag, drawn as a bar along the tab's top edge so tabs from
    /// different deployments are told apart at a glance.
    /// </summary>
    public string? ColorCode { get; }

    public bool HasColor => ColorCode is not null;

    /// <summary>Set by the shell; the tab strip draws the selected tab differently.</summary>
    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>
    /// The shell that opened this tab. Workspaces navigate by asking the shell to open
    /// something, rather than each view reaching for global state.
    /// </summary>
    public ShellViewModel? Host { get; internal set; }

    /// <summary>The connection this workspace belongs to, while it is still open.</summary>
    public ConnectionViewModel? Connection => Host?.FindConnection(ConnectionId);

    /// <summary>
    /// Label/value pairs for the tab's tooltip. Compass shows the connection, database
    /// and collection this way so identically named namespaces stay distinguishable.
    /// </summary>
    public virtual IReadOnlyList<(string Label, string Value)> Tooltip { get; } = [];

    public bool HasTooltip => Tooltip.Count > 0;

    private bool _activated;

    /// <summary>
    /// Runs the tab's first load, once. Tabs are created eagerly when a namespace is
    /// opened but nothing is queried until the tab is actually looked at.
    /// </summary>
    public async Task EnsureActivatedAsync()
    {
        if (_activated) return;
        _activated = true;

        await ActivateAsync().ConfigureAwait(true);
    }

    /// <summary>Loads whatever the tab shows. Called once, by <see cref="EnsureActivatedAsync"/>.</summary>
    protected virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>
    /// Whether opening something else may take this tab's place. Compass replaces the
    /// active tab by default, but a tab holding work that is not saved anywhere else
    /// refuses, and the new workspace opens beside it instead.
    /// </summary>
    public virtual bool CanBeReplaced => true;

    /// <summary>Releases anything the tab owns when it is closed.</summary>
    public virtual void Close() { }
}

/// <summary>The screen shown before anything is open.</summary>
public sealed class WelcomeWorkspaceViewModel() : WorkspaceTabViewModel(null, null)
{
    public override WorkspaceKind Kind => WorkspaceKind.Welcome;
    public override string IconGlyph => "Sparkle";
    public override string Title => "Welcome";
}

/// <summary>Saved queries and pipelines, across every connection.</summary>
public sealed class MyQueriesWorkspaceViewModel : WorkspaceTabViewModel
{
    public MyQueriesWorkspaceViewModel(SavedQueriesViewModel queries)
        : base(null, null) => Queries = queries;

    public SavedQueriesViewModel Queries { get; }

    public override WorkspaceKind Kind => WorkspaceKind.MyQueries;
    public override string IconGlyph => "CurlyBraces";
    public override string Title => "My Queries";

    protected override Task ActivateAsync() => Queries.LoadAsync();
}
