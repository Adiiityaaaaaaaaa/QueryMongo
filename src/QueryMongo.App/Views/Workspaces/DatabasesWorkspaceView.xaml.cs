using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Workspaces;

public sealed partial class DatabasesWorkspaceView : UserControl
{
    public DatabasesWorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(DatabasesWorkspaceViewModel), typeof(DatabasesWorkspaceView),
        new PropertyMetadata(null, (d, e) => ((DatabasesWorkspaceView)d).OnModelChanged(e)));

    public DatabasesWorkspaceViewModel Model
    {
        get => (DatabasesWorkspaceViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void OnModelChanged(DependencyPropertyChangedEventArgs e)
    {
        Trail.Items = (e.NewValue as DatabasesWorkspaceViewModel)?.Trail;
        Bindings.Update();
    }

    private void OnOpenDatabase(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DatabaseRowViewModel row }) Model.OpenDatabase(row.Name);
    }

    private void OnOpenShell(object sender, RoutedEventArgs e) => Model.OpenShell();

    private async void OnCreateDatabase(object sender, RoutedEventArgs e)
    {
        if (Model.Connection is not { } connection) return;

        var dialog = new CreateCollectionDialog { XamlRoot = XamlRoot, IsDatabaseMode = true };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ShellViewModel.CreateDatabaseAsync(
            connection, dialog.DatabaseName, dialog.CollectionName, dialog.BuildOptions());

        if (error is not null)
        {
            await Notify.ErrorAsync(XamlRoot, "Could not create database", error);
            return;
        }

        await Model.RefreshAsync();
    }

    private async void OnDropDatabase(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DatabaseRowViewModel row }) return;
        if (Model.Host is not { } host || Model.Connection is not { } connection) return;

        // Dropping a database destroys every collection in it, so the name must be typed.
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Drop database",
                $"This permanently deletes \"{row.Name}\" and every collection in it. This cannot be undone.",
                row.Name))
            return;

        var error = await host.DropDatabaseAsync(connection, row.Name);

        if (error is not null)
        {
            await Notify.ErrorAsync(XamlRoot, "Could not drop database", error);
            return;
        }

        await Model.RefreshAsync();
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => Reveal(sender, true);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => Reveal(sender, false);

    /// <summary>
    /// Compass keeps a row's actions hidden until the pointer is over it, so a long list
    /// reads as data rather than as a wall of buttons.
    /// </summary>
    private static void Reveal(object sender, bool shown)
    {
        if (sender is not FrameworkElement row) return;

        if (row.FindName("RowFill") is Border fill) fill.Opacity = shown ? 1 : 0;
        if (row.FindName("RowActions") is FrameworkElement actions) actions.Opacity = shown ? 1 : 0;
    }
}
