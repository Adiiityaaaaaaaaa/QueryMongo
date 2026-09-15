using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QueryMongo.Core;
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

    /// <summary>Everything loaded for this database, before the sidebar filter.</summary>
    private readonly List<CollectionNodeViewModel> _all = [];

    private bool _loaded;
    private string _filter = "";

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; }

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
        await ReloadAsync().ConfigureAwait(true);
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            var collections = await _catalog.ListCollectionsAsync(Name).ConfigureAwait(true);

            _all.Clear();
            foreach (var c in collections) _all.Add(new CollectionNodeViewModel(c));

            Project();
            _loaded = true;
        }
        catch
        {
            // A database the user cannot list (permissions) shows as empty rather than
            // taking down the whole tree.
            _loaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Narrows the visible collections. Returns true when something matches, so the
    /// sidebar knows whether to keep this database in the list.
    /// </summary>
    public bool ApplyCollectionFilter(string term)
    {
        _filter = term;
        Project();

        // An unexpanded database has nothing loaded to match against yet.
        return Collections.Count > 0;
    }

    public void ClearCollectionFilter()
    {
        _filter = "";
        Project();
    }

    private void Project()
    {
        Collections.Clear();

        foreach (var c in _all)
        {
            if (_filter.Length > 0 && !c.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                continue;

            Collections.Add(c);
        }
    }
}

public sealed class CollectionNodeViewModel(CollectionInfo info)
{
    public CollectionInfo Info { get; } = info;

    public string Name => Info.Name;

    public string Database => Info.Database;

    public CollectionKind Kind => Info.Kind;

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
