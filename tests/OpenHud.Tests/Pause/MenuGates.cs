using OpenHud.Pause;

namespace OpenHud.Tests.PauseTests;

// Gates of the menu model: keyboard, mouse, wheel, double click, focus.
internal static class MenuGates
{
    public static void Register()
    {
        Gates.Run("pause.menu.keyboard-table", KeyboardTable);
        Gates.Run("pause.menu.ignored-input", IgnoredInput);
        Gates.Run("pause.menu.map-keys", MapKeys);
        Gates.Run("pause.menu.lines-keys", LinesKeys);
        Gates.Run("pause.menu.mouse-hover-focus", HoverFocus);
        Gates.Run("pause.menu.mouse-press-release", PressRelease);
        Gates.Run("pause.menu.mouse-tabs", MouseTabs);
        Gates.Run("pause.menu.mouse-drag-map", DragMap);
        Gates.Run("pause.menu.wheel", Wheel);
        Gates.Run("pause.menu.double-click", DoubleClick);
        Gates.Run("pause.menu.reset-and-transient", ResetAndTransient);
        Gates.Run("pause.menu.hit-test-table", HitTestTable);
        Gates.Run("pause.menu.hostile-input", HostileInput);
    }

    private static PauseMenu NewMenu(MenuViewport? viewport = null)
    {
        var menu = new PauseMenu();
        menu.SetViewport(viewport ?? MenuViewport.Default);
        menu.Reset();
        return menu;
    }

    private static IReadOnlyList<MenuEvent> Send(PauseMenu menu, params PauseInputEvent[] events)
    {
        var all = new List<MenuEvent>();
        foreach (var e in events) all.AddRange(menu.Handle(e));
        return all;
    }

    private static void ExpectAction(IReadOnlyList<MenuEvent> events, PauseAction action, string what)
        => Gates.Check(events.Count == 1 && events[0] is ActionRequested a && a.Action == action, what + ": expected " + action + ", got " + Describe(events));

    private static string Describe(IReadOnlyList<MenuEvent> events) => events.Count == 0 ? "no event" : string.Join(",", events.Select(e => e.ToString()));

    private static void KeyboardTable()
    {
        var table = new (int[] Keys, int Focus, PauseAction? Action, string Case)[]
        {
            (new int[] { }, 0, null, "initial focus is the first item"),
            (new[] { Ev.Down_ }, 1, null, "down"),
            (new[] { Ev.Down_, Ev.Down_ }, 2, null, "down down"),
            (new[] { Ev.Up_ }, 4, null, "up wraps to the last"),
            (new[] { Ev.Down_, Ev.Down_, Ev.Down_, Ev.Down_, Ev.Down_ }, 0, null, "down wraps to the first"),
            (new[] { Ev.End }, 4, null, "end"),
            (new[] { Ev.End, Ev.Home }, 0, null, "home"),
            (new[] { Ev.Enter }, 0, PauseAction.Resume, "enter on the first item"),
            (new[] { Ev.Down_, Ev.Enter }, 1, PauseAction.NewGame, "enter on new game"),
            (new[] { Ev.Down_, Ev.Down_, Ev.Enter }, 2, PauseAction.Save, "enter on save"),
            (new[] { Ev.Down_, Ev.Down_, Ev.Down_, Ev.Enter }, 3, PauseAction.Options, "enter on options"),
            (new[] { Ev.Up_, Ev.Enter }, 4, PauseAction.Quit, "enter on quit"),
            (new[] { 0x20 }, 0, PauseAction.Resume, "space activates"),
            (new[] { Ev.Esc }, 0, PauseAction.Resume, "esc resumes"),
            (new[] { Ev.Down_, Ev.Down_, Ev.Esc }, 2, PauseAction.Resume, "esc resumes from any focus"),
            (new[] { Ev.Left_, Ev.Right_ }, 0, null, "left/right do nothing on the menu page"),
        };
        foreach (var (keys, focus, action, name) in table)
        {
            var menu = NewMenu();
            var events = new List<MenuEvent>();
            foreach (var key in keys) { events.AddRange(menu.Handle(Ev.KeyDown(key))); if (events.OfType<ActionRequested>().Any()) break; }
            Gates.Check(menu.FocusIndex == focus, name + ": focus " + menu.FocusIndex + " expected " + focus);
            if (action is { } a) ExpectAction(events, a, name);
            else Gates.Check(events.Count == 0, name + ": unexpected " + Describe(events));
        }
    }

    private static void IgnoredInput()
    {
        var menu = NewMenu();
        long revision = menu.Revision;
        var events = Send(menu, Ev.KeyUp(Ev.Enter), Ev.KeyUp(Ev.Esc),
            new PauseInputEvent(PauseInputKind.Char, 'a', 0, 0, PauseMouseButton.None, 0, 0),
            Ev.KeyDown(0x41), Ev.KeyDown(0xFF), Ev.KeyDown(-1), Ev.KeyDown(int.MaxValue));
        Gates.Check(events.Count == 0 && menu.Revision == revision, "key up, chars and unbound keys change nothing");
        // Tab on the menu changes the page; shift+tab goes back.
        events = Send(menu, Ev.KeyDown(Ev.Tab));
        Gates.Check(events.Count == 1 && events[0] is PageChanged { Page: MenuPage.Map } && menu.Page == MenuPage.Map, "tab goes to the map");
        events = Send(menu, Ev.KeyDown(Ev.Tab));
        Gates.Check(menu.Page == MenuPage.Lines && events[0] is PageChanged { Page: MenuPage.Lines }, "tab goes to the lines");
        Send(menu, Ev.KeyDown(Ev.Tab));
        Gates.Check(menu.Page == MenuPage.Menu, "tab wraps to the menu");
        events = Send(menu, Ev.KeyDown(Ev.Tab, 1));
        Gates.Check(menu.Page == MenuPage.Lines, "shift+tab goes back (wraps)");
        events = Send(menu, Ev.KeyDown(Ev.Esc));
        ExpectAction(events, PauseAction.Resume, "esc on any page");
    }

    private static void MapKeys()
    {
        var menu = NewMenu();
        Send(menu, Ev.KeyDown(Ev.Tab));
        var e = Send(menu, Ev.KeyDown(Ev.Left_));
        Gates.Check(e.Count == 1 && e[0] is MapPanned { Dx: -PauseMenu.ArrowPanStep, Dy: 0 }, "left pans: " + Describe(e));
        e = Send(menu, Ev.KeyDown(Ev.Down_));
        Gates.Check(e[0] is MapPanned { Dx: 0, Dy: PauseMenu.ArrowPanStep }, "down pans");
        Gates.Check(menu.MapPanX == -PauseMenu.ArrowPanStep && menu.MapPanY == PauseMenu.ArrowPanStep, "pan accumulates");
        e = Send(menu, Ev.KeyDown(0xBB));
        Gates.Check(e[0] is MapZoomChanged { Steps: 1 }, "+ zooms in");
        e = Send(menu, Ev.KeyDown(0x6D));
        Gates.Check(e[0] is MapZoomChanged { Steps: 0 }, "numpad - zooms out");
        for (int i = 0; i < 30; i++) Send(menu, Ev.KeyDown(0xBD));
        Gates.Check(menu.MapZoomSteps == PauseMenu.MapZoomMin, "zoom out clamps at " + PauseMenu.MapZoomMin);
        e = Send(menu, Ev.KeyDown(0xBD));
        Gates.Check(e.Count == 0, "no event when the zoom does not change");
        for (int i = 0; i < 40; i++) Send(menu, Ev.KeyDown(0x6B));
        Gates.Check(menu.MapZoomSteps == PauseMenu.MapZoomMax, "zoom in clamps at " + PauseMenu.MapZoomMax);
        e = Send(menu, Ev.KeyDown(Ev.Home));
        Gates.Check(e.Count == 1 && e[0] is MapRecentered && menu.MapZoomSteps == 0 && menu.MapPanX == 0 && menu.MapPanY == 0, "home recenters");
    }

    private static List<LineEntry> Lines(int count) => Enumerable.Range(0, count).Select(i => new LineEntry("Line " + i, "#336699", "d" + i)).ToList();

    private static void LinesKeys()
    {
        int rows = MenuGeometry.LinesVisibleRows;
        Gates.Check(rows == 9, "nine visible rows at the design size: " + rows);
        var menu = NewMenu();
        Send(menu, Ev.KeyDown(Ev.Tab), Ev.KeyDown(Ev.Tab));
        Gates.Check(menu.Page == MenuPage.Lines, "on the lines page");
        Gates.Check(Send(menu, Ev.KeyDown(Ev.Down_), Ev.KeyDown(Ev.Enter)).Count == 0, "no lines: keys do nothing");
        menu.SetLines(Lines(30));
        Send(menu, Ev.KeyDown(Ev.Down_));
        Gates.Check(menu.SelectedLine == 0, "first down selects the first row");
        Send(menu, Ev.KeyDown(Ev.Down_));
        Gates.Check(menu.SelectedLine == 1 && menu.ScrollRow == 0, "down moves the selection");
        Send(menu, Ev.KeyDown(Ev.Up_), Ev.KeyDown(Ev.Up_), Ev.KeyDown(Ev.Up_));
        Gates.Check(menu.SelectedLine == 0, "up clamps at the first row");
        Send(menu, Ev.KeyDown(Ev.End));
        Gates.Check(menu.SelectedLine == 29 && menu.ScrollRow == 30 - rows, "end selects the last and scrolls it into view: " + menu.ScrollRow);
        Send(menu, Ev.KeyDown(Ev.Down_));
        Gates.Check(menu.SelectedLine == 29, "down clamps at the last row");
        Send(menu, Ev.KeyDown(Ev.Home));
        Gates.Check(menu.SelectedLine == 0 && menu.ScrollRow == 0, "home returns");
        Send(menu, Ev.KeyDown(0x22));
        Gates.Check(menu.SelectedLine == rows && menu.ScrollRow == 1, "page down moves a page: " + menu.SelectedLine + "/" + menu.ScrollRow);
        Send(menu, Ev.KeyDown(0x21));
        Gates.Check(menu.SelectedLine == 0, "page up moves back");
        var events = Send(menu, Ev.KeyDown(Ev.Down_), Ev.KeyDown(Ev.Enter));
        Gates.Check(events.Count == 1 && events[0] is LineActivated { Index: 1 }, "enter activates the selected line: " + Describe(events));
        menu.SetLines(Lines(2));
        Gates.Check(menu.SelectedLine == 1 && menu.ScrollRow == 0, "shrinking the list keeps the selection valid");
        menu.SetLines(null);
        Gates.Check(menu.Lines.Count == 0 && menu.SelectedLine == -1, "no list: nothing selected");
    }

    private static void HoverFocus()
    {
        var menu = NewMenu();
        var g = menu.Geometry;
        var (x, y) = Ev.Center(g, MenuGeometry.ItemRect(2));
        Send(menu, Ev.Move(x, y));
        Gates.Check(menu.Hover == new MenuTarget(MenuTargetKind.Item, 2) && menu.FocusIndex == 2, "hover focuses the item");
        long revision = menu.Revision;
        Send(menu, Ev.Move(x + 1, y));
        Gates.Check(menu.Revision == revision, "moving inside the same item changes nothing");
        // Keyboard then continues from the hovered item.
        Send(menu, Ev.KeyDown(Ev.Down_));
        Gates.Check(menu.FocusIndex == 3, "keyboard continues from the hover");
        Send(menu, Ev.Move(2, 2));
        Gates.Check(menu.Hover.Kind == MenuTargetKind.None && menu.FocusIndex == 3, "leaving the menu clears hover, keeps the focus");
    }

    private static void PressRelease()
    {
        var menu = NewMenu();
        var g = menu.Geometry;
        var item = new Func<int, (int X, int Y)>(i => Ev.Center(g, MenuGeometry.ItemRect(i)));
        var (x1, y1) = item(1);
        var (x3, y3) = item(3);

        var events = Send(menu, Ev.Move(x1, y1), Ev.Down(x1, y1));
        Gates.Check(events.Count == 0 && menu.Pressed == new MenuTarget(MenuTargetKind.Item, 1), "press alone does not activate");
        events = Send(menu, Ev.Up(x1, y1));
        ExpectAction(events, PauseAction.NewGame, "press and release on the same item");
        Gates.Check(menu.Pressed.Kind == MenuTargetKind.None, "press cleared");

        events = Send(menu, Ev.Down(x1, y1), Ev.Move(x3, y3), Ev.Up(x3, y3));
        Gates.Check(events.Count == 0, "release on another item cancels: " + Describe(events));

        events = Send(menu, Ev.Down(x1, y1), Ev.Up(3, 3));
        Gates.Check(events.Count == 0 && menu.Pressed.Kind == MenuTargetKind.None, "release outside cancels");

        events = Send(menu, Ev.Up(x1, y1));
        Gates.Check(events.Count == 0, "a release without a press does nothing");

        events = Send(menu, Ev.Down(x1, y1, PauseMouseButton.Right), Ev.Up(x1, y1, PauseMouseButton.Right),
            Ev.Down(x1, y1, PauseMouseButton.Middle), Ev.Up(x1, y1, PauseMouseButton.Middle));
        Gates.Check(events.Count == 0, "only the left button acts");

        // Pressing on the panel background and releasing on an item must not activate it.
        var (px, py) = Ev.View(g, 600, 560);
        events = Send(menu, Ev.Down(px, py), Ev.Up(x1, y1));
        Gates.Check(events.Count == 0, "press that did not start on a button cannot activate");
    }

    private static void MouseTabs()
    {
        var menu = NewMenu();
        var g = menu.Geometry;
        var (x, y) = Ev.Center(g, MenuGeometry.TabRect(1));
        var events = Send(menu, Ev.Down(x, y), Ev.Up(x, y));
        Gates.Check(events.Count == 1 && events[0] is PageChanged { Page: MenuPage.Map } && menu.Page == MenuPage.Map, "tab click changes the page");
        events = Send(menu, Ev.Down(x, y), Ev.Up(x, y));
        Gates.Check(events.Count == 0, "clicking the current tab changes nothing");
        // On the map page the item rectangles are inert.
        var (ix, iy) = Ev.Center(g, MenuGeometry.ItemRect(4));
        events = Send(menu, Ev.Down(ix, iy), Ev.Up(ix, iy));
        Gates.Check(events.All(e => e is not ActionRequested), "menu items are not clickable on the map page");
    }

    private static void DragMap()
    {
        var menu = NewMenu();
        Send(menu, Ev.KeyDown(Ev.Tab));
        var g = menu.Geometry;
        var (x, y) = Ev.Center(g, MenuGeometry.Content);
        Send(menu, Ev.Down(x, y));
        var events = Send(menu, Ev.Move(x + 30, y + 10));
        Gates.Check(events.Count == 1 && events[0] is MapPanned, "dragging pans");
        var pan = (MapPanned)events[0];
        double expect = 30 / (g.Metrics.ViewFactor * g.Metrics.CanvasScale);
        Gates.Check(Math.Abs(pan.Dx - expect) < 0.05 && pan.Dy > 0, "pan in design units: " + pan.Dx + " vs " + expect);
        Send(menu, Ev.Up(x + 30, y + 10));
        events = Send(menu, Ev.Move(x + 90, y + 40));
        Gates.Check(events.OfType<MapPanned>().Count() == 0, "no pan after the release");
        // A drag that leaves the map keeps panning until the release.
        Send(menu, Ev.Down(x, y));
        events = Send(menu, Ev.Move(1, 1));
        Gates.Check(events.OfType<MapPanned>().Count() == 1, "the drag follows the pointer outside the map");
        Send(menu, Ev.Up(1, 1));
    }

    private static void Wheel()
    {
        var menu = NewMenu();
        var g = menu.Geometry;
        var (mx, my) = Ev.Center(g, MenuGeometry.Content);
        Gates.Check(Send(menu, Ev.Wheel(mx, my, 120)).Count == 0, "wheel does nothing on the menu page");

        Send(menu, Ev.KeyDown(Ev.Tab));
        var e = Send(menu, Ev.Wheel(mx, my, 120));
        Gates.Check(e.Count == 1 && e[0] is MapZoomChanged { Steps: 1 }, "wheel up zooms in");
        e = Send(menu, Ev.Wheel(mx, my, -240));
        Gates.Check(e.Count == 1 && e[0] is MapZoomChanged { Steps: -1 }, "two notches down");
        e = Send(menu, Ev.Wheel(mx, my, 60), Ev.Wheel(mx, my, 60));
        Gates.Check(e.Count == 1 && e[0] is MapZoomChanged { Steps: 0 }, "partial notches accumulate to one step");
        e = Send(menu, Ev.Wheel(2, 2, 120));
        Gates.Check(e.Count == 0, "wheel outside the map does nothing");

        menu = NewMenu();
        menu.SetLines(Lines(40));
        Send(menu, Ev.KeyDown(Ev.Tab), Ev.KeyDown(Ev.Tab));
        (mx, my) = Ev.Center(menu.Geometry, MenuGeometry.Content);
        Send(menu, Ev.Wheel(mx, my, -120));
        Gates.Check(menu.ScrollRow == PauseMenu.LinesPerNotch, "wheel down scrolls three rows: " + menu.ScrollRow);
        Send(menu, Ev.Wheel(mx, my, 120));
        Gates.Check(menu.ScrollRow == 0, "wheel up scrolls back");
        Send(menu, Ev.Wheel(mx, my, 120));
        Gates.Check(menu.ScrollRow == 0, "clamped at the top");
        for (int i = 0; i < 40; i++) Send(menu, Ev.Wheel(mx, my, -120));
        Gates.Check(menu.ScrollRow == 40 - MenuGeometry.LinesVisibleRows, "clamped at the bottom: " + menu.ScrollRow);
        // A short list does not scroll.
        menu.SetLines(Lines(3));
        Send(menu, Ev.Wheel(mx, my, -120));
        Gates.Check(menu.ScrollRow == 0, "short list stays put");
    }

    private static void DoubleClick()
    {
        var menu = NewMenu();
        var g = menu.Geometry;
        var (x, y) = Ev.Center(g, MenuGeometry.ItemRect(3));
        // A double click arrives as: down, up, double-click, up.
        var events = Send(menu, Ev.Down(x, y), Ev.Up(x, y));
        ExpectAction(events, PauseAction.Options, "first click of a double click");
        events = Send(menu, Ev.Double(x, y), Ev.Up(x, y));
        ExpectAction(events, PauseAction.Options, "second click of a double click presses and releases like a click");

        // On the map it recenters.
        menu = NewMenu();
        Send(menu, Ev.KeyDown(Ev.Tab), Ev.KeyDown(0x6B), Ev.KeyDown(Ev.Left_));
        (x, y) = Ev.Center(menu.Geometry, MenuGeometry.Content);
        events = Send(menu, Ev.Double(x, y));
        Gates.Check(events.Count == 1 && events[0] is MapRecentered && menu.MapZoomSteps == 0 && menu.MapPanX == 0, "double click on the map recenters");
        Send(menu, Ev.Up(x, y));

        // On a line it activates that line.
        menu = NewMenu();
        menu.SetLines(Lines(5));
        Send(menu, Ev.KeyDown(Ev.Tab), Ev.KeyDown(Ev.Tab));
        (x, y) = Ev.Center(menu.Geometry, MenuGeometry.LineRowRect(2));
        events = Send(menu, Ev.Double(x, y));
        Gates.Check(events.Count == 1 && events[0] is LineActivated { Index: 2 } && menu.SelectedLine == 2, "double click on a line activates it: " + Describe(events));
        // A single click selects without activating.
        (x, y) = Ev.Center(menu.Geometry, MenuGeometry.LineRowRect(4));
        events = Send(menu, Ev.Down(x, y), Ev.Up(x, y));
        Gates.Check(events.Count == 0 && menu.SelectedLine == 4, "single click selects");
        // Double click on the background does nothing.
        events = Send(menu, Ev.Double(2, 2));
        Gates.Check(events.Count == 0, "double click outside does nothing");
    }

    private static void ResetAndTransient()
    {
        var menu = NewMenu();
        var g = menu.Geometry;
        var (x, y) = Ev.Center(g, MenuGeometry.ItemRect(2));
        Send(menu, Ev.Move(x, y), Ev.Down(x, y));
        Gates.Check(menu.Pressed.Kind == MenuTargetKind.Item, "pressed");
        menu.ClearTransient();
        Gates.Check(menu.Pressed.Kind == MenuTargetKind.None && menu.Hover.Kind == MenuTargetKind.None, "transient state dropped (after a native dialog)");
        Gates.Check(Send(menu, Ev.Up(x, y)).Count == 0, "the release after a dropped press does nothing");
        Send(menu, Ev.KeyDown(Ev.Down_), Ev.KeyDown(Ev.Tab), Ev.KeyDown(0x6B));
        menu.Reset();
        Gates.Check(menu.Page == MenuPage.Menu && menu.FocusIndex == 0 && menu.MapZoomSteps == 0 && menu.ScrollRow == 0, "reset: first page, first item");
        // A new viewport drops hover and press (coordinates changed meaning).
        Send(menu, Ev.Move(x, y), Ev.Down(x, y));
        menu.SetViewport(new MenuViewport(1280, 720, 1, 1));
        Gates.Check(menu.Pressed.Kind == MenuTargetKind.None, "resize drops the press");
        long revision = menu.Revision;
        menu.SetViewport(new MenuViewport(1280, 720, 1, 1));
        Gates.Check(menu.Revision == revision, "the same viewport is not a change");
    }

    private static void HitTestTable()
    {
        var viewports = new[]
        {
            new MenuViewport(800, 600, 1, 1), new MenuViewport(1280, 720, 1, 1), new MenuViewport(1920, 1080, 1, 1),
            new MenuViewport(1920, 1080, 1, 1.5), new MenuViewport(3440, 1440, 1, 1.5), new MenuViewport(1024, 768, 0.75, 1),
        };
        foreach (var vp in viewports)
        {
            var g = new MenuGeometry(vp);
            string ctx = vp.ViewWidth + "x" + vp.ViewHeight + " s=" + vp.Scale + "/" + vp.UiScale;
            for (int i = 0; i < MenuGeometry.ItemCount; i++)
            {
                var (x, y) = Ev.Center(g, MenuGeometry.ItemRect(i));
                Gates.Check(g.HitTest(MenuPage.Menu, x, y) == new MenuTarget(MenuTargetKind.Item, i), ctx + ": item " + i);
                Gates.Check(g.HitTest(MenuPage.Map, x, y) is { Kind: MenuTargetKind.Map }, ctx + ": item area is the map on the map page");
            }
            for (int i = 0; i < MenuGeometry.TabCount; i++)
            {
                var (x, y) = Ev.Center(g, MenuGeometry.TabRect(i));
                foreach (var page in new[] { MenuPage.Menu, MenuPage.Map, MenuPage.Lines })
                    Gates.Check(g.HitTest(page, x, y) == new MenuTarget(MenuTargetKind.Tab, i), ctx + ": tab " + i + " on " + page);
            }
            var (gx, gy) = Ev.View(g, 100, 136 + 60 + 6);   // the gap between two items
            Gates.Check(g.HitTest(MenuPage.Menu, gx, gy).Kind == MenuTargetKind.Panel, ctx + ": gap between items is the panel");
            var (ox, oy) = Ev.View(g, -5, 300);
            Gates.Check(g.HitTest(MenuPage.Menu, ox, oy).Kind == MenuTargetKind.None, ctx + ": left of the panel");
            (ox, oy) = Ev.View(g, 300, MenuMetrics.DesignHeight + 2);
            Gates.Check(g.HitTest(MenuPage.Menu, ox, oy).Kind == MenuTargetKind.None, ctx + ": below the panel");
            Gates.Check(g.HitTest(MenuPage.Menu, -50, -50).Kind == MenuTargetKind.None, ctx + ": outside the view");
            Gates.Check(g.HitTest(MenuPage.Menu, vp.ViewWidth + 50, vp.ViewHeight + 50).Kind == MenuTargetKind.None, ctx + ": beyond the view");
            var (cx, cy) = Ev.Center(g, MenuGeometry.Content);
            Gates.Check(g.HitTest(MenuPage.Lines, cx, cy, 0, 0).Kind == MenuTargetKind.LinesList, ctx + ": empty list is the list area");
            var (lx, ly) = Ev.Center(g, MenuGeometry.LineRowRect(1));
            Gates.Check(g.HitTest(MenuPage.Lines, lx, ly, 7, 20) == new MenuTarget(MenuTargetKind.LineRow, 8), ctx + ": row index is absolute (scroll 7 + 1)");
            Gates.Check(g.HitTest(MenuPage.Lines, lx, ly, 0, 1).Kind == MenuTargetKind.LinesList, ctx + ": below the last row is the list area");
        }
    }

    private static void HostileInput()
    {
        var menu = NewMenu();
        var rng = new Random(7);
        long ticks = 0;
        for (int i = 0; i < 20000; i++)
        {
            var kind = (PauseInputKind)rng.Next(0, 12);
            var button = (PauseMouseButton)rng.Next(0, 6);
            var e = new PauseInputEvent(kind, rng.Next(-5, 0x120), rng.Next(-3000, 6000), rng.Next(-3000, 6000), button, rng.Next(-1000, 1000) * (rng.Next(3) == 0 ? 120 : 1), rng.Next(0, 9));
            foreach (var ev in menu.Handle(e)) ticks++;
            Gates.Check(menu.FocusIndex is >= 0 and < MenuGeometry.ItemCount, "focus stays valid");
            Gates.Check(menu.ScrollRow >= 0 && menu.MapZoomSteps is >= PauseMenu.MapZoomMin and <= PauseMenu.MapZoomMax, "scroll and zoom stay in range");
            if (i % 997 == 0) menu.SetLines(Lines(rng.Next(0, 60)));
            if (i % 499 == 0) { var scene = menu.BuildScene(); Gates.Check(scene.Commands.Count > 5, "still drawable"); }
        }
        Gates.Check(ticks > 0, "the fuzz produced events");
        Gates.Check(menu.SelectedLine < menu.Lines.Count, "selection valid");
    }
}
