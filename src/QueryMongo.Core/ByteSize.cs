namespace QueryMongo.Core;

/// <summary>Formats byte counts for the sidebar, stats strip and index list.</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";

        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < Units.Length - 1)
        {
            size /= 1024;
            order++;
        }

        return order == 0 ? $"{bytes} B" : $"{size:0.#} {Units[order]}";
    }
}
