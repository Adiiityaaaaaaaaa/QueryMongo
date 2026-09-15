using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views;

public sealed partial class WorkspaceView : UserControl
{
    public WorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(WorkspaceView), new PropertyMetadata(null));

    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    private void OnCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CollectionNodeViewModel node })
            Shell.OpenCollection(node.Database, node.Name);
    }
}
