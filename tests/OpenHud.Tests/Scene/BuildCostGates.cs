using System.Diagnostics;
using OpenHud.Radar;
using OpenHud.Scene;
using static OpenHud.Tests.SceneTestUtil;

namespace OpenHud.Tests;

// What building one radar and one full-map scene costs when the view is as full as the module allows (the builders alone,
// no bridge): measured and printed, and held under a ceiling that leaves a lot of the 50 ms tick for everything else.
internal static class BuildCostGates
{
    public static void Register()
    {
        Gates.Run("radar.build-cost-dense-view", RadarCost);
        Gates.Run("fullmap.build-cost-dense-view", FullMapCost);
        Gates.Run("scene.encode-cost-and-hash", EncodeCost);
    }

    private static List<double> Measure(int rounds, Action<int> body)
    {
        for (var i = 0; i < 20; i++) body(i); // warm up the JIT and the caches
        var times = new List<double>(rounds);
        for (var i = 0; i < rounds; i++)
        {
            var started = Stopwatch.GetTimestamp();
            body(i);
            times.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }
        times.Sort();
        return times;
    }

    private static string Summary(List<double> t) =>
        $"p50 {t[t.Count / 2]:0.00} ms, p90 {t[(int)(t.Count * 0.90)]:0.00} ms, p99 {t[(int)(t.Count * 0.99)]:0.00} ms, max {t[^1]:0.00} ms";

    private static void RadarCost()
    {
        var model = DenseModel(2500, 420, 48, 48);
        var builder = new RadarSceneBuilder();
        var options = new RadarOptions { MaxCommands = 1500, MaxPoints = 6000, ZoomStep = 2, Mode = RadarMode.HeadingUp };
        SceneDocument? last = null;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var times = Measure(300, i =>
        {
            // The player moves a little each time, so nothing can be reused from the previous build.
            var moved = model with { Player = new RadarPlayer(i * 0.7, i * 0.3, 20 + i * 0.5) };
            last = builder.Build(moved, options);
        });
        allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 320;
        Console.WriteLine("  radar, 2500 features in a 420 m square: " + Summary(times) + "; " + allocated / 1024 + " KiB allocated per build");
        Gates.Check(last!.Commands.Count > 300, "the view is full: " + last.Commands.Count + " commands");
        Gates.Check(times[times.Count / 2] < 8 && times[(int)(times.Count * 0.99)] < 25, "a full radar builds well inside the tick: " + Summary(times));
    }

    private static void FullMapCost()
    {
        var model = DenseModel(6000, 900, 120, 120);
        var builder = new FullMapSceneBuilder();
        var options = new FullMapOptions { CanvasWidth = 1356, CanvasHeight = 900, Margin = 0, SidePanelWidth = 0, OverBackground = false, MaxCommands = 6900, MaxVehicles = 120, MaxMarkers = 120 };
        SceneDocument? last = null;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var times = Measure(120, i => last = builder.Build(model with { Player = new RadarPlayer(i, 0, 10) }, new FullMapViewport(i * 0.5, 0, 2), options));
        allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 140;
        Console.WriteLine("  full map, 6000 features in an 1800 m square: " + Summary(times) + "; " + allocated / 1024 + " KiB allocated per build");
        Gates.Check(last!.Commands.Count > 1000, "the map is full: " + last.Commands.Count + " commands");
        Gates.Check(times[times.Count / 2] < 25 && times[(int)(times.Count * 0.99)] < 60, "a full map builds inside the tick: " + Summary(times));
    }

    private static void EncodeCost()
    {
        var model = DenseModel(2500, 420, 48, 48);
        var scene = new RadarSceneBuilder().Build(model, new RadarOptions { MaxCommands = 1500, MaxPoints = 6000, ZoomStep = 2 });
        var encode = Measure(300, _ => SceneJson.Encode(scene));
        var encodeLean = Measure(300, _ => SceneJson.Encode(scene, validate: false));
        var hash = Measure(300, _ => SceneHash.Of(scene));
        Console.WriteLine($"  encode ({SceneJson.Encode(scene).Length / 1024} KiB): with validation {Summary(encode)}");
        Console.WriteLine("  encode without validation: " + Summary(encodeLean));
        Console.WriteLine("  hash: " + Summary(hash));
        Gates.Check(hash[hash.Count / 2] < encodeLean[encodeLean.Count / 2], "hashing a scene is cheaper than encoding it");

        // The hash tells equal scenes from different ones, and ignores nothing the encoding shows.
        var same = new RadarSceneBuilder().Build(model, new RadarOptions { MaxCommands = 1500, MaxPoints = 6000, ZoomStep = 2 });
        Gates.Check(SceneHash.Of(same) == SceneHash.Of(scene), "equal scenes, equal hashes");
        foreach (var changed in new[]
        {
            model with { Player = new RadarPlayer(0.1, 0, 45) },
            model with { Player = new RadarPlayer(0, 0, 45.1) },
            model with { Info = new RadarInfo(null, "12:00", null, "5 km/h") },
        })
        {
            var other = new RadarSceneBuilder().Build(changed, new RadarOptions { MaxCommands = 1500, MaxPoints = 6000, ZoomStep = 2 });
            Gates.Check(SceneHash.Of(other) != SceneHash.Of(scene) == (SceneJson.Encode(other) != SceneJson.Encode(scene)), "the hash differs exactly when the encoding does");
        }
        var one = new SceneDocument(100, 100, false, new SceneCommand[] { new RectCommand(1, 2, 3, 4, "#ffffff") });
        Gates.Check(SceneHash.Of(one) != SceneHash.Of(one with { Background = true }) && SceneHash.Of(one) != SceneHash.Of(one with { Width = 101 }), "canvas and background count");
        Gates.Check(SceneHash.Of(one with { Commands = new SceneCommand[] { new RectCommand(-0.0, 2, 3, 4, "#ffffff") } }) == SceneHash.Of(one with { Commands = new SceneCommand[] { new RectCommand(0.0, 2, 3, 4, "#ffffff") } }), "-0 and 0 encode alike and hash alike");
        Gates.Check(SceneHash.Of(one with { Commands = new SceneCommand[] { new RectCommand(1, 2, 3, 4, "#ffffff", null, null, 0) } }) != SceneHash.Of(one), "a present member differs from an absent one");
    }
}
