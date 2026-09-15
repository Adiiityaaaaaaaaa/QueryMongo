using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QueryMongo.App.Dialogs;

/// <summary>
/// Small modal helpers. Destructive actions go through <see cref="ConfirmTypedAsync"/>,
/// which requires the user to type the name of what they are about to destroy.
/// </summary>
public static class Notify
{
    public static async Task ErrorAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    public static async Task InfoAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    public static async Task<bool> ConfirmAsync(
        XamlRoot root, string title, string message, string confirmLabel = "Continue")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmLabel,
            CloseButtonText = "Cancel",
            // Cancel is the default so Enter cannot casually confirm a destructive action.
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Confirmation for irreversible actions: the primary button stays disabled until
    /// the user types <paramref name="requiredText"/> exactly.
    /// </summary>
    public static async Task<bool> ConfirmTypedAsync(
        XamlRoot root, string title, string message, string requiredText)
    {
        var input = new TextBox { PlaceholderText = requiredText };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = $"Type \"{requiredText}\" to confirm:",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });
        panel.Children.Add(input);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = panel,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };

        input.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = input.Text.Trim() == requiredText;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Single-line text prompt. Returns null when cancelled.</summary>
    public static async Task<string?> PromptAsync(
        XamlRoot root, string title, string label, string initial = "")
    {
        var input = new TextBox { Text = initial, SelectionStart = initial.Length };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(input);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    /// <summary>Shows generated code with a copy button, used by Export to language.</summary>
    public static async Task ShowCodeAsync(XamlRoot root, string title, string code)
    {
        var text = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Height = 320,
            Width = 640
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer
            {
                Content = text,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "Copy",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(code);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
    }
}
