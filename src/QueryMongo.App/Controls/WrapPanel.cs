using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace QueryMongo.App.Controls;

/// <summary>
/// Lays children out in a row and starts a new one when the row is full, the way a card
/// grid flows in a browser. WinUI has no panel that does this outside a list control, so
/// the card workspaces bring their own.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Gap between cards on the same row.</summary>
    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public static readonly DependencyProperty LineSpacingProperty = DependencyProperty.Register(
        nameof(LineSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Gap between rows.</summary>
    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // Children are measured against the panel's width but given unlimited height, so
        // a card is never squeezed by how much room is left below it.
        var limit = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;

        double rowWidth = 0, rowHeight = 0, totalWidth = 0, totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(limit, double.PositiveInfinity));
            var size = child.DesiredSize;

            var advance = rowWidth == 0 ? size.Width : rowWidth + ItemSpacing + size.Width;

            if (advance > limit && rowWidth > 0)
            {
                totalWidth = Math.Max(totalWidth, rowWidth);
                totalHeight += rowHeight + LineSpacing;

                rowWidth = size.Width;
                rowHeight = size.Height;
                continue;
            }

            rowWidth = advance;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        totalWidth = Math.Max(totalWidth, rowWidth);
        totalHeight += rowHeight;

        return new Size(
            double.IsInfinity(limit) ? totalWidth : Math.Min(totalWidth, limit),
            totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > finalSize.Width)
            {
                x = 0;
                y += rowHeight + LineSpacing;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));

            x += size.Width + ItemSpacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return finalSize;
    }
}
