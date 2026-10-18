using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using OpenHud.Packages;

namespace OpenHud.Tests.Core;

// Helpers shared by the Core gates: a scratch folder, synthetic files and assertions with messages.
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ohud-core-" + Guid.NewGuid().ToString("N")[..12]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string Write(string relative, byte[] bytes)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    public string Write(string relative, string text) => Write(relative, new UTF8Encoding(false).GetBytes(text));

    public void Dispose()
    {
        try
        {
            // Junctions made by the gates are removed first so the delete never follows them.
            foreach (var directory in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories).ToList())
                if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) Directory.Delete(directory, false);
            Directory.Delete(Path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    // A directory junction (no privilege needed on Windows) from `link` to `target`.
    public static bool MakeJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info)!;
        process.WaitForExit(15000);
        return process.ExitCode == 0 && Directory.Exists(link);
    }
}

internal static class Synthetic
{
    // A PNG with a valid signature and IHDR (CRC included), one IDAT and IEND; the pixel data is not a real image,
    // which is fine because the reader validates headers only. `padding` grows the file.
    public static byte[] Png(int width, int height, int padding = 0, byte colorType = 6, byte depth = 8)
    {
        using var m = new MemoryStream();
        m.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = depth; ihdr[9] = colorType;
        Chunk(m, "IHDR", ihdr);
        Chunk(m, "IDAT", new byte[1 + padding]);
        Chunk(m, "IEND", Array.Empty<byte>());
        return m.ToArray();
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

    public static string Svg(string inner = "<path d=\"M0 0L10 10\" fill=\"#fff\"/>") =>
        "<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" + inner + "</svg>";

    // A TrueType file with a table directory (head, glyf plus extras) whose offsets lie inside the file.
    public static byte[] Font(int padding = 0, params string[] extraTables)
    {
        var tags = new[] { "head", "glyf" }.Concat(extraTables).ToArray();
        var dir = 12 + 16 * tags.Length;
        var total = dir + 16 + padding;
        var b = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(4), (ushort)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var o = 12 + 16 * i;
            Encoding.ASCII.GetBytes(tags[i]).CopyTo(b, o);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o + 8), (uint)dir);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o + 12), 8);
        }
        return b;
    }
}

internal static class Expect
{
    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{what}: expected '{expected}', got '{actual}'");
    }

    public static void Near(double expected, double actual, double tolerance, string what)
    {
        if (!(Math.Abs(expected - actual) <= tolerance)) throw new InvalidOperationException($"{what}: expected {expected} +/- {tolerance}, got {actual}");
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }

    public static void Throws<TException>(Action action, string what) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception exception) { throw new InvalidOperationException(what + ": threw " + exception.GetType().Name + " instead of " + typeof(TException).Name); }
        throw new InvalidOperationException(what + ": did not throw");
    }

    public static bool Mentions(IEnumerable<PackageDiagnostic> diagnostics, string entry, string? reasonPart = null) =>
        diagnostics.Any(d => d.Entry.Contains(entry, StringComparison.OrdinalIgnoreCase) && (reasonPart is null || d.Reason.Contains(reasonPart, StringComparison.OrdinalIgnoreCase)));
}
