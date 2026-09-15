using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Panes;

public sealed partial class ValidationPane : UserControl
{
    public ValidationPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ValidationViewModel), typeof(ValidationPane), new PropertyMetadata(null));

    public ValidationViewModel Model
    {
        get => (ValidationViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var error = await Model.ApplyAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not apply rules", error);
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        if (!await Notify.ConfirmAsync(
                XamlRoot, "Remove validation",
                "Remove all validation rules from this collection?", "Remove"))
            return;

        var error = await Model.ClearAsync();
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not remove rules", error);
    }
}
