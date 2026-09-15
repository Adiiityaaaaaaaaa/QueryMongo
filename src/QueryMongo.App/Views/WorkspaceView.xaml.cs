using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Views;

public sealed partial class WorkspaceView : UserControl
{
    public WorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(WorkspaceView),
        new PropertyMetadata(null, OnShellChanged));

    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>
    /// The empty state and the tab host swap on the same grid cell, and both depend on
    /// shell state, so they are recomputed whenever the shell raises a change.
    /// </summary>
    public bool ShowEmptyState => Shell is null || (!Shell.IsPerformanceOpen && Shell.Tabs.Count == 0);

    public bool ShowTabs => Shell is not null && !Shell.IsPerformanceOpen && Shell.Tabs.Count > 0;

    private static void OnShellChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not WorkspaceView view || e.NewValue is not ShellViewModel shell) return;

        shell.Tabs.CollectionChanged += (_, _) => view.RefreshPanes();
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.IsPerformanceOpen)
                or nameof(ShellViewModel.ActiveTab))
                view.RefreshPanes();
        };
    }

    private void RefreshPanes()
    {
        Bindings.Update();
    }

    // ---- collections -----------------------------------------------------

    /// <summary>The whole database row toggles, so the chevron is not a separate target.</summary>
    private void OnToggleDatabase(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DatabaseNodeViewModel node })
            node.IsExpanded = !node.IsExpanded;
    }

    private async void OnCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionNodeViewModel node }) return;
        await Shell.OpenCollectionAsync(node.Database, node.Name, node.Kind);
    }

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is CollectionTabViewModel tab) Shell.CloseTabCommand.Execute(tab);
    }

    // ---- create / drop ---------------------------------------------------

    private async void OnCreateDatabase(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateCollectionDialog { XamlRoot = XamlRoot, IsDatabaseMode = true };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await Shell.CreateDatabaseAsync(
            dialog.DatabaseName, dialog.CollectionName, dialog.BuildOptions());

        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not create database", error);
    }

    private async void OnCreateCollection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DatabaseNodeViewModel node }) return;

        var dialog = new CreateCollectionDialog
        {
            XamlRoot = XamlRoot,
            IsDatabaseMode = false,
            DatabaseName = node.Name
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await Shell.CreateCollectionAsync(
            node.Name, dialog.CollectionName, dialog.BuildOptions());

        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not create collection", error);
    }

    private async void OnDropDatabase(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DatabaseNodeViewModel node }) return;

        // Dropping a database destroys every collection in it, so the name must be typed.
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Drop database",
                $"This permanently deletes \"{node.Name}\" and every collection in it. This cannot be undone.",
                node.Name))
            return;

        var error = await Shell.DropDatabaseAsync(node.Name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop database", error);
    }

    private async void OnDropCollection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionNodeViewModel node }) return;

        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Drop collection",
                $"This permanently deletes \"{node.Database}.{node.Name}\" and its indexes. This cannot be undone.",
                node.Name))
            return;

        var error = await Shell.DropCollectionAsync(node.Database, node.Name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop collection", error);
    }

    private async void OnRenameCollection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionNodeViewModel node }) return;

        var name = await Notify.PromptAsync(
            XamlRoot, "Rename collection", "New name", node.Name);

        if (string.IsNullOrWhiteSpace(name) || name == node.Name) return;

        var error = await Shell.RenameCollectionAsync(node.Database, node.Name, name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not rename collection", error);
    }

    private async void OnClearCollection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionNodeViewModel node }) return;

        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Delete all documents",
                $"This deletes every document in \"{node.Database}.{node.Name}\". " +
                "Indexes and validation rules are kept. This cannot be undone.",
                node.Name))
            return;

        var error = await Shell.ClearCollectionAsync(node.Database, node.Name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not delete documents", error);
    }

    // ---- performance -----------------------------------------------------

    private async void OnKillOperation(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CurrentOperation operation }) return;
        if (Shell.Performance is null) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot,
                "Kill operation",
                $"Stop operation {operation.OperationId} on {operation.Namespace}?",
                "Kill"))
            return;

        var error = await Shell.Performance.KillAsync(operation);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not kill operation", error);
    }
}
