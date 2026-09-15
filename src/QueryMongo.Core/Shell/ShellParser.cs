namespace QueryMongo.Core.Shell;

/// <summary>One method call in a shell expression, e.g. <c>find({ a: 1 })</c>.</summary>
public sealed record ShellCall(string Method, IReadOnlyList<string> Arguments);

/// <summary>
/// A parsed shell command.
///
/// <c>db.movies.find({}).sort({ year: -1 }).limit(5)</c> becomes
/// Collection="movies" and Calls=[find, sort, limit].
/// </summary>
public sealed record ShellCommand(
    ShellCommandKind Kind,
    string? Target,
    string? Collection,
    IReadOnlyList<ShellCall> Calls);

public enum ShellCommandKind
{
    Empty,
    Help,
    ShowDatabases,
    ShowCollections,
    Use,
    /// <summary>A call on the database object itself, such as <c>db.runCommand(...)</c>.</summary>
    Database,
    /// <summary>A call on a collection, such as <c>db.movies.find(...)</c>.</summary>
    Collection,
    Unknown
}

/// <summary>
/// Splits shell input into a command and its arguments.
///
/// This is deliberately not a JavaScript parser: it recognises the
/// <c>db.collection.method(args).method(args)</c> shape and hands each argument to
/// the BSON reader, which already accepts shell-style JSON. That covers the commands
/// people actually type without embedding a scripting engine.
/// </summary>
public static class ShellParser
{
    public static ShellCommand Parse(string input)
    {
        var text = input.Trim().TrimEnd(';');
        if (text.Length == 0) return new ShellCommand(ShellCommandKind.Empty, null, null, []);

        if (text is "help" or "h" or "?")
            return new ShellCommand(ShellCommandKind.Help, null, null, []);

        if (text.StartsWith("show ", StringComparison.OrdinalIgnoreCase))
        {
            var what = text[5..].Trim().ToLowerInvariant();
            return what switch
            {
                "dbs" or "databases" => new ShellCommand(ShellCommandKind.ShowDatabases, null, null, []),
                "collections" or "tables" => new ShellCommand(ShellCommandKind.ShowCollections, null, null, []),
                _ => new ShellCommand(ShellCommandKind.Unknown, what, null, [])
            };
        }

        if (text.StartsWith("use ", StringComparison.OrdinalIgnoreCase))
            return new ShellCommand(ShellCommandKind.Use, text[4..].Trim(), null, []);

        if (!text.StartsWith("db.", StringComparison.Ordinal) && text != "db")
            return new ShellCommand(ShellCommandKind.Unknown, text, null, []);

        // Everything after "db." is a chain of identifiers and calls.
        var rest = text.Length > 3 ? text[3..] : "";
        var segments = SplitChain(rest);
        if (segments.Count == 0) return new ShellCommand(ShellCommandKind.Unknown, text, null, []);

        // db.runCommand(...) targets the database; db.movies.find(...) targets a collection.
        if (segments[0].IsCall)
            return new ShellCommand(ShellCommandKind.Database, null, null,
                segments.Select(s => new ShellCall(s.Name, s.Arguments)).ToList());

        var collection = segments[0].Name;
        var calls = segments.Skip(1)
            .Where(s => s.IsCall)
            .Select(s => new ShellCall(s.Name, s.Arguments))
            .ToList();

        // db.movies with no call is shorthand for listing it.
        if (calls.Count == 0)
            calls.Add(new ShellCall("find", []));

        return new ShellCommand(ShellCommandKind.Collection, null, collection, calls);
    }

    private sealed record Segment(string Name, bool IsCall, IReadOnlyList<string> Arguments);

    /// <summary>
    /// Walks the chain one segment at a time, tracking nesting so that a dot inside a
    /// string or a document (as in <c>{ "a.b": 1 }</c>) does not split the chain.
    /// </summary>
    private static List<Segment> SplitChain(string text)
    {
        var segments = new List<Segment>();
        var i = 0;

        while (i < text.Length)
        {
            // Read an identifier.
            var start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$' or '-'))
                i++;

            var name = text[start..i];
            if (name.Length == 0) break;

            if (i < text.Length && text[i] == '(')
            {
                var close = FindClosing(text, i, '(', ')');
                if (close < 0) break;

                var inner = text[(i + 1)..close];
                segments.Add(new Segment(name, true, SplitArguments(inner)));
                i = close + 1;
            }
            else
            {
                segments.Add(new Segment(name, false, []));
            }

            // Skip the dot between segments.
            if (i < text.Length && text[i] == '.') i++;
        }

        return segments;
    }

    /// <summary>Splits an argument list on top-level commas only.</summary>
    public static List<string> SplitArguments(string inner)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(inner)) return args;

        var depth = 0;
        var start = 0;
        var quote = '\0';

        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];

            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '{' or '[' or '(':
                    depth++;
                    break;
                case '}' or ']' or ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    args.Add(inner[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        var last = inner[start..].Trim();
        if (last.Length > 0) args.Add(last);

        return args;
    }

    /// <summary>Index of the bracket closing the one at <paramref name="open"/>, or -1.</summary>
    private static int FindClosing(string text, int open, char opener, char closer)
    {
        var depth = 0;
        var quote = '\0';

        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];

            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'') { quote = c; continue; }

            if (c == opener) depth++;
            else if (c == closer)
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }
}
