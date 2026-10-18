using System.Globalization;
using OpenHud.Scene;

namespace OpenHud.Radar;

/// <summary>
/// Builds the round radar (minimap) scene. Everything the radar shows comes from the
/// <see cref="RadarModel"/>; hostile or degenerate input (NaN, empty layers, huge coordinates)
/// drops the offending item and still yields a valid scene. Same inputs give the same scene.
/// </summary>
public sealed class RadarSceneBuilder
{
    private readonly RadarTheme theme;
    private readonly RadarTexts texts;
    private readonly ITextMeasurer measurer;

    public RadarSceneBuilder(RadarTheme? theme = null, RadarTexts? texts = null, ITextMeasurer? measurer = null)
    {
        this.theme = theme ?? new RadarTheme();
        this.theme.Validate();
        this.texts = texts ?? new RadarTexts();
        this.measurer = measurer ?? new ApproximateTextMeasurer();
    }

    // Commands the fixed parts may use besides vehicles, markers and the map layers.
    private const int FixedReserve = 80 + 24 + 12;

    public SceneDocument Build(RadarModel? model, RadarOptions? options = null)
    {
        model ??= RadarModel.Empty;
        options ??= new RadarOptions();

        int w = Math.Clamp(options.CanvasWidth, 64, SceneLimits.MaxCanvas);
        int h = Math.Clamp(options.CanvasHeight, 64, SceneLimits.MaxCanvas);
        double r = Finite(options.Radius, 160);
        r = Math.Clamp(r, 24, Math.Min(w, h) / 2.0);
        double cx = Math.Clamp(Finite(options.CenterX, w - r - 20), r, w - r);
        double cy = Math.Clamp(Finite(options.CenterY, h - r - 20), r, h - r);
        int zoom = RadarOptions.ClampZoom(options.ZoomStep);
        double meters = RadarOptions.ZoomRadiiMeters[zoom];
        double scale = r / meters;

        var player = model.Player ?? new RadarPlayer(0, 0, 0);
        double px = ValidCoordinate(player.X) ? player.X : 0;
        double py = ValidCoordinate(player.Y) ? player.Y : 0;
        double playerHeading = Finite(player.HeadingDegrees, 0);
        double heading = options.Mode == RadarMode.HeadingUp ? playerHeading : 0;

        int maxVehicles = Math.Clamp(options.MaxVehicles, 0, 400);
        int maxMarkers = Math.Clamp(options.MaxMarkers, 0, 400);
        int maxCommands = Math.Clamp(options.MaxCommands, 128, SceneLimits.MaxCommands - 128);
        int featureBudget = maxCommands - FixedReserve - maxVehicles - maxMarkers;
        var budget = new MapBudget(featureBudget, Math.Clamp(options.MaxPoints, 0, 60000));

        var view = new MapView(px, py, cx, cy, scale, heading, cx - r, cy - r, cx + r, cy + r);
        bool InCircle(double x, double y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
        var map = new MapRenderer(theme);
        var b = new SceneBuilder(w, h, false);

        b.Circle(cx, cy, r, SceneColor.WithAlpha(theme.Backdrop, 0.92));
        b.ClipCircle(cx, cy, r - 3);

        map.DrawFeatures(b, model.Features, view, budget);
        DrawRegions(b, model.Regions, view, InCircle, 12, 13);
        map.DrawRoute(b, model.Route, view, 4, 24);
        map.DrawMarkers(b, model.Markers, view, maxMarkers, InCircle);
        map.DrawVehicles(b, model.Vehicles, view, maxVehicles, InCircle);
        map.DrawPlayer(b, view, new RadarPlayer(px, py, playerHeading), 11);
        b.Unclip();

        DrawRing(b, cx, cy, r, heading);
        b.Text(cx, cy + r - 16, ZoomLabel(meters), 11, "medium", theme.TextDim, "c", "mid", null, SceneColor.WithAlpha(theme.Backdrop, 0.8));
        DrawInfo(b, model.Info, cx, cy, r, w, h);
        return b.Build();
    }

    // ---- parts --------------------------------------------------------------------------

    private void DrawRing(SceneBuilder b, double cx, double cy, double r, double heading)
    {
        b.Circle(cx, cy, r, null, theme.Border, 3);
        for (int k = 0; k < 4; k++)
        {
            double a = k * 90 - heading;
            var (x0, y0) = Polar(cx, cy, r - 9, a);
            var (x1, y1) = Polar(cx, cy, r, a);
            b.Line(Round(x0), Round(y0), Round(x1), Round(y1), k == 0 ? theme.North : theme.TextDim, 2);
        }
        // North mark: a small triangle just inside the ring and the letter next to it.
        double north = -heading;
        var tip = Polar(cx, cy, r - 15, north);
        var left = Polar(cx, cy, r - 1, north - 7);
        var right = Polar(cx, cy, r - 1, north + 7);
        b.Poly(new[] { Round(tip.x), Round(tip.y), Round(right.x), Round(right.y), Round(left.x), Round(left.y) }, theme.North, null, null, true);
        var label = Polar(cx, cy, r - 28, north);
        b.Text(Round(label.x), Round(label.y), Clip(texts.North, 3), 14, "bold", theme.Text, "c", "mid", null, SceneColor.WithAlpha(theme.Backdrop, 0.85));
    }

    private void DrawRegions(SceneBuilder b, IReadOnlyList<RadarRegion>? regions, MapView view, Func<double, double, bool> visible, int cap, double size)
    {
        if (regions is null || cap <= 0) return;
        var picked = new List<(double d, int i, double x, double y)>();
        for (int i = 0; i < regions.Count; i++)
        {
            var g = regions[i];
            if (g is null || string.IsNullOrEmpty(g.Name) || !view.Project(g.X, g.Y, out double sx, out double sy) || !visible(sx, sy)) continue;
            double dx = sx - view.ScreenX, dy = sy - view.ScreenY;
            picked.Add((dx * dx + dy * dy, i, sx, sy));
        }
        picked.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.i.CompareTo(c.i));
        int n = 0;
        foreach (var p in picked)
        {
            if (n++ >= cap) break;
            b.Text(Round(p.x), Round(p.y), Clip(regions[p.i].Name, 40), size, "bold", theme.Region, "c", "mid", 140);
        }
    }

    private void DrawInfo(SceneBuilder b, RadarInfo? info, double cx, double cy, double r, int canvasW, int canvasH)
    {
        if (info is null) return;
        string? stop = Clip(info.NextStop, 48), clock = Clip(info.Clock, 12), line = Clip(info.Line, 8);
        if (string.IsNullOrEmpty(stop) && string.IsNullOrEmpty(clock) && string.IsNullOrEmpty(line)) return;

        const double height = 30;
        double x = cx - r, wide = 2 * r;
        double y = cy + r + 10;
        if (y + height > canvasH) y = cy - r - 10 - height; // no room below: put the strip above
        if (y < 0) return;
        b.Panel(x, y, wide, height, 8, theme.PanelOverlay, theme.Border);

        double left = x + 8, right = x + wide - 10;
        if (!string.IsNullOrEmpty(line))
        {
            var chip = new ChipStyle(theme.Accent, theme.Text, null, 13, "bold", 8, 22);
            left += b.Chip(left, y + 4, line!, chip, measurer) + 8;
        }
        if (!string.IsNullOrEmpty(clock))
        {
            b.Text(right, y + height / 2, clock!, 15, "bold", theme.Text, "r", "mid");
            right -= measurer.Measure(clock!, 15, "bold") + 10;
        }
        if (!string.IsNullOrEmpty(stop) && right - left > 24)
        {
            string label = Clip(texts.NextStop, 16) ?? "";
            double labelW = label.Length == 0 ? 0 : measurer.Measure(label, 11, "medium") + 6;
            if (labelW > 0 && right - left - labelW > 24)
            {
                b.Text(left, y + height / 2, label, 11, "medium", theme.TextDim, "l", "mid");
                left += labelW;
            }
            b.Text(left, y + height / 2, stop!, 14, "medium", theme.Text, "l", "mid", Round(right - left));
        }
    }

    // ---- helpers ------------------------------------------------------------------------

    internal static string ZoomLabel(double meters) =>
        meters >= 1000
            ? (meters / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " km"
            : meters.ToString("0", CultureInfo.InvariantCulture) + " m";

    internal static (double x, double y) Polar(double cx, double cy, double radius, double angleDegrees)
    {
        double rad = angleDegrees * Math.PI / 180;
        return (cx + radius * Math.Sin(rad), cy - radius * Math.Cos(rad));
    }

    internal static double Finite(double v, double fallback) => double.IsFinite(v) ? v : fallback;
    internal static bool ValidCoordinate(double v) => Math.Abs(v) <= MapView.MaxCoordinate; // false for NaN
    internal static double Round(double v) => Math.Round(v, 1);

    /// <summary>Caps text taken from the model so a hostile string cannot inflate the scene.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("s")]
    internal static string? Clip(string? s, int max) => s is null ? null : s.Length <= max ? s : s.Substring(0, max);
}
