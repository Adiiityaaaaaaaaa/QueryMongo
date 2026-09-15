using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Connections;

namespace QueryMongo.App.Views.Workspaces;

public sealed partial class MyQueriesWorkspaceView : UserControl
{
    public MyQueriesWorkspaceView() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(SavedQueriesViewModel), typeof(MyQueriesWorkspaceView),
        new PropertyMetadata(null, (d, _) => ((MyQueriesWorkspaceView)d).Bindings.Update()));

    public SavedQueriesViewModel Model
    {
        get => (SavedQueriesViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void OnOpenQuery(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SavedQuery query }) Model.OpenCommand.Execute(query);
    }

    private async void OnDeleteQuery(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SavedQuery query }) return;
        await Model.DeleteCommand.ExecuteAsync(query);
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => Reveal(sender, true);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => Reveal(sender, false);

    private static void Reveal(object sender, bool shown)
    {
        if (sender is FrameworkElement card && card.FindName("CardActions") is FrameworkElement actions)
            actions.Opacity = shown ? 1 : 0;
    }
}
