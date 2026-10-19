namespace OpenHud.Pause;

// Turns the state of a PauseMenu into the draw list. Order is fixed (dimmer,
// panel, header, tabs, page, footer) so equal state gives an equal list, and every
// coordinate lies inside the 1920x1080 canvas by construction of MenuGeometry.
internal static class MenuRenderer
{
    private const string White = "#ffffff";
    private const string Muted = "#ffffffb3";
    private const string Dimmer = "#000000a6";
    private const string PanelFill = "#14171cf2";
    private const string Cell = "#ffffff14";

    public static MenuScene Build(PauseMenu menu)
    {
        var g = menu.Geometry;
        var identity = menu.Identity;
        string accent = Hex.Or(identity.Accent, PauseIdentity.DefaultAccent);
        string line = Hex.Or(identity.Line, PauseIdentity.DefaultLine);
        double cs = g.Metrics.CanvasScale;
        var cmds = new List<MenuDraw>(96);
        var regions = new List<MenuRegion>();
        string T(string key) => PauseStrings.Text(menu.Lookup, key);

        cmds.Add(new RectDraw(0, 0, MenuScene.CanvasWidth, MenuScene.CanvasHeight, Dimmer));

        var panel = g.ToCanvas(MenuGeometry.Panel);
        cmds.Add(new RectDraw(panel.X, panel.Y, panel.W, panel.H, PanelFill, Hex.WithAlpha(line, 0x40), Math.Max(1, g.Length(2)), g.Length(16)));

        Header(menu, g, cmds, identity, accent, line, T);
        Tabs(menu, g, cmds, accent, T);

        switch (menu.Page)
        {
            case MenuPage.Menu: MenuPageContent(menu, g, cmds, identity, accent, T); break;
            case MenuPage.Map:
                MapPageContent(g, cmds, line, T);
                regions.Add(new MenuRegion("map", g.ToCanvas(MenuGeometry.Content)));
                break;
            case MenuPage.Lines:
                LinesPageContent(menu, g, cmds, accent, line, T);
                regions.Add(new MenuRegion("lines", g.ToCanvas(MenuGeometry.Content)));
                break;
        }

        Footer(menu, g, cmds, identity, T);

        return new MenuScene(MenuSceneMode.Menu, MenuScene.CanvasWidth, MenuScene.CanvasHeight, true, cmds, regions);
    }

    // The footer: the hint on the left; on the right the versions and, when the package has one, its credit line.
    private static void Footer(PauseMenu menu, MenuGeometry g, List<MenuDraw> cmds, PauseIdentity identity, Func<string, string> t)
    {
        var footer = g.ToCanvas(MenuGeometry.Footer);
        double cs = g.Metrics.CanvasScale;
        var versions = menu.FooterVersions;
        var credit = string.IsNullOrWhiteSpace(identity.Credit) ? null : identity.Credit;
        var rightWidth = versions is null && credit is null ? 0 : Math.Min(footer.W * 0.42, g.Length(360));
        cmds.Add(new TextDraw(footer.X, footer.Y + footer.H / 2, t(menu.Page == MenuPage.Map ? PauseStrings.MapHint : PauseStrings.Hint),
            14 * cs, TextRole.Condensed, Muted, TextAlign.Left, TextBase.Middle, footer.W - rightWidth - (rightWidth > 0 ? g.Length(16) : 0)));
        if (rightWidth <= 0) return;
        var right = footer.X + footer.W;
        var dim = Hex.WithAlpha(White, 0x80);
        if (versions is not null && credit is not null)
        {
            cmds.Add(new TextDraw(right, footer.Y + footer.H * 0.3, versions, 12 * cs, TextRole.Condensed, Muted, TextAlign.Right, TextBase.Middle, rightWidth));
            cmds.Add(new TextDraw(right, footer.Y + footer.H * 0.74, credit, 12 * cs, TextRole.Condensed, dim, TextAlign.Right, TextBase.Middle, rightWidth));
        }
        else
            cmds.Add(new TextDraw(right, footer.Y + footer.H / 2, (versions ?? credit)!, 12 * cs, TextRole.Condensed, versions is not null ? Muted : dim, TextAlign.Right, TextBase.Middle, rightWidth));
    }

    // A symbol, logo or icon fitted into a slot (already in canvas units). Pictures stay ImageDraw records.
    private static void DrawGlyph(List<MenuDraw> cmds, OpenHud.Art.Glyph glyph, MenuRect slot)
    {
        if (glyph.IsEmpty) return;
        foreach (var command in glyph.Commands(slot.X, slot.Y, slot.W, slot.H))
            cmds.Add(command is OpenHud.Scene.ImageCommand image ? new ImageDraw(image.Id, image.X, image.Y, image.W, image.H, image.Opacity ?? 1, image.Tint) : new RawDraw(command));
    }

    // The OMSI 2 mark: the package's picture, else a generic outlined tag with a steering wheel. openHUD ships no logo of the
    // game and writes no brand text of its own, so the generic mark is a neutral shape.
    private static void Marks(MenuGeometry g, List<MenuDraw> cmds, PauseIdentity identity, string line)
    {
        var omsi = g.ToCanvas(MenuGeometry.Omsi2Mark);
        if (!identity.Omsi2Mark.IsEmpty) DrawGlyph(cmds, identity.Omsi2Mark, omsi);
        else
        {
            static double R(double v) => Math.Round(v, 2); // the menu's JSON rounds to two decimals; so do the shapes
            double w = Math.Min(omsi.W, g.Length(64)), h = Math.Min(omsi.H, g.Length(32));
            double x = R(omsi.X + omsi.W - w), y = R(omsi.Y + (omsi.H - h) / 2);
            double cx = R(x + w / 2), cy = R(y + h / 2), r = R(h * 0.3), hub = R(r * 0.28);
            var stroke = Hex.WithAlpha(line, 0xa0);
            double sw = Math.Max(1, g.Length(1.5));
            cmds.Add(new RectDraw(x, y, w, h, null, Hex.WithAlpha(line, 0x60), sw, g.Length(8)));
            cmds.Add(new CircleDraw(cx, cy, r, null, stroke, sw));
            cmds.Add(new CircleDraw(cx, cy, hub, stroke));
            cmds.Add(new LineDraw(R(cx - r), cy, R(cx - hub), cy, stroke, sw));
            cmds.Add(new LineDraw(R(cx + hub), cy, R(cx + r), cy, stroke, sw));
            cmds.Add(new LineDraw(cx, R(cy + hub), cx, R(cy + r), stroke, sw));
        }
        if (!identity.OpenOmsiMark.IsEmpty) DrawGlyph(cmds, identity.OpenOmsiMark, g.ToCanvas(MenuGeometry.OpenOmsiMark));
    }

    private static void Header(PauseMenu menu, MenuGeometry g, List<MenuDraw> cmds, PauseIdentity identity, string accent, string line, Func<string, string> t)
    {
        var symbol = g.ToCanvas(MenuGeometry.Symbol);
        if (!identity.SymbolGlyph.IsEmpty)
            DrawGlyph(cmds, identity.SymbolGlyph, symbol);
        else
        {
            // Neutral default symbol: an accent disc with a pause glyph.
            double cx = symbol.X + symbol.W / 2, cy = symbol.Y + symbol.H / 2;
            cmds.Add(new CircleDraw(cx, cy, symbol.W / 2, accent));
            double bar = symbol.W * 0.12, height = symbol.H * 0.4;
            cmds.Add(new RectDraw(Math.Round(cx - bar * 1.6, 2), Math.Round(cy - height / 2, 2), Math.Round(bar, 2), Math.Round(height, 2), White, null, 0, Math.Round(bar / 4, 2)));
            cmds.Add(new RectDraw(Math.Round(cx + bar * 0.6, 2), Math.Round(cy - height / 2, 2), Math.Round(bar, 2), Math.Round(height, 2), White, null, 0, Math.Round(bar / 4, 2)));
        }
        string title = identity.TitleFor(menu.Language) ?? t(PauseStrings.Title);
        var (tx, ty) = g.ToCanvasPoint(88, 36);
        cmds.Add(new TextDraw(tx, ty, title, 28 * g.Metrics.CanvasScale, TextRole.Bold, White, TextAlign.Left, TextBase.Middle, g.Length(530)));
        Marks(g, cmds, identity, line);
        var (x1, y) = g.ToCanvasPoint(24, 72);
        var (x2, _) = g.ToCanvasPoint(880, 72);
        cmds.Add(new LineDraw(x1, y, x2, y, Hex.WithAlpha(line, 0x30), Math.Max(1, g.Length(1))));
    }

    private static void Tabs(PauseMenu menu, MenuGeometry g, List<MenuDraw> cmds, string accent, Func<string, string> t)
    {
        string[] keys = { PauseStrings.TabMenu, PauseStrings.TabMap, PauseStrings.TabLines };
        for (int i = 0; i < MenuGeometry.TabCount; i++)
        {
            var rect = g.ToCanvas(MenuGeometry.TabRect(i));
            var target = new MenuTarget(MenuTargetKind.Tab, i);
            bool active = (int)menu.Page == i;
            bool hover = menu.Hover == target;
            bool pressed = hover && menu.Pressed == target;
            string fill = pressed ? Hex.WithAlpha(accent, 0xcc) : active ? accent : hover ? Hex.WithAlpha(accent, 0x55) : Cell;
            cmds.Add(new RectDraw(rect.X, rect.Y, rect.W, rect.H, fill, null, 0, g.Length(10)));
            cmds.Add(new TextDraw(rect.X + rect.W / 2, rect.Y + rect.H / 2, t(keys[i]), 18 * g.Metrics.CanvasScale, TextRole.Medium, White, TextAlign.Center, TextBase.Middle, rect.W - g.Length(16)));
        }
    }

    private static void MenuPageContent(PauseMenu menu, MenuGeometry g, List<MenuDraw> cmds, PauseIdentity identity, string accent, Func<string, string> t)
    {
        string[] keys = { PauseStrings.Resume, PauseStrings.NewGame, PauseStrings.Save, PauseStrings.Options, PauseStrings.Quit };
        double cs = g.Metrics.CanvasScale;
        for (int i = 0; i < MenuGeometry.ItemCount; i++)
        {
            var rect = g.ToCanvas(MenuGeometry.ItemRect(i));
            var target = new MenuTarget(MenuTargetKind.Item, i);
            bool focused = menu.FocusIndex == i;
            bool pressed = menu.Pressed == target && menu.Hover == target;
            string fill = pressed ? Hex.WithAlpha(accent, 0xcc) : focused ? Hex.WithAlpha(accent, 0x40) : Cell;
            cmds.Add(new RectDraw(rect.X, rect.Y, rect.W, rect.H, fill, null, 0, g.Length(12)));
            if (focused) cmds.Add(new RectDraw(rect.X, rect.Y + g.Length(10), g.Length(4), rect.H - g.Length(20), accent, null, 0, g.Length(2)));
            cmds.Add(new TextDraw(rect.X + g.Length(24), rect.Y + rect.H / 2, t(keys[i]), 22 * cs, TextRole.Bold, White, TextAlign.Left, TextBase.Middle, rect.W - g.Length(40)));
        }

        var legend = identity.Legend.Where(e => e is not null).Take(9).ToList();
        if (legend.Count == 0) return;
        var panel = g.ToCanvas(MenuGeometry.LegendPanel);
        cmds.Add(new RectDraw(panel.X, panel.Y, panel.W, panel.H, "#ffffff0d", null, 0, g.Length(12)));
        cmds.Add(new TextDraw(panel.X + g.Length(20), panel.Y + g.Length(28), t(PauseStrings.Legend), 20 * cs, TextRole.Bold, White, TextAlign.Left, TextBase.Middle, panel.W - g.Length(40)));
        for (int i = 0; i < legend.Count; i++)
        {
            string? text = PauseIdentity.Pick(legend[i].Texts, menu.Language);
            if (text is null) continue;
            double y = panel.Y + g.Length(64 + i * 36);
            if (legend[i].Icon is { IsEmpty: false } icon) DrawGlyph(cmds, icon, new MenuRect(panel.X + g.Length(18), y - g.Length(2), g.Length(22), g.Length(22)));
            else cmds.Add(new RectDraw(panel.X + g.Length(20), y, g.Length(18), g.Length(18), Hex.Or(legend[i].Color, accent), null, 0, g.Length(4)));
            cmds.Add(new TextDraw(panel.X + g.Length(50), y + g.Length(9), text, 18 * cs, TextRole.Regular, White, TextAlign.Left, TextBase.Middle, panel.W - g.Length(70)));
        }
    }

    private static void MapPageContent(MenuGeometry g, List<MenuDraw> cmds, string line, Func<string, string> t)
    {
        // The integrator draws the radar scene into the "map" region; this is its frame.
        var rect = g.ToCanvas(MenuGeometry.Content);
        cmds.Add(new RectDraw(rect.X, rect.Y, rect.W, rect.H, "#0b151ecc", Hex.WithAlpha(line, 0x40), Math.Max(1, g.Length(1)), g.Length(12)));
    }

    private static void LinesPageContent(PauseMenu menu, MenuGeometry g, List<MenuDraw> cmds, string accent, string line, Func<string, string> t)
    {
        double cs = g.Metrics.CanvasScale;
        var rect = g.ToCanvas(MenuGeometry.Content);
        cmds.Add(new RectDraw(rect.X, rect.Y, rect.W, rect.H, "#ffffff0d", Hex.WithAlpha(line, 0x30), Math.Max(1, g.Length(1)), g.Length(12)));
        var lines = menu.Lines;
        if (lines.Count == 0)
        {
            cmds.Add(new TextDraw(rect.X + rect.W / 2, rect.Y + rect.H / 2, t(PauseStrings.NoLines), 20 * cs, TextRole.Medium, Muted, TextAlign.Center, TextBase.Middle, rect.W - g.Length(40)));
            return;
        }
        cmds.Add(new ClipDraw(rect.X, rect.Y, rect.W, rect.H));
        var modeIcons = menu.Identity.ModeIcons;
        int rows = MenuGeometry.LinesVisibleRows;
        for (int i = 0; i < rows && menu.ScrollRow + i < lines.Count; i++)
        {
            int index = menu.ScrollRow + i;
            var entry = lines[index];
            var row = g.ToCanvas(MenuGeometry.LineRowRect(i));
            bool selected = index == menu.SelectedLine;
            bool hover = menu.Hover == new MenuTarget(MenuTargetKind.LineRow, index);
            if (selected || hover)
                cmds.Add(new RectDraw(row.X, row.Y, row.W, row.H, selected ? Hex.WithAlpha(accent, 0x40) : Cell, null, 0, g.Length(8)));
            cmds.Add(new RectDraw(row.X + g.Length(12), row.Y + (row.H - g.Length(16)) / 2, g.Length(16), g.Length(16), Hex.Or(entry.Color, accent), null, 0, g.Length(4)));
            // With mode icons in the package the label column moves right, so rows with and without an icon line up.
            double labelX = 40;
            if (modeIcons.Count > 0)
            {
                labelX = 68;
                if (entry.Mode is not null && modeIcons.TryGetValue(entry.Mode, out var modeIcon))
                    DrawGlyph(cmds, modeIcon, new MenuRect(row.X + g.Length(36), row.Y + (row.H - g.Length(22)) / 2, g.Length(22), g.Length(22)));
            }
            string label = string.IsNullOrWhiteSpace(entry.Label) ? "-" : entry.Label;
            cmds.Add(new TextDraw(row.X + g.Length(labelX), row.Y + row.H / 2, label, 18 * cs, TextRole.Bold, White, TextAlign.Left, TextBase.Middle, g.Length(470 - labelX)));
            if (!string.IsNullOrWhiteSpace(entry.Detail))
                cmds.Add(new TextDraw(row.X + row.W - g.Length(12), row.Y + row.H / 2, entry.Detail, 16 * cs, TextRole.Condensed, Muted, TextAlign.Right, TextBase.Middle, g.Length(280)));
        }
        cmds.Add(new UnclipDraw());

        if (lines.Count > rows)
        {
            // Scroll thumb along the right edge of the list.
            double trackX = rect.X + rect.W - g.Length(14), trackY = rect.Y + g.Length(8), trackH = rect.H - g.Length(16);
            cmds.Add(new RectDraw(trackX, trackY, g.Length(6), trackH, "#ffffff1a", null, 0, g.Length(3)));
            double thumbH = Math.Max(g.Length(24), trackH * rows / lines.Count);
            int maxScroll = lines.Count - rows;
            double thumbY = trackY + (trackH - thumbH) * (maxScroll == 0 ? 0 : menu.ScrollRow / (double)maxScroll);
            cmds.Add(new RectDraw(trackX, Math.Round(thumbY, 2), g.Length(6), Math.Round(thumbH, 2), Hex.WithAlpha(accent, 0xcc), null, 0, g.Length(3)));
        }
    }
}
