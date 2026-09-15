using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.Core.Services;
using Windows.Storage.Pickers;

namespace QueryMongo.App.Dialogs;

/// <summary>Collects a file and format for importing or exporting collection data.</summary>
public sealed partial class TransferDialog : ContentDialog
{
    public TransferDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    /// <summary>True for export, false for import. Set before showing.</summary>
    public bool IsExport { get; set; } = true;

    public string FilePath => PathBox.Text.Trim();

    public TransferFormat Format =>
        FormatButtons.SelectedIndex == 1 ? TransferFormat.Csv : TransferFormat.Json;

    public bool ExportAll => ExportAllCheck.IsChecked == true;

    private void Apply()
    {
        Title = IsExport ? "Export collection" : "Import data";
        PrimaryButtonText = IsExport ? "Export" : "Import";

        DescriptionText.Text = IsExport
            ? "Exports the documents matching the current query. Choose CSV to flatten the top-level fields."
            : "Loads documents from a file. JSON may be an array or one document per line.";

        // The limit only applies to an export; an import reads the whole file.
        ExportAllCheck.Visibility = IsExport ? Visibility.Visible : Visibility.Collapsed;

        PathBox.PlaceholderText = IsExport
            ? @"C:\data\export.json"
            : @"C:\data\import.json";
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            // WinUI pickers need the owning window's handle when the app is unpackaged.
            var window = App.MainWindowHandle;

            if (IsExport)
            {
                var picker = new FileSavePicker { SuggestedFileName = "export" };
                picker.FileTypeChoices.Add("JSON", [".json"]);
                picker.FileTypeChoices.Add("CSV", [".csv"]);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, window);

                var file = await picker.PickSaveFileAsync();
                if (file is not null) PathBox.Text = file.Path;
            }
            else
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".json");
                picker.FileTypeFilter.Add(".csv");
                picker.FileTypeFilter.Add(".ndjson");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, window);

                var file = await picker.PickSingleFileAsync();
                if (file is not null) PathBox.Text = file.Path;
            }

            SyncFormatToExtension();
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    /// <summary>Picks the format from the chosen file's extension, so the two cannot disagree.</summary>
    private void SyncFormatToExtension()
    {
        if (PathBox.Text.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            FormatButtons.SelectedIndex = 1;
        else if (PathBox.Text.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                 || PathBox.Text.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
            FormatButtons.SelectedIndex = 0;
    }
}
