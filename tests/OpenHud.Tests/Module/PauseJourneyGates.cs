using OpenHud.Pause;
using OpenHud.Scene;
using OpenHud.Tests.PauseTests;

namespace OpenHud.Tests.ModuleTests;

// The pause menu's take-over end to end through the module: the catalog says paused, the module shows its
// own menu, the captured input drives it, and the game is given back whatever happens.
internal static class PauseJourneyGates
{
    public static void Register()
    {
        Gates.Run("module.pause-take-over-and-resume", TakeOverAndResume);
        Gates.Run("module.pause-mouse-follows-the-view-size", MouseFollowsTheView);
        Gates.Run("module.pause-map-and-lines-pages", MapAndLinesPages);
        Gates.Run("module.pause-native-dialog-hides-the-menu", NativeDialog);
        Gates.Run("module.pause-quit-ends-quietly", Quit);
        Gates.Run("module.pause-provisional-when-take-over-fails", Provisional);
        Gates.Run("module.pause-unresolved-capability-keeps-the-native-menu", Unresolved);
        Gates.Run("module.pause-input-failure-gives-the-native-menu-back", InputFailure);
        Gates.Run("module.pause-draw-layer-failure-gives-the-native-menu-back", DrawFailure);
        Gates.Run("module.pause-scene-failures-give-the-native-menu-back", SceneFailures);
        Gates.Run("module.pause-off-when-input-or-capability-is-missing", PauseOff);
        Gates.Run("module.pause-user-resumes-natively", ResumesNatively);
    }

    private static SceneDocument Last(Session s) => Doc.Decode(s.Native.LastScene!);

    private static string PauseState(Session s) => s.Module.Execute("pause.state", new Dictionary<string, string>())["pause_state"];

    // A started session, the packages adopted, and the user pausing: the menu is open.
    private static Session OpenMenu(bool packages = true, Action<Dictionary<string, string>>? settings = null)
    {
        var s = new Session(settings: settings);
        if (packages) PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        s.Start();
        if (packages) Gates.Check(s.WaitForPackages(), "the packages are adopted");
        s.Tick(5);
        s.Pause.UserPauses();
        s.Tick(2);
        Gates.Check(PauseState(s) == "Open", "the menu opens: " + PauseState(s));
        return s;
    }

    // The rules that hold at every step: nothing of ours while a native dialog is up, the game is never left paused with no menu.
    private static void Watch(Session s, string context)
    {
        if (s.Pause.Busy) Gates.Check(!s.Native.LastSceneDrawsMenu, context + ": the menu is drawn while a native dialog is up");
        Gates.Check(s.Pause.Violations.Count == 0, context + ": " + string.Join("; ", s.Pause.Violations));
        Gates.Check(s.Native.SceneRejects == 0, context + ": scenes rejected");
    }

    private static void Step(Session s, int ticks, string context)
    {
        for (var i = 0; i < ticks; i++) { s.Tick(); Watch(s, context); }
    }

    private static void TakeOverAndResume()
    {
        using var s = OpenMenu();
        Gates.Check(s.Pause.TakenOver && !s.Pause.NativeVisible, "the native menu is hidden by the take-over");
        Gates.Check(s.Input.Capturing && s.Native.Held == 1, "the input and the background are held");
        Gates.Check(s.Native.LastSceneDrawsMenu, "the menu scene is on screen");
        var menu = Last(s);
        var texts = menu.Commands.OfType<TextCommand>().Select(t => t.Text).ToList();
        Gates.Check(texts.Contains("Synthetic Map") && texts.Contains("Back to the game"), "title and items: " + string.Join("|", texts.Take(8)));
        Gates.Check(menu.Commands.OfType<TextCommand>().Any(t => t.Text.Contains("Synthetic station")), "the package's legend is shown");

        // Esc starts the resume: the keys must be released before the game gets the action.
        s.Input.Queue(Keys.Down(Keys.Esc));
        Step(s, 2, "esc held");
        Gates.Check(s.Pause.Actions.Count == 0 && PauseState(s) == "Resuming", "the action waits for the key release: " + PauseState(s));
        s.Input.Queue(Keys.Up(Keys.Esc));
        Step(s, 6, "resume");
        Gates.Check(s.Pause.Actions.SequenceEqual(new[] { "resume" }), "resume is the one action: " + string.Join(",", s.Pause.Actions));
        Gates.Check(PauseState(s) == "Running" && !s.Pause.Paused, "the game runs again");
        Gates.Check(!s.Input.Capturing && s.Native.Held == 0, "everything is let go");
        Gates.Check(s.Input.Starts == s.Input.Ends && s.Native.Captures == s.Native.Releases, "captures are balanced");
        Step(s, 8, "after resume");
        Gates.Check(!s.Native.LastSceneDrawsMenu && Last(s).Commands.OfType<ClipCircleCommand>().Any(), "the radar is back");
        Gates.Check(s.Pause.Restores == 0, "the native menu is not restored on a running game");
        s.CheckAllScenesValid("take over");

        // And again: a second pause opens the menu afresh.
        s.Pause.UserPauses();
        Step(s, 3, "second pause");
        Gates.Check(PauseState(s) == "Open" && s.Native.LastSceneDrawsMenu, "a second pause opens the menu again");
        s.Stop();
        Gates.Check(!s.Input.Capturing && s.Native.Held == 0, "Stop lets go of the input and the background");
        Gates.Check(s.Pause.NativeVisible && s.Pause.Paused, "Stop gives the native menu back (the game is still paused)");
        Gates.Check(s.Native.Visible == false, "Stop hides the layer");
    }

    private static void MouseFollowsTheView()
    {
        using var s = OpenMenu();
        // The layer says the view is 1280x720; a click on "Back to the game" is mapped with that size.
        var g = new MenuGeometry(new MenuViewport(1280, 720, 1, 1));
        var (x, y) = Ev.Center(g, MenuGeometry.ItemRect(0));
        s.Input.Queue(Keys.Move(x, y));
        Step(s, 1, "move");
        s.Input.Queue(Keys.Press(x, y));
        Step(s, 1, "press");
        s.Input.Queue(Keys.Release(x, y));
        Step(s, 8, "release");
        Gates.Check(s.Pause.Actions.SequenceEqual(new[] { "resume" }), "a click resumes: " + string.Join(",", s.Pause.Actions));
        // A different view size changes the mapping: the same pixel is no longer on the item.
        using var other = new Session();
        other.Native.ViewW = 1920; other.Native.ViewH = 1080;
        other.Start();
        other.Tick(5);
        other.Pause.UserPauses();
        other.Tick(2);
        other.Input.Queue(Keys.Move(x, y), Keys.Press(x, y), Keys.Release(x, y));
        Step(other, 6, "other view");
        Gates.Check(other.Pause.Actions.Count == 0 || other.Pause.Actions[0] != "resume" || (x, y) == Ev.Center(new MenuGeometry(new MenuViewport(1920, 1080, 1, 1)), MenuGeometry.ItemRect(0)),
            "the mapping uses the layer's view size");
    }

    private static void MapAndLinesPages()
    {
        using var s = OpenMenu();
        Gates.Check(s.WaitFor(() => { s.Tick(1, advanceDrive: false); return int.Parse(s.Status()["fullmap_features"]) >= 0; }), "ticks run");
        var baseline = Last(s).Commands.Count;
        Gates.Check(!Last(s).Commands.OfType<PushCommand>().Any(), "the menu page has no map");

        s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab));
        Step(s, 2, "to the map page");
        var page = Last(s);
        Gates.Check(page.Commands.OfType<PushCommand>().Count() == 1 && page.Commands.OfType<PopCommand>().Count() == 1, "the full map is composed into the map region");
        Gates.Check(page.Commands.OfType<ClipRectCommand>().Count() >= 2, "the map is clipped to its region");
        Gates.Check(page.Commands.OfType<PushCommand>().Single() is { Tx: > 0, Ty: > 0 }, "the map is placed inside the panel");
        Gates.Check(s.WaitFor(() => { s.Tick(1, advanceDrive: false); return int.Parse(s.Status()["fullmap_features"]) > 0; }), "the full map's window arrives from the worker");
        Step(s, 2, "map features");
        var withFeatures = Last(s);
        Gates.Check(withFeatures.Commands.OfType<PolyCommand>().Any(c => c.Fill == "#2b5b8a"), "the package's water is drawn on the full map");

        // Zoom and pan change the map; Home recentres to the first picture.
        var before = s.Native.LastScene;
        s.Input.Queue(Keys.Down(Keys.Plus), Keys.Up(Keys.Plus));
        Step(s, 3, "zoom");
        Gates.Check(s.Native.LastScene != before, "the zoom key changes the map");
        var zoomed = s.Native.LastScene;
        s.Input.Queue(Keys.Down(Keys.Left_), Keys.Up(Keys.Left_));
        Step(s, 3, "pan");
        Gates.Check(s.Native.LastScene != zoomed, "the arrow keys pan the map");
        s.Input.Queue(Keys.Down(0x24), Keys.Up(0x24)); // Home
        Step(s, 3, "home");
        var g = new MenuGeometry(new MenuViewport(1280, 720, 1, 1));
        var (mx, my) = Ev.Center(g, MenuGeometry.Content);
        s.Input.Queue(Keys.Move(mx, my), Keys.Wheel(mx, my, 120));
        Step(s, 3, "wheel");
        Gates.Check(s.Native.LastScene != before, "the wheel zooms the map under the pointer");

        s.Input.Queue(Keys.Down(Keys.Tab), Keys.Up(Keys.Tab));
        Step(s, 3, "to the lines page");
        var lines = Last(s);
        var texts = lines.Commands.OfType<TextCommand>().Select(t => t.Text).ToList();
        Gates.Check(!lines.Commands.OfType<PushCommand>().Any(), "the lines page has no map");
        Gates.Check(texts.Any(t => t.Contains("North - Central")), "the package's lines are listed: " + string.Join("|", texts.Take(14)));
        Gates.Check(baseline > 0, "baseline");
        s.CheckAllScenesValid("pages");
    }

    private static void NativeDialog()
    {
        using var s = OpenMenu();
        s.Pause.AutoFinish = false;
        // "New game" is the second item: Down, Enter.
        s.Input.Queue(Keys.Down(Keys.Down_), Keys.Up(Keys.Down_), Keys.Down(Keys.Enter), Keys.Up(Keys.Enter));
        Step(s, 3, "new game");
        Gates.Check(s.Pause.Actions.SequenceEqual(new[] { "new-game" }), "the action is sent: " + string.Join(",", s.Pause.Actions));
        Gates.Check(s.Pause.Busy && !s.Input.Capturing, "the capture is off before the native dialog");
        Gates.Check(!s.Native.LastSceneDrawsMenu && Last(s).Commands.Count == 0, "nothing is drawn while the dialog is up");
        Gates.Check(s.Native.Held == 1, "the snapshot is kept for when the menu returns");
        Step(s, 5, "dialog open");
        Gates.Check(PauseState(s) == "Modal", "the machine waits: " + PauseState(s));
        // The dialog ends and the game is still paused: the menu comes back.
        s.Pause.Finish("returned");
        Step(s, 4, "returned");
        Gates.Check(PauseState(s) == "Open" && s.Input.Capturing && s.Native.Held == 1, "the menu reopens with capture and background again");
        Gates.Check(s.Native.LastSceneDrawsMenu, "the menu is drawn again");
        Gates.Check(s.Input.Starts == 2, "the capture was retaken: " + s.Input.Starts);

        // A game started from the dialog ends the pause for good.
        s.Input.Queue(Keys.Down(Keys.Down_), Keys.Up(Keys.Down_), Keys.Down(Keys.Enter), Keys.Up(Keys.Enter));
        Step(s, 3, "second dialog");
        s.Pause.Finish("game-started");
        Step(s, 4, "game started");
        Gates.Check(PauseState(s) == "Running" && !s.Input.Capturing && s.Native.Held == 0, "a new game returns to running");
        Gates.Check(s.Pause.Restores == 0, "no restore of the native menu on a running game");
        Step(s, 5, "radar");
        Gates.Check(Last(s).Commands.OfType<ClipCircleCommand>().Any(), "the radar draws again");
    }

    private static void Quit()
    {
        using var s = OpenMenu();
        s.Pause.AutoFinish = false;
        // Quit is the last item: End, Enter.
        s.Input.Queue(Keys.Down(0x23), Keys.Up(0x23), Keys.Down(Keys.Enter), Keys.Up(Keys.Enter));
        Step(s, 3, "quit");
        Gates.Check(s.Pause.Actions.SequenceEqual(new[] { "quit" }), "quit is sent");
        s.Pause.Finish("quit-requested");
        Step(s, 4, "quit requested");
        Gates.Check(PauseState(s) == "Ending", "the machine ends: " + PauseState(s));
        Gates.Check(Last(s).Commands.Count == 0 && !s.Input.Capturing && s.Native.Held == 0, "nothing is held or drawn while OMSI closes");
        Step(s, 10, "ending");
        s.Stop();
        Gates.Check(s.Pause.Restores == 0, "a closing game is not given its menu back");
    }

    private static void Provisional()
    {
        using var s = new Session();
        PackageKit.Install(s, PackageKit.Surface(), pausePackage: true);
        s.Start();
        s.WaitForPackages();
        s.Tick(5);
        s.Pause.InFrame = true;   // the take-over keeps answering "busy" (OMSI is inside its frame)
        s.Pause.UserPauses();
        s.Tick(30);               // more than the one second the machine retries for
        Gates.Check(PauseState(s) == "Provisional", "the take-over failed: " + PauseState(s));
        Gates.Check(!s.Input.Capturing && s.Input.Starts == 0, "nothing was captured");
        Gates.Check(s.Pause.NativeVisible && !s.Pause.TakenOver, "the native menu stays visible");
        var scene = Last(s);
        Gates.Check(!scene.Background && scene.Commands.OfType<PushCommand>().Count() == 1, "only the map is drawn behind it");
        Gates.Check(!scene.Commands.OfType<TextCommand>().Any(t => t.Text == "Back to the game"), "no menu of ours");
        Gates.Check(s.Module.Execute("pause.state", new Dictionary<string, string>())["pause_last_error"].Contains("OL_E_UI_BUSY"), "the typed error is kept");
        s.CheckAllScenesValid("provisional");
    }

    private static void ResumesNatively()
    {
        using var s = new Session();
        s.Start();
        s.Tick(5);
        s.Pause.InFrame = true;
        s.Pause.UserPauses();
        s.Tick(30);
        Gates.Check(PauseState(s) == "Provisional", "provisional");
        s.Pause.UserResumesNatively();
        s.Tick(6);
        Gates.Check(PauseState(s) == "Running", "the user resumed with the native menu: " + PauseState(s));
        Gates.Check(Last(s).Commands.OfType<ClipCircleCommand>().Any(), "the radar is back");
        Gates.Check(s.Pause.Violations.Count == 0, "no restore on a running game");
        // The next pause starts over and, with the frame free, takes over normally.
        s.Pause.InFrame = false;
        s.Pause.UserPauses();
        s.Tick(3);
        Gates.Check(PauseState(s) == "Open", "the next pause takes over: " + PauseState(s));
    }

    private static void Unresolved()
    {
        using var s = new Session();
        s.Start();
        s.Tick(3);
        s.Runtime.PauseUnresolved = true;
        s.Pause.UserPauses();
        s.Tick(10);
        var status = s.Module.Execute("pause.state", new Dictionary<string, string>());
        Gates.Check(status["pause_unavailable"] == "true", "the machine stops asking");
        Gates.Check(s.Pause.TakeOvers == 0 && !s.Input.Capturing, "nothing was taken");
        Gates.Check(s.Context.NoticeCount() == 1, "one notice says the native menu stays: " + s.Context.NoticeCount());
        var calls = s.Runtime.PerOperation.GetValueOrDefault("ui.pause.state");
        s.Tick(20);
        Gates.Check(s.Runtime.PerOperation.GetValueOrDefault("ui.pause.state") == calls, "an unresolved capability is not polled again");
        Gates.Check(Last(s).Commands.OfType<ClipCircleCommand>().Any(), "the radar keeps working");
    }

    private static void InputFailure()
    {
        using var s = new Session();
        s.Start();
        s.Tick(5);
        s.Input.Chaos.Target = s.Input.Chaos.Ordinal;   // the very next input call (starting the capture) fails
        s.Input.Chaos.Fault = Fault.Error;
        s.Pause.UserPauses();
        s.Tick(4);
        Gates.Check(s.Pause.NativeVisible && !s.Pause.TakenOver && s.Pause.Restores >= 1, "the native menu is given back: restores=" + s.Pause.Restores);
        Gates.Check(s.Native.Held == 0 && !s.Input.Capturing, "the background is released");
        Gates.Check(PauseState(s) == "Provisional", "provisional: " + PauseState(s));
    }

    private static void DrawFailure()
    {
        using var s = OpenMenu();
        s.Native.Failed = true;
        s.Tick(StatsTicks());
        Gates.Check(s.Pause.NativeVisible && !s.Pause.TakenOver, "a failed draw layer cannot leave the game without a menu");
        Gates.Check(!s.Input.Capturing && s.Native.Held == 0, "nothing stays held");
        var status = s.Module.Execute("pause.state", new Dictionary<string, string>());
        Gates.Check(status["pause_menu"] == "degraded", "the pause menu is degraded: " + status["pause_menu"]);
        Gates.Check(s.Context.NoticeCount() == 1, "one notice: " + s.Context.NoticeCount());
        Gates.Check(s.Native.CountOf("visible") >= 2, "the layer is asked to draw again");
        s.Tick(StatsTicks());
        Gates.Check(Last(s).Commands.OfType<ClipCircleCommand>().Any(), "after a retry the radar draws again");
        Gates.Check(s.Context.NoticeCount() == 1, "still one notice");
    }

    private static int StatsTicks() => HudModule.StatsIntervalTicks + 2;

    private static void SceneFailures()
    {
        using var s = OpenMenu();
        s.Native.FailScenes = true;
        s.Input.Queue(Keys.Down(Keys.Down_), Keys.Up(Keys.Down_));
        for (var i = 0; i < 12; i++)
        {
            s.Input.Queue(Keys.Down(Keys.Down_), Keys.Up(Keys.Down_));
            s.Tick();
        }
        Gates.Check(s.Pause.NativeVisible && !s.Pause.TakenOver, "scenes that cannot be published give the native menu back");
        Gates.Check(!s.Input.Capturing, "the input is let go");
        Gates.Check(s.Context.NoticeCount() >= 1, "the user is told");
    }

    private static void PauseOff()
    {
        using (var s = new Session())
        {
            s.Input.Available = false;
            s.Start();
            s.Tick(3);
            s.Pause.UserPauses();
            s.Tick(10);
            var status = s.Module.Execute("pause.state", new Dictionary<string, string>());
            Gates.Check(status["pause_menu"] == "off" && status["pause_reason"].Contains("input"), "no input capture: " + status["pause_reason"]);
            Gates.Check(s.Pause.TakeOvers == 0 && s.Pause.NativeVisible, "the native menu is left alone");
        }
        using (var s = new Session())
        {
            s.Context.Capabilities.Remove("ui.pause-menu.replace");
            s.Start();
            s.Tick(3);
            s.Pause.UserPauses();
            s.Tick(10);
            Gates.Check(s.Module.Execute("pause.state", new Dictionary<string, string>())["pause_reason"].Contains("ui.pause-menu.replace"), "capability missing");
            Gates.Check(s.Runtime.PerOperation.GetValueOrDefault("ui.pause.state") == 0, "no pause call without the capability");
            Gates.Check(Last(s).Commands.OfType<ClipCircleCommand>().Any(), "the radar still works");
        }
        using (var s = new Session(settings: v => v["pause_menu"] = "false"))
        {
            s.Start();
            s.Tick(3);
            s.Pause.UserPauses();
            s.Tick(10);
            Gates.Check(s.Module.Execute("pause.state", new Dictionary<string, string>())["pause_reason"].Contains("settings"), "setting off");
            Gates.Check(s.Pause.TakeOvers == 0, "the setting is respected");
        }
    }
}
