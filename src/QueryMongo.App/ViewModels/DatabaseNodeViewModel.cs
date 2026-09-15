using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QueryMongo.Core.Models;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// One database in the sidebar. Collections load on first expand rather than up
/// front, so connecting to a deployment with hundreds of databases stays instant.
/// </summary>
public sealed partial class DatabaseNodeViewModel(DatabaseInfo info, CatalogService catalog) : ObservableObject
{
    private readonly CatalogService _catalog = catalog;
    private bool _loaded;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public DatabaseInfo Info { get; } = info;

    public string Name => Info.Name;

    public string SizeDescription => ByteSize.Format(Info.SizeOnDisk);

    public ObservableCollection<CollectionNodeViewModel> Collections { get; } = [];

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = EnsureLoadedAsync();
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded || IsLoading) return;

        IsLoading = true;
        try
        {
            var collections = await _catalog.ListCollectionsAsync(Name).ConfigureAwait(true);

            Collections.Clear();
            foreach (var c in collections)
                Collections.Add(new CollectionNodeViewModel(c));

            _loaded = true;
        }
        catch
        {
            // A database the user cannot list (permissions) simply shows as empty
            // rather than taking down the whole tree.
            _loaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public sealed class CollectionNodeViewModel(CollectionInfo info)
{
    public CollectionInfo Info { get; } = info;

    public string Name => Info.Name;

    public string Database => Info.Database;

    public string Glyph => Info.Kind switch
    {
        CollectionKind.View => "",       // preview
        CollectionKind.TimeSeries => "", // chart
        _ => ""                          // list
    };

    public string KindLabel => Info.Kind switch
    {
        CollectionKind.View => "view",
        CollectionKind.TimeSeries => "time series",
        _ => ""
    };
}

/// <summary>Formats byte counts the way the sidebar and stats strip show them.</summary>
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
