using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;
using QueryMongo.Core.Json;
using QueryMongo.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace QueryMongo.App.Views;

public sealed partial class CollectionTabView : UserControl
{
    public CollectionTabView() => InitializeComponent();

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(CollectionTabViewModel), typeof(CollectionTabView), new PropertyMetadata(null));

    public CollectionTabViewModel Tab
    {
        get => (CollectionTabViewModel)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private async void OnPaneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tab is null || sender is not Pivot pivot) return;
        await Tab.OnPaneSelectedAsync(pivot.SelectedIndex);
    }

    // ---- documents: query bar -------------------------------------------

    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        // Enter runs the query, which is what anyone typing a filter expects.
        Tab.Documents.RunQueryCommand.Execute(null);
        e.Handled = true;
    }

    private void OnShowList(object sender, RoutedEventArgs e) =>
        Tab.Documents.ViewMode = DocumentViewMode.List;

    private void OnShowTable(object sender, RoutedEventArgs e) =>
        Tab.Documents.ViewMode = DocumentViewMode.Table;

    private void OnShowJson(object sender, RoutedEventArgs e) =>
        Tab.Documents.ViewMode = DocumentViewMode.Json;

    /// <summary>Keeps the table header aligned with the horizontally scrolled rows.</summary>
    private void OnTableScrolled(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer body)
            HeaderScroll.ChangeView(body.HorizontalOffset, null, null, disableAnimation: true);
    }

    // ---- documents: per-row actions -------------------------------------

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
        await Tab.Documents.SaveEditCommand.ExecuteAsync(doc);
    }

    private async void OnCloneDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot, "Clone document",
                "Insert a copy of this document with a new _id?", "Clone"))
            return;

        await Tab.Documents.CloneDocumentCommand.ExecuteAsync(doc);
    }

    private void OnCopyDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;

        var package = new DataPackage();
        package.SetText(doc.Json);
        Clipboard.SetContent(package);
    }

    private async void OnDeleteDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentViewModel doc }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot, "Delete document",
                $"Permanently delete the document with _id {doc.IdDescription}? This cannot be undone.",
                "Delete"))
            return;

        await Tab.Documents.DeleteDocumentCommand.ExecuteAsync(doc);
    }

    // ---- documents: collection-wide actions ------------------------------

    private async void OnInsertDocument(object sender, RoutedEventArgs e)
    {
        var dialog = new JsonEditorDialog
        {
            XamlRoot = XamlRoot,
            Title = "Insert document",
            PrimaryButtonText = "Insert",
            Json = "{\n  \n}"
        };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Tab.Documents.InsertAsync(dialog.Json);
            if (error is null) return;

            // Keep the dialog contents so the user can fix the document rather than retype it.
            dialog.ErrorMessage = error;
        }
    }

    private async void OnUpdateMany(object sender, RoutedEventArgs e)
    {
        var count = Tab.Documents.MatchingCount;

        var dialog = new JsonEditorDialog
        {
            XamlRoot = XamlRoot,
            Title = "Update matching documents",
            PrimaryButtonText = "Update",
            Description = $"Applies an update to all {count:N0} document(s) matching the current filter.",
            Json = "{\n  \"$set\": {\n    \n  }\n}"
        };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Tab.Documents.UpdateManyAsync(dialog.Json);
            if (error is null) return;
            dialog.ErrorMessage = error;
        }
    }

    private async void OnDeleteMany(object sender, RoutedEventArgs e)
    {
        var count = Tab.Documents.MatchingCount;

        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Delete matching documents",
                $"This permanently deletes all {count:N0} document(s) matching the current filter " +
                $"in \"{Tab.Namespace}\". This cannot be undone.",
                Tab.Collection))
            return;

        var error = await Tab.Documents.DeleteManyAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not delete documents", error);
    }

    private async void OnExportQuery(object sender, RoutedEventArgs e)
    {
        var language = await PickLanguageAsync();
        if (language is not { } chosen) return;

        await Notify.ShowCodeAsync(
            XamlRoot, $"Query as {ExportToLanguage.DisplayName(chosen)}",
            Tab.Documents.ExportQuery(chosen));
    }

    // ---- query history ---------------------------------------------------

    private void OnUseSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SavedQuery saved }) Tab.Documents.ApplySavedQuery(saved);
    }

    private async void OnDeleteSavedQuery(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SavedQuery saved }) return;
        await Tab.Documents.DeleteSavedQueryCommand.ExecuteAsync(saved);
    }

    private async void OnSaveFavorite(object sender, RoutedEventArgs e)
    {
        var name = await Notify.PromptAsync(XamlRoot, "Save query", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;

        await Tab.Documents.SaveFavoriteAsync(name);
    }

    // ---- aggregation -----------------------------------------------------

    private async void OnAddStage(object sender, RoutedEventArgs e)
    {
        var picker = new ListView
        {
            ItemsSource = PipelineStageViewModel.Operators,
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            Height = 320,
            Width = 260
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add stage",
            Content = picker,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Tab.Aggregation.AddStageCommand.Execute(picker.SelectedItem as string);
    }

    private void OnStageUp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Tab.Aggregation.MoveStageUpCommand.Execute(stage);
    }

    private void OnStageDown(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Tab.Aggregation.MoveStageDownCommand.Execute(stage);
    }

    private void OnStageDuplicate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Tab.Aggregation.DuplicateStageCommand.Execute(stage);
    }

    private void OnStageRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Tab.Aggregation.RemoveStageCommand.Execute(stage);
    }

    private async void OnRunWritingPipeline(object sender, RoutedEventArgs e)
    {
        var target = Tab.Aggregation.WriteTarget ?? "the target collection";

        // $out replaces the whole target collection, so this needs the strong confirmation.
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Run pipeline and write results",
                $"This writes the pipeline output to \"{target}\". With $out the existing contents " +
                "of that collection are replaced. This cannot be undone.",
                target))
            return;

        var error = await Tab.Aggregation.RunWriteAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Pipeline failed", error);
        else await Notify.InfoAsync(XamlRoot, "Pipeline complete", $"Results written to \"{target}\".");
    }

    private async void OnExportPipeline(object sender, RoutedEventArgs e)
    {
        var language = await PickLanguageAsync();
        if (language is not { } chosen) return;

        await Notify.ShowCodeAsync(
            XamlRoot, $"Pipeline as {ExportToLanguage.DisplayName(chosen)}",
            Tab.Aggregation.ExportPipeline(chosen));
    }

    // ---- indexes ---------------------------------------------------------

    private async void OnCreateIndex(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateIndexDialog { XamlRoot = XamlRoot, Model = Tab.Indexes };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Tab.Indexes.CreateAsync();
            if (error is null) return;
            dialog.ErrorMessage = error;
        }
    }

    private async void OnDropIndex(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: IndexDetail detail }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot,
                "Drop index",
                $"Drop index \"{detail.Name}\"? Queries relying on it will fall back to a collection scan.",
                "Drop"))
            return;

        var error = await Tab.Indexes.DropAsync(detail);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop index", error);
    }

    // ---- validation ------------------------------------------------------

    private async void OnApplyValidation(object sender, RoutedEventArgs e)
    {
        var error = await Tab.Validation.ApplyAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not apply rules", error);
    }

    private async void OnClearValidation(object sender, RoutedEventArgs e)
    {
        if (!await Notify.ConfirmAsync(
                XamlRoot, "Remove validation",
                "Remove all validation rules from this collection?", "Remove"))
            return;

        var error = await Tab.Validation.ClearAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not remove rules", error);
    }

    // ---- import / export -------------------------------------------------

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferDialog { XamlRoot = XamlRoot, IsExport = true };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var written = await Tab.Transfer.ExportAsync(
                Tab.Database, Tab.Collection,
                Tab.Documents.CurrentSpec with { Limit = dialog.ExportAll ? 0 : Tab.Documents.Limit },
                dialog.FilePath, dialog.Format);

            await Notify.InfoAsync(XamlRoot, "Export complete",
                $"{written:N0} document(s) written to {dialog.FilePath}.");
        }
        catch (Exception ex)
        {
            await Notify.ErrorAsync(XamlRoot, "Export failed", ex.Message);
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferDialog { XamlRoot = XamlRoot, IsExport = false };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var result = await Tab.Transfer.ImportAsync(
                Tab.Database, Tab.Collection, dialog.FilePath, dialog.Format);

            var message = $"{result.Inserted:N0} document(s) inserted.";
            if (result.Failed > 0) message += $" {result.Failed:N0} failed.";
            if (result.Errors.Count > 0)
                message += Environment.NewLine + Environment.NewLine +
                           string.Join(Environment.NewLine, result.Errors.Take(5));

            await Notify.InfoAsync(XamlRoot, "Import complete", message);
            await Tab.Documents.RunQueryAsync();
        }
        catch (Exception ex)
        {
            await Notify.ErrorAsync(XamlRoot, "Import failed", ex.Message);
        }
    }

    // ---- shared ----------------------------------------------------------

    private async Task<TargetLanguage?> PickLanguageAsync()
    {
        var picker = new ListView
        {
            ItemsSource = ExportToLanguage.All.Select(ExportToLanguage.DisplayName).ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            Height = 280,
            Width = 240
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Export to language",
            Content = picker,
            PrimaryButtonText = "Show code",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (picker.SelectedIndex < 0) return null;

        return ExportToLanguage.All[picker.SelectedIndex];
    }
}
