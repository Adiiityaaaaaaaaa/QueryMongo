using System.Globalization;

namespace QueryMongo.Core;

/// <summary>
/// Number formatting for the database, collection and index listings.
///
/// This is a port of Compass's own <c>compactBytes</c> and <c>compactNumber</c>, so the
/// same collection reads the same in both tools: SI units with two decimals rather than
/// the binary units Windows usually shows.
/// </summary>
public static class ByteSize
{
    private static readonly string[] SiUnits = ["B", "kB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];

    private static readonly string[] BinaryUnits =
        ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];

    /// <param name="bytes">May be negative; the sign is kept.</param>
    /// <param name="si">SI (1000-based) units, as Compass uses by default.</param>
    /// <param name="decimals">Digits after the point, two by default.</param>
    public static string Format(long bytes, bool si = true, int decimals = 2)
    {
        var negative = bytes < 0;
        var magnitude = (double)Math.Abs(bytes);

        if (magnitude == 0) return "0 B";

        var threshold = si ? 1000d : 1024d;
        var units = si ? SiUnits : BinaryUnits;

        var index = (int)Math.Floor(Math.Log(magnitude) / Math.Log(threshold));
        if (index >= units.Length) index = units.Length - 1;
        if (index < 0) index = 0;

        var value = magnitude / Math.Pow(threshold, index);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(negative ? "-" : "")}{value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} {units[index]}");
    }

    /// <summary>
    /// Counts in compact notation: 1500 becomes "1.5K", 2_000_000 becomes "2M".
    ///
    /// Compass gets this from <c>Intl.NumberFormat</c> with compact notation, which keeps
    /// at most one fractional digit and drops a trailing zero.
    /// </summary>
    public static string CompactNumber(long number)
    {
        var negative = number < 0;
        var magnitude = (double)Math.Abs(number);

        if (magnitude < 1000)
            return number.ToString(CultureInfo.InvariantCulture);

        string[] suffixes = ["", "K", "M", "B", "T"];

        var index = 0;
        while (magnitude >= 1000 && index < suffixes.Length - 1)
        {
            magnitude /= 1000;
            index++;
        }

        // One fractional digit, but only when it says something: 1.5K, not 2.0M.
        var rounded = Math.Round(magnitude, 1, MidpointRounding.AwayFromZero);
        var text = rounded % 1 == 0
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.0", CultureInfo.InvariantCulture);

        return (negative ? "-" : "") + text + suffixes[index];
    }
}
