namespace OpenHud.Pause;

// The pause menu's state machine. It drives OMSI's native pause menu through
// small ports and shows the module's own menu instead:
//
//   RUNNING --paused && native menu visible--> TAKE_OVER_PENDING --take-over ok--> OPEN
//   OPEN --Esc / Resume--> RESUMING --keys released, action(resume), resumed--> RUNNING
//   OPEN --New game / Save / Options / Quit--> MODAL (capture off, action, wait for busy=false)
//        --returned and still paused--> OPEN again (capture retaken)
//        --game-started / resumed / pause ended--> RUNNING;  quit-requested--> ENDING
//   any failure of a ui.pause.* call --> PROVISIONAL (native menu visible, only the map behind)
//   Stop / failure / degradation from any state --> everything given back (idempotent)
//
// Rules: nothing is drawn while a native modal dialog is in front (busy); the
// capture is released before every action that can open a native dialog; the
// native menu is only restored while the game is still paused (restoring it
// would pause a running game). Single UI thread: every call must come from the
// thread of the first call, anything else throws.
public sealed class PauseMachine
{
    private readonly IPauseGame game;
    private readonly IPauseInput input;
    private readonly IPauseBackground background;
    private readonly IPauseScenePublisher publisher;
    private readonly IPauseClock clock;
    private readonly PauseMenu menu;
    private readonly PauseOptions options;

    private readonly HashSet<int> keysDown = new();
    private int ownerThread = -1;

    private bool captured;          // we hold the input capture
    private bool backgroundHeld;    // we hold the background snapshot
    private bool takenOver;         // the native menu is hidden by us
    private bool lastPaused = true; // last known "paused" (false: restoring native would pause a running game)
    private bool unavailable;       // capability unresolved: stop polling

    private const int CleanupRetryMs = 250;
    private long nextCleanupAt;
    private long pendingSince;
    private long resumeSince;
    private bool resumeActionSent;
    private long publishedRevision = -1;
    private MenuSceneMode publishedMode = MenuSceneMode.None;

    public PauseMachine(IPauseGame game, IPauseInput input, IPauseBackground background, IPauseScenePublisher publisher,
        IPauseClock clock, PauseMenu menu, PauseOptions? options = null)
    {
        this.game = game ?? throw new ArgumentNullException(nameof(game));
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.background = background ?? throw new ArgumentNullException(nameof(background));
        this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.menu = menu ?? throw new ArgumentNullException(nameof(menu));
        this.options = options ?? new PauseOptions();
    }

    public PauseState State { get; private set; } = PauseState.Running;
    // The last failure that was handled (a typed error of a ui.pause.* call, or a port that failed).
    public PauseError? LastError { get; private set; }
    // The key-release wait ended by its timeout instead of by the release (a stuck key).
    public int KeyReleaseTimeouts { get; private set; }
    // True after a capability-unresolved answer: the machine no longer asks the game anything.
    public bool Unavailable => unavailable;
    // True when nothing is held any more: capture off, background released, native menu given back.
    public bool CleanupComplete => !captured && !backgroundHeld && !takenOver;

    public PauseMenu Menu => menu;

    // One step; call it at the module's tick, on the UI thread, never inside OMSI's frame.
    public void Tick()
    {
        Guard();
        switch (State)
        {
            case PauseState.Running: StepRunning(); break;
            case PauseState.TakeOverPending: StepPending(); break;
            case PauseState.Open: StepOpen(); break;
            case PauseState.Resuming: StepResuming(); break;
            case PauseState.Modal: StepModal(); break;
            case PauseState.Provisional: StepProvisional(); break;
        }
    }

    // Gives everything back and ends the machine. Safe to call again: it only repeats
    // the pieces that could not be done (for example a restore refused while a native
    // dialog was in front); once CleanupComplete it makes no port call.
    public void Stop()
    {
        Guard();
        bool ending = State == PauseState.Ending;
        State = PauseState.Stopped;
        Cleanup(restoreNative: !ending && takenOver);
        if (ending) takenOver = false;
        Publish(MenuScene.None);
    }

    // ---- states

    private void StepRunning()
    {
        RetryCleanup();
        if (unavailable) return;
        var result = CallResult(game.State);
        if (!result.IsOk)
        {
            LastError = result.Error;
            if (result.Error.Code == PauseErrorCode.CapabilityUnresolved) unavailable = true;
            return;
        }
        var state = result.State;
        lastPaused = state.Paused;
        if (!state.Paused || !state.NativeMenuVisible || state.Busy) return;
        pendingSince = clock.NowMilliseconds;
        State = PauseState.TakeOverPending;
        StepPending();
    }

    private void StepPending()
    {
        var result = CallResult(game.TakeOver);
        if (!result.IsOk)
        {
            var error = result.Error;
            if (error.Code == PauseErrorCode.Busy && clock.NowMilliseconds - pendingSince < options.TakeOverRetryMs) return;
            if (error.Code == PauseErrorCode.NotPaused) { LastError = error; lastPaused = false; State = PauseState.Running; return; }
            // The game may hold a take-over we do not know of: hand it back with the failure cleanup.
            if (error.Code == PauseErrorCode.AlreadyTakenOver) takenOver = true;
            Fail(error);
            return;
        }
        takenOver = true;
        lastPaused = result.State.Paused;
        // A snapshot a failed cleanup could not release must go before a new one is taken.
        ReleaseBackground();
        if (backgroundHeld) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the previous background snapshot could not be released")); return; }
        if (!CallBool(background.Capture)) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the background snapshot failed")); return; }
        backgroundHeld = true;
        if (!CallBool(() => input.Capture(true))) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the input capture could not start")); return; }
        captured = true;
        keysDown.Clear();
        menu.Reset();
        State = PauseState.Open;
        PublishMenu(force: true);
    }

    private void StepOpen()
    {
        var result = CallResult(game.State);
        if (!result.IsOk) { Fail(result.Error); return; }
        var state = result.State;
        lastPaused = state.Paused;
        if (!state.Paused)
        {
            // Somebody else resumed the game: nothing of ours to restore (restoring would pause it again).
            Cleanup(restoreNative: false);
            takenOver = false;
            Enter(PauseState.Running);
            return;
        }
        if (state.Busy)
        {
            // A native dialog is in front that we did not ask for: let go of the keyboard and wait.
            if (!ReleaseCapture()) return;
            Publish(MenuScene.None);
            Enter(PauseState.Modal);
            return;
        }

        var batch = CallDrain(input.Drain);
        if (batch is null) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the input could not be read")); return; }
        // Key state is tracked over the whole batch even after an action was chosen.
        foreach (var e in batch) TrackKey(e);
        foreach (var e in batch)
        {
            foreach (var menuEvent in menu.Handle(e))
            {
                if (menuEvent is ActionRequested requested) { Act(requested.Action); return; }
            }
        }
        PublishMenu(force: false);
    }

    private void Act(PauseAction action)
    {
        if (action == PauseAction.Resume)
        {
            resumeSince = clock.NowMilliseconds;
            resumeActionSent = false;
            State = PauseState.Resuming;
            StepResuming();
            return;
        }
        // New game, save, options and quit can open a native modal dialog that needs real input.
        if (!ReleaseCapture()) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the input capture could not end")); return; }
        Publish(MenuScene.None);
        var result = CallResult(() => game.Action(action));
        if (!result.IsOk) { Fail(result.Error); return; }
        lastPaused = result.State.Paused;
        Enter(PauseState.Modal);
    }

    private void StepResuming()
    {
        if (!resumeActionSent)
        {
            var batch = CallDrain(input.Drain);
            if (batch is null) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the input could not be read")); return; }
            foreach (var e in batch) TrackKey(e);
            // The game polls DirectInput for the keys too: a key still down at the resume would fire as a new press.
            if (keysDown.Count > 0)
            {
                if (clock.NowMilliseconds - resumeSince < options.KeyReleaseTimeoutMs) return;
                KeyReleaseTimeouts++;
            }
            if (!ReleaseCapture()) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the input capture could not end")); return; }
            Publish(MenuScene.None);
            var result = CallResult(() => game.Action(PauseAction.Resume));
            if (!result.IsOk) { Fail(result.Error); return; }
            lastPaused = result.State.Paused;
            resumeActionSent = true;
            return;
        }
        Evaluate();
    }

    private void StepModal() => Evaluate();

    // The common wait after a scheduled native action (resume included): poll until busy ends, then decide.
    private void Evaluate()
    {
        var result = CallResult(game.State);
        if (!result.IsOk) { Fail(result.Error); return; }
        var state = result.State;
        lastPaused = state.Paused;
        if (state.Busy) return;

        if (state.LastResult == PauseLastResult.QuitRequested)
        {
            // OMSI is closing: nothing more to do, only our own snapshot to let go.
            ReleaseBackground();
            takenOver = false;
            Publish(MenuScene.None);
            State = PauseState.Ending;
            return;
        }
        // Resumed and game-started both end with the pause byte cleared; a stale result with the game
        // still paused must not close the menu, or the user would be left with no menu at all.
        if (!state.Paused)
        {
            Cleanup(restoreNative: false);
            takenOver = false;
            Enter(PauseState.Running);
            return;
        }
        // Returned (or nothing happened) and still paused: take the keyboard back and show the menu again.
        if (!CallBool(() => input.Capture(true))) { Fail(new PauseError(PauseErrorCode.OperationFailed, "the input capture could not start")); return; }
        captured = true;
        keysDown.Clear();
        menu.ClearTransient();
        State = PauseState.Open;
        PublishMenu(force: true);
    }

    private void StepProvisional()
    {
        RetryCleanup();
        if (unavailable) return;
        var result = CallResult(game.State);
        if (!result.IsOk) { LastError = result.Error; return; }
        var state = result.State;
        lastPaused = state.Paused;
        if (!state.Paused)
        {
            // The user resumed with the native menu: the next pause starts over.
            Cleanup(restoreNative: false);
            takenOver = false;
            Enter(PauseState.Running);
            return;
        }
        // Never draw while a native dialog is in front.
        Publish(state.Busy ? MenuScene.None : MenuScene.MapBehind);
    }

    // ---- failure and cleanup

    private void Fail(PauseError error)
    {
        LastError = error;
        if (error.Code == PauseErrorCode.CapabilityUnresolved) unavailable = true;
        // Not paused any more means nothing to give back to the native menu (it would pause the game).
        if (error.Code == PauseErrorCode.NotPaused) lastPaused = false;
        Cleanup(restoreNative: takenOver && lastPaused);
        if (!lastPaused)
        {
            takenOver = false;
            Enter(PauseState.Running);
            return;
        }
        State = PauseState.Provisional;
        Publish(MenuScene.MapBehind);
    }

    // Idempotent and failure tolerant: each piece is retried until it is done.
    private void Cleanup(bool restoreNative)
    {
        ReleaseCapture();
        Publish(MenuScene.None);
        if (restoreNative && takenOver)
        {
            // The state may have changed since it was last read (a resume that was scheduled, a dialog):
            // restoring the native menu on a running game would pause it again, and a busy game refuses.
            bool attempt = true;
            var probe = CallResult(game.State);
            if (probe.IsOk)
            {
                lastPaused = probe.State.Paused;
                attempt = lastPaused && !probe.State.Busy;
                if (!lastPaused) takenOver = false;
            }
            else if (probe.Error.Code == PauseErrorCode.NotPaused)
            {
                lastPaused = false;
                takenOver = false;
                attempt = false;
            }
            if (attempt)
            {
                var result = CallResult(game.RestoreNative);
                if (result.IsOk) takenOver = false;
                else LastError = result.Error;
            }
        }
        ReleaseBackground();
    }

    // Pieces a failed cleanup left behind are retried at a pace, not on every tick.
    private void RetryCleanup()
    {
        if (CleanupComplete || clock.NowMilliseconds < nextCleanupAt) return;
        // An unresolved capability answers the restore with the same error every time: no use asking again.
        Cleanup(restoreNative: takenOver && lastPaused && !unavailable);
        if (!CleanupComplete) nextCleanupAt = clock.NowMilliseconds + CleanupRetryMs;
    }

    private bool ReleaseCapture()
    {
        if (!captured) return true;
        if (!CallBool(() => input.Capture(false)))
        {
            LastError = new PauseError(PauseErrorCode.Other, "the input capture could not end");
            return false;
        }
        captured = false;
        return true;
    }

    private void ReleaseBackground()
    {
        if (!backgroundHeld) return;
        if (CallBool(() => { background.Release(); return true; })) backgroundHeld = false;
        else LastError = new PauseError(PauseErrorCode.Other, "the background snapshot could not be released");
    }

    private void Enter(PauseState state)
    {
        State = state;
        if (state == PauseState.Running) { keysDown.Clear(); resumeActionSent = false; }
    }

    // ---- helpers

    private void TrackKey(PauseInputEvent e)
    {
        if (e.Kind == PauseInputKind.KeyDown) keysDown.Add(e.Key);
        else if (e.Kind == PauseInputKind.KeyUp) keysDown.Remove(e.Key);
    }

    private void PublishMenu(bool force)
    {
        if (!force && publishedMode == MenuSceneMode.Menu && publishedRevision == menu.Revision) return;
        publishedRevision = menu.Revision;
        MenuScene scene;
        try { scene = menu.BuildScene(); }
        catch (Exception exception) { LastError = new PauseError(PauseErrorCode.Other, "scene: " + exception.Message); return; }
        Publish(scene);
    }

    // Publishing is skipped when the mode repeats, except for a menu scene (its content changes).
    private void Publish(MenuScene scene)
    {
        if (scene.Mode != MenuSceneMode.Menu && publishedMode == scene.Mode) return;
        try { publisher.Publish(scene); publishedMode = scene.Mode; }
        catch (Exception exception) { LastError = new PauseError(PauseErrorCode.Other, "publish: " + exception.Message); }
    }

    // A port that throws becomes a failed call.
    private PauseResult CallResult(Func<PauseResult> call)
    {
        try { return call(); }
        catch (Exception exception)
        {
            return PauseResult.Failure(PauseErrorCode.Other, exception.GetType().Name + ": " + exception.Message);
        }
    }

    private bool CallBool(Func<bool> call)
    {
        try { return call(); }
        catch (Exception) { return false; }
    }

    private IReadOnlyList<PauseInputEvent>? CallDrain(Func<IReadOnlyList<PauseInputEvent>> call)
    {
        try { return call(); }
        catch (Exception) { return null; }
    }

    private void Guard()
    {
        int current = Environment.CurrentManagedThreadId;
        if (ownerThread == -1) ownerThread = current;
        else if (ownerThread != current)
            throw new InvalidOperationException("the pause machine belongs to one UI thread") { Source = nameof(PauseMachine) };
    }
}
