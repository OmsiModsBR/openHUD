using System.Security.Cryptography;
using System.Text;

namespace OpenHud.Packages;

// Read-only, path-safe access to one package folder. Nothing outside the folder is ever
// opened: a relative path is checked textually first, then every component below the
// root is checked not to be a link (reparse point) before the file is opened.
public sealed class PackageFolder
{
    public string Root { get; }
    private PackageFolder(string root) => Root = root;

    // Null when `path` is not an existing directory or is itself a link.
    public static PackageFolder? Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!Directory.Exists(full) || IsLink(full)) return null;
            return new PackageFolder(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    private static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    // The textual rule of the spec: forward slashes only, relative, no "..", no drive letter, no tricks.
    public static string? UnsafeReason(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return "empty path";
        if (relative.Length > 240) return "path too long";
        if (relative[0] == '/' || relative[0] == '\\') return "absolute path";
        if (relative.Contains('\\')) return "backslash (use forward slashes)";
        if (relative.Contains(':')) return "drive letter or stream";
        foreach (var c in relative)
            if (c < 0x20 || c is '*' or '?' or '"' or '<' or '>' or '|') return "invalid character";
        foreach (var segment in relative.Split('/'))
        {
            if (segment.Length == 0) return "empty path segment";
            if (segment == "." || segment == "..") return "'.' or '..' segment";
            if (segment[^1] == '.' || segment[^1] == ' ') return "segment ends with a dot or space";
            var stem = segment.Contains('.') ? segment[..segment.IndexOf('.')] : segment;
            if (Array.Exists(Reserved, r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase))) return "reserved device name";
        }
        return null;
    }

    // Resolves to a full path of an existing regular file/directory below the root, or a reason.
    public string? Resolve(string relative, out string? reason, bool directory = false)
    {
        reason = UnsafeReason(relative);
        if (reason is not null) return null;
        try
        {
            var current = Root;
            foreach (var segment in relative.Split('/'))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) { reason = "not found"; return null; }
                if (IsLink(current)) { reason = "is or goes through a link"; return null; }
            }
            var full = Path.GetFullPath(current);
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { reason = "resolves outside the package"; return null; }
            if (directory ? !Directory.Exists(full) : !File.Exists(full)) { reason = directory ? "not a folder" : "not a file"; return null; }
            return full;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            reason = "cannot be read: " + exception.Message;
            return null;
        }
    }

    public bool Exists(string relative) => Resolve(relative, out _) is not null;

    // Length of a safe file, or -1.
    public long Length(string relative)
    {
        var full = Resolve(relative, out _);
        if (full is null) return -1;
        try { return new FileInfo(full).Length; } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return -1; }
    }

    // Whole file when it is at most maxBytes; otherwise null with the reason.
    public byte[]? ReadAll(string relative, long maxBytes, out string? reason)
    {
        var prefix = ReadPrefix(relative, maxBytes, out var length, out reason);
        if (prefix is null) return null;
        if (length > maxBytes) { reason = "larger than " + maxBytes + " bytes"; return null; }
        return prefix;
    }

    // The first `count` bytes (fewer when the file is shorter) and the true length. Bytes past the end of
    // a file that grows between the length check and the read are never returned.
    public byte[]? ReadPrefix(string relative, long count, out long length, out string? reason)
    {
        length = -1;
        var full = Resolve(relative, out reason);
        if (full is null) return null;
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            length = stream.Length;
            var size = (int)Math.Min(count, length);
            var buffer = new byte[size];
            var read = 0;
            while (read < size)
            {
                var n = stream.Read(buffer, read, size - read);
                if (n <= 0) break;
                read += n;
            }
            if (read != size) { reason = "file changed while reading"; return null; }
            return buffer;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reason = "cannot be read: " + exception.Message;
            return null;
        }
    }

    // Regular files directly in a sub-folder (ordinal order); links and sub-folders are skipped.
    public IReadOnlyList<(string Name, long Length)> ListFiles(string relativeFolder)
    {
        var folder = relativeFolder.Length == 0 ? Root : Resolve(relativeFolder, out _, directory: true);
        if (folder is null) return Array.Empty<(string, long)>();
        var result = new List<(string, long)>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                if (IsLink(path)) continue;
                var info = new FileInfo(path);
                result.Add((info.Name, info.Length));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        result.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return result;
    }

    // A cache key over the package's files (name, size, write time), never a trust decision.
    public string Fingerprint(int maxFiles = 4096)
    {
        var lines = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Root);
        var truncated = false;
        try
        {
            // Manual walk: links are neither followed nor listed.
            while (pending.Count > 0 && !truncated)
            {
                var folder = pending.Pop();
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (IsLink(path)) continue;
                    if (Directory.Exists(path)) { pending.Push(path); continue; }
                    if (lines.Count >= maxFiles) { truncated = true; break; }
                    var info = new FileInfo(path);
                    lines.Add(Path.GetRelativePath(Root, path).Replace('\\', '/') + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { lines.Add("error"); }
        lines.Sort(StringComparer.Ordinal);
        var text = string.Join("\n", lines) + (truncated ? "\n...more" : "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return true; }
    }
}
