using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Dialogs;

/// <summary>
/// Collects an index definition. The dialog writes straight onto
/// <see cref="IndexesViewModel"/>, so the caller can retry after a server error
/// without the user re-entering anything.
/// </summary>
public sealed partial class CreateIndexDialog : ContentDialog
{
    public CreateIndexDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
        Closing += (_, _) => Save();
    }

    /// <summary>Set by the caller before showing; the XAML type system needs a settable property.</summary>
    public IndexesViewModel Model { get; set; } = null!;

    public string? ErrorMessage
    {
        get => ErrorBar.Message;
        set
        {
            ErrorBar.Message = value ?? "";
            ErrorBar.IsOpen = !string.IsNullOrWhiteSpace(value);
        }
    }

    private void Load()
    {
        KeysBox.Text = Model.NewIndexKeys;
        NameBox.Text = Model.NewIndexName;
        UniqueCheck.IsChecked = Model.NewIndexUnique;
        SparseCheck.IsChecked = Model.NewIndexSparse;
        BackgroundCheck.IsChecked = Model.NewIndexBackground;
        TtlCheck.IsChecked = Model.NewIndexHasTtl;
        TtlBox.Value = Model.NewIndexTtlSeconds;
        PartialBox.Text = Model.NewIndexPartialFilter;
        CollationBox.Text = Model.NewIndexCollation;

        OnTtlToggled(this, new RoutedEventArgs());
        KeysBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Copies the form back to the view model on close, including on cancel, so a
    /// reopened dialog still shows what was typed.
    /// </summary>
    private void Save()
    {
        Model.NewIndexKeys = KeysBox.Text;
        Model.NewIndexName = NameBox.Text;
        Model.NewIndexUnique = UniqueCheck.IsChecked == true;
        Model.NewIndexSparse = SparseCheck.IsChecked == true;
        Model.NewIndexBackground = BackgroundCheck.IsChecked == true;
        Model.NewIndexHasTtl = TtlCheck.IsChecked == true;
        Model.NewIndexTtlSeconds = double.IsNaN(TtlBox.Value) ? 3600 : (int)TtlBox.Value;
        Model.NewIndexPartialFilter = PartialBox.Text;
        Model.NewIndexCollation = CollationBox.Text;
    }

    private void OnTtlToggled(object sender, RoutedEventArgs e) =>
        TtlBox.Visibility = TtlCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
}
