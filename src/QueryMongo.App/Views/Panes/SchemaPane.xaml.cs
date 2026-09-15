using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Panes;

public sealed partial class SchemaPane : UserControl
{
    public SchemaPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(SchemaViewModel), typeof(SchemaPane), new PropertyMetadata(null));

    public SchemaViewModel Model
    {
        get => (SchemaViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
