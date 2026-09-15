using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QueryMongo.App.Dialogs;
using QueryMongo.App.ViewModels;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Views.Panes;

public sealed partial class AggregationPane : UserControl
{
    public AggregationPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(AggregationViewModel), typeof(AggregationPane), new PropertyMetadata(null));

    public AggregationViewModel Model
    {
        get => (AggregationViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private async void OnAddStage(object sender, RoutedEventArgs e)
    {
        var picker = new ListView
        {
            ItemsSource = PipelineStageViewModel.Operators,
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            Height = 340,
            Width = 240,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas")
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add stage",
            Content = picker,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Model.AddStageCommand.Execute(picker.SelectedItem as string);
    }

    private void OnStageUp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Model.MoveStageUpCommand.Execute(stage);
    }

    private void OnStageDown(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Model.MoveStageDownCommand.Execute(stage);
    }

    private void OnStageDuplicate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Model.DuplicateStageCommand.Execute(stage);
    }

    private void OnStageRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PipelineStageViewModel stage })
            Model.RemoveStageCommand.Execute(stage);
    }

    private async void OnRunWritingPipeline(object sender, RoutedEventArgs e)
    {
        var target = Model.WriteTarget ?? "the target collection";

        // $out replaces the whole target collection, so this needs strong confirmation.
        if (!await Notify.ConfirmTypedAsync(
                XamlRoot,
                "Run pipeline and write results",
                $"This writes the pipeline output to \"{target}\". With $out the existing contents " +
                "of that collection are replaced. This cannot be undone.",
                target))
            return;

        var error = await Model.RunWriteAsync();

        if (error is not null) await Notify.ErrorAsync(XamlRoot, "Pipeline failed", error);
        else await Notify.InfoAsync(XamlRoot, "Pipeline complete", $"Results written to \"{target}\".");
    }

    private async void OnExportPipeline(object sender, RoutedEventArgs e)
    {
        var language = await LanguagePicker.PickAsync(XamlRoot);
        if (language is not { } chosen) return;

        await Notify.ShowCodeAsync(
            XamlRoot, $"Pipeline as {ExportToLanguage.DisplayName(chosen)}", Model.ExportPipeline(chosen));
    }
}
