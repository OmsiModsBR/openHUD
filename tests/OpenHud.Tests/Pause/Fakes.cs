using OpenHud.Pause;

namespace OpenHud.Tests.PauseTests;

// What a fake port does at one call: nothing, a typed error, a thrown exception or "false".
internal enum Fault { None, NotPaused, Busy, AlreadyTakenOver, CapabilityUnresolved, OperationFailed, Other, Exception, False }

// Shared by the fake ports of one rig: counts every call in order (the sweeps inject
// a fault at one ordinal), keeps the call log, checks the thread and collects the
// rule violations the ports observe (for example drawing while a native dialog is up).
internal sealed class Chaos
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private int ordinal;

    public int Target = -1;
    public Fault Fault;
    public readonly List<string> Log = new();
    public readonly List<string> Violations = new();
    public int ThreadViolations;

    public int Calls => ordinal;

    public void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != owner) { ThreadViolations++; Violations.Add("call from another thread"); }
    }

    public Fault Enter(string name)
    {
        CheckThread();
        Log.Add(name);
        return ordinal++ == Target ? Fault : Fault.None;
    }
}

internal sealed class FakeGame : IPauseGame
{
    private readonly Chaos chaos;
    private readonly FakeInput input;

    public FakeGame(Chaos chaos, FakeInput input) { this.chaos = chaos; this.input = input; }

    public bool Paused, NativeVisible, Busy, TakenOver;
    public PauseLastResult LastResult;
    public PauseAction? Pending;
    // True while OMSI is inside its frame: take-over answers busy.
    public bool InFrame;
    // A capability that stopped resolving answers every call with the error.
    public bool Unresolved;
    public int StateCalls, TakeOvers, Restores;
    public readonly List<PauseAction> Actions = new();

    public void UserPauses() { Paused = true; NativeVisible = true; TakenOver = false; Busy = false; Pending = null; LastResult = PauseLastResult.None; }

    // The user closes the native menu with its own button or Esc: the game runs again.
    public void UserResumesNatively()
    {
        if (!Paused || !NativeVisible) return;
        Paused = false; NativeVisible = false; TakenOver = false;
    }

    // The scheduled native action ends with the given outcome.
    public void FinishAction(PauseLastResult result)
    {
        if (Pending is null) return;
        Busy = false;
        Pending = null;
        LastResult = result;
        if (result == PauseLastResult.Resumed || result == PauseLastResult.GameStarted) { Paused = false; NativeVisible = false; TakenOver = false; }
    }

    private PauseGameState Snapshot() => new(Paused, NativeVisible, Busy, LastResult);

    // Applies an injected fault; the game's real state follows what the error says, so the fake never lies.
    private PauseResult? Faulted(Fault fault, bool takeOver = false)
    {
        if (Unresolved) return PauseResult.Failure(PauseErrorCode.CapabilityUnresolved);
        switch (fault)
        {
            case Fault.None: return null;
            case Fault.Exception: throw new InvalidOperationException("injected");
            case Fault.NotPaused:
                Paused = false; NativeVisible = false; TakenOver = false; Busy = false; Pending = null;
                return PauseResult.Failure(PauseErrorCode.NotPaused);
            case Fault.AlreadyTakenOver:
                // Only a take-over can find the menu already taken (the game really is in that state then).
                if (takeOver && Paused) { TakenOver = true; NativeVisible = false; }
                return PauseResult.Failure(PauseErrorCode.AlreadyTakenOver);
            case Fault.CapabilityUnresolved:
                Unresolved = true;
                return PauseResult.Failure(PauseErrorCode.CapabilityUnresolved);
            case Fault.Busy: return PauseResult.Failure(PauseErrorCode.Busy);
            case Fault.OperationFailed: return PauseResult.Failure(PauseErrorCode.OperationFailed, "injected");
            default: return PauseResult.Failure(PauseErrorCode.Other, "injected");
        }
    }

    public PauseResult State()
    {
        StateCalls++;
        if (Faulted(chaos.Enter("state")) is { } fault) return fault;
        return PauseResult.Success(Snapshot());
    }

    public PauseResult TakeOver()
    {
        TakeOvers++;
        if (Faulted(chaos.Enter("takeover"), takeOver: true) is { } fault) return fault;
        if (!Paused || !NativeVisible && !TakenOver) return PauseResult.Failure(PauseErrorCode.NotPaused);
        if (Busy || InFrame) return PauseResult.Failure(PauseErrorCode.Busy);
        if (TakenOver) return PauseResult.Failure(PauseErrorCode.AlreadyTakenOver);
        TakenOver = true;
        NativeVisible = false;
        return PauseResult.Success(Snapshot());
    }

    public PauseResult Action(PauseAction action)
    {
        Actions.Add(action);
        if (Faulted(chaos.Enter("action:" + action)) is { } fault) return fault;
        // The native action can open a dialog that needs real input: the capture must already be off.
        if (input.On) chaos.Violations.Add("action " + action + " while the input capture is on");
        if (!Paused) return PauseResult.Failure(PauseErrorCode.NotPaused);
        if (Busy) return PauseResult.Failure(PauseErrorCode.Busy);
        Busy = true;
        Pending = action;
        LastResult = PauseLastResult.None;
        return PauseResult.Success(Snapshot());
    }

    public PauseResult RestoreNative()
    {
        Restores++;
        if (Faulted(chaos.Enter("restore")) is { } fault) return fault;
        if (Busy) return PauseResult.Failure(PauseErrorCode.Busy);
        // FormShow of the native menu pauses the game again: restoring on a running game is a bug.
        if (!Paused) chaos.Violations.Add("restore-native on a game that is not paused");
        TakenOver = false;
        NativeVisible = true;
        Paused = true;
        return PauseResult.Success(Snapshot());
    }
}

internal sealed class FakeInput : IPauseInput
{
    private readonly Chaos chaos;
    private readonly Queue<PauseInputEvent> queue = new();

    public FakeInput(Chaos chaos) { this.chaos = chaos; }

    public bool On;
    public int EnableCalls, DisableCalls;

    // Events only exist while the capture is on (the bridge hands over nothing otherwise).
    public void Queue(params PauseInputEvent[] events)
    {
        if (!On) return;
        foreach (var e in events) queue.Enqueue(e);
    }

    public bool Capture(bool enabled)
    {
        var fault = chaos.Enter(enabled ? "capture:on" : "capture:off");
        if (fault == Fault.Exception) throw new InvalidOperationException("injected");
        if (fault != Fault.None) return false;
        if (enabled) { if (!On) { EnableCalls++; queue.Clear(); } }
        else if (On) DisableCalls++;
        On = enabled;
        return true;
    }

    public IReadOnlyList<PauseInputEvent> Drain()
    {
        var fault = chaos.Enter("drain");
        if (fault == Fault.Exception) throw new InvalidOperationException("injected");
        if (!On) { queue.Clear(); return Array.Empty<PauseInputEvent>(); }
        var batch = queue.ToArray();
        queue.Clear();
        return batch;
    }
}

internal sealed class FakeBackground : IPauseBackground
{
    private readonly Chaos chaos;
    public FakeBackground(Chaos chaos) { this.chaos = chaos; }

    public int Held, Captures, Releases;

    public bool Capture()
    {
        var fault = chaos.Enter("bg:capture");
        if (fault == Fault.Exception) throw new InvalidOperationException("injected");
        if (fault != Fault.None) return false;
        Held++;
        Captures++;
        if (Held > 1) chaos.Violations.Add("background captured twice");
        return true;
    }

    public void Release()
    {
        var fault = chaos.Enter("bg:release");
        if (fault != Fault.None) throw new InvalidOperationException("injected");
        if (Held == 0) chaos.Violations.Add("background released without being held");
        else Held--;
        Releases++;
    }
}

internal sealed class FakePublisher : IPauseScenePublisher
{
    private readonly Chaos chaos;
    private readonly FakeGame game;
    public FakePublisher(Chaos chaos, FakeGame game) { this.chaos = chaos; this.game = game; }

    public readonly List<MenuScene> Scenes = new();
    public MenuScene? Last => Scenes.Count == 0 ? null : Scenes[^1];

    public void Publish(MenuScene scene)
    {
        chaos.CheckThread();
        // A native modal dialog is in front: nothing of ours may be drawn.
        if (scene.Mode == MenuSceneMode.Menu && game.Busy) chaos.Violations.Add("menu published while busy");
        Scenes.Add(scene);
    }
}

internal sealed class FakeClock : IPauseClock
{
    public long Now;
    public long NowMilliseconds => Now;
}

internal static class Ev
{
    public static PauseInputEvent KeyDown(int vk, int mods = 0) => new(PauseInputKind.KeyDown, vk, 0, 0, PauseMouseButton.None, 0, mods);
    public static PauseInputEvent KeyUp(int vk) => new(PauseInputKind.KeyUp, vk, 0, 0, PauseMouseButton.None, 0, 0);
    public static PauseInputEvent Move(int x, int y) => new(PauseInputKind.MouseMove, 0, x, y, PauseMouseButton.None, 0, 0);
    public static PauseInputEvent Down(int x, int y, PauseMouseButton b = PauseMouseButton.Left) => new(PauseInputKind.ButtonDown, 0, x, y, b, 0, 0);
    public static PauseInputEvent Up(int x, int y, PauseMouseButton b = PauseMouseButton.Left) => new(PauseInputKind.ButtonUp, 0, x, y, b, 0, 0);
    public static PauseInputEvent Double(int x, int y) => new(PauseInputKind.DoubleClick, 0, x, y, PauseMouseButton.Left, 0, 0);
    public static PauseInputEvent Wheel(int x, int y, int delta) => new(PauseInputKind.Wheel, 0, x, y, PauseMouseButton.None, delta, 0);

    public const int Esc = 0x1B, Enter = 0x0D, Tab = 0x09, Up_ = 0x26, Down_ = 0x28, Left_ = 0x25, Right_ = 0x27, Home = 0x24, End = 0x23;

    // The view point (client pixels) of a point given in design units of the menu block.
    public static (int X, int Y) View(MenuGeometry g, double designX, double designY)
    {
        var (cx, cy) = g.ToCanvasPoint(designX, designY);
        var m = g.Metrics;
        return ((int)Math.Round(cx * m.ViewFactor + m.ViewOffsetX), (int)Math.Round(cy * m.ViewFactor + m.ViewOffsetY));
    }

    public static (int X, int Y) Center(MenuGeometry g, MenuRect r) => View(g, r.X + r.W / 2, r.Y + r.H / 2);
}

// The machine with its fake ports, a scripted clock and helpers to drive it.
internal sealed class Rig
{
    public readonly Chaos Chaos = new();
    public readonly FakeClock Clock = new();
    public readonly FakeInput Input;
    public readonly FakeGame Game;
    public readonly FakeBackground Background;
    public readonly FakePublisher Publisher;
    public readonly PauseMenu Menu;
    public readonly PauseMachine Machine;

    public Rig(PauseOptions? options = null, Func<string, string>? lookup = null, PauseIdentity? identity = null)
    {
        Input = new FakeInput(Chaos);
        Game = new FakeGame(Chaos, Input);
        Background = new FakeBackground(Chaos);
        Publisher = new FakePublisher(Chaos, Game);
        Menu = new PauseMenu(lookup, identity);
        Machine = new PauseMachine(Game, Input, Background, Publisher, Clock, Menu, options);
    }

    public PauseState State => Machine.State;

    public void Tick(int times = 1, int advanceMs = 50)
    {
        for (int i = 0; i < times; i++) { Clock.Now += advanceMs; Machine.Tick(); }
    }

    public void Press(int vk) { Input.Queue(Ev.KeyDown(vk)); Tick(); Input.Queue(Ev.KeyUp(vk)); Tick(); }

    // Opens the menu: the user pauses, the machine takes over.
    public void Open()
    {
        Game.UserPauses();
        Tick();
        if (State != PauseState.Open) throw new InvalidOperationException("expected Open, got " + State);
    }

    // Focuses an item with the keyboard and activates it (the keyboard events are consumed by Tick).
    public void Choose(PauseAction action)
    {
        int target = (int)action;
        for (int i = Menu.FocusIndex; i != target; i = (i + 1) % MenuGeometry.ItemCount) Input.Queue(Ev.KeyDown(Ev.Down_), Ev.KeyUp(Ev.Down_));
        Input.Queue(Ev.KeyDown(Ev.Enter), Ev.KeyUp(Ev.Enter));
        Tick();
    }

    // The rules every run must keep, whatever was injected.
    public void CheckInvariants(string context, bool nativeMayStayHidden = false)
    {
        Gates.Check(Chaos.Violations.Count == 0, context + ": violations: " + string.Join("; ", Chaos.Violations));
        Gates.Check(!Input.On, context + ": the input capture is still on");
        Gates.Check(Background.Held == 0, context + ": the background is still held");
        // A capability that no longer resolves cannot take the native menu back; the rest must still be let go.
        if (!nativeMayStayHidden) Gates.Check(Machine.CleanupComplete, context + ": cleanup incomplete");
        Gates.Check(Input.EnableCalls == Input.DisableCalls, context + ": capture on/off unbalanced " + Input.EnableCalls + "/" + Input.DisableCalls);
        Gates.Check(Background.Captures == Background.Releases, context + ": background unbalanced " + Background.Captures + "/" + Background.Releases);
        if (!nativeMayStayHidden)
            Gates.Check(!(Game.Paused && !Game.NativeVisible), context + ": the game is paused with the native menu hidden");
    }
}
