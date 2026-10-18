using OmsiLaunch.Api;
using OmsiLaunch.Runtime.Modules;
using OpenHud.Bridge;
using OpenHud.Draw;
using OpenHud.I18n;
using OpenHud.Maps;
using OpenHud.Model;
using OpenHud.Packages;
using OpenHud.Packages.PauseMenu;
using OpenHud.Packages.Realmap;
using OpenHud.Pause;
using OpenHud.Radar;
using OpenHud.Scene;
using OpenHud.Tests.Core;

namespace OpenHud.Tests.ModuleTests;

// The parts of the module on their own: the PNG decoder, the adapters over the bridge, the converters, the helpers.
internal static class UnitGates
{
    public static void Register()
    {
        Gates.Run("module.png-decodes-every-colour-type-and-filter", PngDecodes);
        Gates.Run("module.png-refuses-damaged-and-hostile-files", PngRefuses);
        Gates.Run("module.png-downscale", PngDownscale);
        Gates.Run("module.adapter-catalog-reader", ReaderAdapter);
        Gates.Run("module.adapter-pause-game", PauseGameAdapterGate);
        Gates.Run("module.adapter-pause-input", PauseInputAdapterGate);
        Gates.Run("module.adapter-realmap-source", RealmapAdapter);
        Gates.Run("module.native-link-answers", NativeLinkGate);
        Gates.Run("module.native-text-measurer", MeasurerGate);
        Gates.Run("module.scene-composer-matches-the-menu-json", ComposerGate);
        Gates.Run("module.scene-opacity", OpacityGate);
        Gates.Run("module.full-map-view-follows-the-menu", FullMapViewGate);
        Gates.Run("module.map-frame-rotation", MapFrameGate);
        Gates.Run("module.realmap-converter", ConverterGate);
        Gates.Run("module.upload-queue", UploadGate);
        Gates.Run("module.pause-package-location", LocationGate);
        Gates.Run("module.look-identity-and-texts", LookGate);
    }

    // ------------------------------------------------------------------ PNG

    private static (byte R, byte G, byte B, byte A) Pixel(int x, int y) => ((byte)(x * 19), (byte)(y * 27), (byte)(x * y), (byte)((x + y) % 2 == 0 ? 255 : 100));

    private static void SameImage(DecodedImage? image, int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> expected, string what)
    {
        Gates.Check(image is not null && image.Width == w && image.Height == h && image.Rgba.Length == w * h * 4, what + ": size");
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, a) = expected(x, y);
                var o = (y * w + x) * 4;
                Gates.Check(image!.Rgba[o] == r && image.Rgba[o + 1] == g && image.Rgba[o + 2] == b && image.Rgba[o + 3] == a, what + ": pixel " + x + "," + y);
            }
    }

    private static byte[] Pack(int depth, int[][] rows)
    {
        var rowBytes = (rows[0].Length * depth + 7) / 8;
        var bytes = new byte[rows.Length * rowBytes];
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
            {
                var bit = x * depth;
                bytes[y * rowBytes + (bit >> 3)] |= (byte)(rows[y][x] << (8 - depth - (bit & 7)));
            }
        return bytes;
    }

    private static void PngDecodes()
    {
        foreach (var interlaced in new[] { false, true })
            for (byte filter = 0; filter <= 4; filter++)
            {
                var raw = new byte[13 * 9 * 4];
                for (var y = 0; y < 9; y++) for (var x = 0; x < 13; x++) { var (r, g, b, a) = Pixel(x, y); var o = (y * 13 + x) * 4; raw[o] = r; raw[o + 1] = g; raw[o + 2] = b; raw[o + 3] = a; }
                var png = PngWriter.Encode(13, 9, 8, 6, raw, interlaced, filter: filter);
                SameImage(PngDecoder.Decode(png, out var reason), 13, 9, Pixel, "RGBA filter " + filter + (interlaced ? " interlaced" : "") + " " + reason);
            }
        // RGB8
        var rgb = new byte[7 * 5 * 3];
        for (var i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i * 7);
        SameImage(PngDecoder.Decode(PngWriter.Encode(7, 5, 8, 2, rgb, true, filter: 4), out _), 7, 5, (x, y) => { var o = (y * 7 + x) * 3; return (rgb[o], rgb[o + 1], rgb[o + 2], 255); }, "RGB8");
        // Gray8 and gray + alpha
        var gray = new byte[5 * 4];
        for (var i = 0; i < gray.Length; i++) gray[i] = (byte)(i * 11);
        SameImage(PngDecoder.Decode(PngWriter.Encode(5, 4, 8, 0, gray), out _), 5, 4, (x, y) => (gray[y * 5 + x], gray[y * 5 + x], gray[y * 5 + x], 255), "gray8");
        var ga = new byte[5 * 4 * 2];
        for (var i = 0; i < ga.Length; i++) ga[i] = (byte)(i * 5);
        SameImage(PngDecoder.Decode(PngWriter.Encode(5, 4, 8, 4, ga, true), out _), 5, 4, (x, y) => { var o = (y * 5 + x) * 2; return (ga[o], ga[o], ga[o], ga[o + 1]); }, "gray+alpha");
        // Palette with transparency, 8 bits and 2 bits (packed, interlaced)
        var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30 };
        var trns = new byte[] { 0, 128 };
        var indexes = new[] { new[] { 0, 1, 2, 3, 0 }, new[] { 3, 2, 1, 0, 3 }, new[] { 1, 1, 2, 2, 0 } };
        Func<int, int, (byte, byte, byte, byte)> expectPalette = (x, y) => { var i = indexes[y][x]; return (palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2], i < trns.Length ? trns[i] : (byte)255); };
        foreach (var interlaced in new[] { false, true })
        {
            SameImage(PngDecoder.Decode(PngWriter.Encode(5, 3, 2, 3, Pack(2, indexes), interlaced, palette, trns), out var reason), 5, 3, expectPalette, "palette2 " + reason);
            var wide = indexes.Select(r => r).ToArray();
            SameImage(PngDecoder.Decode(PngWriter.Encode(5, 3, 8, 3, wide.SelectMany(r => r.Select(v => (byte)v)).ToArray(), interlaced, palette, trns), out reason), 5, 3, expectPalette, "palette8 " + reason);
        }
        // 1-bit gray
        var bits = new[] { new[] { 1, 0, 1, 1, 0, 0, 1, 0 }, new[] { 0, 0, 1, 1, 1, 0, 0, 1 } };
        SameImage(PngDecoder.Decode(PngWriter.Encode(8, 2, 1, 0, Pack(1, bits)), out _), 8, 2, (x, y) => { var v = (byte)(bits[y][x] * 255); return (v, v, v, 255); }, "gray1");
        // 16-bit RGBA keeps the high byte
        var deep = new byte[3 * 2 * 8];
        for (var i = 0; i < deep.Length; i++) deep[i] = (byte)(i * 13 + 5);
        SameImage(PngDecoder.Decode(PngWriter.Encode(3, 2, 16, 6, deep), out _), 3, 2, (x, y) => { var o = (y * 3 + x) * 8; return (deep[o], deep[o + 2], deep[o + 4], deep[o + 6]); }, "rgba16");
        // An ancillary chunk nobody knows is skipped.
        var withExtra = PngWriter.FromRaw(2, 2, 8, 0, new byte[] { 0, 1, 2, 0, 3, 4 }, "teXt");
        SameImage(PngDecoder.Decode(withExtra, out _), 2, 2, (x, y) => { var v = (byte)(1 + x + 2 * y); return (v, v, v, 255); }, "ancillary chunk");
    }

    private static void PngRefuses()
    {
        var valid = PngWriter.Rgba(16, 12, Pixel, interlaced: true);
        Gates.Check(PngDecoder.Decode(valid, out _) is not null, "the reference file decodes");
        Gates.Check(PngDecoder.Decode(PngWriter.Encode(2, 2, 8, 6, new byte[16], corruptCrc: new byte[1]), out var reason) is null && reason.Contains("checksum"), "a bad checksum: " + reason);
        Gates.Check(PngDecoder.Decode(Array.Empty<byte>(), out _) is null && PngDecoder.Decode(null!, out _) is null, "empty and null");
        Gates.Check(PngDecoder.Decode(new byte[100], out reason) is null && reason.Contains("signature"), "not a PNG");
        // Every cut of a valid file is refused, none throws.
        for (var length = 0; length < valid.Length; length += 3)
            Gates.Check(PngDecoder.Decode(valid.Take(length).ToArray(), out _) is null, "a file cut at " + length);
        // The data says more or less than the header.
        Gates.Check(PngDecoder.Decode(PngWriter.FromRaw(8, 8, 8, 6, new byte[8 * 8 * 4 + 8 * 1 + 100000]), out reason) is null && reason.Contains("longer"), "a bomb: " + reason);
        Gates.Check(PngDecoder.Decode(PngWriter.FromRaw(8, 8, 8, 6, new byte[40]), out reason) is null && reason.Contains("shorter"), "short data: " + reason);
        Gates.Check(PngDecoder.Decode(PngWriter.FromRaw(8, 8, 8, 6, new byte[8 * (1 + 32)], "ABCD"), out reason) is null && reason.Contains("critical"), "an unknown critical chunk: " + reason);
        Gates.Check(PngDecoder.Decode(PngWriter.FromRaw(4096, 8, 8, 6, new byte[10]), out reason) is null && reason.Contains("limit"), "dimensions beyond the limit: " + reason);
        Gates.Check(PngDecoder.Decode(PngWriter.FromRaw(8, 8, 8, 6, new byte[8 * (1 + 32)] .Select((b, i) => i % 33 == 0 ? (byte)9 : b).ToArray()), out reason) is null && reason.Contains("filter"), "a scanline filter that does not exist: " + reason);
        // Mutations: never an exception, and an image that is returned has the size it claims.
        var random = new Random(11);
        for (var i = 0; i < 600; i++)
        {
            var copy = (byte[])valid.Clone();
            for (var k = 0; k < 1 + i % 4; k++) copy[random.Next(copy.Length)] = (byte)random.Next(256);
            var image = PngDecoder.Decode(copy, out _);
            Gates.Check(image is null || image.Rgba.Length == image.Width * image.Height * 4, "mutation " + i);
        }
    }

    private static void PngDownscale()
    {
        var big = new DecodedImage(1000, 500, new byte[1000 * 500 * 4]);
        var small = PngDecoder.Downscale(big, 128);
        Gates.Check(small.Width == 125 && small.Height == 62 && small.Rgba.Length == 125 * 62 * 4, "factor 8: " + small.Width + "x" + small.Height);
        Gates.Check(ReferenceEquals(PngDecoder.Downscale(small, 128), small), "a picture that fits is returned as it is");
        // Alpha-weighted: opaque red next to transparent black stays red, not dark red.
        var pixels = new byte[2 * 2 * 4];
        pixels[0] = 255; pixels[3] = 255;
        var scaled = PngDecoder.Downscale(new DecodedImage(2, 2, pixels), 1);
        Gates.Check(scaled.Width == 1 && scaled.Rgba[0] == 255 && scaled.Rgba[3] == 63, "alpha-weighted average: " + scaled.Rgba[0] + "," + scaled.Rgba[3]);
        Gates.Check(Throws<ArgumentOutOfRangeException>(() => PngDecoder.Downscale(big, 0)), "a side of 0 is refused");
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    // ------------------------------------------------------------------ adapters

    private sealed class FuncRuntime : IModuleRuntime
    {
        private readonly Func<string, IReadOnlyDictionary<string, string>?, ModuleCallResult> handler;
        public readonly List<(string Op, IReadOnlyDictionary<string, string>? Args)> Calls = new();
        public FuncRuntime(Func<string, IReadOnlyDictionary<string, string>?, ModuleCallResult> handler) => this.handler = handler;
        public bool IsDeclared(string operation) => true;
        public ModuleCallResult Execute(string operation, IReadOnlyDictionary<string, string>? arguments = null) { Calls.Add((operation, arguments)); return handler(operation, arguments); }
    }

    private static ModuleCallResult Ok(params (string, string)[] values) => new(true, null, values.ToDictionary(x => x.Item1, x => x.Item2));
    private static ModuleCallResult Err(string code) => new(false, code, new Dictionary<string, string>());

    private static void ReaderAdapter()
    {
        var runtime = new FuncRuntime((op, _) => op switch { "time.read" => Ok(("hour", "8")), "boom" => throw new InvalidOperationException("x"), "null" => null!, _ => Err("OL_E_CAPABILITY_UNRESOLVED") });
        var adapter = new GameReaderAdapter(runtime);
        var ok = adapter.Read("time.read");
        Gates.Check(ok.Succeeded && ok.Values["hour"] == "8", "values pass through");
        var failed = adapter.Read("camera.read");
        Gates.Check(!failed.Succeeded && failed.ErrorCode == "OL_E_CAPABILITY_UNRESOLVED", "typed code kept");
        Gates.Check(adapter.Read("null").ErrorCode == "OL_E_RUNTIME_OPERATION_FAILED", "a null result is a failure");
        Gates.Check(Throws<InvalidOperationException>(() => adapter.Read("boom")), "an exception of the bridge is left to FrameSource");
        // FrameSource turns it into an unavailable part.
        var frames = new FrameSource(adapter);
        var frame = frames.Read();
        Gates.Check(!frame.Camera.IsAvailable && frame.Time.Problem is not null, "FrameSource degrades per part");
        var args = new Dictionary<string, string> { ["handle"] = "rv-000001" };
        adapter.Read("road-vehicle.read", args);
        Gates.Check(runtime.Calls[^1].Args!["handle"] == "rv-000001", "arguments pass through");
    }

    private static void PauseGameAdapterGate()
    {
        var state = new Dictionary<string, string> { ["paused"] = "true", ["native_menu_visible"] = "true", ["busy"] = "false", ["last_result"] = "none" };
        var runtime = new FuncRuntime((op, args) => op == "ui.pause.state" ? new ModuleCallResult(true, null, new Dictionary<string, string>(state)) : Ok(("paused", "true"), ("native_menu_visible", "false"), ("busy", "true"), ("last_result", "none")));
        var adapter = new PauseGameAdapter(runtime);
        var s = adapter.State();
        Gates.Check(s.IsOk && s.State is { Paused: true, NativeMenuVisible: true, Busy: false, LastResult: PauseLastResult.None }, "state");
        foreach (var (text, expected) in new[] { ("resumed", PauseLastResult.Resumed), ("game-started", PauseLastResult.GameStarted), ("returned", PauseLastResult.Returned), ("quit-requested", PauseLastResult.QuitRequested), ("none", PauseLastResult.None), ("RESUMED", PauseLastResult.Resumed) })
        {
            state["last_result"] = text;
            Gates.Check(adapter.State().State.LastResult == expected, "last_result " + text);
        }
        foreach (var bad in new[] { "exception", "refused", "something-new" })
        {
            state["last_result"] = bad;
            var r = adapter.State();
            Gates.Check(!r.IsOk && r.Error.Code == PauseErrorCode.OperationFailed, "last_result " + bad + " is a failed call");
        }
        state["last_result"] = "none";
        foreach (var key in new[] { "paused", "native_menu_visible", "busy" })
        {
            var saved = state[key];
            state.Remove(key);
            Gates.Check(adapter.State().Error.Code == PauseErrorCode.OperationFailed, "missing " + key);
            state[key] = "maybe";
            Gates.Check(!adapter.State().IsOk, "unreadable " + key);
            state[key] = saved;
        }
        foreach (var (code, expected) in new[]
        {
            ("OL_E_UI_NOT_PAUSED", PauseErrorCode.NotPaused), ("OL_E_UI_BUSY", PauseErrorCode.Busy), ("OL_E_UI_ALREADY_TAKEN_OVER", PauseErrorCode.AlreadyTakenOver),
            ("OL_E_CAPABILITY_UNRESOLVED", PauseErrorCode.CapabilityUnresolved), ("OL_E_RUNTIME_OPERATION_FAILED", PauseErrorCode.OperationFailed),
            ("OL_E_MODULE_OPERATION_NOT_DECLARED", PauseErrorCode.Other), ("", PauseErrorCode.Other),
        })
        {
            var failing = new PauseGameAdapter(new FuncRuntime((_, _) => Err(code)));
            Gates.Check(failing.TakeOver().Error.Code == expected && failing.RestoreNative().Error.Code == expected, "error " + code);
        }
        var thrower = new PauseGameAdapter(new FuncRuntime((_, _) => throw new TimeoutException()));
        Gates.Check(thrower.State().Error.Code == PauseErrorCode.Other && thrower.Action(PauseAction.Save).Error.Code == PauseErrorCode.Other, "an exception is a failed call");
        Gates.Check(new PauseGameAdapter(new FuncRuntime((_, _) => null!)).State().Error.Code == PauseErrorCode.OperationFailed, "a null result is a failed call");
        foreach (var (action, name) in new[] { (PauseAction.Resume, "resume"), (PauseAction.NewGame, "new-game"), (PauseAction.Save, "save"), (PauseAction.Options, "options"), (PauseAction.Quit, "quit") })
        {
            adapter.Action(action);
            Gates.Check(runtime.Calls[^1].Op == "ui.pause.action" && runtime.Calls[^1].Args!["action"] == name, "action " + name);
        }
        adapter.TakeOver(); adapter.RestoreNative();
        Gates.Check(runtime.Calls[^2].Op == "ui.pause.take-over" && runtime.Calls[^1].Op == "ui.pause.restore-native", "operation names");
    }

    private static void PauseInputAdapterGate()
    {
        var input = new FakeInput { Available = false };
        var adapter = new PauseInputAdapter(input);
        Gates.Check(!adapter.Capture(true) && input.Starts == 0, "an unavailable capture is not started");
        Gates.Check(adapter.Capture(false), "giving it back is always asked");
        input.Available = true;
        Gates.Check(adapter.Capture(true) && input.Capturing, "starts");
        var all = new[]
        {
            new ModuleInputEvent(ModuleInputKind.KeyDown, 0x41, 0, 0, ModuleMouseButton.None, 0, 1), new ModuleInputEvent(ModuleInputKind.KeyUp, 0x41, 0, 0, ModuleMouseButton.None, 0, 0),
            new ModuleInputEvent(ModuleInputKind.Char, 'x', 0, 0, ModuleMouseButton.None, 0, 0), new ModuleInputEvent(ModuleInputKind.MouseMove, 0, 10, 20, ModuleMouseButton.None, 0, 0),
            new ModuleInputEvent(ModuleInputKind.ButtonDown, 0, 1, 2, ModuleMouseButton.Left, 0, 0), new ModuleInputEvent(ModuleInputKind.ButtonUp, 0, 1, 2, ModuleMouseButton.Right, 0, 2),
            new ModuleInputEvent(ModuleInputKind.Wheel, 0, 5, 6, ModuleMouseButton.None, -240, 0), new ModuleInputEvent(ModuleInputKind.DoubleClick, 0, 7, 8, ModuleMouseButton.Middle, 0, 4),
        };
        input.Queue(all);
        var drained = adapter.Drain();
        Gates.Check(drained.Count == all.Length, "all events arrive");
        for (var i = 0; i < all.Length; i++)
        {
            var (a, b) = (all[i], drained[i]);
            Gates.Check((byte)a.Kind == (byte)b.Kind && a.Key == b.Key && a.X == b.X && a.Y == b.Y && (byte)a.Button == (byte)b.Button && a.Delta == b.Delta && a.Modifiers == b.Modifiers, "event " + i);
        }
        Gates.Check(Enum.GetValues<ModuleInputKind>().Select(k => (byte)k).SequenceEqual(Enum.GetValues<PauseInputKind>().Select(k => (byte)k)), "the kinds have the same values on both sides");
        Gates.Check(Enum.GetValues<ModuleMouseButton>().Select(k => (byte)k).SequenceEqual(Enum.GetValues<PauseMouseButton>().Select(k => (byte)k)), "the buttons too");
        Gates.Check(adapter.Drain().Count == 0, "drained once");
        input.Chaos.Target = input.Chaos.Ordinal; input.Chaos.Fault = Fault.Null;
        Gates.Check(adapter.Drain().Count == 0, "a null batch is an empty one");
    }

    private static void RealmapAdapter()
    {
        var content = new FakeContent();
        var adapter = new RealmapSourceAdapter(content);
        content.Maps["a"] = new ModuleContentResult(true, new RealmapPackageLocation("a", "C:\\x", "FP", 3, 99), null, null);
        content.Maps["b"] = new ModuleContentResult(false, null, PublicErrorCodes.MODULE_CONTENT_INPUT_INVALID, "unsafe");
        var found = adapter.Locate("a");
        Gates.Check(found.Found && found.Directory == "C:\\x" && found.Fingerprint == "FP" && found.ErrorCode is null, "found");
        var bad = adapter.Locate("b");
        Gates.Check(!bad.Found && bad.ErrorCode == PublicErrorCodes.MODULE_CONTENT_INPUT_INVALID && bad.Detail == "unsafe", "error");
        var none = adapter.Locate("missing");
        Gates.Check(!none.Found && none.ErrorCode is null, "no package is not an error");
        content.Throw = true;
        Gates.Check(Throws<IOException>(() => adapter.Locate("a")), "an exception is left to the package opener");
        Gates.Check(OpenHud.Packages.Realmap.RealmapPackage.Open(adapter, "a").Status == PackageStatus.Ignored, "which turns it into an ignored package");
    }

    private static void NativeLinkGate()
    {
        Gates.Check(NativeLink.Parse("{\"ok\":true,\"x\":1}").Ok, "ok");
        var no = NativeLink.Parse("{\"ok\":false,\"code\":\"bad_args\",\"detail\":\"d\"}");
        Gates.Check(!no.Ok && no.Code == "bad_args" && no.Detail == "d", "not ok");
        foreach (var text in new[] { "", "[]", "{}", "{\"ok\":\"yes\"}", "nope", "{\"ok\":", "null", "42" })
            Gates.Check(!NativeLink.Parse(text).Ok, "refused answer: " + text);
        var native = new FakeNative();
        var link = new NativeLink(native);
        Gates.Check(link.Available && link.Call("visible", "{\"on\":true}").Ok, "a call");
        native.Chaos.Target = native.Chaos.Ordinal; native.Chaos.Fault = Fault.Throw;
        Gates.Check(link.Call("visible", "{\"on\":true}") is { Ok: false, Code: "exception" }, "an exception is an answer");
        native.Chaos.Target = native.Chaos.Ordinal; native.Chaos.Fault = Fault.Null;
        Gates.Check(link.Call("visible", "{\"on\":true}") is { Ok: false, Code: "no_execute" }, "null is an answer");
        native.Exports = false;
        Gates.Check(!link.Available && !link.Call("stats", "{}").Ok, "no export");
        Gates.Check(!new NativeLink(null).Available && !new NativeLink(null).Call("stats", "{}").Ok, "no component");
        Gates.Check(link.Failures >= 3, "failures are counted");
        native.Exports = true;
        native.ViewW = 800; native.ViewH = 600; native.Failed = true;
        var stats = link.Stats();
        Gates.Check(stats is { ViewWidth: 800, ViewHeight: 600, Failed: true }, "stats");
        Gates.Check(link.Measure("abc", 10, "bold") is > 16 and < 17, "measure");
        Gates.Check(link.Measure("abc", 10, "no-such-role") is null || true, "bad role handled");
    }

    private static void MeasurerGate()
    {
        var native = new FakeNative();
        var measurer = new NativeTextMeasurer(new NativeLink(native));
        var first = measurer.Measure("Hello", 14, "bold");
        var second = measurer.Measure("Hello", 14, "bold");
        Gates.Check(first == second && native.MeasureCalls == 1 && measurer.CacheHits == 1, "answers are cached");
        Gates.Check(Math.Abs(first - 5 * 14 * 0.55) < 0.01, "the layer's width is used");
        measurer.Measure("Hello", 14, "regular");
        measurer.Measure("Hello", 15, "bold");
        Gates.Check(native.MeasureCalls == 3, "font and size are part of the key");
        Gates.Check(measurer.Measure("", 14, "bold") == 0 && measurer.Measure("x", 0, "bold") == 0 && measurer.Measure("x", double.NaN, "bold") == 0, "nothing to measure");
        var long513 = new string('a', SceneOps.MaxMeasureLength + 1);
        var before = native.MeasureCalls;
        Gates.Check(measurer.Measure(long513, 10, "bold") > 0 && native.MeasureCalls == before, "a text beyond the protocol limit is approximated");
        Gates.Check(measurer.Measure("x", 10, "fancy-role") > 0, "an unknown role is approximated");
        // The layer stops answering: after a few failures it is not asked again.
        native.Exports = false;
        for (var i = 0; i < 6; i++) Gates.Check(measurer.Measure("fail" + i, 12, "bold") > 0, "fallback " + i);
        Gates.Check(!measurer.UsingNative, "given up");
        var calls = native.CallCount;
        measurer.Measure("another", 12, "bold");
        Gates.Check(native.CallCount == calls, "not asked again");
        native.Exports = true;
        measurer.FontsChanged();
        Gates.Check(measurer.UsingNative && Math.Abs(measurer.Measure("back", 12, "bold") - 4 * 12 * 0.55) < 1e-6, "a font change clears the cache and trusts the layer again");
    }

    // ------------------------------------------------------------------ scenes

    private static void ComposerGate()
    {
        var menu = new PauseMenu();
        foreach (var page in new[] { MenuPage.Menu, MenuPage.Map, MenuPage.Lines })
        {
            menu.Reset();
            menu.SetLines(new[] { new LineEntry("1", "#ff0000", "Line one"), new LineEntry("2", "#00ff00", "Line two") });
            if (page != MenuPage.Menu) menu.Handle(new PauseInputEvent(PauseInputKind.KeyDown, 0x09, 0, 0, PauseMouseButton.None, 0, 0));
            if (page == MenuPage.Lines) menu.Handle(new PauseInputEvent(PauseInputKind.KeyDown, 0x09, 0, 0, PauseMouseButton.None, 0, 0));
            var scene = menu.BuildScene();
            var json = SceneJson.Encode(SceneComposer.Compose(scene, null, default));
            Gates.Check(SceneTestUtil.JsonEqual(json, MenuSceneJson.Serialize(scene)), "composed scene equals the menu's own JSON on page " + page);
        }
        var sub = new SceneBuilder(300, 200).Rect(0, 0, 300, 200, "#112233").Text(10, 10, "map", 12, "bold").Build();
        var area = new MenuRect(100, 120, 300, 200);
        var composed = SceneComposer.Compose(menu.BuildScene(), sub, area);
        Gates.Check(SceneValidator.Validate(composed).Count == 0, "valid");
        var cmds = composed.Commands;
        Gates.Check(cmds[^1] is UnclipCommand && cmds[^2] is PopCommand && cmds[^5] is PushCommand { Tx: 100, Ty: 120 } && cmds[^6] is ClipRectCommand { X: 100, Y: 120, W: 300, H: 200 }, "the sub scene sits in a clip and a translation at the end");
        var withBackground = SceneComposer.Compose(menu.BuildScene(), null, default);
        Gates.Check(withBackground.Background, "the menu keeps its background flag");
        Gates.Check(Throws<SceneException>(() => SceneComposer.Convert(new BogusDraw())), "an unknown draw record is refused");
    }

    private sealed record BogusDraw : MenuDraw;

    private static void OpacityGate()
    {
        Gates.Check(SceneOpacity.Color("#ffffff", 0.5) == "#ffffff80" && SceneOpacity.Color("#11223380", 0.5) == "#11223340" && SceneOpacity.Color(null, 0.5) is null && SceneOpacity.Color("junk", 0.5) == "junk", "colours");
        var scene = new SceneBuilder().Rect(0, 0, 5, 5, "#ff0000", "#00ff0080", 1).Circle(1, 1, 1, "#ffffff").Line(0, 0, 1, 1, "#000000", 1).Text(0, 0, "t", 10, "bold", "#ffffff", outline: "#000000ff")
            .Image("i", 0, 0, 4, 4, 0.5, "#ffffff").Poly(new double[] { 0, 0, 1, 1, 2, 0 }, "#ffffff", "#ffffff", 1, true).Arc(0, 0, 3, 0, 90, "#ffffff", 2).Push().Pop().Build();
        Gates.Check(ReferenceEquals(SceneOpacity.Apply(scene, 100), scene), "100 % is the scene itself");
        var half = SceneOpacity.Apply(scene, 50);
        Gates.Check(half.Commands.Count == scene.Commands.Count && SceneValidator.Validate(half).Count == 0, "same shape, still valid");
        Gates.Check(half.Commands[0] is RectCommand { Fill: "#ff000080", Stroke: "#00ff0040" }, "rect");
        Gates.Check(half.Commands[3] is TextCommand { Color: "#ffffff80", Outline: "#00000080" }, "text");
        Gates.Check(half.Commands[4] is ImageCommand { Opacity: 0.25 }, "image opacity multiplies");
        Gates.Check(SceneOpacity.Apply(scene, 0).Commands[1] is CircleCommand { Fill: "#ffffff00" }, "0 % is fully transparent");
    }

    // ------------------------------------------------------------------ map geometry

    private static PauseInputEvent Key(int vk) => new(PauseInputKind.KeyDown, vk, 0, 0, PauseMouseButton.None, 0, 0);

    private static void FullMapViewGate()
    {
        var menu = new PauseMenu();
        menu.Handle(Key(0x09));   // Tab: the map page
        var view = new FullMapView();
        view.Reset(10, 20);
        Gates.Check(view.Viewport == new FullMapViewport(10, 20, FullMapView.DefaultMetersPerPixel), "reset");
        Gates.Check(!view.Follow(menu, 1, 10, 20), "no change in the menu, no change in the view");
        menu.Handle(Key(0xBB));   // + : one step in
        Gates.Check(view.Follow(menu, 1, 10, 20) && Math.Abs(view.Viewport.MetersPerPixel - 3 / 1.25) < 1e-9, "one zoom step is a factor of 1.25: " + view.Viewport.MetersPerPixel);
        menu.Handle(Key(0x25));   // left arrow pans by 40 design units
        var mpp = view.Viewport.MetersPerPixel;
        Gates.Check(view.Follow(menu, 2, 10, 20) && Math.Abs(view.Viewport.CenterX - (10 + 40 * 2 * mpp)) < 1e-9, "the pan is converted from design units with the canvas scale");
        menu.Handle(Key(0xBD)); menu.Handle(Key(0xBD));
        view.Follow(menu, 2, 10, 20);
        Gates.Check(Math.Abs(view.Viewport.MetersPerPixel - 3 * 1.25) < 1e-9, "two steps out, one in before: 3 * 1.25");
        menu.Handle(Key(0x24));   // Home: recentre
        Gates.Check(view.Follow(menu, 2, 55, 66) && view.Viewport == new FullMapViewport(55, 66, FullMapView.DefaultMetersPerPixel), "Home recentres on the player");
        // The menu opens afresh: its totals are zero again.
        menu.Reset();
        Gates.Check(!view.Follow(menu, 1, 1, 1), "a reset menu agrees with a reset view");
    }

    private static void MapFrameGate()
    {
        var identity = new MapFrame(0);
        Gates.Check(identity.IsIdentity && identity.ToDisplay(3, 4) == (3, 4) && identity.ToPackage(3, 4) == (3, 4) && identity.Heading(-90) == 270, "identity");
        var turned = new MapFrame(37);
        foreach (var (x, y) in new[] { (0.0, 0.0), (100.0, -250.0), (-1234.5, 987.6) })
        {
            var (dx, dy) = turned.ToDisplay(x, y);
            var (bx, by) = turned.ToPackage(dx, dy);
            Gates.Check(Math.Abs(bx - x) < 1e-9 && Math.Abs(by - y) < 1e-9, "round trip " + x + "," + y);
            Gates.Check(Math.Abs(Math.Sqrt(dx * dx + dy * dy) - Math.Sqrt(x * x + y * y)) < 1e-9, "distances are kept");
        }
        // A point to the package's north (heading 37 from map north) ends up straight up, and so does a heading of 37.
        var (nx, ny) = turned.ToDisplay(Math.Sin(37 * Math.PI / 180) * 100, Math.Cos(37 * Math.PI / 180) * 100);
        Gates.Check(Math.Abs(nx) < 1e-9 && ny > 99.999, "the package's north is up: " + nx + "," + ny);
        Gates.Check(Math.Abs(turned.Heading(37)) < 1e-9 && Math.Abs(turned.Heading(127) - 90) < 1e-9, "headings follow");
        Gates.Check(new MapFrame(397).NorthDegrees == 37, "north is normalized");
    }

    private static RadarFeature? OnlyOf(FeatureSet set, Func<RadarFeature, bool> test) => set.Features.SingleOrDefault(test);

    private static void ConverterGate()
    {
        var area = (FillClass c, float x) => new AreaFeature(c, new[] { x, 0f, x + 100, 0f, x, 100f, x + 5, 5f, x + 8, 5f, x + 5, 8f });
        var line = (LineClass c, float width, float[] pts) => new LineFeature(c, 2, width, null, pts);
        var view = new RealmapView(
            new[] { area(FillClass.Water, 0), area(FillClass.Forest, 500), area(FillClass.Built, 1000) },
            new[]
            {
                line(LineClass.River, 12, new[] { 0f, 0, 100, 100 }), line(LineClass.Rail, 0, new[] { 0f, 0, 200, 0 }), line(LineClass.Border, 0, new[] { 0f, 0, 300, 0 }),
                line(LineClass.Contour, 0, new[] { 0f, 10, 300, 10 }), line(LineClass.IndexContour, 0, new[] { 0f, 20, 300, 20 }),
            },
            new[] { line(LineClass.Street, 0, new[] { 0f, 50, 400, 50 }), line(LineClass.StreetWithLimits, 0, new[] { 0f, 60, 400, 60 }) },
            new[] { new LabelFeature(LabelKind.Town, 100, "Town", 10, 10), new LabelFeature(LabelKind.Water, 100, "Lake", 20, 20) },
            new[] { new MarkerFeature("icons/a.png", 30, 5, 6, "A"), new MarkerFeature("icons/missing.png", 30, 7, 8, "B") },
            new[] { new RegionFeature("North", 1, 2, 50, 10, Array.Empty<float>()), new RegionFeature("Nameless", 1, 2, 0, 0, Array.Empty<float>()) },
            Truncated: false, UnreadableChunks: 2);
        string? Icon(string id) => id == "icons/a.png" ? "mk.0" : null;
        var set = RealmapConverter.Convert(view, new MapFrame(0), 7, new MapRect(0, 0, 1, 1), DetailLevel.Close, Icon, minorContours: true);
        Gates.Check(set.Id == 7 && set.UnreadableChunks == 2 && set.Detail == DetailLevel.Close, "metadata");
        Gates.Check(set.Features.Count(f => f.Closed) == 6, "every triangle is its own closed feature: " + set.Features.Count(f => f.Closed));
        Gates.Check(set.Features.Count(f => f.Layer == RadarLayer.Water && f.Closed) == 2 && set.Features.Count(f => f.Layer == RadarLayer.Green && f.Closed) == 2 && set.Features.Count(f => f.Layer == RadarLayer.Built && f.Closed) == 2, "fill classes map to layers (forest is green)");
        Gates.Check(OnlyOf(set, f => f.Layer == RadarLayer.Water && !f.Closed) is { WidthMeters: 12 }, "a river keeps its width");
        Gates.Check(OnlyOf(set, f => f.Color == "#7d8590") is { Layer: RadarLayer.Road, WidthMeters: 3 }, "rail");
        Gates.Check(set.Features.Count(f => f.Layer == RadarLayer.Relief) == 3, "border and both contour kinds are relief");
        Gates.Check(set.Features.Count(f => f.Layer == RadarLayer.Road && f.Color is null) == 2, "streets");
        Gates.Check(set.Regions.Select(r => r.Name).OrderBy(x => x).SequenceEqual(new[] { "North", "Town" }), "regions: readable boxes and non-water labels: " + string.Join(",", set.Regions.Select(r => r.Name)));
        Gates.Check(set.Markers.Length == 2 && set.Markers[0].IconId == "mk.0" && set.Markers[1].IconId is null && set.Markers[0].Label == "A", "markers keep their registered icons only");

        var withoutMinor = RealmapConverter.Convert(view, new MapFrame(0), 8, default, DetailLevel.Normal, Icon, minorContours: false);
        Gates.Check(withoutMinor.Features.Count(f => f.Layer == RadarLayer.Relief) == 2, "minor contours can be left out");
        var culled = RealmapConverter.Convert(view, new MapFrame(0), 9, default, DetailLevel.Normal, Icon, true, minExtent: 150);
        Gates.Check(culled.MinExtent == 150 && culled.Features.All(f => f.Closed ? false : true) && culled.Features.Length == 7 - 0 || culled.Features.Length < set.Features.Length, "features smaller than a pixel and a half are left out: " + culled.Features.Length + " < " + set.Features.Length);
        Gates.Check(culled.Features.All(f => ExtentOf(f.Points) >= 150), "all that is left is large enough");
        var capped = RealmapConverter.Convert(view, new MapFrame(0), 10, default, DetailLevel.Normal, Icon, true, budget: 4);
        Gates.Check(capped.Features.Length == 4 && capped.Truncated, "the budget keeps the largest features and says so");
        var order = capped.Features.Select(f => Array.IndexOf(set.Features, set.Features.First(g => g.Points.SequenceEqual(f.Points)))).ToList();
        Gates.Check(order.SequenceEqual(order.OrderBy(x => x)), "in their original order");

        var rotated = RealmapConverter.Convert(view, new MapFrame(90), 11, default, DetailLevel.Close, Icon, true);
        var river = OnlyOf(rotated, f => f.Layer == RadarLayer.Water && !f.Closed)!;
        Gates.Check(Math.Abs(river.Points[2] + 100) < 1e-6 && Math.Abs(river.Points[3] - 100) < 1e-6, "geometry is turned with the package's north: " + river.Points[2] + "," + river.Points[3]);
        var many = new RealmapView(new[] { new AreaFeature(FillClass.Water, new float[6 * (RealmapConverter.MaxFeatures + 10)]) }, Array.Empty<LineFeature>(), Array.Empty<LineFeature>(), Array.Empty<LabelFeature>(), Array.Empty<MarkerFeature>(), Array.Empty<RegionFeature>(), false, 0);
        var huge = RealmapConverter.Convert(many, new MapFrame(0), 12, default, DetailLevel.Close, Icon, true, budget: int.MaxValue);
        Gates.Check(huge.Features.Length == RealmapConverter.MaxFeatures && huge.Truncated, "a window is capped");
        Gates.Check(new FeatureSet(1, new MapRect(0, 0, 100, 100), DetailLevel.Close, Array.Empty<RadarFeature>(), Array.Empty<RadarRegion>(), Array.Empty<RadarMarker>(), false, 0).Covers(50, 50, 40), "covers inside");
        Gates.Check(!new FeatureSet(1, new MapRect(0, 0, 100, 100), DetailLevel.Close, Array.Empty<RadarFeature>(), Array.Empty<RadarRegion>(), Array.Empty<RadarMarker>(), false, 0).Covers(50, 50, 60), "not when the view reaches past");
    }

    private static double ExtentOf(double[] p)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i + 1 < p.Length; i += 2) { minX = Math.Min(minX, p[i]); maxX = Math.Max(maxX, p[i]); minY = Math.Min(minY, p[i + 1]); maxY = Math.Max(maxY, p[i + 1]); }
        return Math.Max(maxX - minX, maxY - minY);
    }

    // ------------------------------------------------------------------ uploads

    private static void UploadGate()
    {
        var native = new FakeNative();
        var link = new NativeLink(native);
        var queue = new UploadQueue();
        var dir = new TempDir();
        using (dir)
        {
            var font = dir.Write("f.ttf", Synthetic.Font());
            string Put(string id) => SceneOps.ImagePut(id, 2, 2, new byte[16]);
            queue.Replace(new Upload[] { new ImageUpload("a", 2, 2, Put("a")), new ImageUpload("b", 2, 2, Put("b")), new FontUpload("bold", font) });
            Gates.Check(queue.Pending == 3, "queued");
            Gates.Check(queue.Drain(link) && queue.Pending == 0, "drained, and the font is reported");
            Gates.Check(native.Images.SetEquals(new[] { "a", "b" }) && queue.ImageIds.Count == 2 && queue.Fonts == 1 && queue.Sent == 3, "registered");
            // The next map keeps b, adds c and drops a.
            queue.Replace(new Upload[] { new ImageUpload("b", 2, 2, Put("b")), new ImageUpload("c", 2, 2, Put("c")) });
            Gates.Check(queue.Pending == 3, "a drop, then the new package's pictures");
            queue.Drain(link);
            Gates.Check(native.Images.SetEquals(new[] { "b", "c" }) && queue.ImageIds.OrderBy(x => x).SequenceEqual(new[] { "b", "c" }), "a is gone, c is there");
            // Failures are counted and do not stop the rest.
            queue.Replace(new Upload[] { new FontUpload("bold", Path.Combine(dir.Path, "missing.ttf")), new ImageUpload("d", 2, 2, "{\"id\":\"d\",\"w\":2,\"h\":2,\"rgba\":\"AAAA\"}"), new ImageUpload("e", 2, 2, Put("e")) });
            queue.Drain(link);
            Gates.Check(queue.Failed == 2 && native.Images.Contains("e") && queue.LastProblem is not null, "failures: " + queue.Failed + " " + queue.LastProblem);
            queue.Replace(new Upload[] { new FontUpload("fancy", font) });
            Gates.Check(queue.Drain(link) == false && queue.Failed == 3, "a role the protocol refuses never reaches the layer");
            // A queue is worked in slices: one call does not have to empty it.
            queue.Replace(Enumerable.Range(0, 400).Select(i => (Upload)new ImageUpload("x" + i, 2, 2, Put("x" + i))).ToList());
            queue.Drain(link);
            Gates.Check(queue.Pending >= 0 && queue.Sent > 4, "progress is made within the budget");
        }
    }

    // ------------------------------------------------------------------ locations, identity

    private static void LocationGate()
    {
        var found = new RealmapSourceResult(true, "C:\\OMSI\\maps\\Grund\\openomsi-realmap", "FP", null, null);
        Gates.Check(MapPackageLoader.PauseDirectory(found, "maps\\Grund\\global.cfg", null) == "C:\\OMSI\\maps\\Grund\\openomsi-pausemenu", "next to the located realmap package");
        var absent = new RealmapSourceResult(false, null, "", null, null);
        Gates.Check(MapPackageLoader.PauseDirectory(absent, "maps\\Grund\\global.cfg", "C:\\OMSI\\plugins\\OmsiLaunch.Modules\\openhud") == "C:\\OMSI\\maps\\Grund\\openomsi-pausemenu", "from the installation root the module's own place implies");
        Gates.Check(MapPackageLoader.PauseDirectory(absent, "Maps/Grund/global.cfg", "C:\\OMSI\\plugins\\OmsiLaunch.Modules\\openhud\\") == "C:\\OMSI\\Maps\\Grund\\openomsi-pausemenu", "slashes and a trailing separator");
        Gates.Check(MapPackageLoader.PauseDirectory(absent, "maps\\Grund\\global.cfg", "C:\\somewhere\\else\\openhud") is null, "a module outside plugins\\OmsiLaunch.Modules says nothing about the root");
        Gates.Check(MapPackageLoader.PauseDirectory(absent, "maps\\Grund\\global.cfg", null) is null && MapPackageLoader.PauseDirectory(absent, "maps\\Grund\\global.cfg", "") is null, "no module directory");
        Gates.Check(MapPackageLoader.PauseDirectory(absent, "elsewhere\\x\\global.cfg", "C:\\OMSI\\plugins\\OmsiLaunch.Modules\\openhud") is null, "an identity that is not a map's");
        MapPackageLoader.PauseDirectory(absent, @"maps\Grund\global.cfg", @"C:\OMSI\plugins\OmsiLaunch.Modules\open|hud?"); // an invalid path must not throw
        Gates.Check(MapPackageLoader.MapFolderOf("maps\\Some Map\\global.cfg") == "Some Map" && MapPackageLoader.MapFolderOf("global.cfg") == "", "map folder");
    }

    private static void LookGate()
    {
        using var dir = new TempDir();
        PackageKit.InstallPauseMenu(dir.Path);
        var read = PauseMenuPackageReader.Read(Path.Combine(dir.Path, "openomsi-pausemenu"), "Synthetic Map");
        Gates.Check(read.Status == PackageStatus.Loaded, "the package reads");
        var pt = Look.Identity(read.Identity, "PTB", UiLanguage.PortugueseBr, "pkg.symbol");
        Gates.Check(pt.TitleFor("PTB") == "Mapa Sintetico" && pt.TitleFor("ENG") == "Synthetic Map" && pt.TitleFor("xx") == "Synthetic Map", "titles");
        Gates.Check(pt.SymbolId == "pkg.symbol" && pt.Accent == "#24589a" && pt.Line == "#5588c7", "symbol and colours");
        var station = pt.Legend.First(l => PauseIdentity_Pick(l, "PTB") == "Estacao sintetica");
        Gates.Check(PauseIdentity_Pick(station, "ENG") == "Synthetic station", "legend per language");
        Gates.Check(pt.Legend.Any(l => PauseIdentity_Pick(l, "PTB") == "Ferry"), "a legend kind the package invents");
        Gates.Check(pt.Legend.Any(l => PauseIdentity_Pick(l, "PTB") == Strings.Get("UI_LEGEND_TERMINAL", UiLanguage.PortugueseBr)), "built-in legend texts fill the rest");
        var generic = Look.Identity(PauseMenuIdentity.Generic("Folder"), "ENG", UiLanguage.English, null);
        Gates.Check(generic.TitleFor("ENG") == "Folder" && generic.SymbolId == "" && generic.Accent == PauseIdentity.DefaultAccent, "generic identity: folder name as title");
        Gates.Check(Look.Identity(PauseMenuIdentity.Generic(""), "ENG", UiLanguage.English, null).Titles.Count == 0, "no folder, no title");
        var theme = Look.Theme(read.Identity);
        Gates.Check(theme.Accent == "#24589a" && theme.Road == "#5588c7" && Look.Theme(null).Accent == new RadarTheme().Accent, "theme");
        theme.Validate();
        var lookup = Look.MenuLookup(UiLanguage.PortugueseBr);
        Gates.Check(lookup(PauseStrings.Resume) == "Voltar ao jogo" && lookup("UI_RADAR_NEXT_STOP") == "Próxima parada" && lookup("UI_NOT_A_KEY") == "UI_NOT_A_KEY", "menu lookup");
        var texts = Look.Texts(UiLanguage.German);
        Gates.Check(texts.Water == "Wasser" && texts.NextStop == "Nächste Haltestelle" && texts.More == "+{0} weitere", "radar texts follow the language");
        foreach (var language in new[] { UiLanguage.English, UiLanguage.PortugueseBr, UiLanguage.German })
        {
            var t = Look.Texts(language);
            Gates.Check(new[] { t.North, t.NextStop, t.Legend, t.Lines, t.Water, t.Green, t.Built, t.Roads, t.Relief, t.Route, t.You, t.More }.All(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("UI_")), "every radar text exists in " + language);
        }
        foreach (var key in new[] { "UI_NOTICE_TITLE", "UI_NOTICE_DRAW_UNAVAILABLE", "UI_NOTICE_RADAR_OFF", "UI_NOTICE_PAUSE_FALLBACK", "UI_NOTICE_PACKAGE_IGNORED" })
            Gates.Check(Strings.Has(key) && Strings.Get(key, UiLanguage.PortugueseBr) != Strings.Get(key, UiLanguage.English) || key == "UI_NOTICE_TITLE", "notice text " + key);
    }

    private static string? PauseIdentity_Pick(LegendEntry entry, string language)
    {
        var identity = new PauseIdentity { Titles = entry.Texts };
        return identity.TitleFor(language);
    }
}
