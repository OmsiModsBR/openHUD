using System.Globalization;
using OpenHud.Scene;
using static OpenHud.Radar.RadarSceneBuilder;

namespace OpenHud.Radar;

/// <summary>
/// The pause menu's full-screen map: the same model as the radar, north-up, framed by a
/// <see cref="FullMapViewport"/> (pan/zoom state lives with the caller), plus a scale bar, a
/// legend and the line list in a side panel.
/// </summary>
public sealed class FullMapSceneBuilder
{
    private readonly RadarTheme theme;
    private readonly RadarTexts texts;
    private readonly ITextMeasurer measurer;
    private readonly RadarArt art;

    public FullMapSceneBuilder(RadarTheme? theme = null, RadarTexts? texts = null, ITextMeasurer? measurer = null, RadarArt? art = null)
    {
        this.theme = theme ?? new RadarTheme();
        this.theme.Validate();
        this.texts = texts ?? new RadarTexts();
        this.measurer = measurer ?? new ApproximateTextMeasurer();
        this.art = art ?? RadarArt.None;
    }

    private const int FixedReserve = 140 + 100 + 40 + 48;

    /// <summary>The map rectangle (x, y, w, h) in canvas pixels for the options, so callers can map pointer input to it.</summary>
    public static (double x, double y, double w, double h) MapArea(FullMapOptions options)
    {
        int cw = Math.Clamp(options.CanvasWidth, 320, SceneLimits.MaxCanvas);
        int ch = Math.Clamp(options.CanvasHeight, 240, SceneLimits.MaxCanvas);
        double m = Math.Clamp(Finite(options.Margin, 24), 0, Math.Min(cw, ch) / 8.0);
        double side = Math.Clamp(Finite(options.SidePanelWidth, 360), 0, cw / 2.0);
        return (m, m, cw - side - 3 * m, ch - 2 * m);
    }

    public SceneDocument Build(RadarModel? model, FullMapViewport viewport, FullMapOptions? options = null)
    {
        model ??= RadarModel.Empty;
        options ??= new FullMapOptions();

        int cw = Math.Clamp(options.CanvasWidth, 320, SceneLimits.MaxCanvas);
        int ch = Math.Clamp(options.CanvasHeight, 240, SceneLimits.MaxCanvas);
        var (mx, my, mw, mh) = MapArea(options);
        double m = mx;
        double side = Math.Clamp(Finite(options.SidePanelWidth, 360), 0, cw / 2.0);
        double mpp = Math.Clamp(Finite(viewport.MetersPerPixel, 2), FullMapViewport.MinMetersPerPixel, FullMapViewport.MaxMetersPerPixel);
        double fx = ValidCoordinate(viewport.CenterX) ? viewport.CenterX : 0;
        double fy = ValidCoordinate(viewport.CenterY) ? viewport.CenterY : 0;

        int maxVehicles = Math.Clamp(options.MaxVehicles, 0, 1000);
        int maxMarkers = Math.Clamp(options.MaxMarkers, 0, 1000);
        int maxCommands = Math.Clamp(options.MaxCommands, 256, SceneLimits.MaxCommands - 128);
        int artExtra = art.StopIcon.IsEmpty ? 0 : maxMarkers / 2 * Math.Max(0, art.StopIcon.CommandCount - 1);
        var budget = new MapBudget(Math.Max(0, maxCommands - FixedReserve - maxVehicles - maxMarkers - artExtra), Math.Clamp(options.MaxPoints, 0, 80000));

        var view = new MapView(fx, fy, mx + mw / 2, my + mh / 2, 1 / mpp, 0, mx + 1, my + 1, mx + mw - 1, my + mh - 1);
        bool Visible(double x, double y) => view.Inside(x, y);
        var map = new MapRenderer(theme);
        var b = new SceneBuilder(cw, ch, options.OverBackground);

        if (options.OverBackground) b.Rect(0, 0, cw, ch, "#000000a6");
        else b.Rect(0, 0, cw, ch, theme.Panel);

        // map frame and content
        b.Rect(mx, my, mw, mh, theme.Backdrop, null, null, 12);
        b.ClipRect(mx + 1, my + 1, mw - 2, mh - 2);
        map.DrawFeatures(b, model.Features, view, budget);
        DrawRegions(b, model.Regions, view, Visible, 40, 16);
        map.DrawRoute(b, model.Route, view, 5, 24);
        map.DrawMarkers(b, model.Markers, view, maxMarkers, Visible, art);
        map.DrawVehicles(b, model.Vehicles, view, maxVehicles, Visible);
        var player = model.Player ?? new RadarPlayer(0, 0, 0);
        if (view.Project(player.X, player.Y, out double sx, out double sy) && view.Inside(sx, sy))
        {
            var local = new MapView(player.X, player.Y, sx, sy, view.Scale, 0, view.Left, view.Top, view.Right, view.Bottom);
            map.DrawPlayer(b, local, new RadarPlayer(player.X, player.Y, Finite(player.HeadingDegrees, 0), player.Color, player.Triangle), 13);
        }
        b.Unclip();
        b.Rect(mx, my, mw, mh, null, theme.Border, 2, 12);

        DrawNorthMark(b, mx + 28, my + 30);
        DrawScaleBar(b, mx + 16, my + mh - 40, mpp, options.Units);

        if (side >= 120) DrawSidePanel(b, mx + mw + m, my, side, mh, model);
        return b.Build();
    }

    // ---- parts --------------------------------------------------------------------------

    private void DrawNorthMark(SceneBuilder b, double x, double y)
    {
        b.Circle(x, y, 18, SceneColor.WithAlpha(theme.Backdrop, 0.85), theme.Border, 1);
        b.Poly(new[] { x, y - 14, x + 6, y - 4, x - 6, y - 4 }, theme.North, null, null, true);
        b.Text(x, y + 6, Clip(texts.North, 3) ?? "N", 12, "bold", theme.Text, "c", "mid");
    }

    private void DrawScaleBar(SceneBuilder b, double x, double y, double mpp, OpenHud.Settings.UnitSystem units)
    {
        // The largest 1/2/5 x 10^k length (of the unit system) that fits in 160 px.
        var steps = UnitFormat.ScaleSteps(units, texts);
        var chosen = steps[0];
        foreach (var step in steps)
            if (step.Metres / mpp <= 160) chosen = step;
        double length = chosen.Metres;
        double px = Math.Round(length / mpp, 1);
        string label = chosen.Label;
        b.Rect(x - 8, y - 8, px + 16 + 54, 30, SceneColor.WithAlpha(theme.Backdrop, 0.85), null, null, 8);
        b.Line(x, y + 14, x + px, y + 14, theme.Text, 2);
        b.Line(x, y + 8, x, y + 14, theme.Text, 2);
        b.Line(x + px, y + 8, x + px, y + 14, theme.Text, 2);
        b.Text(x + px + 8, y + 10, label, 12, "medium", theme.Text, "l", "mid");
    }

    private void DrawRegions(SceneBuilder b, IReadOnlyList<RadarRegion>? regions, MapView view, Func<double, double, bool> visible, int cap, double size)
    {
        if (regions is null) return;
        int n = 0;
        for (int i = 0; i < regions.Count && n < cap; i++)
        {
            var g = regions[i];
            if (g is null || string.IsNullOrEmpty(g.Name) || !view.Project(g.X, g.Y, out double sx, out double sy) || !visible(sx, sy)) continue;
            b.Text(Round(sx), Round(sy), Clip(g.Name, 40)!, size, "bold", theme.Region, "c", "mid", 220);
            n++;
        }
    }

    private void DrawSidePanel(SceneBuilder b, double x, double y, double w, double h, RadarModel model)
    {
        b.Panel(x, y, w, h, 12, theme.PanelOverlay, theme.Border, 1);
        double pad = 20, cursor = y + 30;
        double textW = w - pad * 2;

        b.Text(x + pad, cursor, Clip(texts.Legend, 24)!, 18, "bold", theme.Text, "l", "mid", textW);
        cursor += 32;
        void Swatch(string color, string label, bool line)
        {
            if (line) b.Line(x + pad, cursor, x + pad + 22, cursor, color, 4, "round");
            else b.Rect(x + pad, cursor - 6, 22, 12, color, null, null, 3);
            b.Text(x + pad + 34, cursor, Clip(label, 32)!, 14, "regular", theme.Text, "l", "mid", textW - 34);
            cursor += 26;
        }
        Swatch(theme.Water, texts.Water, false);
        Swatch(theme.Green, texts.Green, false);
        Swatch(theme.Built, texts.Built, false);
        Swatch(theme.Road, texts.Roads, true);
        Swatch(theme.Relief, texts.Relief, true);
        Swatch(theme.Route, texts.Route, true);

        cursor += 8;
        b.Line(x + pad, cursor, x + w - pad, cursor, theme.Border, 1);
        cursor += 28;
        var lines = model.Lines;
        if (lines is null || lines.Count == 0) return;

        b.Text(x + pad, cursor, Clip(texts.Lines, 24)!, 18, "bold", theme.Text, "l", "mid", textW);
        cursor += 30;
        const double rowH = 28;
        int rows = (int)Math.Max(0, Math.Floor((y + h - 16 - cursor) / rowH));
        int shown = lines.Count <= rows ? lines.Count : Math.Max(0, rows - 1); // leave one row for "+N more"
        for (int i = 0; i < shown; i++)
        {
            var l = lines[i];
            if (l is null) continue;
            if (l.Active) b.Rect(x + 8, cursor - rowH / 2 + 1, w - 16, rowH - 2, SceneColor.WithAlpha(theme.Accent, 0.22), null, null, 8);
            b.Circle(x + pad + 7, cursor, 6, SceneColor.Or(l.Color, theme.TextDim));
            b.Text(x + pad + 24, cursor, Clip(l.Name, 40) ?? "", 15, l.Active ? "bold" : "regular", theme.Text, "l", "mid", textW - 24);
            cursor += rowH;
        }
        if (shown < lines.Count && rows > 0)
            b.Text(x + pad, cursor, (Clip(texts.More, 24) ?? "+{0}").Replace("{0}", (lines.Count - shown).ToString(CultureInfo.InvariantCulture)), 13, "medium", theme.TextDim, "l", "mid", textW);
    }
}
