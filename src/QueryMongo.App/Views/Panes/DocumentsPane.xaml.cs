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
    public DocumentsPane() => InitializeComponent();

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

    // ---- query bar -------------------------------------------------------

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

    private void OnToggleExpand(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentViewModel doc }) doc.IsExpanded = !doc.IsExpanded;
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
