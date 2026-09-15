using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;
using Windows.ApplicationModel.DataTransfer;

namespace QueryMongo.App.Views;

public sealed partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();

        WorkspaceHost.ContentTemplateSelector =
            WorkspaceTemplateSelector.FromResources(Application.Current.Resources);
    }

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(WorkspaceView),
        new PropertyMetadata(null, OnShellChanged));

    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    private static void OnShellChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not WorkspaceView view || e.NewValue is not ShellViewModel shell) return;

        shell.Tabs.CollectionChanged += (_, _) => view.Bindings.Update();
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.ActiveTab)
                or nameof(ShellViewModel.HasConnections)
                or nameof(ShellViewModel.ConnectionCountLabel))
                view.Bindings.Update();
        };
    }

    // ---- sidebar chrome --------------------------------------------------

    /// <summary>
    /// Drags the sidebar's right edge. Compass allows 210 to 600 pixels; outside that the
    /// tree stops being readable at one end and wastes the window at the other.
    /// </summary>
    private void OnSidebarResize(object sender, ManipulationDeltaRoutedEventArgs e) =>
        Sidebar.Width = Math.Clamp(Sidebar.Width + e.Delta.Translation.X, 210, 600);

    private void OnSplitterPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private void OnSplitterPointerExited(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => Reveal(sender, true);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => Reveal(sender, false);

    /// <summary>
    /// Compass keeps a row's overflow menu hidden until the pointer is on the row, so a
    /// tree of hundreds of collections reads as names rather than as buttons.
    /// </summary>
    private static void Reveal(object sender, bool shown)
    {
        if (sender is not FrameworkElement row) return;

        if (row.FindName("RowFill") is Border fill) fill.Opacity = shown ? 1 : 0;
        if (row.FindName("RowActions") is FrameworkElement actions) actions.Opacity = shown ? 1 : 0;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) =>
        _ = Notify.ErrorAsync(XamlRoot, "Settings", "Settings are not built yet.");

    private void OnOpenMyQueries(object sender, RoutedEventArgs e) => Shell.OpenMyQueriesCommand.Execute(null);

    private void OnNewTab(object sender, RoutedEventArgs e) => Shell.OpenWelcomeCommand.Execute(null);

    // ---- tab strip -------------------------------------------------------

    private void OnTabSelected(object? sender, WorkspaceTabViewModel tab) =>
        Shell.SelectTabCommand.Execute(tab);

    private void OnTabClosed(object? sender, WorkspaceTabViewModel tab) =>
        Shell.CloseTabCommand.Execute(tab);

    private void OnTabCloseOthers(object? sender, WorkspaceTabViewModel tab) =>
        Shell.CloseOtherTabsCommand.Execute(tab);

    // ---- connections -----------------------------------------------------

    private async void OnNewConnection(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionDialog { XamlRoot = XamlRoot };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Shell.AddConnectionAsync(
                dialog.Uri, dialog.ConnectionName, dialog.BuildSshOptions(), dialog.ColorCode);

            if (error is null) return;

            // Keep what was typed so a bad host or password can be corrected in place.
            dialog.ErrorMessage = error;
        }
    }

    private void OnToggleConnection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ConnectionViewModel connection } && connection.CanExpand)
            connection.IsExpanded = !connection.IsExpanded;
    }

    /// <summary>
    /// Clicking a connection connects it when closed, and opens its database list when
    /// already open, which is what Compass does with the same click.
    /// </summary>
    private async void OnConnectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionViewModel connection }) return;

        if (connection.IsConnected)
        {
            connection.IsExpanded = true;
            Shell.OpenDatabases(connection);
            return;
        }

        await Shell.ConnectCommand.ExecuteAsync(connection);
    }

    /// <summary>
    /// The connection overflow menu. The items and their order are Compass's, from
    /// <c>item-actions.ts</c>: what you can do now, then a rule, then how the saved
    /// connection itself is managed.
    /// </summary>
    private void OnConnectionActions(object sender, RightTappedRoutedEventArgs e) =>
        OnConnectionActions(sender, (RoutedEventArgs)e);

    private void OnConnectionActions(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionViewModel connection } anchor) return;

        var menu = new MenuFlyout();

        if (connection.IsConnected)
        {
            Add(menu, "Refresh databases", "Refresh", async () =>
                await connection.RefreshDatabasesAsync());
            Add(menu, "Create database", "Plus", () => CreateDatabase(connection));
            Add(menu, "Open MongoDB shell", "Shell", () => Shell.OpenShellCommand.Execute(connection));
            Add(menu, "View performance metrics", "Gauge", () =>
                Shell.OpenPerformanceCommand.Execute(connection));
            Add(menu, "Show connection info", "InfoWithCircle", () => ShowConnectionInfo(connection));
            Add(menu, "Disconnect", "Disconnect", () => Shell.DisconnectCommand.Execute(connection));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else
        {
            Add(menu, "Connect", "Connect", async () =>
                await Shell.ConnectCommand.ExecuteAsync(connection));
        }

        Add(menu, "Rename connection", "Edit", () => RenameConnection(connection));
        Add(menu, "Copy connection string", "Copy", () => CopyConnectionString(connection));
        Add(menu,
            connection.Profile.IsFavorite ? "Unfavorite connection" : "Favorite connection",
            "Favorite",
            async () => await Shell.ToggleFavoriteCommand.ExecuteAsync(connection));

        AddColorPicker(menu, connection);

        Add(menu, "Remove connection", "Trash", () => ForgetConnection(connection));

        menu.ShowAt(anchor, MenuPlacement);
    }

    private void OnDatabaseActions(object sender, RightTappedRoutedEventArgs e) =>
        OnDatabaseActions(sender, (RoutedEventArgs)e);

    private void OnDatabaseActions(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DatabaseNodeViewModel node } anchor) return;
        if (OwningConnection(node) is not { } connection) return;

        var menu = new MenuFlyout();

        Add(menu, "Create collection", "Plus", () => CreateCollection(connection, node.Name));
        Add(menu, "Drop database", "Trash", () => DropDatabase(connection, node.Name));

        menu.ShowAt(anchor, MenuPlacement);
    }

    private void OnCollectionActions(object sender, RightTappedRoutedEventArgs e) =>
        OnCollectionActions(sender, (RoutedEventArgs)e);

    private void OnCollectionActions(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionNodeViewModel node } anchor) return;
        if (OwningConnection(node.Database) is not { } connection) return;

        var menu = new MenuFlyout();

        Add(menu, "Open in new tab", "OpenNewTab", () =>
            Shell.OpenCollection(connection, node.Database, node.Name, node.Kind, inNewTab: true));

        menu.Items.Add(new MenuFlyoutSeparator());

        if (node.Kind is Core.Models.CollectionKind.Collection)
            Add(menu, "Rename collection", "Edit", () => RenameCollection(connection, node));

        Add(menu, "Delete all documents", "Eraser", () => ClearCollection(connection, node));
        Add(menu,
            node.Kind == Core.Models.CollectionKind.View ? "Drop view" : "Drop collection",
            "Trash",
            () => DropCollection(connection, node));

        menu.ShowAt(anchor, MenuPlacement);
    }

    /// <summary>
    /// Opens a menu under whatever triggered it. A right-click on the row and a click on
    /// the overflow button both land here, so both are anchored the same way.
    /// </summary>
    private static readonly FlyoutShowOptions MenuPlacement = new()
    {
        Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
        ShowMode = FlyoutShowMode.Standard
    };

    private static void Add(MenuFlyout menu, string label, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = label, Icon = LeafyIconSource.For(glyph) };
        item.Click += (_, _) => action();

        menu.Items.Add(item);
    }

    private static void Add(MenuFlyout menu, string label, string glyph, Func<Task> action) =>
        Add(menu, label, glyph, () => _ = action());

    /// <summary>
    /// The colour tag submenu. Compass keeps this in the connection form; having it on
    /// the row as well means a connection can be marked without reopening the form.
    /// </summary>
    private void AddColorPicker(MenuFlyout menu, ConnectionViewModel connection)
    {
        var colors = new MenuFlyoutSubItem { Text = "Colour" };

        var none = new MenuFlyoutItem { Text = "None" };
        none.Click += async (_, _) => await Shell.SetConnectionColorAsync(connection, null);
        colors.Items.Add(none);

        foreach (var (code, name) in ConnectionColors.All)
        {
            if (code == ConnectionColors.Default) continue;

            var item = new MenuFlyoutItem { Text = name };
            item.Click += async (_, _) => await Shell.SetConnectionColorAsync(connection, code);
            colors.Items.Add(item);
        }

        menu.Items.Add(colors);
    }

    // ---- tree ------------------------------------------------------------

    private void OnToggleDatabase(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DatabaseNodeViewModel node })
            node.IsExpanded = !node.IsExpanded;
    }

    private void OnDatabaseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DatabaseNodeViewModel node }) return;
        if (OwningConnection(node) is not { } connection) return;

        node.IsExpanded = true;
        Shell.OpenCollections(connection, node.Name);
    }

    private void OnCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CollectionNodeViewModel node }) return;
        if (OwningConnection(node.Database) is not { } connection) return;

        Shell.OpenCollection(connection, node.Database, node.Name, node.Kind);
    }

    /// <summary>
    /// Finds which open connection owns a node. Templated rows carry only their own view
    /// model, so the connection is resolved by looking for the node in each open tree.
    /// </summary>
    private ConnectionViewModel? OwningConnection(DatabaseNodeViewModel node) =>
        Shell.Connections.FirstOrDefault(c => c.IsConnected && c.Databases.Contains(node));

    private ConnectionViewModel? OwningConnection(string database) =>
        Shell.Connections.FirstOrDefault(c => c.IsConnected && c.Databases.Any(d => d.Name == database));

    // ---- actions ---------------------------------------------------------

    private async void CreateDatabase(ConnectionViewModel connection)
    {
        var dialog = new CreateCollectionDialog { XamlRoot = XamlRoot, IsDatabaseMode = true };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ShellViewModel.CreateDatabaseAsync(
            connection, dialog.DatabaseName, dialog.CollectionName, dialog.BuildOptions());

        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not create database", error);
    }

    private async void CreateCollection(ConnectionViewModel connection, string database)
    {
        var dialog = new CreateCollectionDialog
        {
            XamlRoot = XamlRoot,
            IsDatabaseMode = false,
            DatabaseName = database
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ShellViewModel.CreateCollectionAsync(
            connection, database, dialog.CollectionName, dialog.BuildOptions());

        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not create collection", error);
    }

    private async void DropDatabase(ConnectionViewModel connection, string database)
    {
        // Dropping a database destroys every collection in it, so the name must be typed.
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Drop database",
                $"This permanently deletes \"{database}\" and every collection in it. This cannot be undone.",
                database))
            return;

        var error = await Shell.DropDatabaseAsync(connection, database);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop database", error);
    }

    private async void DropCollection(ConnectionViewModel connection, CollectionNodeViewModel node)
    {
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Drop collection",
                $"This permanently deletes \"{node.Database}.{node.Name}\" and its indexes. " +
                "This cannot be undone.",
                node.Name))
            return;

        var error = await Shell.DropCollectionAsync(connection, node.Database, node.Name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop collection", error);
    }

    private async void RenameCollection(ConnectionViewModel connection, CollectionNodeViewModel node)
    {
        var name = await Notify.PromptAsync(XamlRoot, "Rename collection", "New name", node.Name);
        if (string.IsNullOrWhiteSpace(name) || name == node.Name) return;

        var error = await Shell.RenameCollectionAsync(connection, node.Database, node.Name, name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not rename collection", error);
    }

    private async void ClearCollection(ConnectionViewModel connection, CollectionNodeViewModel node)
    {
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Delete all documents",
                $"This deletes every document in \"{node.Database}.{node.Name}\". " +
                "Indexes and validation rules are kept. This cannot be undone.",
                node.Name))
            return;

        var error = await Shell.ClearCollectionAsync(connection, node.Database, node.Name);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not delete documents", error);
    }

    private async void RenameConnection(ConnectionViewModel connection)
    {
        var name = await Notify.PromptAsync(XamlRoot, "Rename connection", "Name", connection.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        await Shell.RenameConnectionAsync(connection, name);
    }

    private async void ForgetConnection(ConnectionViewModel connection)
    {
        if (!await Notify.ConfirmAsync(
                XamlRoot, "Remove connection",
                $"Remove \"{connection.Name}\" from the list? The deployment itself is untouched.",
                "Remove"))
            return;

        await Shell.ForgetConnectionCommand.ExecuteAsync(connection);
    }

    /// <summary>Copies the connection string with its password left in, since that is the point.</summary>
    private static void CopyConnectionString(ConnectionViewModel connection)
    {
        var package = new DataPackage();
        package.SetText(connection.Profile.ConnectionString);

        Clipboard.SetContent(package);
    }

    private async void ShowConnectionInfo(ConnectionViewModel connection)
    {
        await Notify.ShowCodeAsync(
            XamlRoot,
            $"Connection: {connection.Name}",
            string.Join(
                Environment.NewLine,
                $"URI       {connection.RedactedUri}",
                $"Status    {connection.State}",
                $"Detail    {connection.Detail}"));
    }
}
