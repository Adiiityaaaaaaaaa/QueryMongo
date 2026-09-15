using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace QueryMongo.App.Views.Panes;

public sealed partial class DocumentsPane : UserControl
{
    public DocumentsPane()
    {
        InitializeComponent();

        // Show the page size the view model actually starts on.
        Loaded += (_, _) => SelectPageSize(Model?.Limit ?? 50);
    }

    private void SelectPageSize(int limit)
    {
        foreach (var item in PageSizeBox.Items.OfType<ComboBoxItem>())
            if (item.Tag is string tag && int.TryParse(tag, out var value) && value == limit)
            {
                PageSizeBox.SelectedItem = item;
                return;
            }

        PageSizeBox.SelectedItem = null;
    }

    private async void OnPageSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Model is null) return;
        if (PageSizeBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (!int.TryParse(tag, out var limit) || limit == Model.Limit) return;

        Model.Limit = limit;

        // Changing the page size while looking at page three would leave the view on a
        // page that no longer exists, so paging starts again from the top.
        Model.Skip = 0;

        await Model.RunQueryAsync();
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(DocumentsViewModel), typeof(DocumentsPane), new PropertyMetadata(null));

    public DocumentsViewModel Model
    {
        get => (DocumentsViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public static readonly DependencyProperty ShowScanWarningProperty = DependencyProperty.Register(
        nameof(ShowScanWarning), typeof(bool), typeof(DocumentsPane), new PropertyMetadata(false));

    /// <summary>Owned by the tab, which runs the explain behind it.</summary>
    public bool ShowScanWarning
    {
        get => (bool)GetValue(ShowScanWarningProperty);
        set => SetValue(ShowScanWarningProperty, value);
    }

    /// <summary>
    /// Raised when the Explain button is used. The plan is owned by the collection tab,
    /// not by this pane, so the tab decides where to show it.
    /// </summary>
    public event EventHandler? ExplainRequested;

    /// <summary>Raised for import; the collection tab owns the transfer service.</summary>
    public event EventHandler? ImportRequested;

    /// <summary>Raised for export. True exports the whole collection, not just the results.</summary>
    public event EventHandler<bool>? ExportRequested;

    // ---- query bar -------------------------------------------------------

    private void OnExplain(object sender, RoutedEventArgs e) =>
        ExplainRequested?.Invoke(this, EventArgs.Empty);

    private void OnImportFile(object sender, RoutedEventArgs e) =>
        ImportRequested?.Invoke(this, EventArgs.Empty);

    private void OnExportResults(object sender, RoutedEventArgs e) =>
        ExportRequested?.Invoke(this, false);

    private void OnExportCollection(object sender, RoutedEventArgs e) =>
        ExportRequested?.Invoke(this, true);

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        // Enter runs the query, which is what anyone typing a filter expects.
        Model.RunQueryCommand.Execute(null);
        e.Handled = true;
    }

    private void OnShowList(object sender, RoutedEventArgs e) =>
        Model.ViewMode = DocumentViewMode.List;

    private void OnShowTable(object sender, RoutedEventArgs e) =>
        Model.ViewMode = DocumentViewMode.Table;

    private void OnShowJson(object sender, RoutedEventArgs e) =>
        Model.ViewMode = DocumentViewMode.Json;

    /// <summary>Keeps the table header aligned with the horizontally scrolled rows.</summary>
    private void OnTableScrolled(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer body)
            HeaderScroll.ChangeView(body.HorizontalOffset, null, null, disableAnimation: true);
    }

    // ---- per-document actions -------------------------------------------

    /// <summary>Compass reveals a document's actions only while the pointer is on its card.</summary>
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => RevealActions(sender, true);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => RevealActions(sender, false);

    private static void RevealActions(object sender, bool shown)
    {
        if (sender is FrameworkElement card && card.FindName("CardActions") is FrameworkElement actions)
            actions.Opacity = shown ? 1 : 0;
    }

    private void OnShowMoreFields(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentViewModel doc }) doc.ShowMoreFields();
    }

    private void OnShowFewerFields(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentViewModel doc }) doc.ShowFewerFields();
    }

    /// <summary>Expands or collapses one field row inside a document card.</summary>
    private void OnToggleField(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentFieldViewModel field })
            field.IsExpanded = !field.IsExpanded;
    }

    private void OnToggleRawJson(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentViewModel doc })
            doc.ShowRawJson = !doc.ShowRawJson;
    }

    private void OnEditDocument(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentViewModel doc }) doc.BeginEdit();
    }

    private void OnCancelEdit(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentViewModel doc }) doc.CancelEdit();
    }

    private async void OnSaveDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;
        await Model.SaveEditCommand.ExecuteAsync(doc);
    }

    private void OnCopyDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;

        var package = new DataPackage();
        package.SetText(doc.Json);
        Clipboard.SetContent(package);
    }

    private async void OnCloneDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot, "Clone document",
                "Insert a copy of this document with a new _id?", "Clone"))
            return;

        await Model.CloneDocumentCommand.ExecuteAsync(doc);
    }

    private async void OnDeleteDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot, "Delete document",
                $"Permanently delete the document with _id {doc.IdDescription}? This cannot be undone.",
                "Delete"))
            return;

        await Model.DeleteDocumentCommand.ExecuteAsync(doc);
    }

    // ---- collection-wide actions ----------------------------------------

    private async void OnInsertDocument(object sender, RoutedEventArgs e)
    {
        var dialog = new JsonEditorDialog
        {
            XamlRoot = XamlRoot,
            Title = "Insert document",
            PrimaryButtonText = "Insert",
            Json = "{\n  \n}"
        };

        // Looping keeps the typed document on screen when the server rejects it.
        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Model.InsertAsync(dialog.Json);
            if (error is null) return;
            dialog.ErrorMessage = error;
        }
    }

    private async void OnUpdateMany(object sender, RoutedEventArgs e)
    {
        var dialog = new JsonEditorDialog
        {
            XamlRoot = XamlRoot,
            Title = "Update matching documents",
            PrimaryButtonText = "Update",
            Description = $"Applies an update to all {Model.MatchingCount:N0} document(s) " +
                          "matching the current filter.",
            Json = "{\n  \"$set\": {\n    \n  }\n}"
        };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Model.UpdateManyAsync(dialog.Json);
            if (error is null) return;
            dialog.ErrorMessage = error;
        }
    }

    private async void OnDeleteMany(object sender, RoutedEventArgs e)
    {
        var parts = Model.Namespace.Split('.');
        var collection = parts.Length > 1 ? parts[^1] : Model.Namespace;

        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Delete matching documents",
                $"This permanently deletes all {Model.MatchingCount:N0} document(s) matching the " +
                $"current filter in \"{Model.Namespace}\". This cannot be undone.",
                collection))
            return;

        var error = await Model.DeleteManyAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not delete documents", error);
    }

    private async void OnExportQuery(object sender, RoutedEventArgs e)
    {
        var language = await LanguagePicker.PickAsync(XamlRoot);
        if (language is not { } chosen) return;

        await Notify.ShowCodeAsync(
            XamlRoot, $"Query as {ExportToLanguage.DisplayName(chosen)}", Model.ExportQuery(chosen));
    }

    // ---- history ---------------------------------------------------------

    private void OnUseSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SavedQuery saved }) Model.ApplySavedQuery(saved);
    }

    private async void OnDeleteSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SavedQuery saved }) return;
        await Model.DeleteSavedQueryCommand.ExecuteAsync(saved);
    }

    private async void OnSaveFavorite(object sender, RoutedEventArgs e)
    {
        var name = await Notify.PromptAsync(XamlRoot, "Save query", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;

        await Model.SaveFavoriteAsync(name);
    }
}
