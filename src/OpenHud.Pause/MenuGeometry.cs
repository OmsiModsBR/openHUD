namespace OpenHud.Pause;

public enum MenuPage { Menu, Map, Lines }

public enum MenuTargetKind { None, Panel, Item, Tab, Map, LinesList, LineRow }

// What a point of the view lies on. Index: the item (PauseAction order), the tab
// (MenuPage order) or the line (absolute row).
public readonly record struct MenuTarget(MenuTargetKind Kind, int Index = 0)
{
    public static readonly MenuTarget None = new(MenuTargetKind.None);
    public bool IsButton => Kind == MenuTargetKind.Item || Kind == MenuTargetKind.Tab;
}

// The real view the menu is shown in and the user's scale settings.
public readonly record struct MenuViewport(int ViewWidth, int ViewHeight, double Scale, double UiScale)
{
    public static readonly MenuViewport Default = new(MenuScene.CanvasWidth, MenuScene.CanvasHeight, 1, 1);

    internal MenuViewport Normalized()
    {
        static double Clean(double v, double fallback, double min, double max) => double.IsNaN(v) || double.IsInfinity(v) || v <= 0 ? fallback : Math.Clamp(v, min, max);
        return new MenuViewport(ViewWidth > 0 ? ViewWidth : MenuScene.CanvasWidth, ViewHeight > 0 ? ViewHeight : MenuScene.CanvasHeight,
            Clean(Scale, 1, 0.25, 4), Clean(UiScale, 1, 0.25, 8));
    }
}

// How big the menu is and where it sits. The design is a 904x600 block; the user
// scale s = min(scale*ui_scale, fit) never goes below scale, with
// fit = min(width/904, height*0.94/600) on the real view. The scene canvas is a
// virtual 1920x1080 that the draw layer maps to the view by k = min(w/1920, h/1080),
// so the block is s/k canvas units per design unit; it is clamped so the block
// always lies inside the canvas (only a view too small for the block needs it).
public sealed record MenuMetrics(MenuViewport Viewport, double Fit, double Scale, double ViewFactor, double CanvasScale, bool Clamped,
    double OriginX, double OriginY, double ViewOffsetX, double ViewOffsetY)
{
    public const double DesignWidth = 904;
    public const double DesignHeight = 600;
    private const double CanvasMargin = 0.98;
    private const double MaxCanvasScale = 1.728; // min(1920*0.98/904, 1080*0.96/600): the block keeps a margin of 2% / 4%

    public static MenuMetrics Compute(MenuViewport requested)
    {
        var v = requested.Normalized();
        double fit = Math.Min(v.ViewWidth / DesignWidth, v.ViewHeight * 0.94 / DesignHeight);
        double s = Math.Max(v.Scale, Math.Min(v.Scale * v.UiScale, fit));
        double k = Math.Min(v.ViewWidth / (double)MenuScene.CanvasWidth, v.ViewHeight / (double)MenuScene.CanvasHeight);
        double cs = s / k;
        bool clamped = cs > MaxCanvasScale;
        if (clamped) cs = MaxCanvasScale;
        double ox = (MenuScene.CanvasWidth - DesignWidth * cs) / 2;
        double oy = (MenuScene.CanvasHeight - DesignHeight * cs) / 2;
        // The draw layer centers the canvas in the view.
        double vx = (v.ViewWidth - MenuScene.CanvasWidth * k) / 2;
        double vy = (v.ViewHeight - MenuScene.CanvasHeight * k) / 2;
        return new MenuMetrics(v, fit, s, k, cs, clamped, ox, oy, vx, vy);
    }
}

// The layout of the menu in design units (origin at the block's top left) and its
// mapping to canvas and view coordinates. Pure geometry: hit-testing and drawing
// both use it, so what is drawn is what is hit.
public sealed class MenuGeometry
{
    public const int ItemCount = 5;
    public const int TabCount = 3;
    public const double LineRowHeight = 40;

    public MenuMetrics Metrics { get; }

    public MenuGeometry(MenuViewport viewport) { Metrics = MenuMetrics.Compute(viewport); }

    public static readonly MenuRect Panel = new(0, 0, MenuMetrics.DesignWidth, MenuMetrics.DesignHeight);
    public static readonly MenuRect Header = new(0, 0, 904, 72);
    public static readonly MenuRect Symbol = new(24, 12, 48, 48);
    public static readonly MenuRect Content = new(24, 136, 856, 396);
    public static readonly MenuRect Footer = new(24, 548, 856, 36);
    public static readonly MenuRect LegendPanel = new(348, 136, 532, 396);

    public static MenuRect TabRect(int index) => new(24 + index * 138, 84, 130, 40);

    public static MenuRect ItemRect(int index) => new(24, 136 + index * 72, 300, 60);

    // The rows of the lines list that fit under the list's top padding.
    public static int LinesVisibleRows => (int)((Content.H - 16) / LineRowHeight);

    public static MenuRect LineRowRect(int visibleIndex) => new(Content.X + 8, Content.Y + 8 + visibleIndex * LineRowHeight, Content.W - 32, LineRowHeight - 4);

    public MenuRect ToCanvas(MenuRect design) => new(
        Round(Metrics.OriginX + design.X * Metrics.CanvasScale), Round(Metrics.OriginY + design.Y * Metrics.CanvasScale),
        Round(design.W * Metrics.CanvasScale), Round(design.H * Metrics.CanvasScale));

    public double Length(double design) => Round(design * Metrics.CanvasScale);

    public (double X, double Y) ToCanvasPoint(double designX, double designY) => (Round(Metrics.OriginX + designX * Metrics.CanvasScale), Round(Metrics.OriginY + designY * Metrics.CanvasScale));

    // A point of the game's view (client pixels) in design units.
    public (double X, double Y) ViewToDesign(int viewX, int viewY)
    {
        double canvasX = (viewX - Metrics.ViewOffsetX) / Metrics.ViewFactor;
        double canvasY = (viewY - Metrics.ViewOffsetY) / Metrics.ViewFactor;
        return ((canvasX - Metrics.OriginX) / Metrics.CanvasScale, (canvasY - Metrics.OriginY) / Metrics.CanvasScale);
    }

    // What lies under a view point on a page. Lines: rows are absolute indexes starting at scrollRow.
    public MenuTarget HitTest(MenuPage page, int viewX, int viewY, int scrollRow = 0, int lineCount = 0)
    {
        var (x, y) = ViewToDesign(viewX, viewY);
        if (!Panel.Contains(x, y)) return MenuTarget.None;
        for (int i = 0; i < TabCount; i++)
            if (TabRect(i).Contains(x, y)) return new MenuTarget(MenuTargetKind.Tab, i);
        switch (page)
        {
            case MenuPage.Menu:
                for (int i = 0; i < ItemCount; i++)
                    if (ItemRect(i).Contains(x, y)) return new MenuTarget(MenuTargetKind.Item, i);
                break;
            case MenuPage.Map:
                if (Content.Contains(x, y)) return new MenuTarget(MenuTargetKind.Map);
                break;
            case MenuPage.Lines:
                if (Content.Contains(x, y))
                {
                    for (int i = 0; i < LinesVisibleRows && scrollRow + i < lineCount; i++)
                        if (LineRowRect(i).Contains(x, y)) return new MenuTarget(MenuTargetKind.LineRow, scrollRow + i);
                    return new MenuTarget(MenuTargetKind.LinesList);
                }
                break;
        }
        return new MenuTarget(MenuTargetKind.Panel);
    }

    private static double Round(double v) => Math.Round(v, 2);
}
