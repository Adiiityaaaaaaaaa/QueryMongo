using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Dialogs;

/// <summary>Shared "Export to language" picker, used by both the query and pipeline panes.</summary>
public static class LanguagePicker
{
    public static async Task<TargetLanguage?> PickAsync(XamlRoot root)
    {
        var picker = new ListView
        {
            ItemsSource = ExportToLanguage.All.Select(ExportToLanguage.DisplayName).ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            Height = 300,
            Width = 260
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
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
