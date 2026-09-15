using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Views.Panes;

public sealed partial class ShellPane : UserControl
{
    public ShellPane() => InitializeComponent();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MongoShellViewModel), typeof(ShellPane),
        new PropertyMetadata(null, OnModelChanged));

    public MongoShellViewModel Model
    {
        get => (MongoShellViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ShellPane pane || e.NewValue is not MongoShellViewModel model) return;

        // A terminal is only usable if it stays scrolled to the newest output.
        model.Entries.CollectionChanged += (_, _) => pane.ScrollToEnd();
    }

    private void ScrollToEnd() =>
        DispatcherQueue.TryEnqueue(() => Transcript.ChangeView(null, Transcript.ScrollableHeight, null));

    private async void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                await Model.RunAsync();
                InputBox.Focus(FocusState.Programmatic);
                break;

            case Windows.System.VirtualKey.Up:
                e.Handled = true;
                Model.RecallPrevious();
                MoveCaretToEnd();
                break;

            case Windows.System.VirtualKey.Down:
                e.Handled = true;
                Model.RecallNext();
                MoveCaretToEnd();
                break;
        }
    }

    /// <summary>A recalled command should be ready to edit at its end, not its start.</summary>
    private void MoveCaretToEnd() => InputBox.SelectionStart = InputBox.Text.Length;
}
