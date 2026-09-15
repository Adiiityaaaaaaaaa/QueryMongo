using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views;

public sealed partial class CollectionTabView : UserControl
{
    /// <summary>The sub-tabs, in the order they appear.</summary>
    private static readonly string[] PaneNames =
    [
        "Documents",
        "Aggregations",
        "Schema",
        "Explain Plan",
        "Indexes",
        "Search Indexes",
        "Validation",
        "Map",
        "Shell"
    ];

    private readonly List<Button> _paneButtons = [];

    public CollectionTabView()
    {
        InitializeComponent();
        BuildPaneBar();
    }

    private void BuildPaneBar()
    {
        for (var i = 0; i < PaneNames.Length; i++)
        {
            var index = i;
            var button = new Button { Content = PaneNames[i], Tag = index };

            button.Click += async (_, _) => await SelectPaneAsync(index);

            _paneButtons.Add(button);
            PaneBar.Children.Add(button);
        }

        SelectPane(0);
    }

    private async Task SelectPaneAsync(int index)
    {
        SelectPane(index);

        if (Tab is not null) await Tab.OnPaneSelectedAsync(index);
    }

    /// <summary>
    /// Only the selected sub-tab carries the green underline; the rest are plain, which
    /// is how Compass draws its tab row.
    /// </summary>
    private void SelectPane(int index)
    {
        for (var i = 0; i < _paneButtons.Count; i++)
            _paneButtons[i].Style = (Style)Application.Current.Resources[
                i == index ? "SubTabSelectedStyle" : "SubTabStyle"];
    }

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(CollectionTabViewModel), typeof(CollectionTabView),
        new PropertyMetadata(null, (d, e) => ((CollectionTabView)d).OnTabChanged(e)));

    public CollectionTabViewModel Tab
    {
        get => (CollectionTabViewModel)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private void OnTabChanged(DependencyPropertyChangedEventArgs e)
    {
        Trail.Items = (e.NewValue as CollectionTabViewModel)?.Trail;
        Bindings.Update();
    }

    // ---- requests from the documents pane --------------------------------

    /// <summary>Compass shows the plan in a modal; here it is the tab's Explain Plan pane.</summary>
    private async void OnExplainRequested(object? sender, EventArgs e) =>
        await SelectPaneAsync(3);

    private void OnExportRequested(object? sender, bool fullCollection) => StartExport();

    // ---- import / export -------------------------------------------------

    private void OnExport(object sender, RoutedEventArgs e) => StartExport();

    private async void StartExport()
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

    /// <summary>The header's Import button and the documents pane's menu land here alike.</summary>
    private void OnImport(object sender, RoutedEventArgs e) => StartImport();

    private void OnImport(object? sender, EventArgs e) => StartImport();

    private async void StartImport()
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
