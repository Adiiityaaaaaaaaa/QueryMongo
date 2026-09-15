using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Workspaces;

public sealed partial class CollectionsWorkspaceView : UserControl
{
    public CollectionsWorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(CollectionsWorkspaceViewModel), typeof(CollectionsWorkspaceView),
        new PropertyMetadata(null, (d, e) => ((CollectionsWorkspaceView)d).OnModelChanged(e)));

    public CollectionsWorkspaceViewModel Model
    {
        get => (CollectionsWorkspaceViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void OnModelChanged(DependencyPropertyChangedEventArgs e)
    {
        Trail.Items = (e.NewValue as CollectionsWorkspaceViewModel)?.Trail;
        Bindings.Update();
    }

    private void OnOpenCollection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CollectionRowViewModel row }) Model.OpenCollection(row);
    }

    private void OnOpenShell(object sender, RoutedEventArgs e) => Model.OpenShell();

    private async void OnCreateCollection(object sender, RoutedEventArgs e)
    {
        if (Model.Connection is not { } connection) return;

        var dialog = new CreateCollectionDialog
        {
            XamlRoot = XamlRoot,
            IsDatabaseMode = false,
            DatabaseName = Model.Database
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ShellViewModel.CreateCollectionAsync(
            connection, Model.Database, dialog.CollectionName, dialog.BuildOptions());

        if (error is not null)
        {
            await Notify.ErrorAsync(XamlRoot, "Could not create collection", error);
            return;
        }

        await Model.RefreshAsync();
    }

    private async void OnDropCollection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionRowViewModel row }) return;
        if (Model.Host is not { } host || Model.Connection is not { } connection) return;

        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Drop collection",
                $"This permanently deletes \"{Model.Database}.{row.Name}\" and its indexes. " +
                "This cannot be undone.",
                row.Name))
            return;

        var error = await host.DropCollectionAsync(connection, Model.Database, row.Name);

        if (error is not null)
        {
            await Notify.ErrorAsync(XamlRoot, "Could not drop collection", error);
            return;
        }

        await Model.RefreshAsync();
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => Reveal(sender, true);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => Reveal(sender, false);

    private static void Reveal(object sender, bool shown)
    {
        if (sender is not FrameworkElement row) return;

        if (row.FindName("RowFill") is Border fill) fill.Opacity = shown ? 1 : 0;
        if (row.FindName("RowActions") is FrameworkElement actions) actions.Opacity = shown ? 1 : 0;
    }
}
