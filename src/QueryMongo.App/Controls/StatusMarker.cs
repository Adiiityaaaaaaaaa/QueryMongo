using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace QueryMongo.App.Controls;

/// <summary>
/// The 8x8 badge Compass draws over the bottom-right corner of a connection's icon:
/// a green dot when connected, a spinning arc while connecting, a red warning triangle
/// when the attempt failed, and nothing at all when the connection is simply closed.
///
/// The shapes are Compass's own, from <c>with-status-marker.tsx</c>.
/// </summary>
public sealed class StatusMarker : UserControl
{
    private const double Size = 8;

    /// <summary>A filled circle, r=3.5 at (4.25, 4.25).</summary>
    private const string ConnectedPath = "F1 M0.75,4.25 A3.5,3.5 0 1 0 7.75,4.25 A3.5,3.5 0 1 0 0.75,4.25 Z";

    /// <summary>Three quarters of a ring, so the rotation reads as motion.</summary>
    private const string ConnectingPath =
        "F0 M1.64712 6.14088C1.79974 6.34283 2.09302 6.34006 2.27132 6.16038C2.44962 5.9807 2.44376 " +
        "5.69245 2.30279 5.48221C2.0538 5.1109 1.91667 4.67113 1.91667 4.20833C1.91667 2.94268 2.94268 " +
        "1.91667 4.20833 1.91667C4.74833 1.91667 5.25651 2.10387 5.65961 2.4344C5.85535 2.5949 6.14175 " +
        "2.62823 6.33763 2.4679C6.53351 2.30757 6.56426 2.0159 6.37782 1.84468C5.80668 1.32018 5.0449 " +
        "1 4.20833 1C2.43642 1 1 2.43642 1 4.20833C1 4.93399 1.24091 5.60338 1.64712 6.14088Z";

    /// <summary>A warning triangle on a 7x6 canvas, scaled into the 8x8 badge.</summary>
    private const string FailedPath =
        "F0 M3.62796 0.412185C3.46455 0.112605 3.03545 0.112605 2.87204 0.412185L0.240953 5.23572C0.0839275 " +
        "5.5236 0.291686 5.875 0.618909 5.875H5.88109C6.20831 5.875 6.41607 5.5236 6.25905 5.23572L3.62796 " +
        "0.412185ZM2.8125 1.9375C2.8125 1.69588 3.00838 1.5 3.25 1.5C3.49162 1.5 3.6875 1.69588 3.6875 " +
        "1.9375V3.6875C3.6875 3.92912 3.49162 4.125 3.25 4.125C3.00838 4.125 2.8125 3.92912 2.8125 " +
        "3.6875V1.9375ZM3.6875 5C3.6875 5.24162 3.49162 5.4375 3.25 5.4375C3.00838 5.4375 2.8125 5.24162 " +
        "2.8125 5C2.8125 4.75838 3.00838 4.5625 3.25 4.5625C3.49162 4.5625 3.6875 4.75838 3.6875 5Z";

    // LeafyGreen green.dark1 and red.base, the colours Compass uses for these.
    private static readonly SolidColorBrush Green = new(Converters.Parse("#00A35C"));
    private static readonly SolidColorBrush Red = new(Converters.Parse("#DB3030"));

    private readonly Path _shape = new()
    {
        Stretch = Stretch.Uniform,
        Width = Size,
        Height = Size,
        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5)
    };

    private readonly RotateTransform _spin = new();
    private readonly Storyboard _spinner = new();

    public StatusMarker()
    {
        Width = Size;
        Height = Size;
        IsHitTestVisible = false;
        Content = _shape;

        _shape.RenderTransform = _spin;

        // The connecting marker turns once every 1.5s, linearly, forever.
        var turn = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(1.5)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(turn, _spin);
        Storyboard.SetTargetProperty(turn, "Angle");
        _spinner.Children.Add(turn);

        Apply();
    }

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(StatusMarker),
        new PropertyMetadata("none", (d, _) => ((StatusMarker)d).Apply()));

    /// <summary>One of "connected", "connecting", "failed", or anything else for no badge.</summary>
    public string? Status
    {
        get => (string?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private void Apply()
    {
        _spinner.Stop();

        switch (Status)
        {
            case "connected":
                Draw(ConnectedPath, Green);
                break;

            case "connecting":
                Draw(ConnectingPath, Green);
                _spinner.Begin();
                break;

            case "failed":
                Draw(FailedPath, Red);
                break;

            default:
                // A closed connection carries no badge, so a dormant row stays quiet.
                _shape.Data = null;
                _spin.Angle = 0;
                break;
        }
    }

    private void Draw(string data, Brush fill)
    {
        _shape.Fill = fill;
        _shape.Data = Geometry(data);
        _spin.Angle = 0;
    }

    /// <summary>
    /// Built fresh each time: WinUI geometry has a single parent, so a shared instance
    /// cannot be given to a second marker.
    /// </summary>
    private static Microsoft.UI.Xaml.Media.Geometry? Geometry(string data) => SvgPath.Parse(data);
}
