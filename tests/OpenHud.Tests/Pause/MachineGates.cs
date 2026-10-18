using OpenHud.Pause;

namespace OpenHud.Tests.PauseTests;

// Gates of the state machine against the fake ports and the scripted clock.
internal static class MachineGates
{
    public static void Register()
    {
        Gates.Run("pause.machine.running-idle", RunningIdle);
        Gates.Run("pause.machine.open-and-resume", OpenAndResume);
        Gates.Run("pause.machine.key-release-wait", KeyReleaseWait);
        Gates.Run("pause.machine.key-release-timeout-stuck-key", StuckKey);
        Gates.Run("pause.machine.modal-actions-roundtrip", ModalActions);
        Gates.Run("pause.machine.modal-outcomes", ModalOutcomes);
        Gates.Run("pause.machine.quit", Quit);
        Gates.Run("pause.machine.busy-never-draws", BusyNeverDraws);
        Gates.Run("pause.machine.external-resume", ExternalResume);
        Gates.Run("pause.machine.take-over-retry-in-frame", TakeOverRetry);
        Gates.Run("pause.machine.provisional", Provisional);
        Gates.Run("pause.machine.capability-unresolved-latches", UnresolvedLatches);
        Gates.Run("pause.machine.failure-sweep-every-call-every-error", FailureSweep);
        Gates.Run("pause.machine.persistent-restore-failure", PersistentRestoreFailure);
        Gates.Run("pause.machine.stop-from-every-state", StopEveryState);
        Gates.Run("pause.machine.stop-idempotent", StopIdempotent);
        Gates.Run("pause.machine.cycles-1000", Cycles);
        Gates.Run("pause.machine.single-thread", SingleThread);
        Gates.Run("pause.machine.port-exceptions", PortExceptions);
        Gates.Run("pause.machine.error-catalog-mapping", ErrorMapping);
    }

    private static void RunningIdle()
    {
        var r = new Rig();
        r.Tick(5);
        Gates.Check(r.State == PauseState.Running, "running while the game runs");
        Gates.Check(r.Game.TakeOvers == 0 && r.Game.Actions.Count == 0, "no take-over while not paused");
        Gates.Check(r.Publisher.Scenes.Count == 0, "nothing published while running");
        // Paused with a native modal dialog already up (busy): wait, do not take over.
        r.Game.UserPauses();
        r.Game.Busy = true;
        r.Tick(3);
        Gates.Check(r.State == PauseState.Running && r.Game.TakeOvers == 0, "no take-over while busy");
        // Paused but the native menu is not what is shown (some other pause): leave it alone.
        r.Game.Busy = false;
        r.Game.NativeVisible = false;
        r.Tick(3);
        Gates.Check(r.State == PauseState.Running && r.Game.TakeOvers == 0, "no take-over without the native menu");
        r.CheckInvariants("idle", nativeMayStayHidden: true);
    }

    private static void OpenAndResume()
    {
        var r = new Rig();
        r.Open();
        Gates.Check(r.Input.On && r.Background.Held == 1 && r.Game.TakenOver && !r.Game.NativeVisible, "open holds capture, background, take-over");
        Gates.Check(r.Publisher.Last is { Mode: MenuSceneMode.Menu, Background: true }, "the menu is published over the background");
        int takeover = r.Chaos.Log.IndexOf("takeover");
        Gates.Check(takeover >= 0 && r.Chaos.Log.IndexOf("bg:capture") > takeover && r.Chaos.Log.IndexOf("capture:on") > r.Chaos.Log.IndexOf("bg:capture"), "order: take-over, background snapshot, capture");

        r.Press(Ev.Esc);
        Gates.Check(r.State == PauseState.Resuming, "Esc starts the resume: " + r.State);
        Gates.Check(r.Game.Actions.SequenceEqual(new[] { PauseAction.Resume }), "the resume action was sent");
        Gates.Check(r.Chaos.Log.IndexOf("capture:off") < r.Chaos.Log.IndexOf("action:Resume"), "capture released before the action");
        Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.None, "nothing drawn once the action is sent");
        r.Tick(2);
        Gates.Check(r.State == PauseState.Resuming, "still resuming while the action runs (busy)");
        r.Game.FinishAction(PauseLastResult.Resumed);
        r.Tick();
        Gates.Check(r.State == PauseState.Running, "running after resumed");
        Gates.Check(r.Game.Restores == 0, "no native restore after a resume");
        r.CheckInvariants("open-resume");
    }

    private static void KeyReleaseWait()
    {
        var r = new Rig();
        r.Open();
        r.Input.Queue(Ev.KeyDown(Ev.Esc));
        r.Tick();
        Gates.Check(r.State == PauseState.Resuming && r.Game.Actions.Count == 0, "waits while Esc is down");
        r.Tick(5);
        Gates.Check(r.Game.Actions.Count == 0 && r.Input.On, "still waiting, capture kept so the key release is seen");
        r.Input.Queue(Ev.KeyUp(Ev.Esc));
        r.Tick();
        Gates.Check(r.Game.Actions.Count == 1 && !r.Input.On, "resumes once the key is released");
        Gates.Check(r.Machine.KeyReleaseTimeouts == 0, "no timeout when the key was released");
        r.Game.FinishAction(PauseLastResult.Resumed);
        r.Tick();
        r.CheckInvariants("key-release");
    }

    private static void StuckKey()
    {
        var r = new Rig(new PauseOptions { KeyReleaseTimeoutMs = 400 });
        r.Open();
        r.Input.Queue(Ev.KeyDown(Ev.Esc), Ev.KeyDown(0x41));
        r.Tick();
        r.Input.Queue(Ev.KeyUp(Ev.Esc));
        for (int i = 0; i < 6; i++) r.Tick();   // 300 ms after the first tick of the wait
        Gates.Check(r.State == PauseState.Resuming && r.Game.Actions.Count == 0, "A is stuck: still waiting before the timeout");
        r.Tick(3);                              // past 400 ms
        Gates.Check(r.Game.Actions.Count == 1, "the timeout resumes anyway");
        Gates.Check(r.Machine.KeyReleaseTimeouts == 1, "the timeout path is counted");
        r.Game.FinishAction(PauseLastResult.Resumed);
        r.Tick();
        Gates.Check(r.State == PauseState.Running, "running after a timed-out wait");
        r.CheckInvariants("stuck-key");
    }

    private static void ModalActions()
    {
        foreach (var action in new[] { PauseAction.NewGame, PauseAction.Save, PauseAction.Options })
        {
            var r = new Rig();
            r.Open();
            r.Choose(action);
            Gates.Check(r.State == PauseState.Modal, action + " enters MODAL: " + r.State);
            Gates.Check(!r.Input.On, action + ": capture is off while the native dialog is up");
            Gates.Check(r.Chaos.Log.IndexOf("capture:off") < r.Chaos.Log.IndexOf("action:" + action), action + ": capture released before the action");
            Gates.Check(r.Background.Held == 1 && r.Game.TakenOver, action + ": snapshot and take-over kept across the dialog");
            Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.None, action + ": nothing drawn during the dialog");
            int stateCalls = r.Game.StateCalls;
            r.Tick(4);
            Gates.Check(r.State == PauseState.Modal && r.Game.StateCalls == stateCalls + 4 && !r.Input.On, action + ": polls state, touches nothing while busy");
            r.Game.FinishAction(PauseLastResult.Returned);
            r.Tick();
            Gates.Check(r.State == PauseState.Open && r.Input.On, action + ": returned and still paused reopens the menu");
            Gates.Check(r.Input.EnableCalls == 2, action + ": capture retaken");
            Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.Menu, action + ": menu published again");
            r.Press(Ev.Esc);
            r.Game.FinishAction(PauseLastResult.Resumed);
            r.Tick();
            Gates.Check(r.State == PauseState.Running, action + ": ends running");
            r.CheckInvariants("modal " + action);
        }
    }

    private static void ModalOutcomes()
    {
        // game-started: new world, the game runs, everything given back without restoring the native menu.
        var r = new Rig();
        r.Open();
        r.Choose(PauseAction.NewGame);
        r.Game.FinishAction(PauseLastResult.GameStarted);
        r.Tick();
        Gates.Check(r.State == PauseState.Running && r.Game.Restores == 0, "game-started ends running without a restore");
        r.CheckInvariants("game-started");

        // resumed from inside a dialog flow.
        r = new Rig();
        r.Open();
        r.Choose(PauseAction.Options);
        r.Game.FinishAction(PauseLastResult.Resumed);
        r.Tick();
        Gates.Check(r.State == PauseState.Running, "resumed ends running");
        r.CheckInvariants("resumed");

        // A stale "resumed" while the game is still paused must not close the menu for good.
        r = new Rig();
        r.Open();
        r.Choose(PauseAction.Save);
        r.Game.Busy = false; r.Game.Pending = null; r.Game.LastResult = PauseLastResult.Resumed; // paused stays true
        r.Tick();
        Gates.Check(r.State == PauseState.Open && r.Input.On, "stale resumed with the game paused reopens the menu");
        r.Game.UserResumesNatively();
        r.Game.TakenOver = false;
        r.Machine.Stop();
        r.CheckInvariants("stale-resumed", nativeMayStayHidden: true);

        // A later pause is taken over again after a full cycle.
        r = new Rig();
        for (int i = 0; i < 3; i++)
        {
            r.Open();
            r.Press(Ev.Esc);
            r.Game.FinishAction(PauseLastResult.Resumed);
            r.Tick();
            Gates.Check(r.State == PauseState.Running, "cycle " + i + " ends running");
        }
        Gates.Check(r.Game.TakeOvers == 3, "each pause is taken over once");
    }

    private static void Quit()
    {
        var r = new Rig();
        r.Open();
        r.Choose(PauseAction.Quit);
        Gates.Check(r.State == PauseState.Modal && !r.Input.On, "quit opens the confirmation dialog with the capture off");
        r.Game.FinishAction(PauseLastResult.QuitRequested);
        r.Tick();
        Gates.Check(r.State == PauseState.Ending, "quit-requested ends in ENDING: " + r.State);
        Gates.Check(r.Background.Held == 0 && !r.Input.On, "the snapshot is let go, capture stays off");
        int calls = r.Chaos.Calls;
        r.Tick(5);
        Gates.Check(r.Chaos.Calls == calls, "ENDING makes no further call");
        r.Machine.Stop();
        Gates.Check(r.Game.Restores == 0, "stop in ENDING does not restore the native menu");
        Gates.Check(r.Chaos.Calls == calls, "stop in ENDING makes no call");
        Gates.Check(r.Machine.State == PauseState.Stopped, "stopped");
        r.CheckInvariants("quit", nativeMayStayHidden: true);

        // The user said No in the confirmation: back to the menu.
        r = new Rig();
        r.Open();
        r.Choose(PauseAction.Quit);
        r.Game.FinishAction(PauseLastResult.Returned);
        r.Tick();
        Gates.Check(r.State == PauseState.Open && r.Input.On, "declined quit reopens the menu");
        r.Machine.Stop();
        r.CheckInvariants("quit-declined");
    }

    private static void BusyNeverDraws()
    {
        var r = new Rig();
        r.Open();
        int drawn = r.Publisher.Scenes.Count;
        // A native dialog appears that we did not ask for (busy while open).
        r.Game.Busy = true;
        r.Tick();
        Gates.Check(r.State == PauseState.Modal && !r.Input.On, "unrequested busy releases the keyboard: " + r.State);
        Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.None, "nothing drawn while busy");
        int before = r.Publisher.Scenes.Count;
        r.Tick(3);
        Gates.Check(r.Publisher.Scenes.Skip(before).All(s => s.Mode != MenuSceneMode.Menu), "no menu scene published during the busy period");
        r.Game.Busy = false;
        r.Tick();
        Gates.Check(r.State == PauseState.Open && r.Input.On && r.Publisher.Last!.Mode == MenuSceneMode.Menu, "back to the menu when the dialog is gone");
        r.Machine.Stop();
        r.CheckInvariants("busy");
    }

    private static void ExternalResume()
    {
        var r = new Rig();
        r.Open();
        r.Game.UserResumesNatively();
        r.Game.Paused = false; // the game runs: the take-over ended with the pause
        r.Tick();
        Gates.Check(r.State == PauseState.Running, "the game resumed from elsewhere");
        Gates.Check(r.Game.Restores == 0, "restoring would pause it again: not called");
        r.CheckInvariants("external-resume");
    }

    private static void TakeOverRetry()
    {
        var r = new Rig(new PauseOptions { TakeOverRetryMs = 300 });
        r.Game.UserPauses();
        r.Game.InFrame = true;
        r.Tick();
        Gates.Check(r.State == PauseState.TakeOverPending, "busy take-over waits in TAKE_OVER_PENDING: " + r.State);
        r.Game.InFrame = false;
        r.Tick();
        Gates.Check(r.State == PauseState.Open, "retried and opened");
        r.Machine.Stop();
        r.CheckInvariants("retry-ok");

        r = new Rig(new PauseOptions { TakeOverRetryMs = 300 });
        r.Game.UserPauses();
        r.Game.InFrame = true;
        r.Tick(3);
        Gates.Check(r.State == PauseState.TakeOverPending, "within the retry window");
        r.Tick(4);
        Gates.Check(r.State == PauseState.Provisional, "past the window: provisional");
        Gates.Check(r.Game.NativeVisible && !r.Game.TakenOver, "nothing was taken");
        r.Machine.Stop();
        r.CheckInvariants("retry-timeout");

        // The user resumes while the take-over is pending: back to running, no provisional.
        r = new Rig();
        r.Game.UserPauses();
        r.Game.InFrame = true;
        r.Tick();
        r.Game.UserResumesNatively();
        r.Tick();
        Gates.Check(r.State == PauseState.Running, "not paused any more: running, got " + r.State);
        Gates.Check(r.Publisher.Scenes.All(s => s.Mode != MenuSceneMode.MapBehind), "no map flash for a pause that ended");
    }

    private static void Provisional()
    {
        var r = new Rig();
        r.Chaos.Target = 1; // the take-over (state is call 0)
        r.Chaos.Fault = Fault.OperationFailed;
        r.Game.UserPauses();
        r.Tick();
        Gates.Check(r.State == PauseState.Provisional, "a failed take-over falls to provisional: " + r.State);
        Gates.Check(r.Machine.LastError is { Code: PauseErrorCode.OperationFailed }, "the typed error is kept");
        Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.MapBehind && r.Publisher.Last.Region("map") != null, "only the map is drawn behind");
        Gates.Check(r.Game.NativeVisible && !r.Game.TakenOver && !r.Input.On && r.Background.Held == 0, "nothing taken over");
        r.Tick(3);
        Gates.Check(r.State == PauseState.Provisional && r.Input.EnableCalls == 0, "stays provisional while paused");
        r.Game.Busy = true;
        r.Tick();
        Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.None, "not drawn while a native dialog is in front");
        r.Game.Busy = false;
        r.Tick();
        Gates.Check(r.Publisher.Last!.Mode == MenuSceneMode.MapBehind, "map again after the dialog");
        r.Game.UserResumesNatively();
        r.Tick();
        Gates.Check(r.State == PauseState.Running && r.Publisher.Last!.Mode == MenuSceneMode.None, "the native resume ends provisional");
        // The next pause is tried again.
        r.Game.UserPauses();
        r.Tick();
        Gates.Check(r.State == PauseState.Open, "the next pause is taken over: " + r.State);
        r.Machine.Stop();
        r.CheckInvariants("provisional");
    }

    private static void UnresolvedLatches()
    {
        var r = new Rig();
        r.Chaos.Target = 0;
        r.Chaos.Fault = Fault.CapabilityUnresolved;
        r.Tick();
        Gates.Check(r.State == PauseState.Running && r.Machine.Unavailable, "unresolved on the first read latches");
        int calls = r.Chaos.Calls;
        r.Game.UserPauses();
        r.Tick(5);
        Gates.Check(r.Chaos.Calls == calls && r.State == PauseState.Running, "no further game call once latched");
        r.CheckInvariants("latched", nativeMayStayHidden: true);

        r = new Rig();
        r.Chaos.Target = 1;
        r.Chaos.Fault = Fault.CapabilityUnresolved;
        r.Game.UserPauses();
        r.Tick();
        Gates.Check(r.State == PauseState.Provisional && r.Machine.Unavailable, "unresolved at take-over: provisional and latched");
        r.Game.Unresolved = false;
        r.Game.UserResumesNatively();
        r.Tick(3);
        r.Game.UserPauses();
        r.Tick(3);
        Gates.Check(r.Game.TakeOvers == 1, "a latched machine never tries again");
    }

    // ---- sweeps

    // The user journey every sweep run follows. It is written against the machine's
    // state at each step only through the fakes, so it stays valid after an injected fault.
    private static void Journey(Rig r)
    {
        r.Game.UserPauses();
        r.Tick(2);
        r.Choose(PauseAction.NewGame);
        r.Tick(2);
        r.Game.FinishAction(PauseLastResult.Returned);
        r.Tick(3);
        r.Input.Queue(Ev.KeyDown(Ev.Esc));
        r.Tick(2);
        r.Input.Queue(Ev.KeyUp(Ev.Esc));
        r.Tick(3);
        r.Game.FinishAction(PauseLastResult.Resumed);
        r.Tick(3);
        // The user pauses again and leaves it open; the module stops.
        r.Game.UserPauses();
        r.Tick(4);
        r.Machine.Stop();
        r.Machine.Stop();
    }

    private static IEnumerable<Fault> FaultsFor(string call)
    {
        if (call == "state" || call == "takeover" || call.StartsWith("action:") || call == "restore")
            return new[] { Fault.NotPaused, Fault.Busy, Fault.AlreadyTakenOver, Fault.CapabilityUnresolved, Fault.OperationFailed, Fault.Other, Fault.Exception };
        if (call.StartsWith("capture:") || call == "bg:capture") return new[] { Fault.False, Fault.Exception };
        return new[] { Fault.Exception }; // drain, bg:release
    }

    private static void FailureSweep()
    {
        var clean = new Rig();
        Journey(clean);
        clean.CheckInvariants("clean journey");
        Gates.Check(clean.Machine.LastError is null, "the clean journey has no error");
        var calls = clean.Chaos.Log.ToList();
        Gates.Check(calls.Count > 20, "the journey exercises the ports");
        foreach (var name in new[] { "state", "takeover", "action:NewGame", "action:Resume", "capture:on", "capture:off", "bg:capture", "bg:release", "drain", "restore" })
            Gates.Check(calls.Contains(name), "the journey reaches the call " + name);

        int runs = 0;
        for (int ordinal = 0; ordinal < calls.Count; ordinal++)
        {
            foreach (var fault in FaultsFor(calls[ordinal]))
            {
                var r = new Rig();
                r.Chaos.Target = ordinal;
                r.Chaos.Fault = fault;
                string context = "fault " + fault + " at call #" + ordinal + " (" + calls[ordinal] + ")";
                try { Journey(r); }
                catch (Exception exception) { throw new InvalidOperationException(context + ": threw " + exception.GetType().Name + ": " + exception.Message); }
                // A capability that stopped resolving cannot give the native menu back; everything else must.
                r.CheckInvariants(context, nativeMayStayHidden: r.Game.Unresolved);
                runs++;
            }
        }
        Gates.Check(runs > 100, "enough fault runs: " + runs);
    }

    private static void PersistentRestoreFailure()
    {
        // restore-native keeps answering busy: no loop, no crash, cleanup reports incomplete, retried later.
        var r = new Rig();
        r.Open();
        r.Game.Busy = true;      // a native dialog is in front
        r.Game.Pending = PauseAction.Options;
        r.Tick();                // Open sees busy -> Modal
        Gates.Check(r.State == PauseState.Modal, "modal: " + r.State);
        r.Machine.Stop();
        Gates.Check(!r.Machine.CleanupComplete, "the restore waits while the dialog is up");
        Gates.Check(r.Game.Restores == 0, "no restore attempted against a busy game");
        r.Machine.Stop();
        Gates.Check(r.Game.Restores == 0 && !r.Machine.CleanupComplete, "stop retries only the missing piece");
        r.Game.FinishAction(PauseLastResult.Returned);
        r.Machine.Stop();
        Gates.Check(r.Machine.CleanupComplete && r.Game.NativeVisible && r.Game.Paused, "completed once the dialog is gone");
        int calls = r.Chaos.Calls;
        r.Machine.Stop();
        Gates.Check(r.Chaos.Calls == calls, "complete cleanup makes no more calls");
        r.CheckInvariants("persistent-restore");
    }

    private static Rig Reach(PauseState wanted)
    {
        var r = new Rig();
        switch (wanted)
        {
            case PauseState.Running: break;
            case PauseState.TakeOverPending: r.Game.UserPauses(); r.Game.InFrame = true; r.Tick(); break;
            case PauseState.Open: r.Open(); break;
            case PauseState.Resuming: r.Open(); r.Input.Queue(Ev.KeyDown(Ev.Esc)); r.Tick(); break;
            case PauseState.Modal: r.Open(); r.Choose(PauseAction.Save); break;
            case PauseState.Ending: r.Open(); r.Choose(PauseAction.Quit); r.Game.FinishAction(PauseLastResult.QuitRequested); r.Tick(); break;
            case PauseState.Provisional: r.Chaos.Target = 1; r.Chaos.Fault = Fault.OperationFailed; r.Game.UserPauses(); r.Tick(); break;
            case PauseState.Stopped: r.Open(); r.Machine.Stop(); break;
        }
        Gates.Check(r.State == wanted, "reached " + wanted + ", got " + r.State);
        return r;
    }

    private static void StopEveryState()
    {
        foreach (PauseState state in Enum.GetValues(typeof(PauseState)))
        {
            var r = Reach(state);
            if (state == PauseState.Modal)
            {
                // The dialog is up: the restore is refused until it ends; stop then completes.
                r.Machine.Stop();
                Gates.Check(r.State == PauseState.Stopped, "stopped from " + state);
                r.Game.FinishAction(PauseLastResult.Returned);
                r.Machine.Stop();
            }
            else r.Machine.Stop();
            Gates.Check(r.State == PauseState.Stopped, "stopped from " + state + ": " + r.State);
            r.CheckInvariants("stop from " + state, nativeMayStayHidden: state == PauseState.Ending);
            Gates.Check(r.Publisher.Last is null || r.Publisher.Last.Mode == MenuSceneMode.None, "stop from " + state + " clears the scene");
            int calls = r.Chaos.Calls;
            r.Tick(3);
            Gates.Check(r.Chaos.Calls == calls, "a stopped machine makes no call (" + state + ")");
        }
    }

    private static void StopIdempotent()
    {
        foreach (PauseState state in Enum.GetValues(typeof(PauseState)))
        {
            if (state == PauseState.Modal) continue; // covered with its pending restore above
            var r = Reach(state);
            r.Machine.Stop();
            int calls = r.Chaos.Calls;
            int scenes = r.Publisher.Scenes.Count;
            for (int i = 0; i < 3; i++) r.Machine.Stop();
            Gates.Check(r.Chaos.Calls == calls && r.Publisher.Scenes.Count == scenes, "repeated stop from " + state + " does nothing");
        }
    }

    private static void Cycles()
    {
        var r = new Rig();
        for (int i = 0; i < 1000; i++)
        {
            r.Open();
            if (i % 10 == 0)
            {
                r.Choose(PauseAction.Options);
                r.Game.FinishAction(PauseLastResult.Returned);
                r.Tick();
                Gates.Check(r.State == PauseState.Open, "reopened in cycle " + i);
            }
            r.Press(Ev.Esc);
            r.Game.FinishAction(PauseLastResult.Resumed);
            r.Tick();
            if (r.State != PauseState.Running) throw new InvalidOperationException("cycle " + i + " ended in " + r.State);
        }
        Gates.Check(r.Game.TakeOvers == 1000, "1000 take-overs");
        Gates.Check(r.Background.Captures == 1000 && r.Background.Releases == 1000 && r.Background.Held == 0, "background balanced");
        Gates.Check(r.Input.EnableCalls == 1100 && r.Input.DisableCalls == 1100 && !r.Input.On, "capture balanced: " + r.Input.EnableCalls + "/" + r.Input.DisableCalls);
        Gates.Check(r.Game.Restores == 0, "no restore on any resume");
        r.CheckInvariants("cycles");
    }

    private static void SingleThread()
    {
        var r = new Rig();
        Exception? thrown = null;
        var thread = new Thread(() => { try { r.Machine.Tick(); } catch (Exception e) { thrown = e; } });
        r.Tick(); // binds the machine to this thread
        thread.Start();
        thread.Join();
        Gates.Check(thrown is InvalidOperationException, "a tick from another thread is refused");
        thrown = null;
        thread = new Thread(() => { try { r.Machine.Stop(); } catch (Exception e) { thrown = e; } });
        thread.Start();
        thread.Join();
        Gates.Check(thrown is InvalidOperationException, "a stop from another thread is refused");
        Gates.Check(r.Chaos.ThreadViolations == 0, "the machine itself never called a port off-thread");

        // The fakes do detect an off-thread call (so the check above means something).
        var other = new Thread(() => r.Game.State());
        other.Start();
        other.Join();
        Gates.Check(r.Chaos.ThreadViolations == 1, "the port thread check works");
        r.Tick();
        Gates.Check(r.State == PauseState.Running, "the owner thread still works");
    }

    private static void PortExceptions()
    {
        // Every kind of port failure by exception is a handled failure, never an escape.
        foreach (var call in new[] { "state", "takeover", "bg:capture", "capture:on", "drain", "action:Resume", "capture:off", "bg:release", "restore" })
        {
            var clean = new Rig();
            Journey(clean);
            int ordinal = clean.Chaos.Log.IndexOf(call);
            Gates.Check(ordinal >= 0, "journey has " + call);
            var r = new Rig();
            r.Chaos.Target = ordinal;
            r.Chaos.Fault = Fault.Exception;
            Journey(r);
            r.CheckInvariants("exception at " + call);
            Gates.Check(r.Machine.LastError is not null, "the failure is recorded for " + call);
        }
        // A publisher that throws does not break the machine either.
        var rig = new Rig();
        var throwing = new PauseMachine(rig.Game, rig.Input, rig.Background, new ThrowingPublisher(), rig.Clock, new PauseMenu());
        rig.Game.UserPauses();
        rig.Clock.Now += 50;
        throwing.Tick();
        Gates.Check(throwing.State == PauseState.Open, "a throwing publisher does not stop the take-over");
        throwing.Stop();
        Gates.Check(throwing.CleanupComplete, "and cleanup still completes");
    }

    private sealed class ThrowingPublisher : IPauseScenePublisher
    {
        public void Publish(MenuScene scene) => throw new InvalidOperationException("publisher down");
    }

    private static void ErrorMapping()
    {
        var table = new (string Code, PauseErrorCode Expected)[]
        {
            ("OL_E_UI_NOT_PAUSED", PauseErrorCode.NotPaused),
            ("OL_E_UI_BUSY", PauseErrorCode.Busy),
            ("OL_E_UI_ALREADY_TAKEN_OVER", PauseErrorCode.AlreadyTakenOver),
            ("OL_E_CAPABILITY_UNRESOLVED", PauseErrorCode.CapabilityUnresolved),
            ("OL_E_RUNTIME_OPERATION_FAILED", PauseErrorCode.OperationFailed),
            ("OL_E_RUNTIME_OPERATION_UNAVAILABLE", PauseErrorCode.Other),
            ("", PauseErrorCode.Other),
        };
        foreach (var (code, expected) in table)
        {
            var error = PauseError.FromCatalog(code, "detail");
            Gates.Check(error.Code == expected, code + " maps to " + expected + ", got " + error.Code);
            if (expected != PauseErrorCode.Other) Gates.Check(error.CatalogCode == code, "round trip of " + code);
        }
        Gates.Check(PauseError.FromCatalog(null, null).Code == PauseErrorCode.Other, "null code");
        Gates.Check(PauseError.FromCatalog("X", "y").Detail.Contains('X'), "the raw code of an unknown error is kept");
        var ok = PauseResult.Success(new PauseGameState(true, true, false, PauseLastResult.None));
        Gates.Check(ok.IsOk && ok.State.Paused, "success carries the state");
        bool threw = false;
        try { _ = ok.Error; } catch (InvalidOperationException) { threw = true; }
        Gates.Check(threw, "a success has no error");
        threw = false;
        try { _ = PauseResult.Failure(PauseErrorCode.Busy).State; } catch (InvalidOperationException) { threw = true; }
        Gates.Check(threw, "a failure has no state");
    }
}
