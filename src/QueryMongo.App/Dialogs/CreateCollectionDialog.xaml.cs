using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Dialogs;

/// <summary>
/// Creates a collection, or a database plus its first collection. MongoDB has no
/// create-database command, so the two cases share one form.
/// </summary>
public sealed partial class CreateCollectionDialog : ContentDialog
{
    public CreateCollectionDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    /// <summary>True when creating a database, which also needs a database name.</summary>
    public bool IsDatabaseMode { get; set; }

    public string DatabaseName
    {
        get => DatabaseBox.Text.Trim();
        set => _pendingDatabase = value;
    }

    private string _pendingDatabase = "";

    public string CollectionName => CollectionBox.Text.Trim();

    private void Apply()
    {
        Title = IsDatabaseMode ? "Create database" : "Create collection";
        DatabaseBox.Text = _pendingDatabase;
        DatabaseBox.Visibility = IsDatabaseMode ? Visibility.Visible : Visibility.Collapsed;

        // Focus the field the user actually has to fill in first.
        if (IsDatabaseMode) DatabaseBox.Focus(FocusState.Programmatic);
        else CollectionBox.Focus(FocusState.Programmatic);
    }

    private void OnCappedToggled(object sender, RoutedEventArgs e) =>
        CappedPanel.Visibility = CappedCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnTimeSeriesToggled(object sender, RoutedEventArgs e) =>
        TimeSeriesPanel.Visibility = TimeSeriesCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    public NewCollectionOptions BuildOptions()
    {
        var capped = CappedCheck.IsChecked == true;
        var timeSeries = TimeSeriesCheck.IsChecked == true;

        return new NewCollectionOptions
        {
            Capped = capped,
            MaxSizeBytes = capped ? (long)MaxSizeBox.Value : null,
            // A NumberBox left empty reads as NaN, which means "no limit" here.
            MaxDocuments = capped && !double.IsNaN(MaxDocsBox.Value) && MaxDocsBox.Value > 0
                ? (long)MaxDocsBox.Value
                : null,
            TimeField = timeSeries && !string.IsNullOrWhiteSpace(TimeFieldBox.Text)
                ? TimeFieldBox.Text.Trim()
                : null,
            MetaField = timeSeries && !string.IsNullOrWhiteSpace(MetaFieldBox.Text)
                ? MetaFieldBox.Text.Trim()
                : null,
            CollationLocale = string.IsNullOrWhiteSpace(CollationBox.Text)
                ? null
                : CollationBox.Text.Trim()
        };
    }
}
