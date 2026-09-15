using System.Globalization;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace QueryMongo.App.Controls;

/// <summary>
/// Builds a <see cref="Geometry"/> from SVG path data.
///
/// WinUI only converts path data while it is parsing markup: handing the same string to
/// the converter from code takes the process down rather than returning a geometry or
/// throwing. Since the icon set is SVG to begin with, the data is parsed here instead.
///
/// The grammar is SVG's, which is also XAML's path mini-language: M, L, H, V, C, S, Q,
/// T, A and Z, uppercase for absolute and lowercase for relative, with an optional
/// leading F0 or F1 setting the fill rule.
/// </summary>
public static class SvgPath
{
    /// <summary>Parses path data, or returns null if it is malformed.</summary>
    public static Geometry? Parse(string data)
    {
        try
        {
            return Build(data);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static PathGeometry Build(string data)
    {
        var geometry = new PathGeometry();
        var reader = new Reader(data);

        // An F prefix sets the fill rule for the whole geometry: F0 even-odd, F1 nonzero.
        if (reader.TryReadFillRule(out var evenOdd))
            geometry.FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero;

        PathFigure? figure = null;
        var current = new Point(0, 0);
        var start = new Point(0, 0);

        // Where the previous curve's last control point was, which S and T mirror.
        Point? lastCubicControl = null;
        Point? lastQuadraticControl = null;

        var command = '\0';

        while (reader.TryReadCommand(ref command))
        {
            var relative = char.IsLower(command);

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var point = reader.ReadPoint(current, relative);

                    figure = new PathFigure { StartPoint = point, IsFilled = true, IsClosed = false };
                    geometry.Figures.Add(figure);

                    current = start = point;
                    lastCubicControl = lastQuadraticControl = null;

                    // Extra coordinate pairs after a moveto are implicit linetos.
                    while (reader.PeekIsNumber())
                    {
                        current = reader.ReadPoint(current, relative);
                        figure.Segments.Add(new LineSegment { Point = current });
                    }

                    break;
                }

                case 'L':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        current = reader.ReadPoint(current, relative);
                        figure.Segments.Add(new LineSegment { Point = current });
                    }
                    while (reader.PeekIsNumber());

                    lastCubicControl = lastQuadraticControl = null;
                    break;
                }

                case 'H':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        var x = reader.ReadNumber();
                        current = new Point(relative ? current.X + x : x, current.Y);
                        figure.Segments.Add(new LineSegment { Point = current });
                    }
                    while (reader.PeekIsNumber());

                    lastCubicControl = lastQuadraticControl = null;
                    break;
                }

                case 'V':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        var y = reader.ReadNumber();
                        current = new Point(current.X, relative ? current.Y + y : y);
                        figure.Segments.Add(new LineSegment { Point = current });
                    }
                    while (reader.PeekIsNumber());

                    lastCubicControl = lastQuadraticControl = null;
                    break;
                }

                case 'C':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        var first = reader.ReadPoint(current, relative);
                        var second = reader.ReadPoint(current, relative);
                        var end = reader.ReadPoint(current, relative);

                        figure.Segments.Add(new BezierSegment
                        {
                            Point1 = first,
                            Point2 = second,
                            Point3 = end
                        });

                        lastCubicControl = second;
                        current = end;
                    }
                    while (reader.PeekIsNumber());

                    lastQuadraticControl = null;
                    break;
                }

                case 'S':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        // The first control point mirrors the previous curve's last one.
                        var first = Reflect(lastCubicControl, current);
                        var second = reader.ReadPoint(current, relative);
                        var end = reader.ReadPoint(current, relative);

                        figure.Segments.Add(new BezierSegment
                        {
                            Point1 = first,
                            Point2 = second,
                            Point3 = end
                        });

                        lastCubicControl = second;
                        current = end;
                    }
                    while (reader.PeekIsNumber());

                    lastQuadraticControl = null;
                    break;
                }

                case 'Q':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        var control = reader.ReadPoint(current, relative);
                        var end = reader.ReadPoint(current, relative);

                        figure.Segments.Add(new QuadraticBezierSegment
                        {
                            Point1 = control,
                            Point2 = end
                        });

                        lastQuadraticControl = control;
                        current = end;
                    }
                    while (reader.PeekIsNumber());

                    lastCubicControl = null;
                    break;
                }

                case 'T':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        var control = Reflect(lastQuadraticControl, current);
                        var end = reader.ReadPoint(current, relative);

                        figure.Segments.Add(new QuadraticBezierSegment
                        {
                            Point1 = control,
                            Point2 = end
                        });

                        lastQuadraticControl = control;
                        current = end;
                    }
                    while (reader.PeekIsNumber());

                    lastCubicControl = null;
                    break;
                }

                case 'A':
                {
                    figure = Ensure(geometry, figure, current);

                    do
                    {
                        var rx = reader.ReadNumber();
                        var ry = reader.ReadNumber();
                        var rotation = reader.ReadNumber();
                        var largeArc = reader.ReadFlag();
                        var sweep = reader.ReadFlag();
                        var end = reader.ReadPoint(current, relative);

                        figure.Segments.Add(new ArcSegment
                        {
                            Size = new Size(Math.Abs(rx), Math.Abs(ry)),
                            RotationAngle = rotation,
                            IsLargeArc = largeArc,
                            SweepDirection = sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                            Point = end
                        });

                        current = end;
                    }
                    while (reader.PeekIsNumber());

                    lastCubicControl = lastQuadraticControl = null;
                    break;
                }

                case 'Z':
                {
                    if (figure is not null)
                    {
                        figure.IsClosed = true;
                        current = start;
                    }

                    // A new subpath starts from scratch rather than continuing this one.
                    figure = null;
                    lastCubicControl = lastQuadraticControl = null;
                    break;
                }

                default:
                    throw new FormatException($"Unsupported path command '{command}'.");
            }
        }

        return geometry;
    }

    /// <summary>
    /// Mirrors the previous control point through the current one, which is what the
    /// smooth curve commands mean by "reflected". With no previous curve the control
    /// point sits on the current point, so the curve starts straight.
    /// </summary>
    private static Point Reflect(Point? previous, Point current) =>
        previous is { } p
            ? new Point((2 * current.X) - p.X, (2 * current.Y) - p.Y)
            : current;

    /// <summary>
    /// Path data may draw before it moves, and a figure may continue after a Z. Either
    /// way a figure has to exist to hold the segment.
    /// </summary>
    private static PathFigure Ensure(PathGeometry geometry, PathFigure? figure, Point current)
    {
        if (figure is not null) return figure;

        var created = new PathFigure { StartPoint = current, IsFilled = true };
        geometry.Figures.Add(created);

        return created;
    }

    /// <summary>Walks the path data one token at a time.</summary>
    private sealed class Reader(string text)
    {
        private readonly string _text = text;
        private int _at;

        public bool TryReadFillRule(out bool evenOdd)
        {
            evenOdd = false;
            SkipWhitespace();

            if (_at >= _text.Length || (_text[_at] != 'F' && _text[_at] != 'f')) return false;
            if (_at + 1 >= _text.Length) return false;

            evenOdd = _text[_at + 1] == '0';
            _at += 2;

            return true;
        }

        /// <summary>
        /// Reads the next command letter, or keeps the previous one when the data simply
        /// continues with more numbers, which SVG allows.
        /// </summary>
        public bool TryReadCommand(ref char command)
        {
            SkipSeparators();

            if (_at >= _text.Length) return false;

            var c = _text[_at];

            if (char.IsLetter(c))
            {
                command = c;
                _at++;
                return true;
            }

            // A number here repeats the previous command, except that a repeated moveto
            // means lineto, which the M case already handles.
            return command != '\0';
        }

        public bool PeekIsNumber()
        {
            SkipSeparators();

            return _at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] is '-' or '+' or '.');
        }

        public Point ReadPoint(Point current, bool relative)
        {
            var x = ReadNumber();
            var y = ReadNumber();

            return relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
        }

        /// <summary>Arc flags are a single character, and may be written without separators.</summary>
        public bool ReadFlag()
        {
            SkipSeparators();

            if (_at >= _text.Length) throw new FormatException("Expected an arc flag.");

            var flag = _text[_at] == '1';
            _at++;

            return flag;
        }

        public double ReadNumber()
        {
            SkipSeparators();

            var start = _at;

            if (_at < _text.Length && (_text[_at] == '-' || _text[_at] == '+')) _at++;

            while (_at < _text.Length && char.IsDigit(_text[_at])) _at++;

            if (_at < _text.Length && _text[_at] == '.')
            {
                _at++;
                while (_at < _text.Length && char.IsDigit(_text[_at])) _at++;
            }

            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E'))
            {
                _at++;
                if (_at < _text.Length && (_text[_at] == '-' || _text[_at] == '+')) _at++;
                while (_at < _text.Length && char.IsDigit(_text[_at])) _at++;
            }

            if (_at == start) throw new FormatException($"Expected a number at offset {start}.");

            return double.Parse(_text.AsSpan(start, _at - start), CultureInfo.InvariantCulture);
        }

        private void SkipWhitespace()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at])) _at++;
        }

        /// <summary>Commas and whitespace are interchangeable in path data.</summary>
        private void SkipSeparators()
        {
            while (_at < _text.Length && (char.IsWhiteSpace(_text[_at]) || _text[_at] == ',')) _at++;
        }
    }
}
