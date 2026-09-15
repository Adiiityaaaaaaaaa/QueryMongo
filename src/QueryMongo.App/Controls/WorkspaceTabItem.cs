using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App.Controls;

/// <summary>
/// One tab in the workspace strip, built to Compass's anatomy: a coloured bar along the
/// top edge for the connection, then the workspace icon, its title, and a close button
/// that only appears under the pointer.
///
/// Measurements come from Compass's <c>workspace-tabs/tab.tsx</c>: 40px tall, between 96
/// and 192px wide, a 4px accent bar, a 12px title, and a one-pixel border down the right
/// which the selected tab keeps while dropping the one along the bottom, so it reads as
/// joined to the content below it.
/// </summary>
public sealed class WorkspaceTabItem : UserControl
{
    private const double AccentHeight = 4;

    private readonly Border _accent = new() { Height = AccentHeight };
    private readonly Border _surface = new();
    private readonly LeafyIcon _icon = new() { Size = 16, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _title = new()
    {
        FontSize = 12,
        LineHeight = 16,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly Button _close;
    private readonly Grid _root = new();

    public WorkspaceTabItem()
    {
        MinWidth = 96;
        MaxWidth = 192;
        Height = 40;

        _close = new Button
        {
            Content = new LeafyIcon { Size = 12, Glyph = "X" },
            Opacity = 0,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _close.Click += (_, _) => CloseRequested?.Invoke(this, Tab!);

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var body = new Grid { Margin = new Thickness(0, 0, 0, AccentHeight) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_close, 2);
        _title.Margin = new Thickness(8, 0, 4, 0);

        body.Children.Add(_icon);
        body.Children.Add(_title);
        body.Children.Add(_close);

        Grid.SetRow(_accent, 0);
        Grid.SetRow(body, 1);

        _root.Children.Add(_accent);
        _root.Children.Add(body);

        _surface.Child = _root;
        Content = _surface;

        PointerEntered += (_, _) => _close.Opacity = 1;
        PointerExited += (_, _) => _close.Opacity = 0;
        PointerPressed += OnPointerPressed;

        var menu = new MenuFlyout();

        var closeOthers = new MenuFlyoutItem { Text = "Close all other tabs" };
        closeOthers.Click += (_, _) => CloseOthersRequested?.Invoke(this, Tab!);

        var close = new MenuFlyoutItem { Text = "Close tab" };
        close.Click += (_, _) => CloseRequested?.Invoke(this, Tab!);

        menu.Items.Add(closeOthers);
        menu.Items.Add(close);
        ContextFlyout = menu;
    }

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(WorkspaceTabViewModel), typeof(WorkspaceTabItem),
        new PropertyMetadata(null, (d, e) => ((WorkspaceTabItem)d).OnTabChanged(e)));

    public WorkspaceTabViewModel? Tab
    {
        get => (WorkspaceTabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public event EventHandler<WorkspaceTabViewModel>? Selected;
    public event EventHandler<WorkspaceTabViewModel>? CloseRequested;
    public event EventHandler<WorkspaceTabViewModel>? CloseOthersRequested;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Properties;

        // Middle-click closes a tab, as it does in a browser and in Compass.
        if (point.IsMiddleButtonPressed)
        {
            if (Tab is { } tab) CloseRequested?.Invoke(this, tab);
            e.Handled = true;
            return;
        }

        if (point.IsLeftButtonPressed && Tab is { } selected) Selected?.Invoke(this, selected);
    }

    private void OnTabChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WorkspaceTabViewModel previous)
            previous.PropertyChanged -= OnTabPropertyChanged;

        if (e.NewValue is WorkspaceTabViewModel next)
            next.PropertyChanged += OnTabPropertyChanged;

        Apply();
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.IsSelected)) Apply();
    }

    private void Apply()
    {
        if (Tab is not { } tab)
        {
            ToolTipService.SetToolTip(this, null);
            return;
        }

        _icon.Glyph = tab.IconGlyph;
        _title.Text = tab.Title;

        // The accent bar carries the connection's colour; without one it is invisible
        // rather than a grey line, so uncoloured tabs stay plain.
        _accent.Background = Converters.ConnectionColor(tab.ColorCode);

        var selected = tab.IsSelected;

        _surface.Background = Resource(selected ? "TabSelectedBackgroundBrush" : "TabBackgroundBrush");
        _surface.BorderBrush = Resource("TabBorderBrush");

        // Selected keeps its right border but drops the bottom one, which is what joins
        // it visually to the workspace underneath.
        _surface.BorderThickness = selected
            ? new Thickness(0, 0, 1, 0)
            : new Thickness(0, 0, 1, 1);

        var text = Resource(selected ? "TabSelectedTextBrush" : "TabTextBrush");
        _title.Foreground = text;
        _icon.Foreground = text;
        _close.Foreground = text;

        ToolTipService.SetToolTip(
            this,
            tab.HasTooltip
                ? string.Join("\n", tab.Tooltip.Select(t => $"{t.Label}: {t.Value}"))
                : tab.Title);
    }

    private static Brush? Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
