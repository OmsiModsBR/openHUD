namespace OpenHud.Radar;

// Pure input of the radar and map builders. World coordinates are meters: X east, Y north.
// Headings are degrees, 0 = north, clockwise. The integrator adapts the game/realmap model to
// these types; nothing here knows OMSI. Null lists are treated as empty and non-finite numbers
// make the single item that carries them disappear, never the whole scene.

public enum RadarLayer { Water, Green, Built, Relief, Road }

/// <summary>
/// A polyline or polygon of the map (x,y pairs, meters). <see cref="Closed"/> areas of the
/// water/green/built layers are filled; any other feature is stroked. <see cref="Color"/> overrides the theme.
/// <see cref="WidthMeters"/> is the stroke width of roads (0 = default).
/// </summary>
public sealed record RadarFeature(RadarLayer Layer, double[] Points, bool Closed = false,
    string? Color = null, double WidthMeters = 0);

/// <summary>A faint region name drawn as a watermark where the region lies.</summary>
public sealed record RadarRegion(string Name, double X, double Y);

/// <summary>A point of interest. <see cref="IconId"/> names a registered image; null draws a plain dot.</summary>
public sealed record RadarMarker(double X, double Y, string? IconId = null, string? Label = null, string? Color = null);

public enum RadarVehicleKind { Bus, Car, Truck, Tram, Other }

public sealed record RadarVehicle(double X, double Y, double HeadingDegrees, RadarVehicleKind Kind = RadarVehicleKind.Other);

public sealed record RadarPlayer(double X, double Y, double HeadingDegrees);

/// <summary>The clock / next-stop strip under the radar. Any member may be null.</summary>
public sealed record RadarInfo(string? NextStop = null, string? Clock = null, string? Line = null);

/// <summary>One row of the full map's line list.</summary>
public sealed record RadarLine(string Name, string Color, bool Active = false);

public sealed record RadarModel(
    RadarPlayer Player,
    IReadOnlyList<RadarFeature>? Features = null,
    IReadOnlyList<RadarRegion>? Regions = null,
    IReadOnlyList<RadarMarker>? Markers = null,
    IReadOnlyList<RadarVehicle>? Vehicles = null,
    double[]? Route = null,
    RadarInfo? Info = null,
    IReadOnlyList<RadarLine>? Lines = null)
{
    public static RadarModel Empty { get; } = new(new RadarPlayer(0, 0, 0));
}

public enum RadarMode { NorthUp, HeadingUp }

/// <summary>User-visible strings of the radar and map. The integrator fills them from the i18n table (keys UI_RADAR_*); the defaults are English.</summary>
public sealed record RadarTexts
{
    public string North { get; init; } = "N";
    public string NextStop { get; init; } = "Next";
    public string Legend { get; init; } = "Legend";
    public string Lines { get; init; } = "Lines";
    public string Water { get; init; } = "Water";
    public string Green { get; init; } = "Green areas";
    public string Built { get; init; } = "Built-up";
    public string Roads { get; init; } = "Roads";
    public string Relief { get; init; } = "Relief";
    public string Route { get; init; } = "Route";
    public string You { get; init; } = "You";
    public string More { get; init; } = "+{0} more";
}

/// <summary>
/// Colors of the radar and map. The defaults are generic; a pause-menu identity package supplies
/// brand accents later (<c>color.accent</c> and friends map onto <see cref="Accent"/>, <see cref="Panel"/>, ...).
/// </summary>
public sealed record RadarTheme
{
    public string Panel { get; init; } = "#14171c";
    public string PanelOverlay { get; init; } = "#14171ce6";
    public string Backdrop { get; init; } = "#0b0e12";
    public string Border { get; init; } = "#2a2f38";
    public string Text { get; init; } = "#f2f4f7";
    public string TextDim { get; init; } = "#9aa3b0";
    public string Accent { get; init; } = "#3d8bfd";
    public string North { get; init; } = "#e5484d";
    public string Water { get; init; } = "#2b5b8a";
    public string Green { get; init; } = "#27503a";
    public string Built { get; init; } = "#2d323b";
    public string Relief { get; init; } = "#8a7f6a59";
    public string Road { get; init; } = "#aab2bd";
    public string Region { get; init; } = "#ffffff26";
    public string Route { get; init; } = "#3d8bfd";
    public string RouteCasing { get; init; } = "#0b0e12";
    public string PlayerFill { get; init; } = "#ffffff";
    public string PlayerOutline { get; init; } = "#0b0e12";
    public string VehicleBus { get; init; } = "#ffb020";
    public string VehicleCar { get; init; } = "#c7ccd4";
    public string VehicleTruck { get; init; } = "#d9822b";
    public string VehicleTram { get; init; } = "#b46bff";
    public string VehicleOther { get; init; } = "#8791a0";
    public string Marker { get; init; } = "#ffd166";

    internal string Vehicle(RadarVehicleKind kind) => kind switch
    {
        RadarVehicleKind.Bus => VehicleBus,
        RadarVehicleKind.Car => VehicleCar,
        RadarVehicleKind.Truck => VehicleTruck,
        RadarVehicleKind.Tram => VehicleTram,
        _ => VehicleOther,
    };

    /// <summary>Throws ArgumentException naming the first token that is not a scene color.</summary>
    public void Validate()
    {
        foreach (var p in typeof(RadarTheme).GetProperties())
            if (p.PropertyType == typeof(string) && !OpenHud.Scene.SceneColor.IsValid((string?)p.GetValue(this)))
                throw new ArgumentException("theme token " + p.Name + " is not a color");
    }
}

public sealed record RadarOptions
{
    /// <summary>Visible radius in meters per zoom step.</summary>
    public static readonly double[] ZoomRadiiMeters = { 100, 200, 400, 800, 1600, 3200 };

    public int CanvasWidth { get; init; } = 1920;
    public int CanvasHeight { get; init; } = 1080;
    public double CenterX { get; init; } = 1700;
    public double CenterY { get; init; } = 880;
    public double Radius { get; init; } = 160;
    public RadarMode Mode { get; init; } = RadarMode.HeadingUp;
    public int ZoomStep { get; init; } = 2;
    public int MaxVehicles { get; init; } = 48;
    public int MaxMarkers { get; init; } = 48;
    /// <summary>Command budget of the whole scene; must stay below the protocol cap.</summary>
    public int MaxCommands { get; init; } = 6000;
    /// <summary>Budget of polyline/polygon points of the map layers (keeps the JSON well under 1 MiB).</summary>
    public int MaxPoints { get; init; } = 24000;

    public static int ClampZoom(int step) => Math.Clamp(step, 0, ZoomRadiiMeters.Length - 1);
    public RadarOptions ZoomIn() => this with { ZoomStep = ClampZoom(ZoomStep - 1) };
    public RadarOptions ZoomOut() => this with { ZoomStep = ClampZoom(ZoomStep + 1) };
}

/// <summary>Where the full map looks. MetersPerPixel is clamped to a sane range by the builder.</summary>
public readonly record struct FullMapViewport(double CenterX, double CenterY, double MetersPerPixel)
{
    public const double MinMetersPerPixel = 0.1;
    public const double MaxMetersPerPixel = 200;

    public FullMapViewport Pan(double dxPixels, double dyPixels) =>
        new(CenterX - dxPixels * MetersPerPixel, CenterY + dyPixels * MetersPerPixel, MetersPerPixel);

    /// <summary>Factor above 1 zooms in.</summary>
    public FullMapViewport Zoom(double factor) =>
        new(CenterX, CenterY, Math.Clamp(MetersPerPixel / (factor > 0 && double.IsFinite(factor) ? factor : 1), MinMetersPerPixel, MaxMetersPerPixel));

    /// <summary>Frames the finite features and the player in a map area of the given size.</summary>
    public static FullMapViewport Fit(RadarModel model, double areaWidth, double areaHeight)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Take(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7) return;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        Take(model.Player.X, model.Player.Y);
        if (model.Features is not null)
            foreach (var f in model.Features)
                if (f.Points is not null)
                    for (int i = 0; i + 1 < f.Points.Length; i += 2) Take(f.Points[i], f.Points[i + 1]);
        if (minX > maxX) return new FullMapViewport(0, 0, 2);
        double w = Math.Max(maxX - minX, 50), h = Math.Max(maxY - minY, 50);
        double mpp = Math.Max(w / Math.Max(areaWidth, 1), h / Math.Max(areaHeight, 1)) * 1.08;
        return new FullMapViewport((minX + maxX) / 2, (minY + maxY) / 2, Math.Clamp(mpp, MinMetersPerPixel, MaxMetersPerPixel));
    }
}

public sealed record FullMapOptions
{
    public int CanvasWidth { get; init; } = 1920;
    public int CanvasHeight { get; init; } = 1080;
    public double Margin { get; init; } = 24;
    public double SidePanelWidth { get; init; } = 360;
    /// <summary>True: the scene asks for the captured pause background and dims it; false: an opaque backdrop.</summary>
    public bool OverBackground { get; init; } = true;
    public int MaxVehicles { get; init; } = 200;
    public int MaxMarkers { get; init; } = 200;
    public int MaxCommands { get; init; } = 7000;
    public int MaxPoints { get; init; } = 30000;
}
