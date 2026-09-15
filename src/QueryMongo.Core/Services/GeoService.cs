using MongoDB.Bson;
using MongoDB.Driver;
using QueryMongo.Core.Json;
using QueryMongo.Core.Mongo;

namespace QueryMongo.Core.Services;

/// <summary>One plotted point, with the document it came from.</summary>
public sealed record GeoPoint(double Longitude, double Latitude, string Label, BsonDocument Document);

/// <summary>The extent of a set of points, used to frame the map.</summary>
public sealed record GeoBounds(double MinLongitude, double MinLatitude, double MaxLongitude, double MaxLatitude)
{
    public double Width => Math.Max(MaxLongitude - MinLongitude, 0.0001);
    public double Height => Math.Max(MaxLatitude - MinLatitude, 0.0001);

    /// <summary>The whole world, used when there is nothing to frame.</summary>
    public static GeoBounds World => new(-180, -90, 180, 90);

    public static GeoBounds Around(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0) return World;

        var minLon = points.Min(p => p.Longitude);
        var maxLon = points.Max(p => p.Longitude);
        var minLat = points.Min(p => p.Latitude);
        var maxLat = points.Max(p => p.Latitude);

        // A single point has no extent, so pad it into a small window.
        var padLon = Math.Max((maxLon - minLon) * 0.1, 0.5);
        var padLat = Math.Max((maxLat - minLat) * 0.1, 0.5);

        return new GeoBounds(
            Math.Max(minLon - padLon, -180), Math.Max(minLat - padLat, -90),
            Math.Min(maxLon + padLon, 180), Math.Min(maxLat + padLat, 90));
    }
}

public sealed record GeoResult(
    IReadOnlyList<GeoPoint> Points,
    GeoBounds Bounds,
    IReadOnlyList<string> CandidateFields,
    string? UsedField);

/// <summary>
/// Finds coordinate data in a collection and extracts it for the map view.
///
/// Handles the two shapes people store: GeoJSON (<c>{ type: "Point", coordinates:
/// [lon, lat] }</c>) and a legacy coordinate pair (<c>[lon, lat]</c>), plus separate
/// latitude/longitude fields.
/// </summary>
public sealed class GeoService(MongoSession session)
{
    private readonly MongoSession _session = session;

    /// <summary>More points than this and the scatter stops being readable.</summary>
    public const int MaxPoints = 5000;

    /// <summary>
    /// Scans a sample for fields that look like coordinates, so the UI can offer a
    /// field picker rather than making the user guess.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindGeoFieldsAsync(
        string database, string collection, int sampleSize = 100, CancellationToken ct = default)
    {
        var coll = _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

        var pipeline = new[] { new BsonDocument("$sample", new BsonDocument("size", sampleSize)) };

        using var cursor = await coll.AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct)
            .ConfigureAwait(false);

        var found = new HashSet<string>(StringComparer.Ordinal);

        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
            foreach (var doc in cursor.Current)
                Scan(doc, "", found, depth: 0);

        return found.OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    private static void Scan(BsonDocument doc, string prefix, HashSet<string> found, int depth)
    {
        if (depth > 3) return;

        foreach (var element in doc.Elements)
        {
            var path = prefix.Length == 0 ? element.Name : $"{prefix}.{element.Name}";

            if (LooksGeographic(element.Value)) found.Add(path);

            if (element.Value is BsonDocument nested) Scan(nested, path, found, depth + 1);
        }
    }

    private static bool LooksGeographic(BsonValue value) => value switch
    {
        // GeoJSON.
        BsonDocument d when d.GetValue("type", BsonString.Empty).AsString == "Point"
                            && d.Contains("coordinates") => true,

        // Legacy pair: two numbers in a plausible range.
        BsonArray { Count: 2 } a when a[0].IsNumeric && a[1].IsNumeric
                                     && Math.Abs(a[0].ToDouble()) <= 180
                                     && Math.Abs(a[1].ToDouble()) <= 90 => true,

        _ => false
    };

    /// <summary>
    /// Loads points for the map. Only documents where the field parses as a coordinate
    /// are plotted; the rest are skipped rather than failing the whole view.
    /// </summary>
    public async Task<GeoResult> LoadAsync(
        string database,
        string collection,
        string? field = null,
        string filter = "{}",
        string? labelField = null,
        int limit = MaxPoints,
        CancellationToken ct = default)
    {
        var candidates = await FindGeoFieldsAsync(database, collection, ct: ct).ConfigureAwait(false);

        var chosen = field is { Length: > 0 } ? field : candidates.Count > 0 ? candidates[0] : null;
        if (chosen is null) return new GeoResult([], GeoBounds.World, candidates, null);

        var coll = _session.Client.GetDatabase(database).GetCollection<BsonDocument>(collection);

        var parsed = BsonJson.ParseDocument(filter);
        var query = parsed.IsValid ? parsed.Value ?? new BsonDocument() : new BsonDocument();

        // Only documents that actually have the field are worth fetching.
        var combined = query.ElementCount == 0
            ? new BsonDocument(chosen, new BsonDocument("$exists", true))
            : new BsonDocument("$and", new BsonArray
            {
                query,
                new BsonDocument(chosen, new BsonDocument("$exists", true))
            });

        using var cursor = await coll
            .FindAsync(combined, new FindOptions<BsonDocument> { Limit = limit, BatchSize = 500 }, ct)
            .ConfigureAwait(false);

        var points = new List<GeoPoint>();

        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                if (Extract(doc, chosen) is not { } coords) continue;

                var label = labelField is { Length: > 0 } && ReadPath(doc, labelField) is { } l
                    ? l.ToString() ?? ""
                    : doc.GetValue("_id", BsonNull.Value).ToString() ?? "";

                points.Add(new GeoPoint(coords.Longitude, coords.Latitude, label, doc));
            }
        }

        return new GeoResult(points, GeoBounds.Around(points), candidates, chosen);
    }

    private static (double Longitude, double Latitude)? Extract(BsonDocument doc, string path)
    {
        var value = ReadPath(doc, path);

        return value switch
        {
            BsonDocument d when d.Contains("coordinates")
                                && d["coordinates"] is BsonArray { Count: 2 } c
                                && c[0].IsNumeric && c[1].IsNumeric
                => (c[0].ToDouble(), c[1].ToDouble()),

            BsonArray { Count: 2 } a when a[0].IsNumeric && a[1].IsNumeric
                => (a[0].ToDouble(), a[1].ToDouble()),

            _ => null
        };
    }

    private static BsonValue? ReadPath(BsonDocument doc, string path)
    {
        BsonValue current = doc;

        foreach (var segment in path.Split('.'))
        {
            if (current is not BsonDocument d || !d.TryGetValue(segment, out var next)) return null;
            current = next;
        }

        return current;
    }
}
