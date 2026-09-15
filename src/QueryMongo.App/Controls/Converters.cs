using Microsoft.UI.Xaml;

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

    /// <summary>For InfoBar.IsOpen, which takes a bool rather than a Visibility.</summary>
    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    public static Visibility VisibleIfAny(int count) => Visible(count > 0);

    public static bool Not(bool value) => !value;
}
