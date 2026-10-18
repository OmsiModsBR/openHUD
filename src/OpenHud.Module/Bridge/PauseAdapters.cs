using OmsiLaunch.Runtime.Modules;
using OpenHud.Pause;

namespace OpenHud.Bridge;

// IPauseGame over the four ui.pause.* catalog operations. The typed errors of the catalog keep
// their meaning (the machine decides by them); an exception of the bridge is a failed call.
internal sealed class PauseGameAdapter : IPauseGame
{
    private readonly IModuleRuntime runtime;

    public PauseGameAdapter(IModuleRuntime runtime) => this.runtime = runtime;

    public PauseResult State() => Run("ui.pause.state", null);
    public PauseResult TakeOver() => Run("ui.pause.take-over", null);
    public PauseResult RestoreNative() => Run("ui.pause.restore-native", null);

    public PauseResult Action(PauseAction action) =>
        Run("ui.pause.action", new Dictionary<string, string>(StringComparer.Ordinal) { ["action"] = ActionName(action) });

    internal static string ActionName(PauseAction action) => action switch
    {
        PauseAction.Resume => "resume",
        PauseAction.NewGame => "new-game",
        PauseAction.Save => "save",
        PauseAction.Options => "options",
        _ => "quit",
    };

    private PauseResult Run(string operation, IReadOnlyDictionary<string, string>? arguments)
    {
        ModuleCallResult result;
        try { result = runtime.Execute(operation, arguments); }
        catch (Exception exception) { return PauseResult.Failure(PauseErrorCode.Other, exception.GetType().Name); }
        if (result is null) return PauseResult.Failure(PauseErrorCode.OperationFailed, "no answer");
        var values = result.Values ?? new Dictionary<string, string>();
        if (!result.Succeeded)
        {
            values.TryGetValue("detail", out var detail);
            if (detail is null) values.TryGetValue("message", out detail);
            return PauseResult.Failure(PauseError.FromCatalog(result.ErrorCode, detail));
        }
        return ParseState(values);
    }

    // paused, native_menu_visible, busy and last_result; anything unreadable is a failed call, not a guess.
    internal static PauseResult ParseState(IReadOnlyDictionary<string, string> values)
    {
        if (!TryBool(values, "paused", out var paused) || !TryBool(values, "native_menu_visible", out var visible) || !TryBool(values, "busy", out var busy))
            return PauseResult.Failure(PauseErrorCode.OperationFailed, "ui.pause.state answered without paused, native_menu_visible or busy");
        var last = PauseLastResult.None;
        if (values.TryGetValue("last_result", out var text) && text is not null)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "none": case "": break;
                case "resumed": last = PauseLastResult.Resumed; break;
                case "game-started": last = PauseLastResult.GameStarted; break;
                case "returned": last = PauseLastResult.Returned; break;
                case "quit-requested": last = PauseLastResult.QuitRequested; break;
                // The native handler failed or refused: the machine treats it like a failed call.
                case "exception": case "refused": return PauseResult.Failure(PauseErrorCode.OperationFailed, "last_result=" + text.Trim());
                default: return PauseResult.Failure(PauseErrorCode.OperationFailed, "unknown last_result");
            }
        }
        return PauseResult.Success(new PauseGameState(paused, visible, busy, last));
    }

    private static bool TryBool(IReadOnlyDictionary<string, string> values, string key, out bool value)
    {
        value = false;
        if (!values.TryGetValue(key, out var text) || text is null) return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "true": case "1": value = true; return true;
            case "false": case "0": return true;
            default: return false;
        }
    }
}

// IPauseInput over IModuleInput. The event kinds and values are the same on both sides.
internal sealed class PauseInputAdapter : IPauseInput
{
    private readonly IModuleInput input;

    public PauseInputAdapter(IModuleInput input) => this.input = input;

    public bool Capture(bool enabled)
    {
        // Giving the input back is always asked, even when starting it was never possible.
        if (enabled && !input.IsAvailable) return false;
        return input.Capture(enabled);
    }

    public IReadOnlyList<PauseInputEvent> Drain()
    {
        var events = input.Drain();
        if (events is null || events.Count == 0) return Array.Empty<PauseInputEvent>();
        var list = new PauseInputEvent[events.Count];
        for (var i = 0; i < list.Length; i++)
        {
            var e = events[i];
            list[i] = new PauseInputEvent((PauseInputKind)(byte)e.Kind, e.Key, e.X, e.Y, (PauseMouseButton)(byte)e.Button, e.Delta, e.Modifiers);
        }
        return list;
    }
}

// IPauseBackground over the native background.capture / background.release operations.
internal sealed class PauseBackgroundAdapter : IPauseBackground
{
    private readonly NativeLink native;

    public PauseBackgroundAdapter(NativeLink native) => this.native = native;

    public bool Capture() => native.Call("background.capture", "{}").Ok;

    // The machine retries a release that threw.
    public void Release()
    {
        var answer = native.Call("background.release", "{}");
        if (!answer.Ok) throw new InvalidOperationException("background.release: " + NativeLink.Describe(answer));
    }
}

// IPauseClock on the monotonic clock; tests inject their own.
internal sealed class StopwatchClock : IPauseClock
{
    private readonly long start = System.Diagnostics.Stopwatch.GetTimestamp();

    public long NowMilliseconds => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000 / System.Diagnostics.Stopwatch.Frequency;
}

// The scene the pause machine wants shown. The module composes the final scene from it (the map region
// is filled with the full map) at its own tick, so publishing here only records.
internal sealed class PauseScenePublisher : IPauseScenePublisher
{
    public MenuScene Current { get; private set; } = MenuScene.None;
    public long Version { get; private set; }

    public void Publish(MenuScene scene)
    {
        Current = scene ?? MenuScene.None;
        Version++;
    }
}
