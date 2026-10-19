namespace OpenHud.Pause;

// What the menu asks of its owner after an input event.
public abstract record MenuEvent;
public sealed record ActionRequested(PauseAction Action) : MenuEvent;
public sealed record PageChanged(MenuPage Page) : MenuEvent;
// Zoom steps changed (absolute value, 0 = the default zoom).
public sealed record MapZoomChanged(int Steps) : MenuEvent;
// The map content moved by this much, in design units (cursor-following).
public sealed record MapPanned(double Dx, double Dy) : MenuEvent;
public sealed record MapRecentered : MenuEvent;
public sealed record LineActivated(int Index) : MenuEvent;

// A row of the lines page; the integrator supplies them from the radar data.
public sealed record LineEntry(string Label, string Color, string Detail = "", string? Mode = null);

// The model of the menu: pages, focus, hover/press state, scrolling, and the
// keyboard and mouse rules. It holds no game state and does not know the state
// machine; events are returned to the caller, and BuildScene turns the state into
// the draw list. Single-threaded by design (the UI thread of the game).
public sealed class PauseMenu
{
    public const int MapZoomMin = -8;
    public const int MapZoomMax = 12;
    public const double ArrowPanStep = 40;
    public const int LinesPerNotch = 3;

    private const int VkTab = 0x09, VkEnter = 0x0D, VkEscape = 0x1B, VkSpace = 0x20, VkPageUp = 0x21, VkPageDown = 0x22,
        VkEnd = 0x23, VkHome = 0x24, VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28,
        VkAdd = 0x6B, VkSubtract = 0x6D, VkOemPlus = 0xBB, VkOemMinus = 0xBD;
    private const int ShiftFlag = 1;

    private readonly Func<string, string>? lookup;
    private MenuGeometry geometry = new(MenuViewport.Default);
    private IReadOnlyList<LineEntry> lines = Array.Empty<LineEntry>();
    private int wheelRemainder;
    private (int X, int Y)? dragLast;

    public PauseMenu(Func<string, string>? lookup = null, PauseIdentity? identity = null, string language = "en")
    {
        this.lookup = lookup;
        Identity = identity ?? PauseIdentity.Generic;
        Language = language;
    }

    public PauseIdentity Identity { get; private set; }
    public string Language { get; private set; }
    public MenuViewport Viewport { get; private set; } = MenuViewport.Default;
    public MenuGeometry Geometry => geometry;
    public MenuPage Page { get; private set; }
    // The focused item of the menu page, in PauseAction order.
    public int FocusIndex { get; private set; }
    public MenuTarget Hover { get; private set; } = MenuTarget.None;
    public MenuTarget Pressed { get; private set; } = MenuTarget.None;
    public int MapZoomSteps { get; private set; }
    public double MapPanX { get; private set; }
    public double MapPanY { get; private set; }
    public int ScrollRow { get; private set; }
    public int SelectedLine { get; private set; } = -1;
    public IReadOnlyList<LineEntry> Lines => lines;
    // "openHUD 0.1.0 · OMSI 2.3.004": the footer's version text, set by the integrator (null = none shown).
    public string? FooterVersions { get; private set; }
    // Increases whenever something visible changed; the machine republishes the scene on a change.
    public long Revision { get; private set; }

    internal Func<string, string>? Lookup => lookup;

    public void SetViewport(MenuViewport viewport)
    {
        var normalized = viewport.Normalized();
        if (normalized == Viewport) return;
        Viewport = normalized;
        geometry = new MenuGeometry(normalized);
        ClearTransient();
        Touch();
    }

    public void SetIdentity(PauseIdentity? identity) { Identity = identity ?? PauseIdentity.Generic; Touch(); }

    public void SetFooterVersions(string? text)
    {
        var clean = string.IsNullOrWhiteSpace(text) ? null : text;
        if (clean == FooterVersions) return;
        FooterVersions = clean;
        Touch();
    }

    public void SetLanguage(string language) { Language = language ?? ""; Touch(); }

    public void SetLines(IReadOnlyList<LineEntry>? entries)
    {
        lines = entries ?? Array.Empty<LineEntry>();
        ScrollRow = Math.Clamp(ScrollRow, 0, MaxScroll);
        if (SelectedLine >= lines.Count) SelectedLine = lines.Count - 1;
        Touch();
    }

    // Back to the first page with the first item focused (a new opening of the menu).
    public void Reset()
    {
        Page = MenuPage.Menu;
        FocusIndex = 0;
        ScrollRow = 0;
        SelectedLine = -1;
        MapZoomSteps = 0;
        MapPanX = MapPanY = 0;
        ClearTransient();
        Touch();
    }

    // Drops hover, press and drag (after a native dialog or a resize: the pointer position is unknown).
    public void ClearTransient()
    {
        Hover = MenuTarget.None;
        Pressed = MenuTarget.None;
        dragLast = null;
        wheelRemainder = 0;
    }

    public MenuScene BuildScene() => MenuRenderer.Build(this);

    private int MaxScroll => Math.Max(0, lines.Count - MenuGeometry.LinesVisibleRows);

    private void Touch() => Revision++;

    public IReadOnlyList<MenuEvent> Handle(PauseInputEvent input)
    {
        var events = new List<MenuEvent>();
        switch (input.Kind)
        {
            case PauseInputKind.KeyDown: Key(input, events); break;
            case PauseInputKind.MouseMove: Move(input, events); break;
            case PauseInputKind.ButtonDown:
                if (input.Button == PauseMouseButton.Left) Down(input);
                break;
            case PauseInputKind.DoubleClick:
                if (input.Button == PauseMouseButton.Left || input.Button == PauseMouseButton.None) Double(input, events);
                break;
            case PauseInputKind.ButtonUp:
                if (input.Button == PauseMouseButton.Left) Up(input, events);
                break;
            case PauseInputKind.Wheel: Wheel(input, events); break;
        }
        return events;
    }

    // ---- keyboard

    private void Key(PauseInputEvent input, List<MenuEvent> events)
    {
        switch (input.Key)
        {
            case VkEscape: events.Add(new ActionRequested(PauseAction.Resume)); return;
            case VkTab: SetPage(Cycle(Page, (input.Modifiers & ShiftFlag) != 0 ? -1 : 1), events); return;
        }
        switch (Page)
        {
            case MenuPage.Menu: MenuKey(input.Key, events); break;
            case MenuPage.Map: MapKey(input.Key, events); break;
            case MenuPage.Lines: LinesKey(input.Key, events); break;
        }
    }

    private static MenuPage Cycle(MenuPage page, int step) => (MenuPage)(((int)page + step + MenuGeometry.TabCount) % MenuGeometry.TabCount);

    private void MenuKey(int key, List<MenuEvent> events)
    {
        switch (key)
        {
            case VkDown: SetFocus((FocusIndex + 1) % MenuGeometry.ItemCount); break;
            case VkUp: SetFocus((FocusIndex + MenuGeometry.ItemCount - 1) % MenuGeometry.ItemCount); break;
            case VkHome: SetFocus(0); break;
            case VkEnd: SetFocus(MenuGeometry.ItemCount - 1); break;
            case VkEnter:
            case VkSpace: events.Add(new ActionRequested((PauseAction)FocusIndex)); break;
        }
    }

    private void SetFocus(int index)
    {
        if (index == FocusIndex) return;
        FocusIndex = index;
        Touch();
    }

    private void MapKey(int key, List<MenuEvent> events)
    {
        switch (key)
        {
            case VkLeft: Pan(-ArrowPanStep, 0, events); break;
            case VkRight: Pan(ArrowPanStep, 0, events); break;
            case VkUp: Pan(0, -ArrowPanStep, events); break;
            case VkDown: Pan(0, ArrowPanStep, events); break;
            case VkAdd:
            case VkOemPlus: Zoom(1, events); break;
            case VkSubtract:
            case VkOemMinus: Zoom(-1, events); break;
            case VkHome: Recenter(events); break;
        }
    }

    private void LinesKey(int key, List<MenuEvent> events)
    {
        if (lines.Count == 0) return;
        int rows = MenuGeometry.LinesVisibleRows;
        int from = SelectedLine < 0 ? 0 : SelectedLine;
        switch (key)
        {
            case VkDown: SelectLine(SelectedLine < 0 ? 0 : SelectedLine + 1); break;
            case VkUp: SelectLine(SelectedLine < 0 ? 0 : SelectedLine - 1); break;
            case VkPageDown: SelectLine(from + rows); break;
            case VkPageUp: SelectLine(from - rows); break;
            case VkHome: SelectLine(0); break;
            case VkEnd: SelectLine(lines.Count - 1); break;
            case VkEnter: if (SelectedLine >= 0) events.Add(new LineActivated(SelectedLine)); break;
        }
    }

    // Selects a row (clamped) and scrolls it into view.
    private void SelectLine(int index)
    {
        index = Math.Clamp(index, 0, lines.Count - 1);
        int rows = MenuGeometry.LinesVisibleRows;
        int scroll = ScrollRow;
        if (index < scroll) scroll = index;
        else if (index >= scroll + rows) scroll = index - rows + 1;
        scroll = Math.Clamp(scroll, 0, MaxScroll);
        if (index == SelectedLine && scroll == ScrollRow) return;
        SelectedLine = index;
        ScrollRow = scroll;
        Touch();
    }

    // ---- mouse

    private void Move(PauseInputEvent input, List<MenuEvent> events)
    {
        if (dragLast is { } last)
        {
            var (x0, y0) = geometry.ViewToDesign(last.X, last.Y);
            var (x1, y1) = geometry.ViewToDesign(input.X, input.Y);
            dragLast = (input.X, input.Y);
            Pan(x1 - x0, y1 - y0, events);
            return;
        }
        var target = geometry.HitTest(Page, input.X, input.Y, ScrollRow, lines.Count);
        if (target == Hover) return;
        Hover = target;
        if (target.Kind == MenuTargetKind.Item && Page == MenuPage.Menu) FocusIndex = target.Index;
        Touch();
    }

    private void Down(PauseInputEvent input)
    {
        var target = geometry.HitTest(Page, input.X, input.Y, ScrollRow, lines.Count);
        Hover = target;
        if (target.IsButton)
        {
            if (target.Kind == MenuTargetKind.Item) FocusIndex = target.Index;
            Pressed = target;
        }
        else if (target.Kind == MenuTargetKind.Map) dragLast = (input.X, input.Y);
        else if (target.Kind == MenuTargetKind.LineRow && target.Index != SelectedLine) SelectLine(target.Index);
        Touch();
    }

    // A double click replaces the second button-down: on a button it presses (the
    // release activates, as for a single click); elsewhere it has its own meaning.
    private void Double(PauseInputEvent input, List<MenuEvent> events)
    {
        var target = geometry.HitTest(Page, input.X, input.Y, ScrollRow, lines.Count);
        switch (target.Kind)
        {
            case MenuTargetKind.Item:
            case MenuTargetKind.Tab: Down(input); break;
            case MenuTargetKind.Map: dragLast = null; Recenter(events); break;
            case MenuTargetKind.LineRow: SelectLine(target.Index); events.Add(new LineActivated(target.Index)); break;
        }
    }

    private void Up(PauseInputEvent input, List<MenuEvent> events)
    {
        var pressed = Pressed;
        Pressed = MenuTarget.None;
        dragLast = null;
        var target = geometry.HitTest(Page, input.X, input.Y, ScrollRow, lines.Count);
        Hover = target;
        Touch();
        // Releasing somewhere else cancels the press.
        if (!pressed.IsButton || pressed != target) return;
        if (target.Kind == MenuTargetKind.Item) events.Add(new ActionRequested((PauseAction)target.Index));
        else SetPage((MenuPage)target.Index, events);
    }

    private void Wheel(PauseInputEvent input, List<MenuEvent> events)
    {
        wheelRemainder += input.Delta;
        int notches = wheelRemainder / 120;
        if (notches == 0) return;
        wheelRemainder -= notches * 120;
        var target = geometry.HitTest(Page, input.X, input.Y, ScrollRow, lines.Count);
        if (Page == MenuPage.Map && target.Kind == MenuTargetKind.Map) Zoom(notches, events);
        else if (Page == MenuPage.Lines && (target.Kind == MenuTargetKind.LinesList || target.Kind == MenuTargetKind.LineRow))
        {
            int scroll = Math.Clamp(ScrollRow - notches * LinesPerNotch, 0, MaxScroll);
            if (scroll != ScrollRow) { ScrollRow = scroll; Touch(); }
        }
    }

    // ---- shared moves

    private void SetPage(MenuPage page, List<MenuEvent> events)
    {
        if (page == Page) return;
        Page = page;
        ClearTransient();
        Touch();
        events.Add(new PageChanged(page));
    }

    private void Zoom(int delta, List<MenuEvent> events)
    {
        int steps = Math.Clamp(MapZoomSteps + delta, MapZoomMin, MapZoomMax);
        if (steps == MapZoomSteps) return;
        MapZoomSteps = steps;
        Touch();
        events.Add(new MapZoomChanged(steps));
    }

    private void Pan(double dx, double dy, List<MenuEvent> events)
    {
        if (dx == 0 && dy == 0) return;
        MapPanX += dx;
        MapPanY += dy;
        Touch();
        events.Add(new MapPanned(dx, dy));
    }

    private void Recenter(List<MenuEvent> events)
    {
        MapPanX = MapPanY = 0;
        MapZoomSteps = 0;
        Touch();
        events.Add(new MapRecentered());
    }
}
