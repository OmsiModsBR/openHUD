using System.Collections.Immutable;

namespace OpenHud.Packages.Realmap;

// Where the bridge says a map's realmap package is. The adapter over the bridge's content API
// (IModuleContent.LocateRealmapPackage) lives in the module project; Core only sees this port.
public interface IRealmapSource
{
    RealmapSourceResult Locate(string mapIdentity);
}

// Found false without a code: the map has no package (not an error). A code: it exists but cannot be read safely.
public sealed record RealmapSourceResult(bool Found, string? Directory, string Fingerprint, string? ErrorCode, string? Detail);

// What the package offers at file level. Streets, contours, speed limits and labels live inside
// surface.oms chunks, so queries (not this flag set) show whether a given area has them.
[Flags]
public enum RealmapLayers
{
    None = 0,
    Surface = 1,
    Markers = 2,
    Regions = 4,
    TransitLines = 8,
}

public enum FillClass : byte { Water = 0, Green = 1, Built = 2, Grass = 3, Forest = 4, Sport = 5, Farm = 6 }
public enum LineClass : byte { River = 0, Rail = 1, Border = 2, Street = 3, StreetWithLimits = 4, Contour = 5, IndexContour = 6 }
public enum LabelKind : byte { District = 0, Terminal = 1, Town = 2, Landmark = 3, Water = 4 }
public enum Alignment { Unspecified, Exact, Fitted }
public enum TransitKind { Bus, Tram, Metro, Rail }

// Level of detail of a query: what is returned and how much the lines are simplified.
public enum DetailLevel
{
    // Far zoom: fills, water, rails, borders, important labels, markers.
    Overview,
    // Normal radar zoom: adds index contour lines and more labels.
    Normal,
    // Close: everything, streets (names and limits) and every contour line.
    Close,
}

// Axis-aligned window in map space (metres of the author's grid, X east, Y north).
public readonly record struct MapRect(double MinX, double MinY, double MaxX, double MaxY)
{
    public static MapRect Around(double x, double y, double halfSize) => new(x - halfSize, y - halfSize, x + halfSize, y + halfSize);
    public bool IsValid => double.IsFinite(MinX) && double.IsFinite(MinY) && double.IsFinite(MaxX) && double.IsFinite(MaxY) && MaxX >= MinX && MaxY >= MinY;
    public bool Intersects(double minX, double minY, double maxX, double maxY) => maxX >= MinX && minX <= MaxX && maxY >= MinY && minY <= MaxY;
}

// Triangles (6 floats each: x0 y0 x1 y1 x2 y2) of one class, in map space.
public sealed record AreaFeature(FillClass Class, float[] Triangles)
{
    public int TriangleCount => Triangles.Length / 6;
}

// A polyline (x y pairs). Width carries the class meaning of the spec: river width in metres, speed limit
// of a street with limits (km/h, 0 = none), height above sea level of a contour line.
public sealed record LineFeature(LineClass Class, byte Flags, float Width, string? Name, float[] Points)
{
    public int PointCount => Points.Length / 2;
    public bool IsStreet => Class is LineClass.Street or LineClass.StreetWithLimits;
    public bool IsContour => Class is LineClass.Contour or LineClass.IndexContour;
    // Street rank 0-5 (class 3 only).
    public int? StreetRank => Class == LineClass.Street ? Flags : null;
    public int? SpeedLimitKmh => Class == LineClass.StreetWithLimits && Width > 0 ? (int)Math.Round(Width) : null;
    // 0 in the file means the same as the general limit (or none).
    public int? HeavyLimitKmh => Class == LineClass.StreetWithLimits && Flags > 0 ? Flags : null;
    // Contour height in metres above sea level, terrain offset applied.
    public float? HeightMetres => IsContour ? Width : null;
}

public sealed record LabelFeature(LabelKind Kind, byte Rank, string? Text, float X, float Y);

// An icon placed on the map. IconId is the package-relative path of a validated PNG (see RealmapPackage.Icon).
public sealed record MarkerFeature(string IconId, float WidthPixels, float X, float Y, string? Name);

public sealed record RealmapIcon(string Id, int Width, int Height, long Bytes, string FilePath);

// A municipality: where the player is, and where its name fits as a watermark.
public sealed record RegionFeature(string Name, float LabelX, float LabelY, float BoxWidth, float BoxHeight, float[] Polygon)
{
    public bool HasReadableLabel => BoxWidth > 0 && BoxHeight > 0;
}

public sealed record TransitRoute(float Kilometres, int Stops, int Minutes, bool Circular, string Days, string Model, string Itinerary, float[] Way, float[] StopPoints);

// One form of a line. From/To are yyyymmdd dates (0 = open); the panel shows the forms valid on the session's date.
public sealed record TransitLine(string Number, string Group, string Name, TransitKind Kind, int From, int To, ImmutableArray<TransitRoute> Routes)
{
    public bool RunsOn(int yyyymmdd) => (From == 0 || yyyymmdd >= From) && (To == 0 || yyyymmdd <= To);
}

public sealed record PlayerMarkerStyle(bool Triangle, HudColor? Free, HudColor? OnLine, HudColor? OnBrtLine, ImmutableArray<string> BrtLines);

// manifest.cfg, validated.
public sealed record RealmapManifest(
    string? Name,
    double GridMetres,
    string? Credit,
    double NorthDegrees,
    Alignment Alignment,
    double TerrainOffsetMetres,
    ImmutableDictionary<string, HudColor> DarkColors,
    ImmutableDictionary<string, HudColor> LightColors,
    PlayerMarkerStyle Player,
    ImmutableArray<string> TramVehicles,
    ImmutableArray<string> MetroVehicles,
    ImmutableArray<string> RailVehicles);

// What a window query returns: plain geometry in map space, nothing the caller must free or decode.
public sealed record RealmapView(
    IReadOnlyList<AreaFeature> Areas,
    IReadOnlyList<LineFeature> Lines,
    IReadOnlyList<LineFeature> Streets,
    IReadOnlyList<LabelFeature> Labels,
    IReadOnlyList<MarkerFeature> Markers,
    IReadOnlyList<RegionFeature> Regions,
    // True when the window was larger than the reader serves in one query and only the chunks nearest its centre were used.
    bool Truncated,
    // Chunks of the window that could not be read (corrupt or missing); the rest is complete.
    int UnreadableChunks)
{
    public static RealmapView Empty { get; } = new(Array.Empty<AreaFeature>(), Array.Empty<LineFeature>(), Array.Empty<LineFeature>(), Array.Empty<LabelFeature>(), Array.Empty<MarkerFeature>(), Array.Empty<RegionFeature>(), false, 0);
}

// The street under a point, as the "you are on" row and the speed-limit sign need it.
public sealed record StreetHit(string? Name, int? Rank, int? SpeedLimitKmh, int? HeavyLimitKmh, double DistanceMetres);
