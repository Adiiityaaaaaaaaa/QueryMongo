using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Shape = Microsoft.UI.Xaml.Shapes.Shape;

namespace QueryMongo.App.Controls;

/// <summary>
/// Draws one glyph from the LeafyGreen icon set, the set Compass is drawn with.
///
/// Glyph names are the ones Compass uses in its own source ("Database", "Folder",
/// "Visibility"), so a pane can be written against the same names its Compass
/// counterpart uses.
/// </summary>
public sealed class LeafyIcon : UserControl
{
    private readonly Path _path = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    public LeafyIcon()
    {
        Content = _path;
        IsHitTestVisible = false;

        // Shapes do not inherit Foreground, but Control does, so the glyph follows the
        // colour of whatever row or button it sits in, exactly like an inline SVG would.
        _path.SetBinding(
            Shape.FillProperty,
            new Binding { Path = new PropertyPath(nameof(Foreground)), Source = this });

        ApplySize();
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(LeafyIcon),
        new PropertyMetadata(null, (d, _) => ((LeafyIcon)d).ApplyGlyph()));

    /// <summary>The LeafyGreen glyph name, for example "Database" or "MagnifyingGlass".</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LeafyIcon),
        new PropertyMetadata(LeafyGlyphs.CanvasSize, (d, _) => ((LeafyIcon)d).ApplySize()));

    /// <summary>
    /// Edge length in pixels. LeafyGreen's own steps are 14 (small), 16 (default),
    /// 20 (large) and 24 (xlarge).
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void ApplySize()
    {
        var size = Size <= 0 ? LeafyGlyphs.CanvasSize : Size;

        Width = size;
        Height = size;
        _path.Width = size;
        _path.Height = size;
    }

    private void ApplyGlyph()
    {
        var data = LeafyGlyphs.Find(Glyph);

        // An unknown name draws nothing rather than throwing, so a typo in a template
        // shows up as a blank slot instead of taking the window down.
        _path.Data = data is null ? null : ParseGeometry(data);
    }

    /// <summary>
    /// Geometry is built fresh for every icon rather than shared from a cache: unlike
    /// WPF, WinUI gives a Geometry a single parent, so handing one instance to a second
    /// Path fails. Parsing a glyph is a few hundred characters, which is cheap next to
    /// laying the icon out.
    /// </summary>
    private static Geometry? ParseGeometry(string data) => SvgPath.Parse(data);
}
