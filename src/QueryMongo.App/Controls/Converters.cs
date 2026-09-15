using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using QueryMongo.Core.Connections;
using Windows.UI;

namespace QueryMongo.App.Controls;

/// <summary>
/// Helpers used from XAML through x:Bind function binding, which keeps the bindings
/// compiled and avoids registering converter instances in resource dictionaries.
/// </summary>
public static class Converters
{
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NotEmpty(string? value) => Visible(!string.IsNullOrWhiteSpace(value));

    public static Visibility VisibleIfNull(object? value) => Visible(value is null);

    public static Visibility VisibleIfNotNull(object? value) => Visible(value is not null);

    /// <summary>Shows a pane only when the tab strip has it selected.</summary>
    public static Visibility VisibleWhen(int selected, int index) => Visible(selected == index);

    /// <summary>Shows an element only while a named state is current, such as a status badge.</summary>
    public static Visibility VisibleIfEqual(string? value, string expected) =>
        Visible(string.Equals(value, expected, StringComparison.Ordinal));

    /// <summary>For InfoBar.IsOpen, which takes a bool rather than a Visibility.</summary>
    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    public static Visibility VisibleIfAny(int count) => Visible(count > 0);

    public static bool Not(bool value) => !value;

    /// <summary>Renders a count the way Compass's own tables do.</summary>
    public static string Count(int value) => value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    // ---- connection colours ---------------------------------------------

    /// <summary>
    /// The connection's colour tag as a brush, transparent when it has none.
    ///
    /// Theme is read from the app rather than passed in, because these bindings are
    /// evaluated inside item templates that have no theme context of their own.
    /// </summary>
    public static Brush ConnectionColor(string? colorCode) => ToBrush(colorCode, active: false);

    /// <summary>The stronger shade, for a hovered or selected row.</summary>
    public static Brush ConnectionColorActive(string? colorCode) => ToBrush(colorCode, active: true);

    private static SolidColorBrush ToBrush(string? colorCode, bool active)
    {
        var hex = ConnectionColors.ToHex(colorCode, IsDarkTheme(), active);

        return hex is null ? Transparent : new SolidColorBrush(Parse(hex));
    }

    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    private static bool IsDarkTheme() =>
        Application.Current?.RequestedTheme == ApplicationTheme.Dark;

    /// <summary>Parses <c>#RRGGBB</c>; anything else is treated as transparent.</summary>
    public static Color Parse(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#') return Colors.Transparent;

        return Color.FromArgb(
            255,
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16));
    }
}
