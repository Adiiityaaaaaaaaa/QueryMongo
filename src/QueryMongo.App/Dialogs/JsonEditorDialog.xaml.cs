using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QueryMongo.App.Dialogs;

/// <summary>
/// A JSON text editor in a dialog, used for inserting a document and for writing a
/// bulk update. Reopening it after a failure keeps the text, so an error does not
/// cost the user their work.
/// </summary>
public sealed partial class JsonEditorDialog : ContentDialog
{
    public JsonEditorDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Editor.Focus(FocusState.Programmatic);
    }

    public string Json
    {
        get => Editor.Text;
        set => Editor.Text = value;
    }

    /// <summary>Optional line above the editor explaining what the text will do.</summary>
    public string? Description
    {
        get => DescriptionText.Text;
        set
        {
            DescriptionText.Text = value ?? "";
            DescriptionText.Visibility = string.IsNullOrWhiteSpace(value)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    public string? ErrorMessage
    {
        get => ErrorBar.Message;
        set
        {
            ErrorBar.Message = value ?? "";
            ErrorBar.IsOpen = !string.IsNullOrWhiteSpace(value);
        }
    }
}
