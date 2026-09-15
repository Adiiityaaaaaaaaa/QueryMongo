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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaretGlyph))]
    public partial bool IsExpanded { get; set; }

    /// <summary>The tree caret, pointing down when the branch is open.</summary>
    public string CaretGlyph => IsExpanded ? "CaretDown" : "CaretRight";

    /// <summary>The glyph Compass gives a database row in the sidebar.</summary>
    // Constant today, but x:Bind resolves against the instance, so it stays one.
#pragma warning disable CA1822
    public string IconGlyph => "Database";
#pragma warning restore CA1822

    public DatabaseInfo Info { get; } = info;

    public string Name => Info.Name;

    public string SizeDescription => ByteSize.Format(Info.SizeOnDisk);

    /// <summary>Collection count, available once the database has been expanded.</summary>
    public string CountDescription => _loaded
        ? _all.Count == 1 ? "1 collection" : $"{_all.Count} collections"
        : "";

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
            OnPropertyChanged(nameof(CountDescription));
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

    /// <summary>Document count for the sidebar row; blank when the server reported none.</summary>
    public string CountDescription => Info.DocumentCount is { } count ? $"{count:N0}" : "";

    public string SizeDescription => Info.StorageSizeBytes is { } bytes and > 0
        ? ByteSize.Format(bytes)
        : "";

    /// <summary>Full detail for the row tooltip, where there is room for it.</summary>
    public string Tooltip
    {
        get
        {
            var parts = new List<string> { $"{Database}.{Name}" };

            if (Info.DocumentCount is { } count) parts.Add($"{count:N0} documents");
            if (Info.StorageSizeBytes is { } bytes and > 0) parts.Add(ByteSize.Format(bytes));
            if (Info.IndexCount is { } indexes) parts.Add($"{indexes} indexes");

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>The glyph Compass gives each kind of collection.</summary>
    public string IconGlyph => Info.Kind switch
    {
        CollectionKind.View => "Visibility",
        CollectionKind.TimeSeries => "TimeSeries",
        _ => "Folder"
    };

    public string KindLabel => Info.Kind switch
    {
        CollectionKind.View => "view",
        CollectionKind.TimeSeries => "time series",
        _ => ""
    };
}
