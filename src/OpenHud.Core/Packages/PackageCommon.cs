using System.Text;

namespace OpenHud.Packages;

public enum DiagnosticSeverity { Info, Dropped, Ignored }

// Absent: no package. Ignored: a package that cannot be used (unknown format, unsafe, unreadable). Loaded: in use.
public enum PackageStatus { Absent, Ignored, Loaded }

// One line of the package log: an entry (file or key) that was dropped or a package that was ignored, and why.
public sealed record PackageDiagnostic(DiagnosticSeverity Severity, string Entry, string Reason)
{
    public override string ToString() => Severity + ": " + Entry + ": " + Reason;
}

// Collects diagnostics, bounded so a hostile package cannot grow the log without limit.
public sealed class DiagnosticSink
{
    public const int MaxEntries = 200;
    private readonly List<PackageDiagnostic> items = new();
    private int overflow;

    public void Add(DiagnosticSeverity severity, string entry, string reason)
    {
        if (items.Count < MaxEntries) items.Add(new PackageDiagnostic(severity, Clip(entry), reason));
        else overflow++;
    }

    public void Info(string entry, string reason) => Add(DiagnosticSeverity.Info, entry, reason);
    public void Dropped(string entry, string reason) => Add(DiagnosticSeverity.Dropped, entry, reason);
    public void Ignored(string entry, string reason) => Add(DiagnosticSeverity.Ignored, entry, reason);

    public IReadOnlyList<PackageDiagnostic> ToList()
    {
        var list = new List<PackageDiagnostic>(items);
        if (overflow > 0) list.Add(new PackageDiagnostic(DiagnosticSeverity.Info, "log", overflow + " more entries not listed"));
        return list;
    }

    private static string Clip(string value) => value.Length <= 160 ? value : value[..160] + "...";
}

// #rrggbb or #rrggbbaa, straight alpha.
public readonly record struct HudColor(byte R, byte G, byte B, byte A = 255)
{
    public static bool TryParse(string? text, out HudColor color)
    {
        color = default;
        if (text is null) return false;
        text = text.Trim();
        if (text.Length is not (7 or 9) || text[0] != '#') return false;
        for (var i = 1; i < text.Length; i++) if (!Uri.IsHexDigit(text[i])) return false;
        byte At(int i) => Convert.ToByte(text.Substring(i, 2), 16);
        color = new HudColor(At(1), At(3), At(5), text.Length == 9 ? At(7) : (byte)255);
        return true;
    }

    public string ToHex() => A == 255 ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";
}

public readonly record struct CfgEntry(string Key, string Value, int Line);

// A parsed key = value file. Comments (#), blank lines, BOM, CRLF/LF/CR are handled;
// a file that is not valid UTF-8 or holds a NUL is refused as a whole.
public sealed class CfgDocument
{
    private readonly Dictionary<string, CfgEntry> first = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CfgEntry> Entries { get; }

    internal CfgDocument(List<CfgEntry> entries, DiagnosticSink sink, string name, ISet<string>? repeatable)
    {
        Entries = entries;
        foreach (var entry in entries)
        {
            if (first.TryAdd(entry.Key, entry)) continue;
            if (repeatable is null || !repeatable.Contains(entry.Key))
                sink.Dropped(name + ":" + entry.Line, "duplicate key '" + entry.Key + "' (the first one counts)");
        }
    }

    // First occurrence of a key (case-insensitive) or null.
    public string? Get(string key) => first.TryGetValue(key, out var e) ? e.Value : null;
    public IEnumerable<CfgEntry> Unique => first.Values;
    public IEnumerable<CfgEntry> All(string key) => Entries.Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}

public static class CfgReader
{
    public const int MaxLines = 20_000;
    public const int MaxLineLength = 8192;
    private static readonly UTF8Encoding Strict = new(false, true);

    // Returns null (and logs) when the file as a whole cannot be used.
    public static CfgDocument? Parse(byte[] bytes, string name, DiagnosticSink sink, ISet<string>? repeatableKeys = null)
    {
        var text = Decode(bytes, name, sink);
        if (text is null) return null;

        var entries = new List<CfgEntry>();
        var lineNo = 0;
        var pos = 0;
        while (pos <= text.Length)
        {
            var end = pos;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
            lineNo++;
            if (lineNo > MaxLines) { sink.Dropped(name, "more than " + MaxLines + " lines; the rest is not read"); break; }
            var line = text.AsSpan(pos, end - pos);
            if (end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n') end++;
            pos = end + 1;
            if (line.Length > MaxLineLength) { sink.Dropped(name + ":" + lineNo, "line longer than " + MaxLineLength); continue; }
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) { sink.Dropped(name + ":" + lineNo, "not a key = value line"); continue; }
            var key = trimmed[..eq].Trim().ToString();
            var value = StripInlineComment(trimmed[(eq + 1)..].Trim().ToString());
            if (key.Length == 0 || key.Any(char.IsControl)) { sink.Dropped(name + ":" + lineNo, "invalid key"); continue; }
            if (value.Any(c => char.IsControl(c) && c != '\t')) { sink.Dropped(name + ":" + lineNo, "control character in the value of '" + key + "'"); continue; }
            entries.Add(new CfgEntry(key, value, lineNo));
        }
        return new CfgDocument(entries, sink, name, repeatableKeys);
    }

    // Strict UTF-8 (a BOM is skipped); null and a log line when the bytes are not text.
    public static string? Decode(byte[] bytes, string name, DiagnosticSink sink)
    {
        string text;
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { text = Strict.GetString(bytes, start, bytes.Length - start); }
        catch (DecoderFallbackException) { sink.Dropped(name, "not valid UTF-8"); return null; }
        if (text.Contains('\0')) { sink.Dropped(name, "contains a NUL character"); return null; }
        return text;
    }

    // "value   # note": a '#' after whitespace ends the value; a value that starts with '#' is a colour.
    private static string StripInlineComment(string value)
    {
        for (var i = 1; i < value.Length; i++)
            if (value[i] == '#' && char.IsWhiteSpace(value[i - 1])) return value[..i].TrimEnd();
        return value;
    }
}
