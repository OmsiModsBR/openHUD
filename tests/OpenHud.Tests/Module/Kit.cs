#pragma warning disable CS0649 // knobs of the fakes that only some gates turn
using System.Globalization;
using System.Text;
using System.Text.Json;
using OmsiLaunch.Api;
using OmsiLaunch.Runtime.Modules;
using OpenHud.Model;
using OpenHud.Pause;
using OpenHud.Scene;
using OpenHud.Tests.Core;

namespace OpenHud.Tests.ModuleTests;

// What a fake does at one call.
internal enum Fault { None, Error, Throw, Garbage, Empty, Null }

// Decides, per call ordinal, whether a fake misbehaves: either at one chosen call or at random with a
// fixed seed, so a failing run replays.
internal sealed class Chaos
{
    private readonly Random random;
    public int Target = -1;
    public Fault Fault = Fault.None;
    public double Probability;
    public Fault[] Menu = { Fault.Error, Fault.Throw, Fault.Garbage, Fault.Empty, Fault.Null };
    public int Ordinal { get; private set; }

    public Chaos(int seed = 1) => random = new Random(seed);

    public Fault Next()
    {
        var at = Ordinal++;
        if (at == Target) return Fault;
        if (Probability > 0 && random.NextDouble() < Probability) return Menu[random.Next(Menu.Length)];
        return Fault.None;
    }
}

internal sealed class FakeClock : IPauseClock
{
    public long Now;
    public long NowMilliseconds => Now;
}

// The OMSI catalog as the module sees it: frames of a recorded drive for the readings, a small state
// machine for ui.pause.*, and an optional fault source. Calls to anything the manifest does not declare
// are recorded (and answered like the bridge does): a gate asserts there were none.
internal sealed class FakeRuntime : IModuleRuntime
{
    public ReplayFrame Frame = new();
    public readonly FakePauseCatalog Pause;
    public readonly HashSet<string> Declared;
    public readonly Chaos Chaos = new();
    public readonly List<string> Calls = new();
    public readonly List<string> Undeclared = new();
    public readonly Dictionary<string, int> PerOperation = new();
    public bool PauseUnresolved;
    // Only ui.pause.* calls meet the fault source.
    public bool PauseFaultsOnly;
    // Off for the long runs: the fake itself must not grow with the number of ticks.
    public bool Recording = true;
    public long CallCount;

    public FakeRuntime(IEnumerable<string> declared, FakeInput input)
    {
        Declared = new HashSet<string>(declared, StringComparer.Ordinal);
        Pause = new FakePauseCatalog(input);
    }

    public bool IsDeclared(string operation) => Declared.Contains(operation);

    public ModuleCallResult Execute(string operation, IReadOnlyDictionary<string, string>? arguments = null)
    {
        CallCount++;
        if (Recording)
        {
            Calls.Add(operation);
            PerOperation[operation] = PerOperation.GetValueOrDefault(operation) + 1;
        }
        if (!Declared.Contains(operation))
        {
            Undeclared.Add(operation);
            return new ModuleCallResult(false, PublicErrorCodes.MODULE_OPERATION_NOT_DECLARED, new Dictionary<string, string>());
        }
        var isPause = operation.StartsWith("ui.pause.", StringComparison.Ordinal);
        var fault = (isPause || !PauseFaultsOnly) ? Chaos.Next() : Fault.None;
        if (fault == Fault.Throw) throw new InvalidOperationException("injected");
        if (fault == Fault.Null) return null!;
        if (fault == Fault.Error) return new ModuleCallResult(false, isPause ? "OL_E_RUNTIME_OPERATION_FAILED" : "OL_E_RUNTIME_OPERATION_UNAVAILABLE", new Dictionary<string, string>());
        if (fault == Fault.Empty) return new ModuleCallResult(true, null, new Dictionary<string, string>());

        ModuleCallResult real;
        if (isPause) real = PauseUnresolved ? new ModuleCallResult(false, "OL_E_CAPABILITY_UNRESOLVED", new Dictionary<string, string>()) : Pause.Handle(operation, arguments);
        else
        {
            var key = ReplayGameReader.Key(operation, arguments);
            real = Frame.Replies.TryGetValue(key, out var reply)
                ? new ModuleCallResult(reply.Succeeded, reply.ErrorCode, reply.Values)
                : new ModuleCallResult(false, "OL_E_RUNTIME_OPERATION_UNAVAILABLE", new Dictionary<string, string>());
        }
        if (fault != Fault.Garbage || !real.Succeeded) return real;
        var junk = new[] { "NaN", "abc", "", "1e999", "\0", "true", "-7", new string('9', 400), "\ud800" };
        var rnd = new Random(Chaos.Ordinal);
        return new ModuleCallResult(true, null, real.Values.Keys.ToDictionary(k => k, _ => junk[rnd.Next(junk.Length)]));
    }
}

// ui.pause.* at catalog level, with the typed errors of docs/reference/capabilities.md.
internal sealed class FakePauseCatalog
{
    private readonly FakeInput input;
    public bool Paused, NativeVisible, Busy, TakenOver, InFrame;
    public string? Pending;
    public string LastResult = "none";
    public int TakeOvers, Restores;
    public readonly List<string> Actions = new();
    public readonly List<string> Violations = new();
    // Completes a scheduled action by itself at the next state read.
    public bool AutoFinish = true;

    public FakePauseCatalog(FakeInput input) => this.input = input;

    public void UserPauses() { Paused = true; NativeVisible = true; TakenOver = false; Busy = false; Pending = null; LastResult = "none"; }

    public void UserResumesNatively() { if (Paused && NativeVisible) { Paused = false; NativeVisible = false; TakenOver = false; } }

    public void Finish(string result)
    {
        if (Pending is null) return;
        Busy = false;
        Pending = null;
        LastResult = result;
        if (result is "resumed" or "game-started") { Paused = false; NativeVisible = false; TakenOver = false; }
    }

    private Dictionary<string, string> Values() => new(StringComparer.Ordinal)
    {
        ["paused"] = Paused ? "true" : "false", ["native_menu_visible"] = NativeVisible ? "true" : "false", ["main_form_ready"] = "true",
        ["in_frame"] = InFrame ? "true" : "false", ["taken_over"] = TakenOver ? "true" : "false", ["busy"] = Busy ? "true" : "false",
        ["pending_action"] = Pending ?? "none", ["last_action"] = "none", ["last_result"] = LastResult,
    };

    private ModuleCallResult Ok() => new(true, null, Values());
    private static ModuleCallResult Error(string code) => new(false, code, new Dictionary<string, string>());

    public ModuleCallResult Handle(string operation, IReadOnlyDictionary<string, string>? arguments)
    {
        switch (operation)
        {
            case "ui.pause.state":
                if (AutoFinish && Pending is not null) Finish(Pending == "resume" ? "resumed" : "returned");
                return Ok();
            case "ui.pause.take-over":
                TakeOvers++;
                if (!Paused || (!NativeVisible && !TakenOver)) return Error("OL_E_UI_NOT_PAUSED");
                if (Busy || InFrame) return Error("OL_E_UI_BUSY");
                if (TakenOver) return Error("OL_E_UI_ALREADY_TAKEN_OVER");
                TakenOver = true; NativeVisible = false;
                return Ok();
            case "ui.pause.restore-native":
                Restores++;
                if (Busy) return Error("OL_E_UI_BUSY");
                if (!Paused) Violations.Add("restore-native on a game that is not paused");
                TakenOver = false; NativeVisible = true; Paused = true;
                return Ok();
            case "ui.pause.action":
            {
                var action = arguments is not null && arguments.TryGetValue("action", out var a) ? a : null;
                if (action is null) return Error("OL_E_RUNTIME_ARGUMENT_REQUIRED");
                if (Array.IndexOf(new[] { "resume", "new-game", "save", "options", "quit" }, action) < 0) return Error("OL_E_RUNTIME_VALUE_INVALID");
                Actions.Add(action);
                if (input.Capturing) Violations.Add("action " + action + " while the input capture is on");
                if (!Paused) return Error("OL_E_UI_NOT_PAUSED");
                if (Busy) return Error("OL_E_UI_BUSY");
                Busy = true; Pending = action; LastResult = "none";
                var values = Values();
                values["scheduled"] = "true"; values["action"] = action;
                return new ModuleCallResult(true, null, values);
            }
            default: return Error("OL_E_RUNTIME_OPERATION_UNKNOWN");
        }
    }
}

internal sealed class FakeInput : IModuleInput
{
    public bool Available = true;
    public bool Capturing;
    public int Starts, Ends;
    public readonly Chaos Chaos = new(7);
    private readonly Queue<ModuleInputEvent> queue = new();

    public bool IsAvailable => Available;
    public bool IsCapturing => Capturing;

    public void Queue(params ModuleInputEvent[] events)
    {
        if (!Capturing) return;
        foreach (var e in events) queue.Enqueue(e);
    }

    public bool Capture(bool enabled)
    {
        var fault = Chaos.Next();
        if (fault == Fault.Throw) throw new InvalidOperationException("injected");
        if (fault != Fault.None) return false;
        if (enabled)
        {
            if (!Available) return false;
            if (!Capturing) { Starts++; queue.Clear(); }
        }
        else if (Capturing) Ends++;
        Capturing = enabled;
        return true;
    }

    public IReadOnlyList<ModuleInputEvent> Drain()
    {
        var fault = Chaos.Next();
        if (fault == Fault.Throw) throw new InvalidOperationException("injected");
        if (fault == Fault.Null) return null!;
        if (!Capturing) { queue.Clear(); return Array.Empty<ModuleInputEvent>(); }
        var batch = queue.ToArray();
        queue.Clear();
        return batch;
    }
}

internal static class Keys
{
    public static ModuleInputEvent Down(int vk, int mods = 0) => new(ModuleInputKind.KeyDown, vk, 0, 0, ModuleMouseButton.None, 0, mods);
    public static ModuleInputEvent Up(int vk) => new(ModuleInputKind.KeyUp, vk, 0, 0, ModuleMouseButton.None, 0, 0);
    public static ModuleInputEvent Move(int x, int y) => new(ModuleInputKind.MouseMove, 0, x, y, ModuleMouseButton.None, 0, 0);
    public static ModuleInputEvent Press(int x, int y) => new(ModuleInputKind.ButtonDown, 0, x, y, ModuleMouseButton.Left, 0, 0);
    public static ModuleInputEvent Release(int x, int y) => new(ModuleInputKind.ButtonUp, 0, x, y, ModuleMouseButton.Left, 0, 0);
    public static ModuleInputEvent Wheel(int x, int y, int delta) => new(ModuleInputKind.Wheel, 0, x, y, ModuleMouseButton.None, delta, 0);
    public const int Esc = 0x1B, Enter = 0x0D, Tab = 0x09, Down_ = 0x28, Up_ = 0x26, Left_ = 0x25, Right_ = 0x27, Plus = 0xBB;
}

// The draw layer as far as the module can tell: it validates what the real one validates (scenes through
// the decoder and the validator, pictures by size, fonts by path) and records everything.
internal sealed class FakeNative : IModuleNativeComponent
{
    public readonly List<(string Op, string Args)> Calls = new();
    public readonly List<string> Scenes = new();
    public bool Recording = true;
    public bool CheckScenes = true;
    public bool FailScenes;
    public long CallCount, SceneCount;
    private string? lastScene;
    public readonly HashSet<string> Images = new(StringComparer.Ordinal);
    public readonly List<(string Role, string Path)> Fonts = new();
    public readonly List<string> Problems = new();
    public readonly Chaos Chaos = new(3);
    public bool Exports = true;
    public bool Visible = true;
    public bool Failed;
    public int ViewW = 1280, ViewH = 720;
    public int Captures, Releases, Held;
    public int MeasureCalls;
    public int SceneRejects;
    public bool IsDisposed;

    public bool HasTick => false;
    public bool HasExecute => Exports && !IsDisposed;
    public void GameplayReady() { }
    public void Tick(uint elapsedMilliseconds) { }
    public void Dispose() => IsDisposed = true;

    public string? LastScene => lastScene;

    public bool LastSceneDrawsMenu
    {
        get
        {
            if (LastScene is null) return false;
            return Doc.Decode(LastScene).Background;
        }
    }

    public int CountOf(string op) => Calls.Count(c => c.Op == op);

    public string? Execute(string operation, string argumentsJson)
    {
        if (!HasExecute) return null;
        CallCount++;
        if (Recording) Calls.Add((operation, argumentsJson));
        var fault = Chaos.Next();
        if (fault == Fault.Throw) throw new InvalidOperationException("injected");
        if (fault == Fault.Null) return null;
        if (fault == Fault.Garbage) return "this is not json";
        if (fault == Fault.Empty) return "{}";
        if (fault == Fault.Error) return "{\"ok\":false,\"code\":\"injected\",\"detail\":\"injected\"}";
        switch (operation)
        {
            case "scene":
            {
                if (FailScenes) return Failure("injected", "scenes are refused");
                // Timing runs switch the check off: the fake's decoding and validating of a big scene would be counted as the module's.
                if (!CheckScenes) { lastScene = argumentsJson; SceneCount++; return "{\"ok\":true}"; }
                try
                {
                    var scene = Doc.Decode(argumentsJson);
                    var issues = SceneValidator.Validate(scene);
                    if (issues.Count > 0) throw new FormatException(string.Join("; ", issues));
                }
                catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    SceneRejects++;
                    Problems.Add("scene rejected: " + exception.Message);
                    return Failure("scene_invalid", exception.Message);
                }
                if (Recording) Scenes.Add(argumentsJson);
                lastScene = argumentsJson;
                SceneCount++;
                return "{\"ok\":true}";
            }
            case "image.put":
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;
                var id = root.GetProperty("id").GetString()!;
                var w = root.GetProperty("w").GetInt32();
                var h = root.GetProperty("h").GetInt32();
                var bytes = Convert.FromBase64String(root.GetProperty("rgba").GetString()!);
                if (bytes.Length != w * h * 4 || w * h * 4 > 4 << 20) return Failure("bad_pixels", "wrong size");
                if (!Images.Contains(id) && Images.Count >= 128) return Failure("too_many_images", "at most 128");
                Images.Add(id);
                return "{\"ok\":true,\"images\":" + Images.Count + "}";
            }
            case "image.drop":
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var existed = Images.Remove(doc.RootElement.GetProperty("id").GetString()!);
                return "{\"ok\":true,\"existed\":" + (existed ? "true" : "false") + "}";
            }
            case "font.put":
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var role = doc.RootElement.GetProperty("role").GetString()!;
                var path = doc.RootElement.GetProperty("path").GetString()!;
                if (Array.IndexOf(SceneValidator.FontRoles, role) < 0 || !Path.IsPathRooted(path) || !(path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                    return Failure("bad_font_path", path);
                if (!File.Exists(path)) return Failure("font_rejected", "no such file");
                Fonts.Add((role, path));
                return "{\"ok\":true,\"fonts\":" + Fonts.Count + "}";
            }
            case "visible":
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                Visible = doc.RootElement.GetProperty("on").GetBoolean();
                if (Visible) Failed = false;
                return "{\"ok\":true,\"visible\":" + (Visible ? "true" : "false") + "}";
            }
            case "background.capture": Captures++; Held++; return "{\"ok\":true,\"pending\":true}";
            case "background.release": Releases++; Held = 0; return "{\"ok\":true}";
            case "measure":
            {
                MeasureCalls++;
                using var doc = JsonDocument.Parse(argumentsJson);
                var text = doc.RootElement.GetProperty("text").GetString()!;
                var size = doc.RootElement.GetProperty("size").GetDouble();
                return "{\"ok\":true,\"w\":" + (text.Length * size * 0.55).ToString("0.###", CultureInfo.InvariantCulture) + "}";
            }
            case "stats":
                return "{\"ok\":true,\"scenes_drawn\":" + SceneCount + ",\"images\":" + Images.Count + ",\"fonts\":" + Fonts.Count
                    + ",\"view_w\":" + ViewW + ",\"view_h\":" + ViewH + ",\"visible\":" + (Visible ? "true" : "false") + ",\"failed\":" + (Failed ? "true" : "false") + ",\"last_error\":\"\"}";
            default: return Failure("unknown_operation", operation);
        }
    }

    private static string Failure(string code, string detail) => "{\"ok\":false,\"code\":" + JsonSerializer.Serialize(code) + ",\"detail\":" + JsonSerializer.Serialize(detail) + "}";
}

// Scene documents as the tests read them: the empty scene may omit its canvas (the draw layer accepts it).
internal static class Doc
{
    public static SceneDocument Decode(string json)
    {
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("w", out _) && root.TryGetProperty("cmds", out var cmds) && cmds.ValueKind == JsonValueKind.Array && cmds.GetArrayLength() == 0)
                return new SceneDocument(1920, 1080, false, Array.Empty<SceneCommand>());
        }
        return SceneDecoder.Decode(json);
    }
}

internal sealed class FakeContent : IModuleContent
{
    public readonly Dictionary<string, ModuleContentResult> Maps = new(StringComparer.OrdinalIgnoreCase);
    public int Locates;
    public bool Throw;

    public bool IsDeclared(ModuleContentInputKind kind) => true;

    public ModuleContentResult LocateRealmapPackage(string mapIdentity)
    {
        Locates++;
        if (Throw) throw new IOException("injected");
        return Maps.TryGetValue(mapIdentity, out var result) ? result : new ModuleContentResult(false, null, null, null);
    }
}

internal sealed class FakeContext : IRuntimeModuleContext
{
    public readonly FakeRuntime RuntimeFake;
    public readonly FakeNative NativeFake;
    public readonly FakeInput InputFake;
    public readonly FakeContent ContentFake = new();
    public readonly List<(string Name, IReadOnlyDictionary<string, string>? Data)> Events = new();
    public readonly List<Thread> Threads = new();
    public readonly CancellationTokenSource Cancel = new();
    public readonly HashSet<string> Capabilities = new(StringComparer.Ordinal) { "ui.pause-menu.replace", "bridge.runtime", "bridge.input" };
    public Dictionary<string, string> SettingsValues = new(StringComparer.Ordinal);
    public bool HasNative = true;
    public bool BridgeRejectsEvents;

    public FakeContext(IEnumerable<string> declaredOperations, string moduleDirectory)
    {
        InputFake = new FakeInput();
        RuntimeFake = new FakeRuntime(declaredOperations, InputFake);
        NativeFake = new FakeNative();
        ModuleDirectory = moduleDirectory;
    }

    public Guid SessionId { get; } = Guid.NewGuid();
    public string ModuleId => "openhud";
    public string ModuleDirectory { get; }
    public IReadOnlyDictionary<string, string> Settings => SettingsValues;
    public string DataDirectory => ModuleDirectory;
    public string ProductVersion => "0.1.0-beta4";
    public string OmsiVersion => "2.3.004";
    public IModuleRuntime Runtime => RuntimeFake;
    public IModuleNativeComponent? NativeComponent => HasNative ? NativeFake : null;
    public IModuleContent Content => ContentFake;
    public IModuleInput Input => InputFake;
    public string? SessionChannelName => null;
    public CancellationToken Stopping => Cancel.Token;
    public bool IsCapabilityAvailable(string capability) => Capabilities.Contains(capability);

    public void Emit(string name, IReadOnlyDictionary<string, string>? data = null)
    {
        if (BridgeRejectsEvents) throw new ArgumentException("event refused");
        lock (Events) Events.Add((name, data));
    }

    public void Track(string kind, IDisposable resource) { }

    public void RunBackground(string name, Action<CancellationToken> work)
    {
        var thread = new Thread(() => { try { work(Cancel.Token); } catch (OperationCanceledException) { } }) { IsBackground = true, Name = name };
        lock (Threads) Threads.Add(thread);
        thread.Start();
    }

    public IModuleAuxiliaryProcess StartAuxiliaryProcess(string componentPath, string arguments) => throw new NotSupportedException();

    public int NoticeCount(string? key = null)
    {
        lock (Events) return Events.Count(e => e.Name == "notice" && (key is null || (e.Data?.GetValueOrDefault("text") ?? "").Contains(key)));
    }

    public IReadOnlyList<string> EventNames()
    {
        lock (Events) return Events.Select(e => e.Name).ToList();
    }

    // Ends the module's background work the way the bridge does at cleanup.
    public void Shutdown()
    {
        Cancel.Cancel();
        List<Thread> copy;
        lock (Threads) copy = Threads.ToList();
        foreach (var thread in copy) thread.Join(TimeSpan.FromSeconds(5));
    }
}
