using Microsoft.UI.Xaml.Controls;

namespace QueryMongo.App.Controls;

/// <summary>
/// LeafyGreen glyphs as <see cref="IconElement"/>, for the places WinUI insists on one:
/// menu items, and anything else taking an Icon rather than arbitrary content.
/// </summary>
public static class LeafyIconSource
{
    /// <summary>
    /// An icon element for a glyph, or null when the name is unknown, which leaves the
    /// menu item without an icon rather than failing.
    /// </summary>
    /// <remarks>
    /// A fresh element is returned each time. Unlike geometry, an IconElement is a
    /// UIElement and cannot appear in two menus at once.
    /// </remarks>
    public static IconElement? For(string glyph)
    {
        if (LeafyGlyphs.Find(glyph) is not { } data) return null;

        // The glyphs are drawn on a 16x16 canvas, which is also PathIcon's own box.
        return SvgPath.Parse(data) is { } geometry ? new PathIcon { Data = geometry } : null;
    }
}
