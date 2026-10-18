using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OmsiLaunch.Runtime.Modules;
using OpenHud.Model;
using OpenHud.Packages;
using OpenHud.Scene;
using OpenHud.Tests.Core;

namespace OpenHud.Tests.ModuleTests;

// Where the module template lives (found above the test binary).
internal static class Repo
{
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "module.template.json"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("module.template.json not found above " + AppContext.BaseDirectory);
    }

    public static string TemplatePath => Path.Combine(Root(), "module.template.json");

    // The manifest with its pins filled in the way the build does (only the shape matters for the parser).
    public static ModuleManifest Manifest()
    {
        var text = File.ReadAllText(TemplatePath).Replace("\"sha256\": \"\"", "\"sha256\": \"" + new string('0', 64) + "\"");
        return ModuleManifestParser.Parse(Encoding.UTF8.GetBytes(text));
    }
}

// A module wired to the fakes, a clock, and a recorded drive. Everything runs on the creating thread, the
// way the bridge calls a module on OMSI's UI thread; only the module's own background work runs elsewhere.
internal sealed class Session : IDisposable
{
    public const string MapIdentity = "Maps\\Synthetic Map\\global.cfg";

    public readonly TempDir Dir = new();
    public readonly FakeContext Context;
    public readonly FakeClock Clock = new();
    public readonly HudModule Module;
    public readonly SyntheticDrive Drive;
    public int Frame;
    public bool Armed, Ready, Stopped;
    // The drive is replayed in a loop so a long run stays inside the synthetic map's tiles.
    public const int Cycle = 150;
    private readonly List<ReplayFrame> frames;

    public FakeRuntime Runtime => Context.RuntimeFake;
    public FakeNative Native => Context.NativeFake;
    public FakeInput Input => Context.InputFake;
    public FakePauseCatalog Pause => Context.RuntimeFake.Pause;
    public string MapsRoot => Path.Combine(Dir.Path, "maps", "Synthetic Map");

    public Session(Action<SyntheticDrive>? drive = null, Action<Dictionary<string, string>>? settings = null)
    {
        Context = new FakeContext(Repo.Manifest().EffectiveRuntimeOperations, Dir.Path);
        Drive = new SyntheticDrive { Frames = Cycle, StartX = 100, StartY = 100, HeadingDegrees = 90, SpeedMetresPerSecond = 2, AiVehicles = 12 };
        drive?.Invoke(Drive);
        frames = Drive.Generate();
        if (settings is not null) settings(Context.SettingsValues);
        Module = new HudModule(Clock);
        Runtime.Frame = frames[0];
    }

    public void Arm() { Module.Arm(Context); Armed = true; }

    public void GameplayReady() { Module.GameplayReady(); Ready = true; }

    public void Start() { Arm(); GameplayReady(); }

    // One module tick per call: the clock moves, the catalog shows the next frame of the drive.
    public void Tick(int times = 1, int advanceMs = 50, bool advanceDrive = true)
    {
        for (var i = 0; i < times; i++)
        {
            Clock.Now += advanceMs;
            if (advanceDrive) Runtime.Frame = frames[Frame = (Frame + 1) % frames.Count];
            Module.Tick(TimeSpan.FromMilliseconds(advanceMs));
        }
    }

    public IReadOnlyDictionary<string, string> Status() => Module.Execute("status", new Dictionary<string, string>());

    public bool WaitFor(Func<bool> condition, int milliseconds = 8000)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    // Ticks until the packages are adopted (the worker thread finishes in its own time).
    public bool WaitForPackages(int milliseconds = 8000)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Tick(1, 50, advanceDrive: false);
            if (Status()["packages"] == "loaded") return true;
            Thread.Sleep(5);
        }
        return false;
    }

    public void Stop()
    {
        if (Stopped) return;
        Stopped = true;
        Module.Stop();
    }

    public void Dispose()
    {
        try { Stop(); } catch (Exception) { }
        Context.Shutdown();
        Dir.Dispose();
    }

    // ---- what the draw layer was given

    public IEnumerable<SceneDocument> SceneDocuments() => Native.Scenes.Select(Doc.Decode);

    public void CheckAllScenesValid(string context)
    {
        Gates.Check(Native.SceneRejects == 0, context + ": the draw layer rejected " + Native.SceneRejects + " scene(s): " + string.Join(" | ", Native.Problems.Take(3)));
        foreach (var json in Native.Scenes)
        {
            var issues = SceneValidator.Validate(Doc.Decode(json));
            Gates.Check(issues.Count == 0, context + ": invalid scene: " + string.Join("; ", issues));
        }
    }
}

// A PNG writer for the tests (the module only reads them): every colour type and depth, with or without Adam7.
internal static class PngWriter
{
    public static byte[] Rgba(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel, bool interlaced = false)
    {
        var raw = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var o = (y * width + x) * 4;
                raw[o] = r; raw[o + 1] = g; raw[o + 2] = b; raw[o + 3] = a;
            }
        return Encode(width, height, 8, 6, raw, interlaced);
    }

    // `samples` holds the pixels' bytes row by row without padding (for sub-byte depths: already packed per row).
    public static byte[] Encode(int width, int height, int depth, int colorType, byte[] samples, bool interlaced = false,
        byte[]? palette = null, byte[]? transparency = null, byte filter = 0, byte[]? corruptCrc = null)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = (byte)depth; ihdr[9] = (byte)colorType; ihdr[12] = (byte)(interlaced ? 1 : 0);
        Chunk(output, "IHDR", ihdr);
        if (palette is not null) Chunk(output, "PLTE", palette);
        if (transparency is not null) Chunk(output, "tRNS", transparency);
        var bits = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 } * depth;
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, true))
        {
            if (!interlaced) WritePass(z, samples, width, height, 0, 0, 1, 1, width, bits, filter);
            else
                foreach (var (x0, y0, dx, dy) in new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) })
                    WritePass(z, samples, width, height, x0, y0, dx, dy, width, bits, filter);
        }
        Chunk(output, "IDAT", idat.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        var bytes = output.ToArray();
        if (corruptCrc is not null) bytes[^1] ^= 0xFF;
        return bytes;
    }

    // Extracts one pass from full-resolution packed rows (only valid for depths of 8 or more bits per pixel, or
    // for whole-image passes at lower depths) and writes it with the given filter.
    private static void WritePass(Stream z, byte[] samples, int width, int height, int x0, int y0, int dx, int dy, int fullWidth, int bitsPerPixel, byte filter)
    {
        var pw = (width - x0 + dx - 1) / dx;
        var ph = (height - y0 + dy - 1) / dy;
        if (pw <= 0 || ph <= 0) return;
        var fullRow = (width * bitsPerPixel + 7) / 8;
        var rowBytes = (pw * bitsPerPixel + 7) / 8;
        var previous = new byte[rowBytes];
        for (var row = 0; row < ph; row++)
        {
            var line = new byte[rowBytes];
            var sy = y0 + row * dy;
            if (bitsPerPixel >= 8)
            {
                var bpp = bitsPerPixel / 8;
                for (var col = 0; col < pw; col++) Array.Copy(samples, sy * fullRow + (x0 + col * dx) * bpp, line, col * bpp, bpp);
            }
            else
            {
                for (var col = 0; col < pw; col++)
                {
                    var sx = x0 + col * dx;
                    var value = (samples[sy * fullRow + (sx * bitsPerPixel >> 3)] >> (8 - bitsPerPixel - ((sx * bitsPerPixel) & 7))) & ((1 << bitsPerPixel) - 1);
                    line[(col * bitsPerPixel) >> 3] |= (byte)(value << (8 - bitsPerPixel - ((col * bitsPerPixel) & 7)));
                }
            }
            z.WriteByte(filter);
            var bppFilter = Math.Max(1, bitsPerPixel / 8);
            for (var i = 0; i < rowBytes; i++)
            {
                int a = i >= bppFilter ? line[i - bppFilter] : 0, b = previous[i], c = i >= bppFilter ? previous[i - bppFilter] : 0;
                var predicted = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                z.WriteByte((byte)(line[i] - predicted));
            }
            previous = line;
        }
    }

    // A PNG whose compressed data is exactly `raw` (whatever the header says it should be): for hostile-input gates.
    public static byte[] FromRaw(int width, int height, int depth, int colorType, byte[] raw, string? extraChunk = null)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = (byte)depth; ihdr[9] = (byte)colorType;
        Chunk(output, "IHDR", ihdr);
        if (extraChunk is not null) Chunk(output, extraChunk, new byte[] { 1, 2, 3 });
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Optimal, true)) z.Write(raw);
        Chunk(output, "IDAT", idat.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        s.Write(length);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, AssetProbe.Crc32(body));
        s.Write(crc);
    }
}
