using System.Diagnostics;
using System.Globalization;
using OpenHud.Scene;

namespace OpenHud.Tests.ModuleTests;

// What a tick costs when the radar has to be rebuilt every time, and when the pause map is only hovered. The numbers are
// printed so a run shows them; the gates fail when a tick would not fit the 50 ms period with room to spare.
internal static class TickCostGates
{
    public static void Register()
    {
        Gates.Run("module.tick-cost-radar-rebuilt-every-tick", RadarEveryTick);
        Gates.Run("module.tick-cost-pause-map-hover", PauseMapHover);
        Gates.Run("module.tick-cost-package-queries-are-incremental", IncrementalQueries);
    }

    internal sealed record Timings(double P50, double P90, double P99, double Max)
    {
        public override string ToString() => $"p50 {P50:0.00} ms, p90 {P90:0.00} ms, p99 {P99:0.00} ms, max {Max:0.00} ms";
    }

    internal static Timings Summarize(List<double> times)
    {
        times.Sort();
        return new Timings(times[times.Count / 2], times[(int)(times.Count * 0.90)], times[Math.Min(times.Count - 1, (int)(times.Count * 0.99))], times[^1]);
    }

    private static double Time(Action action)
    {
        var started = Stopwatch.GetTimestamp();
        action();
        return (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
    }

    // A fast drive through a dense map at the default zoom, one tick per 100 ms: the signature changes every time, so every
    // tick projects, clips and encodes the radar.
    private static void RadarEveryTick()
    {
        using var s = new Session(d => { d.SpeedMetresPerSecond = 8; d.AiVehicles = 40; });
        PackageKit.Install(s, PackageKit.Surface(extraTriangles: 20000, extraLines: 2000, triangleMin: 40, triangleSpread: 260), pausePackage: true);
        s.Start();
        Gates.Check(s.WaitForPackages(20000), "adopted");
        Gates.Check(s.WaitFor(() => { s.Tick(1, 100); return int.Parse(s.Status()["radar_features"]) >= 1500; }, 20000), "the dense window arrives: " + s.Status()["radar_features"]);
        s.Tick(30, 100);
        var published = long.Parse(s.Status()["scenes_published"]);
        var times = new List<double>();
        s.Native.CheckScenes = false; s.Native.Recording = false;
        for (var i = 0; i < 120; i++) times.Add(Time(() => s.Tick(1, 100)));
        var builds = long.Parse(s.Status()["scenes_published"]) - published;
        var t = Summarize(times);
        Console.WriteLine($"  fast drive, radar rebuilt on {builds} of 120 ticks: {t}");
        Gates.Check(builds >= 100, "the drive changes the radar on (nearly) every tick: " + builds);
        Gates.Check(t.P50 < 4 && t.P90 < 8 && t.P99 < 25, "a rebuilding tick stays far under the 50 ms period: " + t);
        Gates.Check(t.Max < 45, "no tick comes near the period: " + t);
        s.Native.CheckScenes = true; s.Native.Recording = true;
        s.Tick(6, 100);
        s.CheckAllScenesValid("rebuild every tick");
    }

    // The pause menu on the map page with the pointer moving over the menu: the menu's draw list changes, the map does not.
    private static void PauseMapHover()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(extraTriangles: 20000, extraLines: 2000, triangleMin: 40, triangleSpread: 260), pausePackage: true);
        s.Start();
        Gates.Check(s.WaitForPackages(20000), "adopted");
        s.Tick(10);
        s.Pause.UserPauses();
        s.Tick(3);
        s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab));
        s.Tick(3);
        Gates.Check(s.WaitFor(() => { s.Tick(1, advanceDrive: false); return int.Parse(s.Status()["fullmap_features"]) > 5000; }, 20000), "the map window arrives");
        s.Tick(10, advanceDrive: false);
        var times = new List<double>();
        var view = s.Status()["view"].Split('x');
        var geometry = new OpenHud.Pause.MenuGeometry(new OpenHud.Pause.MenuViewport(int.Parse(view[0]), int.Parse(view[1]), 1, 1));
        var tabs = new[] { OpenHud.Tests.PauseTests.Ev.Center(geometry, OpenHud.Pause.MenuGeometry.TabRect(0)), OpenHud.Tests.PauseTests.Ev.Center(geometry, OpenHud.Pause.MenuGeometry.TabRect(2)) };
        var published = long.Parse(s.Status()["scenes_published"]);
        s.Native.CheckScenes = false; s.Native.Recording = false;
        for (var i = 0; i < 120; i++)
        {
            // Over the tab buttons: the hover highlight changes the menu, never the map under it.
            s.Input.Queue(Keys.Move(tabs[i % 2].X, tabs[i % 2].Y));
            times.Add(Time(() => s.Tick(1, advanceDrive: false)));
        }
        var scenes = long.Parse(s.Status()["scenes_published"]) - published;
        Gates.Check(scenes >= 100, "the hover changes the menu's scene on (nearly) every tick: " + scenes);
        var t = Summarize(times);
        Console.WriteLine($"  pause map, pointer moving over the menu: {t}");
        Gates.Check(t.P50 < 2 && t.P90 < 4, "hovering costs little (the map is not rebuilt or re-encoded): " + t);
        Gates.Check(t.Max < 45, "no tick comes near the period: " + t);
        s.Native.CheckScenes = true; s.Native.Recording = true;
        s.Input.Queue(Keys.Move(tabs[0].X, tabs[0].Y));
        s.Tick(2, advanceDrive: false);
        s.CheckAllScenesValid("pause hover");
    }

    // The map data is asked for once per window, not once per tick: a long drive through one window asks a few times.
    private static void IncrementalQueries()
    {
        using var s = new Session(d => d.SpeedMetresPerSecond = 3);
        PackageKit.Install(s, PackageKit.Surface(extraTriangles: 2000, extraLines: 200), pausePackage: false);
        s.Start();
        Gates.Check(s.WaitForPackages(20000), "adopted");
        s.Tick(10, 100);
        var before = Queries(s);
        for (var i = 0; i < 100; i++) s.Tick(1, 100);
        var asked = Queries(s) - before;
        Console.WriteLine($"  100 ticks over 300 m of road: {asked} package queries");
        Gates.Check(asked <= 2, "the package is queried once per window, not on every tick: " + asked);
        s.CheckAllScenesValid("incremental queries");
    }

    private static long Queries(Session s) => long.Parse(s.Status()["map_queries"], CultureInfo.InvariantCulture);
}
