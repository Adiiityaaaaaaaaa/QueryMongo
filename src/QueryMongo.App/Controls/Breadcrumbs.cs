using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace QueryMongo.App.Controls;

/// <summary>One step in a breadcrumb trail. The last step has nothing to click.</summary>
/// <param name="Name">What the step is labelled with.</param>
/// <param name="Open">Where clicking it goes, or null for the step you are already on.</param>
public sealed record Crumb(string Name, Action? Open = null);

/// <summary>
/// The trail Compass puts above a database list, collection list and collection: each
/// ancestor is a link, the step you are on is plain, and a small chevron separates them.
/// </summary>
public sealed class Breadcrumbs : UserControl
{
    private readonly StackPanel _row = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center
    };

    public Breadcrumbs() => Content = _row;

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(object), typeof(Breadcrumbs),
        new PropertyMetadata(null, (d, _) => ((Breadcrumbs)d).Rebuild()));

    /// <summary>The trail, outermost first.</summary>
    public IReadOnlyList<Crumb>? Items
    {
        get => (IReadOnlyList<Crumb>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    private void Rebuild()
    {
        _row.Children.Clear();

        if (Items is not { Count: > 0 } items) return;

        for (var i = 0; i < items.Count; i++)
        {
            var crumb = items[i];
            var last = i == items.Count - 1;

            if (last || crumb.Open is null)
            {
                _row.Children.Add(new TextBlock
                {
                    Text = crumb.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Resource("InkMutedBrush")
                });
            }
            else
            {
                var open = crumb.Open;
                var link = new Button
                {
                    Content = new TextBlock
                    {
                        Text = crumb.Name,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    Style = Resource<Style>("BreadcrumbLinkStyle")
                };

                link.Click += (_, _) => open();
                _row.Children.Add(link);
            }

            if (last) continue;

            _row.Children.Add(new LeafyIcon
            {
                Glyph = "ChevronRight",
                Size = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Resource("InkFaintBrush")
            });
        }
    }

    private static Brush? Resource(string key) => Resource<Brush>(key);

    private static T? Resource<T>(string key) where T : class =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as T : null;
}
