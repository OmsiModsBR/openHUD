using System.Text.Json;
using OpenHud.Radar;
using OpenHud.Scene;

namespace OpenHud.Tests;

// Shared helpers of the scene and radar gates.
internal static class SceneTestUtil
{
    public static string VectorsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "vectors");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/vectors not found above " + AppContext.BaseDirectory);
    }

    public static string ReadVector(string name) => File.ReadAllText(Path.Combine(VectorsDirectory(), name)).Replace("\r\n", "\n");

    /// <summary>Structural JSON equality: member order is irrelevant, numbers compare as doubles.</summary>
    public static bool JsonEqual(string a, string b)
    {
        using var da = JsonDocument.Parse(a);
        using var db = JsonDocument.Parse(b);
        return Equal(da.RootElement, db.RootElement);
    }

    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var ma = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var mb = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (ma.Count != mb.Count) return false;
                foreach (var kv in ma)
                    if (!mb.TryGetValue(kv.Key, out var other) || !Equal(kv.Value, other)) return false;
                return true;
            case JsonValueKind.Array:
                var xa = a.EnumerateArray().ToList();
                var xb = b.EnumerateArray().ToList();
                if (xa.Count != xb.Count) return false;
                for (int i = 0; i < xa.Count; i++) if (!Equal(xa[i], xb[i])) return false;
                return true;
            case JsonValueKind.Number: return a.GetDouble() == b.GetDouble();
            case JsonValueKind.String: return a.GetString() == b.GetString();
            default: return true; // true / false / null: the kind is the value
        }
    }

    public static T Throws<T>(Action action, string what) where T : Exception
    {
        try { action(); }
        catch (T e) { return e; }
        throw new InvalidOperationException(what + ": expected " + typeof(T).Name);
    }

    public static int Count<T>(SceneDocument scene) where T : SceneCommand => scene.Commands.OfType<T>().Count();

    public static List<PolyCommand> Polys(SceneDocument scene) => scene.Commands.OfType<PolyCommand>().ToList();

    /// <summary>Every coordinate of the scene's shapes lies within the canvas (plus a tolerance).</summary>
    public static bool WithinCanvas(SceneDocument scene, double tolerance)
    {
        bool Ok(double x, double y) => x >= -tolerance && y >= -tolerance && x <= scene.Width + tolerance && y <= scene.Height + tolerance;
        foreach (var c in scene.Commands)
        {
            switch (c)
            {
                case PolyCommand p:
                    for (int i = 0; i + 1 < p.Points.Length; i += 2) if (!Ok(p.Points[i], p.Points[i + 1])) return false;
                    break;
                case CircleCommand ci: if (!Ok(ci.Cx, ci.Cy)) return false; break;
                case RectCommand r: if (!Ok(r.X, r.Y) || !Ok(r.X + r.W, r.Y + r.H)) return false; break;
                case TextCommand t: if (!Ok(t.X, t.Y)) return false; break;
                case LineCommand l: if (!Ok(l.X1, l.Y1) || !Ok(l.X2, l.Y2)) return false; break;
            }
        }
        return true;
    }

    // ---- fixtures -------------------------------------------------------------------------

    /// <summary>The small fixed model behind the golden radar scene.</summary>
    public static RadarModel SmallModel(double heading = 30) => new(
        new RadarPlayer(0, 0, heading),
        new[]
        {
            new RadarFeature(RadarLayer.Water, new double[] { -90, 40, -30, 40, -30, 100, -90, 100 }, true),
            new RadarFeature(RadarLayer.Green, new double[] { 20, -120, 140, -120, 140, -40, 20, -40 }, true),
            new RadarFeature(RadarLayer.Built, new double[] { 60, 30, 100, 30, 100, 70, 60, 70 }, true),
            new RadarFeature(RadarLayer.Relief, new double[] { -150, -80, -60, -60, 20, -90, 120, -110 }),
            new RadarFeature(RadarLayer.Road, new double[] { -200, 0, 0, 0, 150, 100 }, false, null, 8),
            new RadarFeature(RadarLayer.Road, new double[] { 0, -200, 0, 0, 0, 220 }),
        },
        new[] { new RadarRegion("Riverside", 50, -60) },
        new[] { new RadarMarker(60, 60, "stop"), new RadarMarker(-120, 20) },
        new[]
        {
            new RadarVehicle(40, 20, 90, RadarVehicleKind.Bus),
            new RadarVehicle(-30, -50, 200, RadarVehicleKind.Car),
        },
        new double[] { -150, -20, 0, 10, 120, 90 },
        new RadarInfo("Central Station", "14:32", "12"),
        new[] { new RadarLine("12", "#3d8bfd", true), new RadarLine("34", "#e5484d") });

    /// <summary>Deterministic synthetic map: n features scattered over a square of the given half size.</summary>
    public static RadarModel DenseModel(int features, double halfSize = 2000, int vehicles = 0, int markers = 0, int seed = 7)
    {
        var rng = new Random(seed);
        var list = new List<RadarFeature>(features);
        var layers = new[] { RadarLayer.Road, RadarLayer.Road, RadarLayer.Water, RadarLayer.Green, RadarLayer.Built, RadarLayer.Relief };
        for (int i = 0; i < features; i++)
        {
            var layer = layers[i % layers.Length];
            double cx = (rng.NextDouble() * 2 - 1) * halfSize, cy = (rng.NextDouble() * 2 - 1) * halfSize;
            double size = 40 + rng.NextDouble() * 260;
            bool area = layer is RadarLayer.Water or RadarLayer.Green or RadarLayer.Built;
            var pts = new double[12];
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3 + (area ? 0 : rng.NextDouble() * 0.5);
                double rr = area ? size : size * (k + 1) / 6;
                pts[2 * k] = cx + Math.Cos(a) * rr;
                pts[2 * k + 1] = cy + Math.Sin(a) * rr * (area ? 1 : 0.4);
            }
            list.Add(new RadarFeature(layer, pts, area));
        }
        var vs = new List<RadarVehicle>();
        for (int i = 0; i < vehicles; i++)
            vs.Add(new RadarVehicle((rng.NextDouble() * 2 - 1) * halfSize, (rng.NextDouble() * 2 - 1) * halfSize, rng.NextDouble() * 360, (RadarVehicleKind)(i % 5)));
        var ms = new List<RadarMarker>();
        for (int i = 0; i < markers; i++)
            ms.Add(new RadarMarker((rng.NextDouble() * 2 - 1) * halfSize, (rng.NextDouble() * 2 - 1) * halfSize, i % 2 == 0 ? "stop" : null));
        return new RadarModel(new RadarPlayer(0, 0, 45), list, null, ms, vs);
    }
}
