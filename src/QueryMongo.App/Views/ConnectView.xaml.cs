using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;

namespace QueryMongo.App.Views;

public sealed partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ConnectView), new PropertyMetadata(null));

    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    private void OnConnectionStringKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        // Enter connects, which is what anyone who just pasted a URI expects.
        if (Shell.ConnectCommand.CanExecute(null)) Shell.ConnectCommand.Execute(null);
        e.Handled = true;
    }

    private void OnUseSavedConnection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ConnectionProfile profile })
            Shell.UseSavedConnection(profile);
    }

    private void OnDeleteSavedConnection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ConnectionProfile profile })
            Shell.DeleteSavedConnectionCommand.Execute(profile);
    }
}
