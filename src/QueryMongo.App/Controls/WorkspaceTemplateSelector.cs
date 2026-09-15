using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Controls;

/// <summary>
/// Picks the view for whatever workspace is on the active tab. This is the counterpart of
/// Compass's workspace registry, where each workspace type names the component that
/// renders it.
/// </summary>
public sealed partial class WorkspaceTemplateSelector : DataTemplateSelector
{
    /// <summary>
    /// Builds the selector from the templates in <c>Themes/Workspaces.xaml</c>. They are
    /// looked up by key rather than assigned in markup, because the XAML binary format
    /// will not set plain properties on a selector through property-element syntax.
    /// </summary>
    public static WorkspaceTemplateSelector FromResources(ResourceDictionary resources) => new()
    {
        Welcome = Template(resources, "WelcomeWorkspaceTemplate"),
        MyQueries = Template(resources, "MyQueriesWorkspaceTemplate"),
        Databases = Template(resources, "DatabasesWorkspaceTemplate"),
        Collections = Template(resources, "CollectionsWorkspaceTemplate"),
        Collection = Template(resources, "CollectionWorkspaceTemplate"),
        Shell = Template(resources, "ShellWorkspaceTemplate"),
        Performance = Template(resources, "PerformanceWorkspaceTemplate")
    };

    private static DataTemplate? Template(ResourceDictionary resources, string key) =>
        resources.TryGetValue(key, out var value) ? value as DataTemplate : null;

    public DataTemplate? Welcome { get; set; }
    public DataTemplate? MyQueries { get; set; }
    public DataTemplate? Databases { get; set; }
    public DataTemplate? Collections { get; set; }
    public DataTemplate? Collection { get; set; }
    public DataTemplate? Shell { get; set; }
    public DataTemplate? Performance { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        WelcomeWorkspaceViewModel => Welcome,
        MyQueriesWorkspaceViewModel => MyQueries,
        DatabasesWorkspaceViewModel => Databases,
        CollectionsWorkspaceViewModel => Collections,
        CollectionTabViewModel => Collection,
        ShellWorkspaceViewModel => Shell,
        PerformanceWorkspaceViewModel => Performance,
        _ => null
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
