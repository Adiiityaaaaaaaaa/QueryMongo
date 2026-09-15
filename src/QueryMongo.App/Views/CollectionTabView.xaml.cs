using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views;

public sealed partial class CollectionTabView : UserControl
{
    public CollectionTabView()
    {
        InitializeComponent();

        // SelectorBar starts with nothing selected, which would show no pane at all.
        Loaded += (_, _) => PaneBar.SelectedItem ??= PaneBar.Items[0];
    }

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(CollectionTabViewModel), typeof(CollectionTabView), new PropertyMetadata(null));

    public CollectionTabViewModel Tab
    {
        get => (CollectionTabViewModel)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private async void OnPaneChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (Tab is null || sender.SelectedItem is null) return;

        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index < 0) return;

        await Tab.OnPaneSelectedAsync(index);
    }

    // ---- import / export -------------------------------------------------

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferDialog { XamlRoot = XamlRoot, IsExport = true };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (string.IsNullOrWhiteSpace(dialog.FilePath))
        {
            await Notify.ErrorAsync(XamlRoot, "Export failed", "Choose a file to export to.");
            return;
        }

        try
        {
            // Limit 0 means "everything matching", which is what an export usually wants.
            var spec = Tab.Documents.CurrentSpec with
            {
                Limit = dialog.ExportAll ? 0 : Tab.Documents.Limit
            };

            var written = await Tab.Transfer.ExportAsync(
                Tab.Database, Tab.Collection, spec, dialog.FilePath, dialog.Format);

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

        if (!File.Exists(dialog.FilePath))
        {
            await Notify.ErrorAsync(XamlRoot, "Import failed", "Choose a file to import.");
            return;
        }

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
            await Tab.LoadStatsAsync();
        }
        catch (Exception ex)
        {
            await Notify.ErrorAsync(XamlRoot, "Import failed", ex.Message);
        }
    }
}
