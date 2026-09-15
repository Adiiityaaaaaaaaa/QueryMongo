using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Models;

namespace QueryMongo.App.Views.Panes;

public sealed partial class ExplainPane : UserControl
{
    public ExplainPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ExplainViewModel), typeof(ExplainPane), new PropertyMetadata(null));

    public ExplainViewModel Model
    {
        get => (ExplainViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>
    /// The plan describes the Documents query, so the pane is told which spec to
    /// explain rather than holding a query of its own.
    /// </summary>
    public static readonly DependencyProperty SpecSourceProperty = DependencyProperty.Register(
        nameof(SpecSource), typeof(DocumentsViewModel), typeof(ExplainPane), new PropertyMetadata(null));

    public DocumentsViewModel SpecSource
    {
        get => (DocumentsViewModel)GetValue(SpecSourceProperty);
        set => SetValue(SpecSourceProperty, value);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (SpecSource is null) return;
        await Model.RefreshAsync(SpecSource.CurrentSpec);
    }
}
