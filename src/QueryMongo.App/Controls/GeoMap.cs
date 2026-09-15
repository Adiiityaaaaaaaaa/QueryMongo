using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QueryMongo.Core.Services;

namespace QueryMongo.App.Controls;

/// <summary>
/// Plots geographic points on an equirectangular projection with a latitude and
/// longitude graticule.
///
/// There are no map tiles: fetching them would mean calling a third-party service
/// from an app that otherwise only talks to the user's database. The graticule plus
/// degree labels is enough to read where points sit and how they cluster.
///
/// Built on Canvas so positions go through Canvas.Left/Top and the normal layout
/// pass does the arranging.
/// </summary>
public sealed class GeoMap : Canvas
{
    private IReadOnlyList<GeoPoint> _points = [];
    private GeoBounds _bounds = GeoBounds.World;

    public GeoMap()
    {
        SizeChanged += (_, _) => Redraw();
    }

    public void SetPoints(IReadOnlyList<GeoPoint> points, GeoBounds bounds)
    {
        _points = points;
        _bounds = bounds;
        Redraw();
    }

    private void Redraw()
    {
        Children.Clear();

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 20 || height < 20) return;

        DrawGraticule(width, height);
        DrawPoints(width, height);
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];

    private void DrawGraticule(double width, double height)
    {
        var hairline = Resource("HairlineBrush");
        var faint = Resource("InkFaintBrush");

        // Round the step to something a reader can label: 0.01 up to 60 degrees.
        var lonStep = ChooseStep(_bounds.Width);
        var latStep = ChooseStep(_bounds.Height);

        for (var lon = Math.Ceiling(_bounds.MinLongitude / lonStep) * lonStep;
             lon <= _bounds.MaxLongitude;
             lon += lonStep)
        {
            var x = ToX(lon, width);
            AddLine(x, 0, x, height, hairline);
            AddLabel($"{lon:0.#}°", x + 3, height - 16, faint);
        }

        for (var lat = Math.Ceiling(_bounds.MinLatitude / latStep) * latStep;
             lat <= _bounds.MaxLatitude;
             lat += latStep)
        {
            var y = ToY(lat, height);
            AddLine(0, y, width, y, hairline);
            AddLabel($"{lat:0.#}°", 4, y + 2, faint);
        }

        // The equator and prime meridian get a stronger line when they are in view.
        if (_bounds.MinLatitude <= 0 && _bounds.MaxLatitude >= 0)
            AddLine(0, ToY(0, height), width, ToY(0, height), faint);

        if (_bounds.MinLongitude <= 0 && _bounds.MaxLongitude >= 0)
            AddLine(ToX(0, width), 0, ToX(0, width), height, faint);
    }

    private void DrawPoints(double width, double height)
    {
        if (_points.Count == 0) return;

        var accent = Resource("AccentGreenBrush");

        // Dense data reads better as small translucent marks that build up where
        // points overlap, rather than as a solid mass.
        var radius = _points.Count > 1500 ? 2.0 : _points.Count > 400 ? 3.0 : 4.5;
        var opacity = _points.Count > 1500 ? 0.45 : 0.75;

        foreach (var point in _points)
        {
            var dot = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = accent,
                Opacity = opacity
            };

            ToolTipService.SetToolTip(dot,
                $"{point.Label}\n{point.Latitude:0.####}, {point.Longitude:0.####}");

            SetLeft(dot, ToX(point.Longitude, width) - radius);
            SetTop(dot, ToY(point.Latitude, height) - radius);

            Children.Add(dot);
        }
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush stroke) =>
        Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke,
            StrokeThickness = 1
        });

    private void AddLabel(string text, double x, double y, Brush foreground)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = foreground
        };

        SetLeft(block, x);
        SetTop(block, y);
        Children.Add(block);
    }

    private double ToX(double longitude, double width) =>
        (longitude - _bounds.MinLongitude) / _bounds.Width * width;

    /// <summary>Latitude grows upward, so the axis is inverted against screen coordinates.</summary>
    private double ToY(double latitude, double height) =>
        height - ((latitude - _bounds.MinLatitude) / _bounds.Height * height);

    private static double ChooseStep(double span)
    {
        double[] steps = [0.01, 0.05, 0.1, 0.5, 1, 5, 10, 30, 60];

        foreach (var step in steps)
            if (span / step <= 10)
                return step;

        return 60;
    }
}
