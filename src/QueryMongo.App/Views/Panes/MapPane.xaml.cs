using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Panes;

public sealed partial class MapPane : UserControl
{
    public MapPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MapViewModel), typeof(MapPane),
        new PropertyMetadata(null, OnModelChanged));

    public MapViewModel Model
    {
        get => (MapViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not MapPane pane || e.NewValue is not MapViewModel model) return;

        // The map draws imperatively, so it redraws when the view model says points changed.
        model.PointsChanged += (_, _) =>
            pane.DispatcherQueue.TryEnqueue(() => pane.Map.SetPoints(model.Points, model.Bounds));
    }
}
