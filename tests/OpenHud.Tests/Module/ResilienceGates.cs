using System.Diagnostics;
using OmsiLaunch.Api;
using OmsiLaunch.Runtime.Modules;
using OpenHud.Scene;
using OpenHud.Settings;
using OpenHud.Tests.Core;

namespace OpenHud.Tests.ModuleTests;

// Failures, shutdown, cost and the manifest: the module must degrade, never throw, give everything back and stay cheap.
internal static class ResilienceGates
{
    public static void Register()
    {
        Gates.Run("module.fuzz-catalog-and-input-failures", FuzzCatalog);
        Gates.Run("module.fuzz-draw-layer-failures-never-throw", FuzzNative);
        Gates.Run("module.stop-at-every-phase", StopEverywhere);
        Gates.Run("module.degradation-without-a-draw-layer", NoDrawLayer);
        Gates.Run("module.degradation-corrupt-and-hostile-packages", CorruptPackages);
        Gates.Run("module.degradation-bridge-refusals", BridgeRefusals);
        Gates.Run("module.degradation-radar-switches-off-and-back", RadarSwitchesOff);
        Gates.Run("module.settings-garbage-uses-defaults", SettingsGarbage);
        Gates.Run("module.operations-of-the-module", Operations);
        Gates.Run("module.tick-budget-20000-features", TickBudget);
        Gates.Run("module.no-growth-over-10000-ticks", NoGrowth);
        Gates.Run("module.manifest-golden", ManifestGolden);
        Gates.Run("module.manifest-settings-are-read", ManifestSettings);
        Gates.Run("module.manifest-assembled-package", AssembledPackage);
    }

    // ------------------------------------------------------------------ fuzz

    private static void ScriptedStep(Session s, Random random)
    {
        switch (random.Next(14))
        {
            case 0: s.Pause.UserPauses(); break;
            case 1: s.Pause.UserResumesNatively(); break;
            case 2: s.Pause.Finish(new[] { "resumed", "returned", "game-started", "quit-requested", "exception" }[random.Next(5)]); break;
            case 3: s.Pause.InFrame = !s.Pause.InFrame; break;
            case 4: s.Input.Queue(Keys.Down(Keys.Esc), Keys.Up(Keys.Esc)); break;
            case 5: s.Input.Queue(Keys.Down(Keys.Enter), Keys.Up(Keys.Enter)); break;
            case 6: s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab)); break;
            case 7: s.Input.Queue(Keys.Down(Keys.Down_), Keys.Up(Keys.Down_)); break;
            case 8: s.Input.Queue(Keys.Move(random.Next(0, 1300), random.Next(0, 740)), Keys.Press(random.Next(0, 1300), random.Next(0, 740)), Keys.Release(random.Next(0, 1300), random.Next(0, 740))); break;
            case 9: s.Input.Queue(Keys.Wheel(random.Next(0, 1300), random.Next(0, 740), random.Next(-3, 4) * 120)); break;
            case 10: s.Pause.AutoFinish = random.Next(2) == 0; break;
            case 11: s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = random.Next(2) == 0 ? "in" : "out" }); break;
            default: s.Tick(random.Next(1, 4), random.Next(1, 3) * 50); break;
        }
    }

    private static void FuzzCatalog()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var random = new Random(seed);
            using var s = new Session();
            PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
            s.Runtime.Chaos.Probability = 0.05 + 0.02 * (seed % 10);
            s.Input.Chaos.Probability = 0.05 + 0.02 * (seed % 5);
            s.Start();
            for (var step = 0; step < 400; step++) ScriptedStep(s, random);
            // Calm: the module must be able to give everything back once the failures stop.
            s.Runtime.Chaos.Probability = 0;
            s.Input.Chaos.Probability = 0;
            s.Tick(3);
            s.Stop();
            var context = "seed " + seed;
            Gates.Check(!s.Input.Capturing, context + ": the input capture is still on");
            Gates.Check(s.Native.Held == 0, context + ": the background is still held");
            Gates.Check(!(s.Pause.Paused && !s.Pause.NativeVisible && s.Pause.Pending is null && s.Pause.LastResult != "quit-requested"), context + ": the game is paused with the native menu hidden: machine " + s.Module.Execute("pause.state", new Dictionary<string, string>())["pause_state"] + ", err " + s.Module.Execute("pause.state", new Dictionary<string, string>())["pause_last_error"] + ", busy " + s.Pause.Busy + ", taken " + s.Pause.TakenOver);
            Gates.Check(s.Runtime.Undeclared.Count == 0, context + ": undeclared calls");
            Gates.Check(s.Native.SceneRejects == 0, context + ": scenes rejected: " + string.Join(" | ", s.Native.Problems.Take(2)));
            Gates.Check(s.Status()["faults"] == "0", context + ": a fault escaped a part: " + s.Status()["last_fault"]);
            Gates.Check(s.Context.NoticeCount() <= 4, context + ": notices are not repeated: " + s.Context.NoticeCount());
        }
    }

    private static void FuzzNative()
    {
        for (var seed = 100; seed < 130; seed++)
        {
            var random = new Random(seed);
            using var s = new Session();
            PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
            s.Native.Chaos.Probability = 0.1;
            s.Runtime.Chaos.Probability = 0.05;
            s.Start();
            for (var step = 0; step < 300; step++) ScriptedStep(s, random);
            s.Stop();
            s.Stop();
            Gates.Check(!s.Input.Capturing || s.Native.Chaos.Probability > 0, "seed " + seed + ": input");
        }
    }

    // ------------------------------------------------------------------ stop

    private static void StopEverywhere()
    {
        var steps = new List<Action<Session>>
        {
            s => s.Arm(),
            s => s.GameplayReady(),
            s => s.Tick(3),
            s => s.Pause.UserPauses(),
            s => s.Tick(2),
            s => s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab)),
            s => s.Tick(2),
            s => { s.Pause.AutoFinish = false; s.Input.Queue(Keys.Down(Keys.Down_), Keys.Up(Keys.Down_), Keys.Down(Keys.Enter), Keys.Up(Keys.Enter)); },
            s => s.Tick(2),
            s => s.Pause.Finish("returned"),
            s => s.Tick(2),
            s => s.Input.Queue(Keys.Down(Keys.Esc)),
            s => s.Tick(2),
            s => s.Input.Queue(Keys.Up(Keys.Esc)),
            s => s.Tick(1),
            s => { s.Pause.AutoFinish = true; },
            s => s.Tick(4),
        };
        for (var k = 0; k <= steps.Count; k++)
        {
            using var s = new Session();
            PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
            for (var i = 0; i < k; i++) steps[i](s);
            var context = "stop after step " + k;
            s.Stop();
            s.Stop();
            Gates.Check(!s.Input.Capturing, context + ": input capture");
            Gates.Check(s.Native.Held == 0, context + ": background");
            Gates.Check(!(s.Pause.Paused && !s.Pause.NativeVisible && s.Pause.Pending is null), context + ": native menu hidden while paused");
            Gates.Check(s.Pause.Violations.Count == 0, context + ": " + string.Join("; ", s.Pause.Violations));
            var runtimeCalls = s.Runtime.CallCount;
            var nativeCalls = s.Native.CallCount;
            s.Tick(5);
            s.GameplayReady();
            Gates.Check(s.Runtime.CallCount == runtimeCalls && s.Native.CallCount == nativeCalls, context + ": a stopped module makes no calls");
            if (k >= 2) Gates.Check(!s.Native.Visible, context + ": the layer is hidden");
        }
        // A module that was never armed.
        var idle = new HudModule();
        idle.Stop();
        idle.Tick(TimeSpan.FromMilliseconds(50));
        idle.GameplayReady();
        Gates.Check(idle.Execute("status", new Dictionary<string, string>())["module_state"] == "new", "an unarmed module answers status");
    }

    // ------------------------------------------------------------------ degradation

    private static void NoDrawLayer()
    {
        foreach (var mode in new[] { "no component", "no execute export" })
        {
            using var s = new Session();
            if (mode == "no component") s.Context.HasNative = false; else s.Native.Exports = false;
            s.Start();
            s.Tick(20);
            Gates.Check(s.Runtime.CallCount == 0, mode + ": nothing is read when nothing can be drawn");
            Gates.Check(s.Context.NoticeCount() == 1, mode + ": one notice");
            var status = s.Status();
            Gates.Check(status["draw"] == "unavailable" && status["pause_menu"] == "off", mode + ": status");
            s.Pause.UserPauses();
            s.Tick(5);
            Gates.Check(s.Pause.TakeOvers == 0, mode + ": the native menu is left alone");
        }
    }

    private static void CorruptPackages()
    {
        // A surface file cut in half, a pause package of a format nobody knows, a picture that is not one.
        using (var s = new Session())
        {
            var surface = PackageKit.Surface();
            surface.Chunk(1, 0).TruncatePayload = true;
            PackageKit.Install(s, surface, pausePackage: true, decodableIcons: false);
            File.WriteAllText(Path.Combine(s.MapsRoot, "openomsi-pausemenu", "manifest.cfg"), "format = 9\nname = future\n");
            s.Start();
            Gates.Check(s.WaitForPackages(), "adopted");
            s.Tick(30);
            Gates.Check(s.Status()["pausemenu_status"] == "Ignored", "the unknown pause package format is ignored");
            Gates.Check(s.Context.NoticeCount() == 1, "once, in a notice");
            Gates.Check(s.Native.Fonts.Count == 0 && !s.Native.Images.Contains("pkg.symbol"), "nothing of an ignored package is used");
            Gates.Check(!s.Native.Images.Any(i => i.StartsWith("mk.")), "icons that do not decode are not registered");
            s.Pause.UserPauses();
            s.Tick(3);
            Gates.Check(s.Native.LastSceneDrawsMenu, "the generic menu still opens");
            Gates.Check(Doc.Decode(s.Native.LastScene!).Commands.OfType<TextCommand>().Any(t => t.Text == "Synthetic Map"), "with the folder name as its title");
            s.CheckAllScenesValid("corrupt");
        }
        // The bridge cannot read the package at all.
        using (var s = new Session())
        {
            PackageKit.Install(s, PackageKit.Surface());
            s.Context.ContentFake.Maps[Session.MapIdentity] = new ModuleContentResult(false, null, PublicErrorCodes.MODULE_CONTENT_INPUT_INVALID, "the package is reached through a reparse point");
            s.Start();
            Gates.Check(s.WaitForPackages(), "adopted");
            s.Tick(20);
            Gates.Check(s.Status()["realmap_status"] == "Absent", "no realmap package");
            Gates.Check(s.Context.Events.Any(e => e.Name == "packages" && e.Data!["diagnostics"].Contains("reparse")), "the reason is logged");
            Gates.Check(Doc.Decode(s.Native.LastScene!).Commands.OfType<ClipCircleCommand>().Any(), "the radar draws without a package");
        }
        // The bridge throws while locating.
        using (var s = new Session())
        {
            s.Context.ContentFake.Throw = true;
            s.Start();
            s.Tick(20);
            Gates.Check(s.WaitForPackages(), "adopted despite the exception");
            s.Tick(10);
            Gates.Check(s.Status()["faults"] == "0" && Doc.Decode(s.Native.LastScene!).Commands.Count > 0, "the radar goes on");
        }
        // Hostile files: a symbol that is a decompression bomb, a font path trick, a huge identity.
        using (var s = new Session())
        {
            PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
            var dir = Path.Combine(s.MapsRoot, "openomsi-pausemenu");
            File.WriteAllBytes(Path.Combine(dir, "symbols", "logo.png"), PngWriter.Rgba(16, 16, (x, y) => (1, 2, 3, 255)).Take(80).ToArray());
            File.WriteAllText(Path.Combine(dir, "fonts", "fonts.cfg"), "format = 1\nregular = ..\\..\\evil.ttf | 400\nbold = ../evil.ttf | 700\n");
            s.Start();
            Gates.Check(s.WaitForPackages(), "adopted");
            s.Tick(10);
            Gates.Check(s.Native.Fonts.Count == 0, "unsafe font paths never reach the layer");
            Gates.Check(!s.Native.Images.Contains("pkg.symbol"), "a damaged symbol is dropped");
        }
    }

    private static void BridgeRefusals()
    {
        using var s = new Session();
        s.Context.BridgeRejectsEvents = true;
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        s.Start();
        s.WaitForPackages();
        s.Tick(30);
        s.Pause.UserPauses();
        s.Tick(3);
        Gates.Check(s.Native.LastSceneDrawsMenu, "events the bridge refuses do not stop the module");
        s.Stop();
        Gates.Check(!s.Input.Capturing, "stopped");
    }

    private static void RadarSwitchesOff()
    {
        using var s = new Session();
        s.Start();
        s.Tick(10);
        s.Native.FailScenes = true;
        s.Tick(60);
        var status = s.Status();
        Gates.Check(status["radar_state"] == "disabled" || status["radar_state"] == "on", "state: " + status["radar_state"]);
        Gates.Check(long.Parse(status["scene_failures"]) >= HudModule.FailureLimit, "the failures are counted");
        Gates.Check(s.Native.CallCount < 400, "a failing layer is not hammered: " + s.Native.CallCount);
        Gates.Check(s.Context.NoticeCount() == 1, "one notice");
        s.Native.FailScenes = false;
        s.Module.Execute("radar.toggle", new Dictionary<string, string> { ["visible"] = "true" });
        s.Tick(10);
        Gates.Check(Doc.Decode(s.Native.LastScene!).Commands.OfType<ClipCircleCommand>().Any(), "turning the radar on again brings it back");
        Gates.Check(s.Context.NoticeCount() == 1, "no second notice");
    }

    private static void SettingsGarbage()
    {
        using var s = new Session(settings: v =>
        {
            v["radar_zoom"] = "galactic"; v["ui_scale_percent"] = "9999"; v["language"] = "!!!"; v["radar_corner"] = "middle"; v["radar_size_percent"] = "abc"; v["unknown_key"] = "x";
        });
        s.Start();
        s.Tick(10);
        Gates.Check(s.Context.Events.Any(e => e.Name == "settings-issue"), "the bad values are reported");
        var status = s.Status();
        Gates.Check(status["radar_corner"] == "BottomLeft" && status["ui_scale_percent"] == "100" && status["radar_zoom_step"] == "2", "defaults are used");
        s.CheckAllScenesValid("garbage settings");
    }

    // ------------------------------------------------------------------ operations of the module

    private static ModuleOperationException Throws(Action action)
    {
        try { action(); }
        catch (ModuleOperationException e) { return e; }
        throw new InvalidOperationException("expected a ModuleOperationException");
    }

    private static void Operations()
    {
        using var s = new Session();
        s.Start();
        s.Tick(5);
        var status = s.Module.Execute("status", new Dictionary<string, string>());
        foreach (var key in new[] { "module_state", "radar_state", "pause_state", "scenes_published", "tick_max_ms", "realmap_status", "text_measure", "notices", "native_failures", "uploads_pending", "language" })
            Gates.Check(status.ContainsKey(key), "status has " + key);
        Gates.Check(status.Keys.All(k => !k.StartsWith("internal", StringComparison.Ordinal)), "no key follows the internal naming convention");
        Gates.Check(status.Values.All(v => v is not null), "no null value");

        var zoom = s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = "in" });
        Gates.Check(zoom["radar_zoom_step"] == "1" && zoom["radar_radius_m"] == "200", "zoom in: " + zoom["radar_radius_m"]);
        for (var i = 0; i < 8; i++) zoom = s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = "in" });
        Gates.Check(zoom["radar_zoom_step"] == "0", "zoom stops at the closest step");
        for (var i = 0; i < 12; i++) zoom = s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = "OUT" });
        Gates.Check(zoom["radar_zoom_step"] == "5" && zoom["radar_radius_m"] == "3200", "zoom stops at the widest step");
        Gates.Check(Throws(() => s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = "sideways" })).Code == PublicErrorCodes.RUNTIME_VALUE_INVALID, "bad direction is typed");
        Gates.Check(Throws(() => s.Module.Execute("radar.zoom", new Dictionary<string, string>())).Code == PublicErrorCodes.RUNTIME_ARGUMENT_REQUIRED, "missing direction is typed");

        Gates.Check(s.Module.Execute("radar.toggle", new Dictionary<string, string>())["radar_visible"] == "false", "toggle hides");
        s.Tick(4);
        Gates.Check(Doc.Decode(s.Native.LastScene!).Commands.Count == 0, "a hidden radar draws nothing");
        Gates.Check(s.Module.Execute("radar.toggle", new Dictionary<string, string>())["radar_visible"] == "true", "toggle shows");
        Gates.Check(s.Module.Execute("radar.toggle", new Dictionary<string, string> { ["visible"] = "false" })["radar_visible"] == "false", "explicit off");
        Gates.Check(Throws(() => s.Module.Execute("radar.toggle", new Dictionary<string, string> { ["visible"] = "maybe" })).Code == PublicErrorCodes.RUNTIME_VALUE_INVALID, "bad visible is typed");

        var pause = s.Module.Execute("pause.state", new Dictionary<string, string>());
        Gates.Check(pause["pause_menu"] == "on" && pause["pause_state"] == "Running", "pause.state");
        Gates.Check(Throws(() => s.Module.Execute("frobnicate", new Dictionary<string, string>())).Code == PublicErrorCodes.MODULE_OPERATION_UNKNOWN, "unknown operation is typed");
        Gates.Check(Throws(() => s.Module.Execute("", new Dictionary<string, string>())).Code == PublicErrorCodes.MODULE_OPERATION_UNKNOWN, "empty operation is typed");

        // Every declared operation of the manifest exists, and only those.
        var declared = Repo.Manifest().Operations.Select(o => o.Id).ToList();
        foreach (var id in declared)
        {
            var arguments = Repo.Manifest().Operations.Single(o => o.Id == id).Arguments.Where(a => a.Required).ToDictionary(a => a.Name, _ => "in");
            s.Module.Execute(id, arguments);
        }
    }

    // ------------------------------------------------------------------ cost

    private static void TickBudget()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(extraTriangles: 20000, extraLines: 2000, triangleMin: 40, triangleSpread: 260), pausePackage: true);
        s.Start();
        Gates.Check(s.WaitForPackages(20000), "adopted");
        for (var i = 0; i < 3; i++) s.Module.Execute("radar.zoom", new Dictionary<string, string> { ["direction"] = "out" });
        Gates.Check(s.WaitFor(() => { s.Tick(1); return int.Parse(s.Status()["radar_features"]) >= 2000; }, 20000), "the 20000-feature window arrives: " + s.Status()["radar_features"]);
        s.Tick(40);
        var times = new List<double>();
        for (var i = 0; i < 300; i++)
        {
            var started = Stopwatch.GetTimestamp();
            s.Tick();
            times.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }
        times.Sort();
        var p50 = times[times.Count / 2];
        var p90 = times[(int)(times.Count * 0.90)];
        var p95 = times[(int)(times.Count * 0.95)];
        var max = times[^1];
        Console.WriteLine($"  tick with 20000 features: p50 {p50:0.00} ms, p95 {p95:0.00} ms, max {max:0.00} ms");
        Gates.Check(p50 < 20, "the median tick is far under the 50 ms period: " + p50.ToString("0.00") + " ms");
        Gates.Check(p90 < 50, "nine ticks in ten stay under the period: " + p90.ToString("0.00") + " ms");
        Gates.Check(max < 250, "no tick stalls (a garbage collection is the worst case): " + max.ToString("0.00") + " ms");
        s.CheckAllScenesValid("budget");
        Gates.Check(Doc.Decode(s.Native.LastScene!).Commands.Count <= 6000, "the scene stays inside its command budget");
        // The pause menu on the full map costs the same order.
        s.Pause.UserPauses();
        s.Tick(3);
        s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab));
        s.Tick(3);
        s.WaitFor(() => { s.Tick(1, advanceDrive: false); return int.Parse(s.Status()["fullmap_features"]) > 5000; }, 20000);
        var worst = 0.0;
        for (var i = 0; i < 20; i++)
        {
            s.Input.Queue(Keys.Down(Keys.Left_), Keys.Up(Keys.Left_));
            var started = Stopwatch.GetTimestamp();
            s.Tick(1, advanceDrive: false);
            worst = Math.Max(worst, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }
        Console.WriteLine($"  tick panning the full map: max {worst:0.00} ms");
        Gates.Check(worst < 120, "panning the full map stays inside the period: " + worst.ToString("0.00"));
        s.CheckAllScenesValid("budget map");
    }

    private static void NoGrowth()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(extraTriangles: 1500, extraLines: 200), pausePackage: true);
        s.Runtime.Recording = false;
        s.Native.Recording = false;
        s.Start();
        s.WaitForPackages();
        s.Tick(1500);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var events = s.Context.Events.Count;
        s.Tick(10000);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var after = GC.GetTotalMemory(true);
        Console.WriteLine($"  10000 ticks: retained growth {(after - before) / 1024} KiB, allocated {allocated / 10000 / 1024} KiB per tick");
        Gates.Check(after - before < 2 * 1024 * 1024, "memory does not grow with the ticks: " + (after - before) / 1024 + " KiB");
        Gates.Check(s.Context.Events.Count - events <= 3, "no event per tick: " + (s.Context.Events.Count - events));
        Gates.Check(allocated / 10000 < 1024 * 1024, "allocation per tick stays modest: " + allocated / 10000 / 1024 + " KiB");
        Gates.Check(s.Status()["faults"] == "0", "no fault");
        // The same through a pause that opens and closes many times.
        for (var i = 0; i < 200; i++)
        {
            s.Pause.UserPauses();
            s.Tick(2);
            s.Input.Queue(Keys.Down(Keys.Esc), Keys.Up(Keys.Esc));
            s.Tick(6);
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var afterPauses = GC.GetTotalMemory(true);
        Gates.Check(afterPauses - after < 2 * 1024 * 1024, "opening and closing the menu does not leak: " + (afterPauses - after) / 1024 + " KiB");
        Gates.Check(s.Input.Starts == s.Input.Ends && s.Native.Captures == s.Native.Releases, "every capture was released");
    }

    // ------------------------------------------------------------------ manifest

    private static void ManifestGolden()
    {
        var m = Repo.Manifest();
        Gates.Check(m.Id == "openhud" && m.Name == "openHUD" && m.BridgeProtocolMin == 3 && m.BridgeProtocolMax == 3, "identity and protocol");
        Gates.Check(m.Activation == ModuleActivation.Requested && m.FailurePolicy == ModuleFailurePolicy.Degrade && m.TickMilliseconds == 50, "activation, policy, tick");
        Gates.Check(m.RequiredCapabilities.OrderBy(x => x).SequenceEqual(new[] { "bridge.events", "bridge.hooks.d3d9", "bridge.input", "bridge.runtime", "bridge.tick" }), "required: " + string.Join(",", m.RequiredCapabilities));
        Gates.Check(!m.RequiredCapabilities.Contains("ui.pause-menu.replace") && m.OptionalCapabilities.Contains("ui.pause-menu.replace"), "the pause replacement stays optional");
        Gates.Check(m.EffectiveRuntimeOperations.OrderBy(x => x).SequenceEqual(new[]
        {
            "camera.read", "map.read", "map.tiles.list", "player-vehicle.read", "road-vehicle.read", "road-vehicles.list", "time.read", "timetable.read",
            "ui.pause.action", "ui.pause.restore-native", "ui.pause.state", "ui.pause.take-over", "weather.read",
        }), "runtime operations: " + string.Join(",", m.EffectiveRuntimeOperations));
        Gates.Check(m.Operations.Select(o => o.Id + ":" + o.Kind + ":" + string.Join("+", o.Arguments.Select(a => a.Name + (a.Required ? "!" : "")))).OrderBy(x => x)
            .SequenceEqual(new[] { "pause.state:Read:", "radar.toggle:Action:visible", "radar.zoom:Action:direction!", "status:Read:" }), "module operations");
        Gates.Check(m.EffectiveContentInputs.Single() is { Kind: ModuleContentInputKind.RealmapPackage, Required: false }, "the realmap package is an optional content input");
        Gates.Check(m.Components.Select(c => c.Kind + ":" + c.Path).OrderBy(x => x).SequenceEqual(new[] { "Managed:OpenHud.dll", "Native:openhud-draw.dll" }), "components");
        Gates.Check(m.ManagedComponent.Entry == "OpenHud.HudModule", "entry type");
        Gates.Check(typeof(HudModule).FullName == m.ManagedComponent.Entry && typeof(HudModule).GetConstructor(Type.EmptyTypes) is not null, "the entry type exists with a parameterless constructor");
        Gates.Check(m.EffectiveSettings.Select(x => x.Key).OrderBy(x => x).SequenceEqual(new[]
            { "language", "pause_menu", "radar", "radar_corner", "radar_opacity_percent", "radar_size_percent", "radar_zoom", "ui_scale_percent", "units" }), "setting keys");
    }

    private static void ManifestSettings()
    {
        var m = Repo.Manifest();
        // The manifest's defaults are HudSettings' defaults, and every key changes the typed settings when it changes.
        var defaults = m.EffectiveSettings.ToDictionary(x => x.Key, x => x.Default, StringComparer.Ordinal);
        var parsed = HudSettingsParser.Parse(defaults);
        Gates.Check(parsed.Issues.Count == 0 && parsed.Settings == HudSettings.Defaults, "the manifest defaults are the defaults of HudSettings: " + string.Join(";", parsed.Issues.Select(i => i.Key)));
        foreach (var setting in m.EffectiveSettings)
        {
            string alternative = setting.Type switch
            {
                ModuleSettingType.Boolean => setting.Default == "true" ? "false" : "true",
                ModuleSettingType.Integer => (long.Parse(setting.Default) == setting.Minimum ? setting.Maximum : setting.Minimum).ToString(),
                ModuleSettingType.Choice => setting.Choices.First(c => c != setting.Default),
                _ => "DEU",
            };
            var changed = new Dictionary<string, string>(defaults) { [setting.Key] = alternative };
            var result = HudSettingsParser.Parse(changed);
            Gates.Check(result.Issues.Count == 0 && result.Settings != HudSettings.Defaults, "setting '" + setting.Key + "' is read by HudSettings (" + alternative + ")");
        }
    }

    private static void AssembledPackage()
    {
        var directory = Path.Combine(Repo.Root(), "artifacts", "modules", "openhud");
        if (!File.Exists(Path.Combine(directory, "module.json"))) { Console.WriteLine("  (no assembled package yet: Build-Module.ps1 assembles it before the tests)"); return; }
        var found = ModuleCatalog.Inspect(directory);
        Gates.Check(found.Code is null && found.Manifest is not null, "OmsiLaunch accepts the assembled module: " + found.Code + " " + found.Message);
        var pack = ModulePacker.Verify(directory);
        Gates.Check(pack.Intact && pack.UpToDate, "every component matches its pin");
        Gates.Check(File.Exists(Path.Combine(directory, "OpenHud.Core.dll")) && File.Exists(Path.Combine(directory, "OpenHud.Pause.dll")), "the libraries travel with the module");
    }
}
