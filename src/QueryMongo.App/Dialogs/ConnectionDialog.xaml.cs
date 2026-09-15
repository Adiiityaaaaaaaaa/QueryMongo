using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.Core.Connections;
using Windows.Storage.Pickers;

namespace QueryMongo.App.Dialogs;

/// <summary>
/// Collects a connection URI and optional SSH tunnel. Reopening after a failure keeps
/// everything typed, so a wrong host or password can be corrected in place.
/// </summary>
public sealed partial class ConnectionDialog : ContentDialog
{
    public ConnectionDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UriBox.Focus(FocusState.Programmatic);
    }

    public string Uri => UriBox.Text.Trim();

    public string? ConnectionName =>
        string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();

    /// <summary>The chosen colour tag, or null for none.</summary>
    public string? ColorCode =>
        ColorBox.SelectedItem is ComboBoxItem { Tag: string code } && code.Length > 0 ? code : null;

    public string? ErrorMessage
    {
        get => ErrorBar.Message;
        set
        {
            ErrorBar.Message = value ?? "";
            ErrorBar.IsOpen = !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>Reads the SSH form, or null when the tunnel is switched off or incomplete.</summary>
    public SshOptions? BuildSshOptions()
    {
        if (UseSshCheck.IsChecked != true) return null;
        if (string.IsNullOrWhiteSpace(SshHostBox.Text) || string.IsNullOrWhiteSpace(SshUserBox.Text))
            return null;

        var usesKey = SshAuthButtons.SelectedIndex == 1;

        return new SshOptions
        {
            Host = SshHostBox.Text.Trim(),
            Port = double.IsNaN(SshPortBox.Value) ? 22 : (int)SshPortBox.Value,
            Username = SshUserBox.Text.Trim(),
            Password = usesKey ? null : SshPasswordBox.Password,
            PrivateKeyPath = usesKey && !string.IsNullOrWhiteSpace(SshKeyPathBox.Text)
                ? SshKeyPathBox.Text.Trim()
                : null,
            PrivateKeyPassphrase = usesKey && SshPassphraseBox.Password.Length > 0
                ? SshPassphraseBox.Password
                : null,
            RemoteHost = string.IsNullOrWhiteSpace(SshRemoteHostBox.Text)
                ? "127.0.0.1"
                : SshRemoteHostBox.Text.Trim(),
            RemotePort = double.IsNaN(SshRemotePortBox.Value) ? 27017 : (int)SshRemotePortBox.Value
        };
    }

    private void OnSshToggled(object sender, RoutedEventArgs e) =>
        SshPanel.Visibility = UseSshCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnSshAuthChanged(object sender, SelectionChangedEventArgs e)
    {
        // The handler fires during load, before the panels are built.
        if (SshKeyPanel is null || SshPasswordBox is null) return;

        var usesKey = SshAuthButtons.SelectedIndex == 1;

        SshKeyPanel.Visibility = usesKey ? Visibility.Visible : Visibility.Collapsed;
        SshPasswordBox.Visibility = usesKey ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnBrowseKey(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");

            // Pickers need the owning window's handle when the app is unpackaged.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            if (await picker.PickSingleFileAsync() is { } file) SshKeyPathBox.Text = file.Path;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
