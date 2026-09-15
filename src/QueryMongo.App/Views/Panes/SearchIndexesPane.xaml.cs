using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Views.Panes;

public sealed partial class SearchIndexesPane : UserControl
{
    public SearchIndexesPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(SearchIndexesViewModel), typeof(SearchIndexesPane),
        new PropertyMetadata(null));

    public SearchIndexesViewModel Model
    {
        get => (SearchIndexesViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Index name", Text = Model.NewIndexName };

        var type = new ComboBox
        {
            Header = "Type",
            ItemsSource = SearchIndexesViewModel.Types,
            SelectedItem = Model.NewIndexType,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var definition = new TextBox
        {
            Header = "Definition",
            Text = Model.NewIndexDefinition,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 240,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            IsSpellCheckEnabled = false
        };

        // Switching type swaps in the matching template while it is still untouched.
        type.SelectionChanged += (_, _) =>
        {
            Model.NewIndexDefinition = definition.Text;
            Model.NewIndexType = (string)type.SelectedItem;
            definition.Text = Model.NewIndexDefinition;
        };

        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = false };

        var panel = new StackPanel { Spacing = 12, Width = 520 };
        panel.Children.Add(name);
        panel.Children.Add(type);
        panel.Children.Add(definition);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create search index",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Model.NewIndexName = name.Text;
            Model.NewIndexType = (string)type.SelectedItem;
            Model.NewIndexDefinition = definition.Text;

            var failure = await Model.CreateAsync();
            if (failure is null) return;

            error.Message = failure;
            error.IsOpen = true;
        }
    }

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SearchIndexInfo index }) return;

        var editor = new TextBox
        {
            Text = index.DefinitionJson,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 300,
            Width = 520,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            IsSpellCheckEnabled = false
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Edit \"{index.Name}\"",
            Content = editor,
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await Model.UpdateAsync(index, editor.Text);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not update index", error);
    }

    private async void OnDrop(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SearchIndexInfo index }) return;

        if (!await Notify.ConfirmAsync(
                XamlRoot, "Drop search index",
                $"Drop search index \"{index.Name}\"? Queries using $search against it will fail.",
                "Drop"))
            return;

        var error = await Model.DropAsync(index);
        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Could not drop index", error);
    }
}
