using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;
using Windows.Storage.Pickers;

namespace QueryMongo.App.Views;

public sealed partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ConnectView),
        new PropertyMetadata(null, OnShellChanged));

    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>Drives the empty-state hint above the saved list.</summary>
    public bool HasSaved => Shell?.SavedConnections.Count > 0;

    private static void OnShellChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ConnectView view || e.NewValue is not ShellViewModel shell) return;

        shell.SavedConnections.CollectionChanged += (_, _) => view.Bindings.Update();
    }

    // ---- connecting ------------------------------------------------------

    private void OnConnectionStringKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        // Enter connects, which is what anyone who just pasted a URI expects.
        e.Handled = true;
        Connect();
    }

    private void OnConnect(object sender, RoutedEventArgs e) => Connect();

    private void Connect()
    {
        Shell.PendingSsh = BuildSshOptions();

        if (Shell.ConnectCommand.CanExecute(null)) Shell.ConnectCommand.Execute(null);
    }

    /// <summary>Reads the SSH form, or null when the tunnel is switched off.</summary>
    private SshOptions? BuildSshOptions()
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

    // ---- SSH form --------------------------------------------------------

    private void OnSshToggled(object sender, RoutedEventArgs e) =>
        SshPanel.Visibility = UseSshCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnSshAuthChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: the handler fires during load, before the panels exist.
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
            Shell.ErrorMessage = ex.Message;
        }
    }

    // ---- saved connections -----------------------------------------------

    private void OnUseSavedConnection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionProfile profile }) return;

        Shell.UseSavedConnection(profile);
        LoadSshForm(profile.Ssh);
    }

    /// <summary>Restores a saved connection's SSH settings into the form.</summary>
    private void LoadSshForm(SshOptions? ssh)
    {
        UseSshCheck.IsChecked = ssh is { IsConfigured: true };
        OnSshToggled(this, new RoutedEventArgs());

        if (ssh is null) return;

        SshHostBox.Text = ssh.Host;
        SshPortBox.Value = ssh.Port;
        SshUserBox.Text = ssh.Username;
        SshRemoteHostBox.Text = ssh.RemoteHost;
        SshRemotePortBox.Value = ssh.RemotePort;

        SshAuthButtons.SelectedIndex = string.IsNullOrWhiteSpace(ssh.PrivateKeyPath) ? 0 : 1;
        SshKeyPathBox.Text = ssh.PrivateKeyPath ?? "";

        // Secrets come back from the store, so the form can reconnect without retyping.
        SshPasswordBox.Password = ssh.Password ?? "";
        SshPassphraseBox.Password = ssh.PrivateKeyPassphrase ?? "";
    }

    private async void OnDeleteSavedConnection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionProfile profile }) return;
        await Shell.DeleteSavedConnectionCommand.ExecuteAsync(profile);
    }

    private async void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionProfile profile }) return;
        await Shell.ToggleFavoriteCommand.ExecuteAsync(profile);
    }
}
