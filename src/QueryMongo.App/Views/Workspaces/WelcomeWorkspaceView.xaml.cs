using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Workspaces;

public sealed partial class WelcomeWorkspaceView : UserControl
{
    public WelcomeWorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(WelcomeWorkspaceViewModel), typeof(WelcomeWorkspaceView),
        new PropertyMetadata(null));

    public WelcomeWorkspaceViewModel Model
    {
        get => (WelcomeWorkspaceViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private async void OnAddConnection(object sender, RoutedEventArgs e)
    {
        if (Model?.Host is not { } shell) return;

        var dialog = new ConnectionDialog { XamlRoot = XamlRoot };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await shell.AddConnectionAsync(
                dialog.Uri, dialog.ConnectionName, dialog.BuildSshOptions(), dialog.ColorCode);

            if (error is null) return;

            // Keep what was typed, so a bad host or password can be corrected in place.
            dialog.ErrorMessage = error;
        }
    }
}
