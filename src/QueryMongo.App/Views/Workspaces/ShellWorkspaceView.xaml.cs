using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Workspaces;

public sealed partial class ShellWorkspaceView : UserControl
{
    public ShellWorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MongoShellViewModel), typeof(ShellWorkspaceView),
        new PropertyMetadata(null, (d, _) => ((ShellWorkspaceView)d).Bindings.Update()));

    public MongoShellViewModel Model
    {
        get => (MongoShellViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
