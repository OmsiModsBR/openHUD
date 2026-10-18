using System.Text;

namespace OpenHud.Tests.Core;

// Builds synthetic surface.oms files (OMSF v1) in test code; no real package data is involved.
internal sealed class SurfaceBuilder
{
    public double OriginX = 1000, OriginY = 2000;
    public float ChunkSize = 2000;
    public uint Version = 1;
    public string Magic = "OMSF";
    private readonly List<string> strings = new();
    private readonly List<ChunkSpec> chunks = new();

    public sealed class ChunkSpec
    {
        public int Cx, Cy;
        public readonly List<(byte Class, float[] Tris)> Fills = new();
        public readonly List<(byte Class, byte Flags, float Width, uint Name, float[] Pts)> Lines = new();
        public readonly List<(byte Kind, byte Rank, uint Name, float X, float Y)> Labels = new();
        // Overrides that make the payload lie about its own structure.
        public uint? FillCountOverride, LineCountOverride, LabelCountOverride;
        public bool TruncatePayload;
    }

    public uint Str(string text)
    {
        var i = strings.IndexOf(text);
        if (i < 0) { strings.Add(text); i = strings.Count - 1; }
        return (uint)i;
    }

    public ChunkSpec Chunk(int cx, int cy)
    {
        var c = new ChunkSpec { Cx = cx, Cy = cy };
        chunks.Add(c);
        return c;
    }

    private static byte[] Payload(ChunkSpec c)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write(c.FillCountOverride ?? (uint)c.Fills.Count);
        foreach (var (cls, tris) in c.Fills) { w.Write(cls); w.Write((uint)(tris.Length / 6)); foreach (var f in tris) w.Write(f); }
        w.Write(c.LineCountOverride ?? (uint)c.Lines.Count);
        foreach (var (cls, flags, width, name, pts) in c.Lines) { w.Write(cls); w.Write(flags); w.Write(width); w.Write(name); w.Write((uint)(pts.Length / 2)); foreach (var f in pts) w.Write(f); }
        w.Write(c.LabelCountOverride ?? (uint)c.Labels.Count);
        foreach (var (kind, rank, name, x, y) in c.Labels) { w.Write(kind); w.Write(rank); w.Write(name); w.Write(x); w.Write(y); }
        w.Flush();
        var bytes = m.ToArray();
        return c.TruncatePayload ? bytes[..(bytes.Length / 2)] : bytes;
    }

    public byte[] Build()
    {
        var payloads = chunks.Select(Payload).ToList();
        using var head = new MemoryStream();
        using var w = new BinaryWriter(head, Encoding.UTF8);
        w.Write(Encoding.ASCII.GetBytes(Magic));
        w.Write(Version);
        w.Write(OriginX); w.Write(OriginY);
        w.Write(-5000f); w.Write(5000f); w.Write(-5000f); w.Write(5000f);
        w.Write(ChunkSize);
        w.Write((uint)strings.Count);
        foreach (var s in strings) { var b = Encoding.UTF8.GetBytes(s); w.Write((ushort)b.Length); w.Write(b); }
        w.Write((uint)chunks.Count);
        w.Flush();
        var indexStart = head.Length;
        var dataStart = indexStart + chunks.Count * 20L;
        var offset = dataStart;
        for (var i = 0; i < chunks.Count; i++)
        {
            w.Write(chunks[i].Cx); w.Write(chunks[i].Cy); w.Write((ulong)offset); w.Write((uint)payloads[i].Length);
            offset += payloads[i].Length;
        }
        w.Flush();
        foreach (var p in payloads) head.Write(p);
        return head.ToArray();
    }

    // Offset of the index (for tests that corrupt entries): header bytes before the first entry.
    public long IndexStart()
    {
        var size = 4 + 4 + 16 + 16 + 4 + 4 + 4L;
        foreach (var s in strings) size += 2 + Encoding.UTF8.GetByteCount(s);
        return size;
    }

    public static float[] Tri(float x, float y, float size = 100) => new[] { x, y, x + size, y, x, y + size };
}

internal static class RealmapFiles
{
    public const string Manifest =
        "format = 1\r\nname = Synthetic Map\r\ngrid = 300\r\ncredit = (c) synthetic test data\r\nnorth = 172.2\r\nalignment = fitted\r\n" +
        "water = #7fb2e5\r\nlight_water = #112233\r\nbuilt = notacolour\r\nplayer = triangle\r\nplayer_free = #ffffff     # no timetable\r\nplayer_line = #5588c7\r\nplayer_brt = #1e7b34\r\n" +
        "brt_lines = A1, A2 ,7\r\ntram_vehicles = Vehicles\\Tram\\, vehicles/tram2/\r\nmetro_vehicles = vehicles/metro/\r\nterrain_offset = 12.5\r\n";

    // Standard dataset. Origin (1000, 2000), chunks of 2000 m. Absolute = origin + relative.
    public static SurfaceBuilder Standard()
    {
        var b = new SurfaceBuilder();
        var river = b.Str("Synthetic River");
        var ruaA = b.Str("Street A");
        var avB = b.Str("Avenue B");
        var centro = b.Str("Centre");
        var praca = b.Str("Little Square");
        var border = new float[] { 0, 1500, 1900, 1500, 2100, 1600, 3900, 1600 };

        var c0 = b.Chunk(0, 0);
        c0.Fills.Add((0, SurfaceBuilder.Tri(100, 100, 300)));          // water, near the origin
        c0.Fills.Add((1, SurfaceBuilder.Tri(1500, 1500, 200)));        // green, far corner
        c0.Fills.Add((99, SurfaceBuilder.Tri(100, 100)));              // unknown class: skipped
        c0.Lines.Add((0, 0, 6, river, new float[] { 0, 500, 900, 520, 1900, 500 }));
        c0.Lines.Add((2, 0, 0, uint.MaxValue, border));
        c0.Lines.Add((3, 2, 0, ruaA, new float[] { 200, 200, 800, 200 }));
        c0.Lines.Add((4, 40, 60, avB, new float[] { 200, 300, 800, 300 }));
        c0.Lines.Add((5, 0, 100, uint.MaxValue, new float[] { 100, 900, 1800, 900 }));
        c0.Lines.Add((6, 0, 150, uint.MaxValue, new float[] { 100, 1000, 1800, 1000 }));
        c0.Lines.Add((77, 0, 1, uint.MaxValue, new float[] { 1, 1, 2, 2 })); // unknown line class: skipped
        c0.Labels.Add((0, 200, centro, 500, 500));
        c0.Labels.Add((3, 10, praca, 600, 600));
        c0.Labels.Add((9, 250, uint.MaxValue, 700, 700));                  // unknown kind: skipped

        var c1 = b.Chunk(1, 0);
        c1.Fills.Add((2, SurfaceBuilder.Tri(2100, 100, 300)));
        c1.Lines.Add((2, 0, 0, uint.MaxValue, border));                    // the same whole-map line repeated
        c1.Labels.Add((2, 100, b.Str("Town"), 2500, 500));

        var cn = b.Chunk(-1, 0);
        cn.Fills.Add((4, SurfaceBuilder.Tri(-800, 100, 300)));
        cn.Labels.Add((1, 255, b.Str("Terminal"), -500, 200));
        return b;
    }

    public const string Markers =
        "# icon | width | x | y | name\n" +
        "marker = icons/terminal.png | 30 | 1200.5 | 2840.0 | Central Terminal\n" +
        "marker = icons/depot.png | 54 | 1650 | 2410.5 | Depot\n" +
        "marker = icons/terminal.png | 30 | 9000 | 9000 | Far away\n" +
        "marker = icons/missing.png | 30 | 1 | 1 | no such file\n" +
        "marker = icons/../manifest.cfg | 30 | 1 | 1 | traversal\n" +
        "marker = icons/huge.png | 30 | 1 | 1 | picture too large\n" +
        "marker = icons/terminal.png | -4 | 1 | 1 | bad width\n" +
        "marker = icons/terminal.png | 30 | nan | 1 | bad x\n" +
        "marker = icons/terminal.png | 30 | 1\n" +
        "marker = elsewhere/x.png | 30 | 1 | 1 | not under icons\n";

    public const string Regions =
        "# region = name | label x | label y | box w | box h | polygon\n" +
        "region = Alpha | 5200.0 | 3100.0 | 4800.0 | 520.0 | 1000,1000 9000,1000 9000,6000 1000,6000\n" +
        "region = Inner | 5000 | 3000 | 0 | 0 | 4000,2000 6000,2000 6000,4000 4000,4000\n" +
        "region = Broken | 1 | 1 | 1 | 1 | 1,1 2,2\n" +
        "region = AlsoBroken | 1 | 1 | 1 | 1 | 1,1 2,x 3,3\n" +
        "region = Short | 1 | 1\n";

    public const string Lines =
        "# lines\r\nRLIN\t1\r\nORIGEM\t100.0\t200.0\r\n" +
        "L\t7\tGRP\tNorth - Central\tbus\t0\t0\r\n" +
        "R\t18.9\t35\t35\t0\tEvery day\tArticulated\tNorth - Central\r\nP\t1.0 2.0 3.0 4.0\r\nS\t1.0 2.0\r\n" +
        "R\t19.1\t34\t36\t0\tEvery day\tArticulated\tCentral - North\r\nP\t3.0 4.0 1.0 2.0\r\nS\t3.0 4.0\r\n" +
        "R\t1\t1\t1\t0\tx\ty\tthird route is dropped\r\nP\t0 0\r\n" +
        "L\t7\tGRP\tNorth - Central (old)\tbus\t0\t19891231\r\nR\t10\t10\t10\t1\tMon-Fri\tSmall\tLoop\r\nP\t0 0 1 1\r\nS\t0 0\r\n" +
        "L\tT1\tGRP\tTram line\ttram\t19900101\t0\r\n" +
        "L\tM1\tGRP\tMetro line\tmetro\r\n" +
        "L\tX1\tGRP\tHovercraft\thovercraft\r\n" +
        "L\tB1\tGRP\tBad dates\tbus\t20231301\t0\r\n" +
        "Z\tfuture record is ignored\r\n";

    public static void Write(TempDir dir, SurfaceBuilder? surface = null, bool markers = true, bool regions = true, bool lines = true, string? manifest = null)
    {
        dir.Write("manifest.cfg", manifest ?? Manifest);
        if (surface is not null) dir.Write("surface.oms", surface.Build());
        if (markers)
        {
            dir.Write("markers.cfg", Markers);
            dir.Write("icons/terminal.png", Synthetic.Png(64, 64));
            dir.Write("icons/depot.png", Synthetic.Png(128, 96));
            dir.Write("icons/huge.png", Synthetic.Png(1025, 10));
        }
        if (regions) dir.Write("regions.cfg", Regions);
        if (lines) dir.Write("lines.lin", Lines);
    }
}
