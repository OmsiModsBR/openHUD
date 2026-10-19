using OpenHud.Scene;

namespace OpenHud.Radar;

// Shared map drawing for the radar and the full map: projects world meters to canvas pixels,
// culls and simplifies layer geometry against a rectangular window, and spends a fixed command
// and point budget by priority. Everything is deterministic: same model + options = same scene.

/// <summary>World-to-canvas mapping: focus point at (ScreenX, ScreenY), rotated so HeadingDegrees points up.</summary>
internal sealed class MapView
{
    public const double MaxCoordinate = 1e9; // beyond this a coordinate is treated as garbage

    public double FocusX, FocusY;
    public double ScreenX, ScreenY;
    public double Scale; // pixels per meter
    public double HeadingDegrees;
    // Clip window on the canvas (the rectangle the geometry is cut against).
    public double Left, Top, Right, Bottom;

    private double cos, sin;

    public MapView(double focusX, double focusY, double screenX, double screenY, double scale, double headingDegrees,
        double left, double top, double right, double bottom)
    {
        FocusX = focusX; FocusY = focusY; ScreenX = screenX; ScreenY = screenY; Scale = scale; HeadingDegrees = headingDegrees;
        Left = left; Top = top; Right = right; Bottom = bottom;
        double rad = headingDegrees * Math.PI / 180;
        cos = Math.Cos(rad); sin = Math.Sin(rad);
    }

    /// <summary>False when the input is not finite or absurdly large.</summary>
    public bool Project(double wx, double wy, out double sx, out double sy)
    {
        sx = sy = 0;
        if (!(Math.Abs(wx) <= MaxCoordinate) || !(Math.Abs(wy) <= MaxCoordinate)) return false;
        double dx = wx - FocusX, dy = wy - FocusY;
        double right = dx * cos - dy * sin;
        double up = dx * sin + dy * cos;
        sx = ScreenX + right * Scale;
        sy = ScreenY - up * Scale;
        return double.IsFinite(sx) && double.IsFinite(sy);
    }

    public bool Inside(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

internal sealed class MapBudget
{
    public int Commands;
    public int Points;
    public MapBudget(int commands, int points) { Commands = Math.Max(0, commands); Points = Math.Max(0, points); }
}

internal sealed class MapRenderer
{
    private sealed class Candidate
    {
        public RadarLayer Layer;
        public double[] Pts = Array.Empty<double>();
        public bool Area;
        public string Color = "";
        public double Width;
        public double Score;
        public int Order;
        public bool Selected;
    }

    // A radar build handles thousands of candidates; their objects and the scratch list are kept per thread and reused, so a
    // build allocates little beyond the geometry it hands to the scene (the garbage collector runs less often on the tick).
    [ThreadStatic] private static List<Candidate>? spare;
    [ThreadStatic] private static List<double>? scratch;

    private readonly RadarTheme theme;
    private readonly List<Candidate> candidates = new();
    private readonly List<double> run = new();
    private int rented;

    private Candidate Rent()
    {
        var list = spare ??= new List<Candidate>();
        if (rented == list.Count) list.Add(new Candidate());
        var c = list[rented++];
        c.Selected = false; c.Width = 0;
        return c;
    }

    public MapRenderer(RadarTheme theme) => this.theme = theme;

    private static readonly RadarLayer[] DrawOrder = { RadarLayer.Water, RadarLayer.Green, RadarLayer.Built, RadarLayer.Relief, RadarLayer.Road };

    private static double LayerWeight(RadarLayer layer) => layer switch
    {
        RadarLayer.Road => 4.0,
        RadarLayer.Water => 2.0,
        RadarLayer.Built => 1.5,
        RadarLayer.Green => 1.0,
        _ => 0.7,
    };

    private string LayerColor(RadarLayer layer) => layer switch
    {
        RadarLayer.Water => theme.Water,
        RadarLayer.Green => theme.Green,
        RadarLayer.Built => theme.Built,
        RadarLayer.Relief => theme.Relief,
        _ => theme.Road,
    };

    private static double Round(double v) => Math.Round(v, 1);

    // ---- layers -------------------------------------------------------------------------

    /// <summary>Emits the map layers into <paramref name="b"/>, spending at most the budget; returns the number of commands added.</summary>
    public int DrawFeatures(SceneBuilder b, IReadOnlyList<RadarFeature>? features, MapView view, MapBudget budget)
    {
        candidates.Clear();
        rented = 0;
        if (features is null || budget.Commands <= 0) return 0;
        // The window as a box in world metres around the focus (any heading fits inside the circle through its corners):
        // a feature whose own box misses it cannot show, and is not projected.
        double wx = Math.Max(Math.Abs(view.Left - view.ScreenX), Math.Abs(view.Right - view.ScreenX));
        double wy = Math.Max(Math.Abs(view.Top - view.ScreenY), Math.Abs(view.Bottom - view.ScreenY));
        double reach = (Math.Sqrt(wx * wx + wy * wy) + 2) / view.Scale;
        bool cull = view.Scale > 0 && double.IsFinite(reach);
        double boxL = view.FocusX - reach, boxR = view.FocusX + reach, boxB = view.FocusY - reach, boxT = view.FocusY + reach;
        for (int i = 0; i < features.Count; i++)
        {
            var f = features[i];
            if (f?.Points is null || f.Points.Length < 4) continue;
            if (cull && !FeatureBounds.Of(f.Points).Intersects(boxL, boxB, boxR, boxT)) continue;
            bool area = f.Closed && f.Layer is RadarLayer.Water or RadarLayer.Green or RadarLayer.Built;
            string color = SceneColor.Or(f.Color, LayerColor(f.Layer));
            double widthMeters = double.IsFinite(f.WidthMeters) && f.WidthMeters > 0 ? Math.Min(f.WidthMeters, 60) : 6;
            if (area) AddArea(f, view, color);
            else AddLines(f, view, color, widthMeters);
        }
        if (candidates.Count == 0) return 0;

        // Over budget: keep the most visible features (big on screen, important layer), drop the rest.
        long totalPts = 0;
        foreach (var c in candidates) totalPts += c.Pts.Length / 2;
        if (candidates.Count > budget.Commands || totalPts > budget.Points)
        {
            var order = new int[candidates.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, c) =>
            {
                int byScore = candidates[c].Score.CompareTo(candidates[a].Score);
                return byScore != 0 ? byScore : candidates[a].Order.CompareTo(candidates[c].Order);
            });
            int cmds = budget.Commands, pts = budget.Points;
            foreach (int idx in order)
            {
                var c = candidates[idx];
                int n = c.Pts.Length / 2;
                if (cmds <= 0) break;
                if (n > pts) continue;
                c.Selected = true; cmds--; pts -= n;
            }
        }
        else foreach (var c in candidates) c.Selected = true;

        int emitted = 0, emittedPts = 0;
        foreach (var layer in DrawOrder)
            foreach (var c in candidates)
            {
                if (c.Layer != layer || !c.Selected) continue;
                if (c.Area) b.Poly(c.Pts, c.Color, null, null, true);
                else b.Poly(c.Pts, null, c.Color, c.Width, false);
                emitted++; emittedPts += c.Pts.Length / 2;
            }
        budget.Commands -= emitted;
        budget.Points -= emittedPts;
        return emitted;
    }

    private void AddArea(RadarFeature f, MapView view, string color)
    {
        run.Clear();
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i + 1 < f.Points.Length; i += 2)
        {
            if (!view.Project(f.Points[i], f.Points[i + 1], out double sx, out double sy)) continue;
            run.Add(sx); run.Add(sy);
            if (sx < minX) minX = sx; if (sx > maxX) maxX = sx;
            if (sy < minY) minY = sy; if (sy > maxY) maxY = sy;
        }
        if (run.Count < 6) return;
        if (maxX < view.Left || minX > view.Right || maxY < view.Top || minY > view.Bottom) return;
        // Most areas lie wholly inside the window: they need no clipping, which allocates a list per window edge.
        var inside = minX >= view.Left && maxX <= view.Right && minY >= view.Top && maxY <= view.Bottom;
        var clipped = inside ? run : ClipPolygon(run, view);
        if (clipped.Count < 6) return;
        var pts = Simplify(clipped, closed: true);
        if (pts.Length < 6) return;
        double diag = Diagonal(pts);
        if (diag < 2) return;
        var area = Rent();
        area.Layer = f.Layer; area.Pts = pts; area.Area = true; area.Color = color; area.Score = diag * LayerWeight(f.Layer); area.Order = candidates.Count;
        candidates.Add(area);
    }

    private void AddLines(RadarFeature f, MapView view, string color, double widthMeters)
    {
        double widthPx = f.Layer == RadarLayer.Road ? Math.Clamp(widthMeters * view.Scale, 1.5, 10)
            : f.Layer == RadarLayer.Relief ? 1 : 1.5;
        widthPx = Round(widthPx);
        run.Clear();
        bool havePrev = false;
        double px = 0, py = 0;
        int n = f.Points.Length / 2;
        int count = f.Closed ? n + 1 : n;
        for (int i = 0; i < count; i++)
        {
            int k = i == n ? 0 : i;
            if (!view.Project(f.Points[2 * k], f.Points[2 * k + 1], out double sx, out double sy))
            {
                Flush(f, color, widthPx); havePrev = false; continue;
            }
            if (havePrev)
            {
                if (ClipSegment(view, px, py, sx, sy, out double ax, out double ay, out double bx, out double by))
                {
                    // A clipped entry point differs from the last kept point: start a new run there.
                    if (run.Count >= 2 && (Math.Abs(run[^2] - ax) > 1e-6 || Math.Abs(run[^1] - ay) > 1e-6)) Flush(f, color, widthPx);
                    AddRunPoint(ax, ay);
                    AddRunPoint(bx, by);
                }
                else Flush(f, color, widthPx);
            }
            px = sx; py = sy; havePrev = true;
        }
        Flush(f, color, widthPx);
    }

    private void AddRunPoint(double x, double y)
    {
        int n = run.Count;
        if (n >= 2)
        {
            double dx = x - run[n - 2], dy = y - run[n - 1];
            if (dx * dx + dy * dy < 1) return; // closer than one pixel to the last kept point
        }
        run.Add(x); run.Add(y);
    }

    private void Flush(RadarFeature f, string color, double widthPx)
    {
        if (run.Count >= 4)
        {
            var pts = new double[run.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = Round(run[i]);
            double diag = Diagonal(pts);
            if (diag >= 2)
            {
                var line = Rent();
                line.Layer = f.Layer; line.Pts = pts; line.Area = false; line.Color = color; line.Width = widthPx; line.Score = diag * LayerWeight(f.Layer); line.Order = candidates.Count;
                candidates.Add(line);
            }
        }
        run.Clear();
    }

    private static double Diagonal(double[] pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i + 1 < pts.Length; i += 2)
        {
            if (pts[i] < minX) minX = pts[i]; if (pts[i] > maxX) maxX = pts[i];
            if (pts[i + 1] < minY) minY = pts[i + 1]; if (pts[i + 1] > maxY) maxY = pts[i + 1];
        }
        return Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
    }

    // Drops points closer than one pixel to the previous kept one and rounds to 0.1 px.
    private static double[] Simplify(List<double> src, bool closed)
    {
        var dst = scratch ??= new List<double>(256);
        dst.Clear();
        for (int i = 0; i + 1 < src.Count; i += 2)
        {
            double x = Round(src[i]), y = Round(src[i + 1]);
            int m = dst.Count;
            if (m >= 2)
            {
                double dx = x - dst[m - 2], dy = y - dst[m - 1];
                if (dx * dx + dy * dy < 1) continue;
            }
            dst.Add(x); dst.Add(y);
        }
        if (closed && dst.Count >= 4)
        {
            double dx = dst[0] - dst[^2], dy = dst[1] - dst[^1];
            if (dx * dx + dy * dy < 1) { dst.RemoveAt(dst.Count - 1); dst.RemoveAt(dst.Count - 1); }
        }
        return dst.ToArray();
    }

    // ---- clipping -----------------------------------------------------------------------

    // Liang-Barsky against the view window.
    internal static bool ClipSegment(MapView v, double x0, double y0, double x1, double y1,
        out double ax, out double ay, out double bx, out double by)
    {
        ax = ay = bx = by = 0;
        double dx = x1 - x0, dy = y1 - y0;
        double t0 = 0, t1 = 1;
        bool Edge(double p, double q)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        if (!Edge(-dx, x0 - v.Left) || !Edge(dx, v.Right - x0) || !Edge(-dy, y0 - v.Top) || !Edge(dy, v.Bottom - y0)) return false;
        ax = x0 + t0 * dx; ay = y0 + t0 * dy;
        bx = x0 + t1 * dx; by = y0 + t1 * dy;
        return double.IsFinite(ax) && double.IsFinite(ay) && double.IsFinite(bx) && double.IsFinite(by);
    }

    // Sutherland-Hodgman against the four window edges. The result lives in a per-thread scratch list that the next call
    // overwrites: the caller copies what it keeps (Simplify does).
    [ThreadStatic] private static List<double>? clipFrom;
    [ThreadStatic] private static List<double>? clipTo;

    private static List<double> ClipPolygon(List<double> input, MapView v)
    {
        var current = clipFrom ??= new List<double>(64);
        var next = clipTo ??= new List<double>(64);
        current.Clear();
        current.AddRange(input);
        for (int edge = 0; edge < 4 && current.Count >= 6; edge++)
        {
            next.Clear();
            int n = current.Count / 2;
            for (int i = 0; i < n; i++)
            {
                double cx = current[2 * i], cy = current[2 * i + 1];
                int j = (i + n - 1) % n;
                double px = current[2 * j], py = current[2 * j + 1];
                bool cin = Inside(edge, cx, cy, v), pin = Inside(edge, px, py, v);
                if (cin != pin) { Intersect(edge, px, py, cx, cy, v, out double ix, out double iy); next.Add(ix); next.Add(iy); }
                if (cin) { next.Add(cx); next.Add(cy); }
            }
            (current, next) = (next, current);
        }
        // Keep the two scratch lists straight whichever one ended up holding the result.
        clipFrom = current;
        clipTo = next;
        return current;
    }

    private static bool Inside(int edge, double x, double y, MapView v) => edge switch
    {
        0 => x >= v.Left,
        1 => x <= v.Right,
        2 => y >= v.Top,
        _ => y <= v.Bottom,
    };

    private static void Intersect(int edge, double x0, double y0, double x1, double y1, MapView v, out double x, out double y)
    {
        switch (edge)
        {
            case 0: x = v.Left; y = y0 + (y1 - y0) * (v.Left - x0) / (x1 - x0); break;
            case 1: x = v.Right; y = y0 + (y1 - y0) * (v.Right - x0) / (x1 - x0); break;
            case 2: y = v.Top; x = x0 + (x1 - x0) * (v.Top - y0) / (y1 - y0); break;
            default: y = v.Bottom; x = x0 + (x1 - x0) * (v.Bottom - y0) / (y1 - y0); break;
        }
    }

    // ---- route, markers, vehicles, player -------------------------------------------------

    /// <summary>Draws the route as a casing plus a colored line per visible run (at most <paramref name="maxRuns"/>).</summary>
    public void DrawRoute(SceneBuilder b, double[]? route, MapView view, double widthPx, int maxRuns)
    {
        if (route is null || route.Length < 4 || maxRuns <= 0) return;
        var runs = new List<double[]>();
        run.Clear();
        bool havePrev = false; double px = 0, py = 0;
        void End()
        {
            if (run.Count >= 4 && runs.Count < maxRuns)
            {
                var a = new double[run.Count];
                for (int i = 0; i < a.Length; i++) a[i] = Round(run[i]);
                runs.Add(a);
            }
            run.Clear();
        }
        for (int i = 0; i + 1 < route.Length; i += 2)
        {
            if (!view.Project(route[i], route[i + 1], out double sx, out double sy)) { End(); havePrev = false; continue; }
            if (havePrev)
            {
                if (ClipSegment(view, px, py, sx, sy, out double ax, out double ay, out double bx, out double by))
                {
                    if (run.Count >= 2 && (Math.Abs(run[^2] - ax) > 1e-6 || Math.Abs(run[^1] - ay) > 1e-6)) End();
                    AddRunPoint(ax, ay); AddRunPoint(bx, by);
                }
                else End();
            }
            px = sx; py = sy; havePrev = true;
        }
        End();
        // Casing first so the colored line of a later run never sits under an earlier run's casing.
        foreach (var r in runs) b.Poly(r, null, theme.RouteCasing, widthPx + 3, false);
        foreach (var r in runs) b.Poly(r, null, theme.Route, widthPx, false);
    }

    public int DrawMarkers(SceneBuilder b, IReadOnlyList<RadarMarker>? markers, MapView view, int cap, Func<double, double, bool> visible, RadarArt? art = null)
    {
        if (markers is null || cap <= 0) return 0;
        var picked = new List<(double d, int i, double x, double y)>();
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (m is null || !view.Project(m.X, m.Y, out double sx, out double sy) || !visible(sx, sy)) continue;
            double dx = sx - view.ScreenX, dy = sy - view.ScreenY;
            picked.Add((dx * dx + dy * dy, i, sx, sy));
        }
        picked.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.i.CompareTo(c.i));
        int emitted = 0, withArt = 0;
        var stop = art?.StopIcon ?? OpenHud.Art.Glyph.None;
        var artCap = Math.Max(1, cap / 2); // the scene builders reserved commands for this many
        foreach (var p in picked)
        {
            if (emitted >= cap) break;
            var m = markers[p.i];
            if (!string.IsNullOrEmpty(m.IconId)) b.Image(m.IconId!, Round(p.x - 9), Round(p.y - 9), 18, 18, null, m.Color is not null && SceneColor.IsValid(m.Color) ? m.Color : null);
            else if (!stop.IsEmpty && withArt < artCap)
            {
                // The package's stop icon stands in for the dot; a marker tint only reaches a picture.
                var list = new List<SceneCommand>(stop.CommandCount);
                stop.AppendTo(list, Round(p.x - 9), Round(p.y - 9), 18, 18, 1, m.Color is not null && SceneColor.IsValid(m.Color) ? m.Color : null);
                foreach (var command in list) b.Add(command);
                withArt++;
            }
            else b.Circle(Round(p.x), Round(p.y), 4, SceneColor.Or(m.Color, theme.Marker), theme.PlayerOutline, 1);
            emitted++;
        }
        return emitted;
    }

    public int DrawVehicles(SceneBuilder b, IReadOnlyList<RadarVehicle>? vehicles, MapView view, int cap, Func<double, double, bool> visible)
    {
        if (vehicles is null || cap <= 0) return 0;
        var picked = new List<(double d, int i, double x, double y)>();
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v is null || !view.Project(v.X, v.Y, out double sx, out double sy) || !visible(sx, sy)) continue;
            double dx = sx - view.ScreenX, dy = sy - view.ScreenY;
            picked.Add((dx * dx + dy * dy, i, sx, sy));
        }
        picked.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.i.CompareTo(c.i));
        int emitted = 0;
        foreach (var p in picked)
        {
            if (emitted >= cap) break;
            var v = vehicles[p.i];
            double heading = double.IsFinite(v.HeadingDegrees) ? v.HeadingDegrees : 0;
            double len = v.Kind == RadarVehicleKind.Bus || v.Kind == RadarVehicleKind.Tram ? 9 : 7;
            b.Poly(Arrow(p.x, p.y, heading - view.HeadingDegrees, len, len * 0.55, 0.45), theme.Vehicle(v.Kind), theme.PlayerOutline, 1, true);
            emitted++;
        }
        return emitted;
    }

    public void DrawPlayer(SceneBuilder b, MapView view, RadarPlayer player, double size)
    {
        double heading = double.IsFinite(player.HeadingDegrees) ? player.HeadingDegrees : 0;
        // A package may ask for a plain triangle (no notch) and a fill of its own.
        var points = Arrow(view.ScreenX, view.ScreenY, heading - view.HeadingDegrees, size, size * 0.7, 0.5);
        if (player.Triangle) points = new[] { points[0], points[1], points[2], points[3], points[6], points[7] };
        b.Poly(points, SceneColor.Or(player.Color, theme.PlayerFill), theme.PlayerOutline, 2, true);
    }

    // A chevron: tip ahead of the center, two rear corners, a notch between them.
    internal static double[] Arrow(double cx, double cy, double angleDegrees, double length, double halfWidth, double notch)
    {
        double rad = angleDegrees * Math.PI / 180;
        double fx = Math.Sin(rad), fy = -Math.Cos(rad); // forward on screen
        double rx = Math.Cos(rad), ry = Math.Sin(rad);  // right on screen
        double back = length * 0.8;
        double[] local = { 0, length, halfWidth, -back, 0, -back + length * notch * 1.0, -halfWidth, -back };
        // local pairs are (across, along); note the rear corners sit behind the center.
        var pts = new double[8];
        for (int i = 0; i < 4; i++)
        {
            double across = local[2 * i], along = local[2 * i + 1];
            pts[2 * i] = Round(cx + rx * across + fx * along);
            pts[2 * i + 1] = Round(cy + ry * across + fy * along);
        }
        return pts;
    }
}
