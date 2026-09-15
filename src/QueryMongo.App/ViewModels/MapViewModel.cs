using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The map pane: plots GeoJSON or coordinate-pair fields.
///
/// Points are drawn on a graticule rather than over map tiles, because tiles would
/// mean a network call to a third-party service from a desktop app that otherwise
/// talks only to your database.
/// </summary>
public sealed partial class MapViewModel : ObservableObject
{
    private readonly GeoService _geo;
    private readonly string _database;
    private readonly string _collection;

    public MapViewModel(string database, string collection, GeoService geo)
    {
        _database = database;
        _collection = collection;
        _geo = geo;

        Filter = "{}";
        Summary = "Load to plot coordinate data from this collection.";
    }

    public ObservableCollection<string> CandidateFields { get; } = [];

    /// <summary>The loaded points, handed to the map control to draw.</summary>
    public IReadOnlyList<GeoPoint> Points { get; private set; } = [];

    public GeoBounds Bounds { get; private set; } = GeoBounds.World;

    [ObservableProperty] public partial string Filter { get; set; }
    [ObservableProperty] public partial string? SelectedField { get; set; }
    [ObservableProperty] public partial string? LabelField { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial bool HasPoints { get; set; }
    [ObservableProperty] public partial bool HasGeoData { get; set; } = true;

    /// <summary>Raised when new points are ready, so the map control can redraw.</summary>
    public event EventHandler? PointsChanged;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _geo
                .LoadAsync(_database, _collection, SelectedField, Filter, LabelField)
                .ConfigureAwait(true);

            CandidateFields.Clear();
            foreach (var field in result.CandidateFields) CandidateFields.Add(field);

            SelectedField ??= result.UsedField;

            Points = result.Points;
            Bounds = result.Bounds;
            HasPoints = result.Points.Count > 0;
            HasGeoData = result.CandidateFields.Count > 0;

            Summary = result.CandidateFields.Count == 0
                ? "No coordinate fields found in a sample of this collection."
                : result.Points.Count == 0
                    ? $"No points matched using \"{result.UsedField}\"."
                    : $"{result.Points.Count:N0} point(s) from \"{result.UsedField}\"" +
                      (result.Points.Count >= GeoService.MaxPoints ? " (limit reached)" : "");

            PointsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
            Points = [];
            HasPoints = false;
            PointsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
