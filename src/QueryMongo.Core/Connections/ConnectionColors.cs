namespace QueryMongo.Core.Connections;

/// <summary>
/// The colour a connection can be tagged with, so a production deployment is visibly
/// different from a local one before a query is run against the wrong server.
///
/// The codes and hex values are Compass's own, which is why they are named
/// <c>color1</c>..<c>color10</c> rather than by the colour: the code is what is stored,
/// and each theme renders it differently.
/// </summary>
public static class ConnectionColors
{
    /// <summary>The code meaning "no colour chosen".</summary>
    public const string Default = "color10";

    private static readonly (string Code, string Name)[] Codes =
    [
        ("color1", "Green"),
        ("color2", "Teal"),
        ("color3", "Blue"),
        ("color4", "Iris"),
        ("color5", "Purple"),
        ("color6", "Red"),
        ("color7", "Pink"),
        ("color8", "Orange"),
        ("color9", "Yellow"),
        ("color10", "Gray")
    ];

    private static readonly Dictionary<string, string> LightDefault = new(StringComparer.Ordinal)
    {
        ["color1"] = "#D6F1DF", ["color2"] = "#CCF3EA", ["color3"] = "#D5EFFF",
        ["color4"] = "#E6E7FF", ["color5"] = "#F2E2FC", ["color6"] = "#FFDBDC",
        ["color7"] = "#FBDCEF", ["color8"] = "#FFDFB5", ["color9"] = "#FFF394",
        ["color10"] = "#C1C7C6"
    };

    private static readonly Dictionary<string, string> LightActive = new(StringComparer.Ordinal)
    {
        ["color1"] = "#C4E8D1", ["color2"] = "#B8EAE0", ["color3"] = "#C2E5FF",
        ["color4"] = "#DADCFF", ["color5"] = "#EAD5F9", ["color6"] = "#FFCDCE",
        ["color7"] = "#F6CEE7", ["color8"] = "#FFD19A", ["color9"] = "#FFE770",
        ["color10"] = "#E8EDEB"
    };

    private static readonly Dictionary<string, string> DarkDefault = new(StringComparer.Ordinal)
    {
        ["color1"] = "#113B29", ["color2"] = "#023B37", ["color3"] = "#003362",
        ["color4"] = "#262A65", ["color5"] = "#3D224E", ["color6"] = "#500F1C",
        ["color7"] = "#4B143D", ["color8"] = "#462100", ["color9"] = "#362B00",
        ["color10"] = "#E8EDEB"
    };

    private static readonly Dictionary<string, string> DarkActive = new(StringComparer.Ordinal)
    {
        ["color1"] = "#174933", ["color2"] = "#084843", ["color3"] = "#004074",
        ["color4"] = "#303374", ["color5"] = "#48295C", ["color6"] = "#611623",
        ["color7"] = "#591C47", ["color8"] = "#562800", ["color9"] = "#433500",
        ["color10"] = "#E8EDEB"
    };

    /// <summary>Every code a connection can be tagged with, with its display name.</summary>
    public static IReadOnlyList<(string Code, string Name)> All => Codes;

    /// <summary>True when the code names a colour the user actually chose.</summary>
    public static bool IsCustom(string? code) =>
        code is not null && code != Default && LightDefault.ContainsKey(code);

    /// <param name="active">The stronger shade, used for hover and the selected row.</param>
    public static string? ToHex(string? code, bool darkMode, bool active = false)
    {
        if (code is null) return null;

        var map = (darkMode, active) switch
        {
            (true, true) => DarkActive,
            (true, false) => DarkDefault,
            (false, true) => LightActive,
            _ => LightDefault
        };

        return map.TryGetValue(code, out var hex) ? hex : null;
    }

    public static string? ToName(string? code) =>
        code is null ? null : Codes.FirstOrDefault(c => c.Code == code).Name;
}
