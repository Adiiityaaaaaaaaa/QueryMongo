using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Views.Panes;

public sealed partial class IndexesPane : UserControl
{
    public IndexesPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(IndexesViewModel), typeof(IndexesPane), new PropertyMetadata(null));

    public IndexesViewModel Model
    {
        get => (IndexesViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private async void OnCreateIndex(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateIndexDialog { XamlRoot = XamlRoot, Model = Model };

        // Reopening keeps the form filled in, so a server rejection costs no retyping.
        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var error = await Model.CreateAsync();
            if (error is null) return;
            dialog.ErrorMessage = error;
        }
    }

    private async void OnDropIndex(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: IndexDetail detail }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot,
                "Drop index",
                $"Drop index \"{detail.Name}\"? Queries relying on it will fall back to a collection scan.",
                "Drop"))
            return;

        var error = await Model.DropAsync(detail);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop index", error);
    }
}
