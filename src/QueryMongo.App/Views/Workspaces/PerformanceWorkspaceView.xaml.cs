using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Views.Workspaces;

public sealed partial class PerformanceWorkspaceView : UserControl
{
    public PerformanceWorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(PerformanceViewModel), typeof(PerformanceWorkspaceView),
        new PropertyMetadata(null, (d, _) => ((PerformanceWorkspaceView)d).Bindings.Update()));

    public PerformanceViewModel Model
    {
        get => (PerformanceViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private async void OnKillOperation(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CurrentOperation operation }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot,
                "Kill operation",
                $"Stop operation {operation.OperationId} on {operation.Namespace}?",
                "Kill"))
            return;

        var error = await Model.KillAsync(operation);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not kill operation", error);
    }
}
