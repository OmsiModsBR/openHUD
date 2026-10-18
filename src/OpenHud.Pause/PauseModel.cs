namespace OpenHud.Pause;

// Plain types and ports of the pause menu. Nothing here knows the game, the
// bridge or the other openHUD projects: the integrator adapts its own types to
// these ports, which is also what lets the state machine run against fakes.

public enum PauseState { Running, TakeOverPending, Open, Resuming, Modal, Ending, Provisional, Stopped }

public enum PauseAction { Resume, NewGame, Save, Options, Quit }

// ui.pause.state last_result (the "exception" and "refused" outcomes of the
// catalog are reported by the adapter as an OperationFailed error instead).
public enum PauseLastResult { None, Resumed, Returned, GameStarted, QuitRequested }

public enum PauseErrorCode { NotPaused, Busy, AlreadyTakenOver, CapabilityUnresolved, OperationFailed, Other }

public readonly record struct PauseError(PauseErrorCode Code, string Detail)
{
    public const string NotPausedText = "OL_E_UI_NOT_PAUSED";
    public const string BusyText = "OL_E_UI_BUSY";
    public const string AlreadyTakenOverText = "OL_E_UI_ALREADY_TAKEN_OVER";
    public const string CapabilityUnresolvedText = "OL_E_CAPABILITY_UNRESOLVED";
    public const string OperationFailedText = "OL_E_RUNTIME_OPERATION_FAILED";

    // The typed catalog code of the error (the raw text for Other is in Detail).
    public string CatalogCode => Code switch
    {
        PauseErrorCode.NotPaused => NotPausedText,
        PauseErrorCode.Busy => BusyText,
        PauseErrorCode.AlreadyTakenOver => AlreadyTakenOverText,
        PauseErrorCode.CapabilityUnresolved => CapabilityUnresolvedText,
        PauseErrorCode.OperationFailed => OperationFailedText,
        _ => "OTHER",
    };

    // Maps an OL_E_* code of a failed catalog call; anything unknown is Other.
    public static PauseError FromCatalog(string? code, string? detail) => code switch
    {
        NotPausedText => new(PauseErrorCode.NotPaused, detail ?? ""),
        BusyText => new(PauseErrorCode.Busy, detail ?? ""),
        AlreadyTakenOverText => new(PauseErrorCode.AlreadyTakenOver, detail ?? ""),
        CapabilityUnresolvedText => new(PauseErrorCode.CapabilityUnresolved, detail ?? ""),
        OperationFailedText => new(PauseErrorCode.OperationFailed, detail ?? ""),
        _ => new(PauseErrorCode.Other, (code ?? "") + (string.IsNullOrEmpty(detail) ? "" : ": " + detail)),
    };
}

// The ui.pause.state keys the machine needs.
public readonly record struct PauseGameState(bool Paused, bool NativeMenuVisible, bool Busy, PauseLastResult LastResult);

// Result of a ui.pause.* call: the state after it, or a typed error.
public readonly struct PauseResult
{
    private readonly PauseGameState state;
    private readonly PauseError error;
    private readonly bool ok;

    private PauseResult(bool ok, PauseGameState state, PauseError error) { this.ok = ok; this.state = state; this.error = error; }

    public static PauseResult Success(PauseGameState state) => new(true, state, default);
    public static PauseResult Failure(PauseErrorCode code, string detail = "") => new(false, default, new PauseError(code, detail));
    public static PauseResult Failure(PauseError error) => new(false, default, error);

    public bool IsOk => ok;
    public PauseGameState State => ok ? state : throw new InvalidOperationException("the call failed: " + error.CatalogCode);
    public PauseError Error => ok ? throw new InvalidOperationException("the call succeeded") : error;
}

// The game side: the four catalog operations ui.pause.state, take-over, action
// and restore-native. Never throws for a failed operation (returns the typed
// error); the machine still guards against a port that throws.
public interface IPauseGame
{
    PauseResult State();
    PauseResult TakeOver();
    // Schedules the native action; its outcome appears in State() (busy, then last_result).
    PauseResult Action(PauseAction action);
    PauseResult RestoreNative();
}

public enum PauseInputKind : byte { KeyDown = 1, KeyUp = 2, Char = 3, MouseMove = 4, ButtonDown = 5, ButtonUp = 6, Wheel = 7, DoubleClick = 8 }

public enum PauseMouseButton : byte { None = 0, Left = 1, Right = 2, Middle = 3 }

// Same fields and values as the bridge's captured input event; keyboard events
// carry the virtual key, mouse coordinates are client coordinates of the game's
// view, Delta is the wheel's notch count times 120, Modifiers: 1 Shift, 2 Ctrl, 4 Alt.
public readonly record struct PauseInputEvent(PauseInputKind Kind, int Key, int X, int Y, PauseMouseButton Button, int Delta, int Modifiers);

public interface IPauseInput
{
    // False when the capture cannot start (or end).
    bool Capture(bool enabled);
    IReadOnlyList<PauseInputEvent> Drain();
}

public interface IPauseBackground
{
    // Snapshot of the current frame as the menu background; false when it failed.
    bool Capture();
    void Release();
}

public interface IPauseScenePublisher
{
    // The scene to draw from now on; a scene of mode None means "draw nothing".
    void Publish(MenuScene scene);
}

public interface IPauseClock
{
    // Monotonic milliseconds.
    long NowMilliseconds { get; }
}

public sealed record PauseOptions
{
    // How long the machine waits for the keys to be released before resuming anyway.
    public int KeyReleaseTimeoutMs { get; init; } = 750;
    // How long a take-over answered with "busy" (inside the frame) is retried.
    public int TakeOverRetryMs { get; init; } = 1000;
}
