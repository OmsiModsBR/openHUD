using System.Globalization;

namespace OpenHud.Art;

// A 2D affine map: x' = A x + C y + E, y' = B x + D y + F (the SVG matrix(a b c d e f)).
public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    // `this` applied after `inner`: this * inner.
    public Affine Times(Affine inner) => new(
        A * inner.A + C * inner.B, B * inner.A + D * inner.B,
        A * inner.C + C * inner.D, B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E, B * inner.E + D * inner.F + F);

    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

    public double Determinant => A * D - B * C;

    // Length scale: the square root of the area scale.
    public double Scale => Math.Sqrt(Math.Abs(Determinant));

    public bool IsFinite => double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D) && double.IsFinite(E) && double.IsFinite(F);

    // Uniform scale, rotation and translation (a circle stays a circle); reflection allowed.
    public bool IsSimilarity => Math.Abs(A * A + B * B - (C * C + D * D)) <= 1e-9 * Math.Max(1, A * A + B * B) && Math.Abs(A * C + B * D) <= 1e-9 * Math.Max(1, A * A + B * B);

    // Scale and translation only, same factor on both axes, positive (a rectangle stays an axis-aligned rectangle).
    public bool IsUniformScale => Math.Abs(B) <= 1e-12 && Math.Abs(C) <= 1e-12 && A > 0 && Math.Abs(A - D) <= 1e-9 * A;
}

// One flattened sub-path: x,y pairs and whether it was closed with Z.
public sealed record FlatSubpath(double[] Points, bool Closed);

public sealed class PathResult
{
    public List<FlatSubpath> Subpaths { get; } = new();
    // Null when the whole data was read; else why reading stopped (what was read before is still there).
    public string? Error { get; set; }
}

// The path budget of a conversion: a count shared by every shape, so one document cannot spend more than its share.
public sealed class PointBudget
{
    public PointBudget(int total, int perShape) { Remaining = total; PerShape = perShape; }
    public int Remaining { get; private set; }
    public int PerShape { get; }
    public bool Spend(int count)
    {
        if (count > Remaining) { Remaining = 0; return false; }
        Remaining -= count;
        return true;
    }
}

// Thrown when a document asks for more points than its budget; the converter turns it into a refusal.
public sealed class ArtLimitException : Exception
{
    public ArtLimitException(string message) : base(message) { }
}

// SVG path data (M L H V C S Q T A Z, absolute and relative) to polylines. Curves and arcs are flattened
// within a tolerance with a bounded number of segments, so a hostile path cannot ask for unbounded work.
public static class PathFlattener
{
    public const int MaxSegmentsPerCurve = 48;
    public const int MaxSegmentsPerArc = 64;
    public const int MaxNumberLength = 40;

    // Segments for a cubic whose control polygon has second difference `dd`, so the flattening error stays under `tolerance`.
    public static int CubicSegments(double dd, double tolerance) => Capped(Math.Sqrt(0.75 * dd / Math.Max(tolerance, 1e-9)), MaxSegmentsPerCurve);

    public static int QuadSegments(double dd, double tolerance) => Capped(Math.Sqrt(0.25 * dd / Math.Max(tolerance, 1e-9)), MaxSegmentsPerCurve);

    // ceil(value) within 1..cap, decided in floating point first: a huge or NaN value must not reach an int conversion.
    private static int Capped(double value, int cap) => !(value > 1) ? 1 : value >= cap ? cap : (int)Math.Ceiling(value);

    // Appends the points of the cubic after its start (the start is the caller's last point).
    public static void Cubic(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tolerance, List<double> output)
    {
        var ddx = Math.Max(Math.Abs(x0 - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3));
        var ddy = Math.Max(Math.Abs(y0 - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
        var n = CubicSegments(Math.Sqrt(ddx * ddx + ddy * ddy), tolerance);
        for (var i = 1; i <= n; i++)
        {
            var t = (double)i / n; var u = 1 - t;
            output.Add(u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3);
            output.Add(u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3);
        }
    }

    public static void Quad(double x0, double y0, double x1, double y1, double x2, double y2, double tolerance, List<double> output)
    {
        var ddx = Math.Abs(x0 - 2 * x1 + x2);
        var ddy = Math.Abs(y0 - 2 * y1 + y2);
        var n = QuadSegments(Math.Sqrt(ddx * ddx + ddy * ddy), tolerance);
        for (var i = 1; i <= n; i++)
        {
            var t = (double)i / n; var u = 1 - t;
            output.Add(u * u * x0 + 2 * u * t * x1 + t * t * x2);
            output.Add(u * u * y0 + 2 * u * t * y1 + t * t * y2);
        }
    }

    // SVG elliptical arc from (x0,y0) to (x,y) (appendix F.6): radii corrected when too small, a degenerate arc is a line.
    public static void Arc(double x0, double y0, double rx, double ry, double rotationDegrees, bool largeArc, bool sweep, double x, double y, double tolerance, List<double> output)
    {
        if (x0 == x && y0 == y) return;
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-12 || ry < 1e-12) { output.Add(x); output.Add(y); return; }
        var phi = rotationDegrees * Math.PI / 180;
        double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
        double dx2 = (x0 - x) / 2, dy2 = (y0 - y) / 2;
        double x1p = cosPhi * dx2 + sinPhi * dy2, y1p = -sinPhi * dx2 + cosPhi * dy2;
        var lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1) { var k = Math.Sqrt(lambda); rx *= k; ry *= k; }
        var num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        var den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var coef = den == 0 ? 0 : Math.Sqrt(Math.Max(0, num / den));
        if (largeArc == sweep) coef = -coef;
        double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
        double cx = cosPhi * cxp - sinPhi * cyp + (x0 + x) / 2, cy = sinPhi * cxp + cosPhi * cyp + (y0 + y) / 2;
        var theta1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var theta2 = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var delta = theta2 - theta1;
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        var radius = Math.Max(rx, ry);
        var step = tolerance >= radius ? Math.PI / 2 : Math.Min(Math.PI / 2, 2 * Math.Acos(1 - tolerance / radius));
        var n = Capped(Math.Abs(delta) / Math.Max(step, 1e-3), MaxSegmentsPerArc);
        for (var i = 1; i <= n; i++)
        {
            if (i == n) { output.Add(x); output.Add(y); break; } // end exactly on the target
            var t = theta1 + delta * i / n;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            output.Add(cosPhi * ex - sinPhi * ey + cx);
            output.Add(sinPhi * ex + cosPhi * ey + cy);
        }
    }

    // Flattens `data` in user space within `tolerance` (user units) and maps the points through `map`.
    // Reading stops at the first error; the sub-paths read before it are kept. Points count against `budget`.
    public static PathResult Flatten(string data, Affine map, double tolerance, PointBudget budget, double maxAbs)
    {
        var result = new PathResult();
        var reader = new Reader(data);
        var current = new List<double>();
        double cx = 0, cy = 0, sx = 0, sy = 0, lastCx = 0, lastCy = 0;
        var previous = ' ';
        var started = false;
        var open = false;

        void Finish(bool closed)
        {
            if (current.Count >= 4 || (closed && current.Count >= 2))
            {
                if (current.Count / 2 > budget.PerShape) throw new ArtLimitException("a path has more than " + budget.PerShape + " points");
                if (!budget.Spend(current.Count / 2)) throw new ArtLimitException("the document has more points than the limit");
                var points = new double[current.Count];
                for (var i = 0; i < points.Length; i += 2)
                {
                    var (px, py) = map.Apply(current[i], current[i + 1]);
                    points[i] = px; points[i + 1] = py;
                }
                result.Subpaths.Add(new FlatSubpath(points, closed));
            }
            current.Clear();
            open = false;
        }

        var beyond = false;
        void Add(double x, double y)
        {
            // NaN fails this test too, so overflowing arithmetic cannot put a non-finite number in a path.
            if (!(Math.Abs(x) <= maxAbs) || !(Math.Abs(y) <= maxAbs)) { beyond = true; return; }
            current.Add(x); current.Add(y);
            if (current.Count / 2 > budget.PerShape + 1) throw new ArtLimitException("a path has more than " + budget.PerShape + " points");
        }

        {
            while (true)
            {
                reader.SkipSeparators();
                if (reader.AtEnd) break;
                var command = reader.Peek();
                bool isCommand = char.IsLetter(command);
                if (isCommand) reader.Advance();
                else if (previous is ' ' or 'Z' or 'z') { result.Error = "expected a command at " + reader.Position; break; }
                else command = previous == 'M' ? 'L' : previous == 'm' ? 'l' : previous; // implicit repetition
                if (!started && command is not ('M' or 'm')) { result.Error = "path data must start with M"; break; }
                if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(command) < 0) { result.Error = "unknown path command '" + command + "' at " + (reader.Position - 1); break; }
                var relative = char.IsLower(command);
                var upper = char.ToUpperInvariant(command);

                if (upper == 'Z')
                {
                    if (open) { Finish(true); cx = sx; cy = sy; }
                    previous = command;
                    continue;
                }
                if (!open && upper != 'M')
                {
                    // A command after Z continues from the start point of the closed sub-path.
                    current.Clear(); Add(cx, cy); open = true;
                }

                switch (upper)
                {
                    case 'M':
                    {
                        if (!reader.Number(out var x) || !reader.Number(out var y)) { result.Error = "move needs two numbers at " + reader.Position; goto stop; }
                        if (relative) { x += cx; y += cy; }
                        if (open) Finish(false);
                        started = true;
                        cx = sx = x; cy = sy = y;
                        current.Clear(); Add(cx, cy); open = true;
                        break;
                    }
                    case 'L':
                    {
                        if (!reader.Number(out var x) || !reader.Number(out var y)) { result.Error = "line needs two numbers at " + reader.Position; goto stop; }
                        if (relative) { x += cx; y += cy; }
                        Add(x, y); cx = x; cy = y;
                        break;
                    }
                    case 'H':
                    {
                        if (!reader.Number(out var x)) { result.Error = "horizontal line needs a number at " + reader.Position; goto stop; }
                        if (relative) x += cx;
                        Add(x, cy); cx = x;
                        break;
                    }
                    case 'V':
                    {
                        if (!reader.Number(out var y)) { result.Error = "vertical line needs a number at " + reader.Position; goto stop; }
                        if (relative) y += cy;
                        Add(cx, y); cy = y;
                        break;
                    }
                    case 'C':
                    case 'S':
                    {
                        double x1, y1;
                        if (upper == 'C')
                        {
                            if (!reader.Number(out x1) || !reader.Number(out y1)) { result.Error = "curve needs six numbers at " + reader.Position; goto stop; }
                            if (relative) { x1 += cx; y1 += cy; }
                        }
                        else if (previous is 'C' or 'c' or 'S' or 's') { x1 = 2 * cx - lastCx; y1 = 2 * cy - lastCy; }
                        else { x1 = cx; y1 = cy; }
                        if (!reader.Number(out var x2) || !reader.Number(out var y2) || !reader.Number(out var x) || !reader.Number(out var y)) { result.Error = "curve needs numbers at " + reader.Position; goto stop; }
                        if (relative) { x2 += cx; y2 += cy; x += cx; y += cy; }
                        var points = new List<double>();
                        Cubic(cx, cy, x1, y1, x2, y2, x, y, tolerance, points);
                        for (var i = 0; i < points.Count; i += 2) Add(points[i], points[i + 1]);
                        lastCx = x2; lastCy = y2; cx = x; cy = y;
                        break;
                    }
                    case 'Q':
                    case 'T':
                    {
                        double x1, y1;
                        if (upper == 'Q')
                        {
                            if (!reader.Number(out x1) || !reader.Number(out y1)) { result.Error = "curve needs four numbers at " + reader.Position; goto stop; }
                            if (relative) { x1 += cx; y1 += cy; }
                        }
                        else if (previous is 'Q' or 'q' or 'T' or 't') { x1 = 2 * cx - lastCx; y1 = 2 * cy - lastCy; }
                        else { x1 = cx; y1 = cy; }
                        if (!reader.Number(out var x) || !reader.Number(out var y)) { result.Error = "curve needs numbers at " + reader.Position; goto stop; }
                        if (relative) { x += cx; y += cy; }
                        var points = new List<double>();
                        Quad(cx, cy, x1, y1, x, y, tolerance, points);
                        for (var i = 0; i < points.Count; i += 2) Add(points[i], points[i + 1]);
                        lastCx = x1; lastCy = y1; cx = x; cy = y;
                        break;
                    }
                    case 'A':
                    {
                        if (!reader.Number(out var rx) || !reader.Number(out var ry) || !reader.Number(out var rotation)
                            || !reader.Flag(out var large) || !reader.Flag(out var sweep) || !reader.Number(out var x) || !reader.Number(out var y))
                        { result.Error = "arc needs seven values at " + reader.Position; goto stop; }
                        if (relative) { x += cx; y += cy; }
                        var points = new List<double>();
                        Arc(cx, cy, rx, ry, rotation, large, sweep, x, y, tolerance, points);
                        for (var i = 0; i < points.Count; i += 2) Add(points[i], points[i + 1]);
                        cx = x; cy = y;
                        break;
                    }
                }
                if (beyond || !(Math.Abs(cx) <= maxAbs) || !(Math.Abs(cy) <= maxAbs))
                {
                    result.Error = "a coordinate is beyond " + maxAbs.ToString("0", CultureInfo.InvariantCulture);
                    goto stop;
                }
                previous = command;
            }
        stop:
            if (open) Finish(false);
        }
        return result;
    }

    // The tokenizer of path data and number lists.
    internal sealed class Reader
    {
        private readonly string text;
        private int index;

        public Reader(string text) { this.text = text; }

        public int Position => index;
        public bool AtEnd => index >= text.Length;
        public char Peek() => text[index];
        public void Advance() => index++;

        public void SkipSeparators()
        {
            while (index < text.Length && (text[index] is ' ' or '\t' or '\r' or '\n' or ',')) index++;
        }

        // A number: sign, digits, a point, an exponent. NaN, Infinity and anything longer than MaxNumberLength are errors.
        public bool Number(out double value)
        {
            value = 0;
            SkipSeparators();
            var start = index;
            var i = index;
            if (i < text.Length && text[i] is '+' or '-') i++;
            var digits = 0;
            while (i < text.Length && text[i] is >= '0' and <= '9') { i++; digits++; }
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && text[i] is >= '0' and <= '9') { i++; digits++; }
            }
            if (digits == 0) return false;
            if (i < text.Length && text[i] is 'e' or 'E')
            {
                var j = i + 1;
                if (j < text.Length && text[j] is '+' or '-') j++;
                var exponentDigits = 0;
                while (j < text.Length && text[j] is >= '0' and <= '9') { j++; exponentDigits++; }
                if (exponentDigits > 0) i = j;
            }
            if (i - start > MaxNumberLength) return false;
            if (!double.TryParse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value)) return false;
            index = i;
            return true;
        }

        // An arc flag: one character, 0 or 1, with no separator needed after it.
        public bool Flag(out bool value)
        {
            value = false;
            SkipSeparators();
            if (index >= text.Length || text[index] is not ('0' or '1')) return false;
            value = text[index] == '1';
            index++;
            return true;
        }
    }
}
