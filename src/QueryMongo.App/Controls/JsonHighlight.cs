using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace QueryMongo.App.Controls;

/// <summary>
/// Syntax-colours JSON into a TextBlock's inlines.
///
/// Attach it in XAML with <c>c:JsonHighlight.Json="{x:Bind Json}"</c>. Colouring a
/// document makes the shape scannable, which is most of what makes a document list
/// readable rather than a wall of text.
/// </summary>
public static class JsonHighlight
{
    /// <summary>
    /// Documents above this size are shown unhighlighted. Building inlines is linear,
    /// but a 16 MB document would still produce hundreds of thousands of Runs and
    /// stall the UI thread.
    /// </summary>
    private const int MaxHighlightLength = 200_000;

    public static readonly DependencyProperty JsonProperty = DependencyProperty.RegisterAttached(
        "Json", typeof(string), typeof(JsonHighlight), new PropertyMetadata(null, OnJsonChanged));

    public static string GetJson(DependencyObject target) => (string)target.GetValue(JsonProperty);

    public static void SetJson(DependencyObject target, string value) =>
        target.SetValue(JsonProperty, value);

    private static void OnJsonChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock block) return;

        block.Inlines.Clear();

        if (e.NewValue is not string json || json.Length == 0) return;

        if (json.Length > MaxHighlightLength)
        {
            block.Inlines.Add(new Run { Text = json });
            return;
        }

        foreach (var run in Tokenize(json)) block.Inlines.Add(run);
    }

    private enum TokenKind { Key, String, Number, Keyword, Punctuation, Plain }

    private static IEnumerable<Run> Tokenize(string json)
    {
        var i = 0;

        while (i < json.Length)
        {
            var c = json[i];

            if (c == '"')
            {
                var end = FindStringEnd(json, i);
                var text = json[i..end];

                // A string immediately followed by a colon is a field name.
                var after = SkipWhitespace(json, end);
                var isKey = after < json.Length && json[after] == ':';

                yield return Make(text, isKey ? TokenKind.Key : TokenKind.String);
                i = end;
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < json.Length && char.IsDigit(json[i + 1])))
            {
                var start = i;
                while (i < json.Length && (char.IsDigit(json[i]) || json[i] is '.' or 'e' or 'E' or '+' or '-'))
                    i++;

                yield return Make(json[start..i], TokenKind.Number);
                continue;
            }

            if (char.IsLetter(c))
            {
                var start = i;
                while (i < json.Length && char.IsLetter(json[i])) i++;

                var word = json[start..i];
                yield return Make(word,
                    word is "true" or "false" or "null" ? TokenKind.Keyword : TokenKind.Plain);
                continue;
            }

            if (c is '{' or '}' or '[' or ']' or ':' or ',')
            {
                yield return Make(c.ToString(), TokenKind.Punctuation);
                i++;
                continue;
            }

            // Whitespace and anything else passes through untouched.
            var plainStart = i;
            while (i < json.Length
                   && json[i] != '"'
                   && !char.IsDigit(json[i])
                   && !char.IsLetter(json[i])
                   && json[i] is not ('{' or '}' or '[' or ']' or ':' or ','))
                i++;

            // Guard against a character that matches none of the branches above.
            if (i == plainStart) i++;

            yield return Make(json[plainStart..i], TokenKind.Plain);
        }
    }

    private static int FindStringEnd(string json, int openQuote)
    {
        var i = openQuote + 1;

        while (i < json.Length)
        {
            if (json[i] == '\\') { i += 2; continue; }
            if (json[i] == '"') return i + 1;
            i++;
        }

        return json.Length;
    }

    private static int SkipWhitespace(string json, int from)
    {
        var i = from;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        return i;
    }

    private static Run Make(string text, TokenKind kind) => new()
    {
        Text = text,
        Foreground = BrushFor(kind)
    };

    /// <summary>
    /// Theme brushes, so the colours follow light and dark mode rather than being
    /// hard-coded against one background.
    /// </summary>
    private static Brush BrushFor(TokenKind kind)
    {
        var key = kind switch
        {
            TokenKind.Key => "JsonKeyBrush",
            TokenKind.String => "JsonStringBrush",
            TokenKind.Number => "JsonNumberBrush",
            TokenKind.Keyword => "JsonKeywordBrush",
            TokenKind.Punctuation => "JsonPunctuationBrush",
            _ => "JsonPlainBrush"
        };

        if (Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush found)
            return found;

        return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }
}
