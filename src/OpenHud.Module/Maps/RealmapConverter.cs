using OpenHud.Model;
using OpenHud.Packages.Realmap;
using OpenHud.Radar;

namespace OpenHud.Maps;

// The geometry of one window of the realmap package in the form the radar and the map builders take,
// already rotated into the display frame (see MapFrame).
internal sealed record FeatureSet(
    long Id,
    MapRect Window,
    DetailLevel Detail,
    RadarFeature[] Features,
    RadarRegion[] Regions,
    RadarMarker[] Markers,
    bool Truncated,
    int UnreadableChunks,
    // Features smaller than this (metres) were left out as too small to see at the scale it was asked for.
    double MinExtent = 0)
{

    // True when the window holds the square of half-size `half` around (x, y).
    public bool Covers(double x, double y, double half) =>
        Window.IsValid && x - half >= Window.MinX && x + half <= Window.MaxX && y - half >= Window.MinY && y + half <= Window.MaxY;
}

// Package space (the author's metres, X east, Y north) to display space: the same plane turned by the
// package's `north` so that real north is up, around the origin. Everything the radar draws goes through it,
// so features, vehicles and the player always agree.
internal readonly struct MapFrame
{
    private readonly RadarProjection projection;

    public MapFrame(double northDegrees)
    {
        NorthDegrees = Angles.Normalize(northDegrees);
        projection = new RadarProjection(0, 0, NorthDegrees);
    }

    public double NorthDegrees { get; }
    public bool IsIdentity => NorthDegrees == 0;

    public (double X, double Y) ToDisplay(double x, double y) => IsIdentity ? (x, y) : projection.WorldToRadar(x, y);
    public (double X, double Y) ToPackage(double x, double y) => IsIdentity ? (x, y) : projection.RadarToWorld(x, y);
    public double Heading(double headingDegrees) => IsIdentity ? Angles.Normalize(headingDegrees) : projection.ObjectAngle(headingDegrees);
}

// Turns a RealmapView into radar features. Pure; the caller decides the window, the detail and the rotation.
internal static class RealmapConverter
{
    // Triangles are many and tiny; past this the rest of the window is left out (the builders spend a
    // smaller budget anyway) and the set says so.
    public const int MaxFeatures = 40_000;
    // What a tick is asked to project and clip: the largest features of the window up to this many.
    public const int DefaultBudget = 7000;
    public const int MaxRegions = 200;
    public const int MaxMarkers = 400;

    public static FeatureSet Convert(RealmapView view, MapFrame frame, long id, MapRect window, DetailLevel detail,
        Func<string, string?> imageIdOf, bool minorContours, double minExtent = 0, int budget = DefaultBudget, MapPalette? palette = null)
    {
        var features = new List<RadarFeature>(Math.Min(MaxFeatures, view.Areas.Count * 4 + view.Lines.Count + view.Streets.Count));
        var truncated = view.Truncated;

        foreach (var area in view.Areas)
        {
            var layer = AreaLayer(area.Class);
            var t = area.Triangles;
            for (var i = 0; i + 5 < t.Length; i += 6)
            {
                if (features.Count >= MaxFeatures) { truncated = true; break; }
                if (minExtent > 0 && Extent(t, i, 6) < minExtent) continue;
                var points = new double[6];
                for (var k = 0; k < 3; k++)
                {
                    var (x, y) = frame.ToDisplay(t[i + 2 * k], t[i + 2 * k + 1]);
                    points[2 * k] = x; points[2 * k + 1] = y;
                }
                features.Add(new RadarFeature(layer, points, true, palette?.Fill(area.Class)));
            }
        }

        foreach (var line in view.Lines)
        {
            if (line.Class == LineClass.Contour && !minorContours) continue;
            if (features.Count >= MaxFeatures) { truncated = true; break; }
            if (minExtent > 0 && Extent(line.Points, 0, line.Points.Length) < minExtent) continue;
            var drawn = Line(line, frame, palette);
            // A rail's edge colour is a wider line under it.
            if (line.Class == LineClass.Rail && palette?.RailEdge is { } edge && features.Count < MaxFeatures) features.Add(new RadarFeature(RadarLayer.Road, drawn.Points, false, edge, 5.5));
            features.Add(drawn);
        }
        foreach (var street in view.Streets)
        {
            if (features.Count >= MaxFeatures) { truncated = true; break; }
            if (minExtent > 0 && Extent(street.Points, 0, street.Points.Length) < minExtent) continue;
            features.Add(Line(street, frame, palette));
        }

        var regions = new List<RadarRegion>();
        foreach (var region in view.Regions)
        {
            if (regions.Count >= MaxRegions) break;
            if (!region.HasReadableLabel || string.IsNullOrWhiteSpace(region.Name)) continue;
            var (x, y) = frame.ToDisplay(region.LabelX, region.LabelY);
            regions.Add(new RadarRegion(region.Name, x, y));
        }
        foreach (var label in view.Labels)
        {
            if (regions.Count >= MaxRegions) break;
            if (label.Kind == LabelKind.Water || string.IsNullOrWhiteSpace(label.Text)) continue;
            var (x, y) = frame.ToDisplay(label.X, label.Y);
            regions.Add(new RadarRegion(label.Text!, x, y));
        }

        var markers = new List<RadarMarker>();
        foreach (var marker in view.Markers)
        {
            if (markers.Count >= MaxMarkers) break;
            var (x, y) = frame.ToDisplay(marker.X, marker.Y);
            markers.Add(new RadarMarker(x, y, imageIdOf(marker.IconId), marker.Name));
        }

        var kept = features.Count > budget ? Largest(features, budget) : features.ToArray();
        return new FeatureSet(id, window, detail, kept, regions.ToArray(), markers.ToArray(), truncated || features.Count > budget, view.UnreadableChunks, minExtent);
    }

    // Longest side of the bounding box of x,y pairs in points[from..from+count).
    private static double Extent(float[] points, int from, int count)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = from; i + 1 < from + count; i += 2)
        {
            if (points[i] < minX) minX = points[i]; if (points[i] > maxX) maxX = points[i];
            if (points[i + 1] < minY) minY = points[i + 1]; if (points[i + 1] > maxY) maxY = points[i + 1];
        }
        return Math.Max(maxX - minX, maxY - minY);
    }

    // The `budget` largest features, in their original order (the builders order by layer themselves).
    private static RadarFeature[] Largest(List<RadarFeature> features, int budget)
    {
        var extents = new double[features.Count];
        for (var i = 0; i < extents.Length; i++)
        {
            var p = features[i].Points;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var k = 0; k + 1 < p.Length; k += 2)
            {
                if (p[k] < minX) minX = p[k]; if (p[k] > maxX) maxX = p[k];
                if (p[k + 1] < minY) minY = p[k + 1]; if (p[k + 1] > maxY) maxY = p[k + 1];
            }
            // Roads and rivers count for more than their box says: they are what the eye follows.
            extents[i] = Math.Max(maxX - minX, maxY - minY) * (features[i].Layer == RadarLayer.Road ? 2 : 1);
        }
        var order = Enumerable.Range(0, extents.Length).OrderByDescending(i => extents[i]).ThenBy(i => i).Take(budget).OrderBy(i => i).ToArray();
        var result = new RadarFeature[order.Length];
        for (var i = 0; i < order.Length; i++) result[i] = features[order[i]];
        return result;
    }

    private static RadarLayer AreaLayer(FillClass fill) => fill switch
    {
        FillClass.Water => RadarLayer.Water,
        FillClass.Built => RadarLayer.Built,
        _ => RadarLayer.Green,
    };

    private static RadarFeature Line(LineFeature line, MapFrame frame, MapPalette? palette)
    {
        var points = new double[line.Points.Length - line.Points.Length % 2];
        for (var i = 0; i < points.Length; i += 2)
        {
            var (x, y) = frame.ToDisplay(line.Points[i], line.Points[i + 1]);
            points[i] = x; points[i + 1] = y;
        }
        switch (line.Class)
        {
            case LineClass.River: return new RadarFeature(RadarLayer.Water, points, false, palette?.River, line.Width > 0 ? line.Width : 8);
            case LineClass.Rail: return new RadarFeature(RadarLayer.Road, points, false, palette?.Rail ?? "#7d8590", 3);
            case LineClass.Border: return new RadarFeature(RadarLayer.Relief, points, false, palette?.Border ?? "#c7cdd640", 2);
            case LineClass.Contour: case LineClass.IndexContour: return new RadarFeature(RadarLayer.Relief, points);
            case LineClass.StreetWithLimits: return new RadarFeature(RadarLayer.Road, points, false, null, 8);
            default: return new RadarFeature(RadarLayer.Road, points, false, null, StreetWidth(line.StreetRank));
        }
    }

    // Wider for the ranks the package marks as main roads (rank 0..5, the lower the more important).
    private static double StreetWidth(int? rank) => rank switch { <= 1 => 10, 2 => 8, 3 => 7, _ => 5 };
}
