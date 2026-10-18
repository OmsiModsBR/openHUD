using System.Globalization;
using OpenHud.Model;
using OpenHud.Pause;
using OpenHud.Radar;

namespace OpenHud.Maps;

// FrameSnapshot + the map's geometry -> the model the radar and the full map draw. Positions go through the
// MapFrame, so features, vehicles and the player share one plane.
internal static class RadarModels
{
    // The pose to draw the radar around, or why there is none.
    public static VehicleState? PlayerFor(FrameSnapshot frame, out string reason)
    {
        var player = frame.PlayerVehicle;
        if (player is null) { reason = frame.Player.IsAvailable ? "no player vehicle" : "player unreadable"; return null; }
        if (!player.WorldResolved) { reason = "the player's tile is not in the map's tile table"; return null; }
        reason = "";
        return player;
    }

    public static RadarModel Build(FrameSnapshot frame, VehicleState player, MapFrame mapFrame, FeatureSet? features, IReadOnlyList<RadarLine>? lines)
    {
        var (px, py) = mapFrame.ToDisplay(player.WorldX, player.WorldY);
        var radarPlayer = new RadarPlayer(px, py, mapFrame.Heading(player.HeadingDegrees ?? 0));

        var list = new List<RadarVehicle>();
        if (frame.Vehicles.Value is { } nearby)
        {
            foreach (var vehicle in nearby.Vehicles)
            {
                if (!vehicle.WorldResolved) continue;
                var (vx, vy) = mapFrame.ToDisplay(vehicle.WorldX, vehicle.WorldY);
                list.Add(new RadarVehicle(vx, vy, mapFrame.Heading(vehicle.HeadingDegrees ?? 0), RadarVehicleKind.Other));
            }
        }

        string? clock = null;
        if (frame.Time.Value is { } time) clock = time.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + time.Minute.ToString("00", CultureInfo.InvariantCulture);

        return new RadarModel(radarPlayer, features?.Features, features?.Regions, features?.Markers, list, null,
            clock is null ? null : new RadarInfo(null, clock, null), lines);
    }

    // Cheap fingerprint of what the radar would show, to skip the build when nothing visible changed:
    // positions to a quarter metre, headings to a degree, the clock to the minute, plus the map data and the view.
    public static long Signature(FrameSnapshot frame, VehicleState player, long featureId, int zoomStep, int stamp)
    {
        var h = new HashCode();
        h.Add(Q(player.WorldX, 4)); h.Add(Q(player.WorldY, 4)); h.Add(Q(player.HeadingDegrees ?? 0, 1));
        if (frame.Vehicles.Value is { } nearby)
        {
            h.Add(nearby.Vehicles.Count);
            foreach (var v in nearby.Vehicles) { h.Add(Q(v.WorldX, 2)); h.Add(Q(v.WorldY, 2)); h.Add(Q(v.HeadingDegrees ?? 0, 0.2)); }
        }
        if (frame.Time.Value is { } t) { h.Add(t.Hour); h.Add(t.Minute); }
        h.Add(featureId); h.Add(zoomStep); h.Add(stamp);
        return h.ToHashCode();
    }

    private static long Q(double value, double perUnit) => (long)Math.Round(value * perUnit);

    // The transit lines running on the session's date (every line when the date is incomplete).
    public static (IReadOnlyList<RadarLine> Radar, IReadOnlyList<LineEntry> Menu) Lines(LoadedMap? map, int? dateNumber, string accent)
    {
        if (map is null || map.Realmap.TransitLines.Count == 0) return (Array.Empty<RadarLine>(), Array.Empty<LineEntry>());
        var source = dateNumber is { } date ? map.Realmap.LinesOn(date) : map.Realmap.TransitLines;
        var radar = new List<RadarLine>();
        var menu = new List<LineEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in source)
        {
            if (radar.Count >= 200) break;
            if (!seen.Add(line.Number + "|" + line.Name)) continue;
            var color = map.PauseMenu.Identity.GroupColors.TryGetValue(line.Group, out var group) ? group.ToHex() : accent;
            radar.Add(new RadarLine(line.Number + " " + line.Name, color));
            menu.Add(new LineEntry(line.Number, color, line.Name));
        }
        return (radar, menu);
    }
}

// The full map's viewport follows the pause menu's pan and zoom state. The menu keeps absolute totals
// (design units for the pan, whole steps for the zoom); the view applies the change since last time, so a
// zoom after a pan stays where the user left it, and a return to zero is a recentre.
internal sealed class FullMapView
{
    public const double DefaultMetersPerPixel = 3;
    public const double StepFactor = 1.25;

    private int lastSteps;
    private double lastPanX, lastPanY;

    public FullMapViewport Viewport { get; private set; } = new(0, 0, DefaultMetersPerPixel);

    public void Reset(double centerX, double centerY)
    {
        Viewport = new FullMapViewport(centerX, centerY, DefaultMetersPerPixel);
        lastSteps = 0;
        lastPanX = lastPanY = 0;
    }

    // Follows the menu; `canvasScale` converts design units to canvas pixels. True when the viewport moved.
    public bool Follow(PauseMenu menu, double canvasScale, double playerX, double playerY)
    {
        var steps = menu.MapZoomSteps;
        double panX = menu.MapPanX, panY = menu.MapPanY;
        if (steps == lastSteps && panX == lastPanX && panY == lastPanY) return false;
        if (steps == 0 && panX == 0 && panY == 0)
        {
            Reset(playerX, playerY);
            return true;
        }
        var view = Viewport.Pan((panX - lastPanX) * canvasScale, (panY - lastPanY) * canvasScale);
        if (steps != lastSteps) view = view.Zoom(Math.Pow(StepFactor, steps - lastSteps));
        Viewport = view;
        lastSteps = steps;
        lastPanX = panX; lastPanY = panY;
        return true;
    }
}
