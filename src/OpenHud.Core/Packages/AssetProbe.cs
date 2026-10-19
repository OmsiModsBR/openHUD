using System.Text;
using System.Text.RegularExpressions;

namespace OpenHud.Packages;

// Header-and-size validation of the files a package carries. Nothing is decoded: the checks
// say "this is what it claims to be and within limits", the draw layer decodes later.
public static class AssetProbe
{
    public const int MaxPictureSide = 2048;
    public const long MaxPictureBytes = 2L * 1024 * 1024;
    public const long MaxSvgBytes = 256L * 1024;
    public const long MaxFontBytes = 4L * 1024 * 1024;

    // Reads the 33-byte PNG prefix (signature + IHDR chunk) and checks it, CRC included.
    public static bool TryProbePng(byte[] prefix, long length, out int width, out int height, out string reason)
    {
        width = height = 0;
        if (length > MaxPictureBytes) { reason = "larger than " + MaxPictureBytes / (1024 * 1024) + " MiB"; return false; }
        if (prefix.Length < 33 || length < 57) { reason = "too short to be a PNG"; return false; }
        ReadOnlySpan<byte> signature = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!prefix.AsSpan(0, 8).SequenceEqual(signature)) { reason = "not a PNG (signature)"; return false; }
        if (U32(prefix, 8) != 13 || prefix[12] != 'I' || prefix[13] != 'H' || prefix[14] != 'D' || prefix[15] != 'R') { reason = "PNG header chunk missing"; return false; }
        if (Crc32(prefix.AsSpan(12, 17)) != U32(prefix, 29)) { reason = "PNG header checksum mismatch"; return false; }
        var w = U32(prefix, 16);
        var h = U32(prefix, 20);
        if (w == 0 || h == 0 || w > MaxPictureSide || h > MaxPictureSide) { reason = "PNG is " + w + "x" + h + ", the limit is " + MaxPictureSide + "x" + MaxPictureSide; return false; }
        int depth = prefix[24], color = prefix[25];
        var valid = color switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => false,
        };
        if (!valid) { reason = "PNG colour type/bit depth combination is invalid"; return false; }
        if (prefix[26] != 0 || prefix[27] != 0 || prefix[28] > 1) { reason = "PNG compression/filter/interlace method unknown"; return false; }
        width = (int)w; height = (int)h; reason = "";
        return true;
    }

    private static readonly Regex SvgRoot = new(@"\A\s*(<\?xml[^>]*\?>\s*)?(<!--.*?-->\s*)*<svg[\s>]", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex HrefAttr = new(@"href\s*=\s*[""']\s*([^""']*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex UrlFunc = new(@"url\(\s*[""']?\s*([^)""'\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex EventAttr = new(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly string[] SvgForbidden = { "<script", "<foreignobject", "<!doctype", "<!entity", "<iframe", "<image", "<embed", "<object", "javascript:", "@import", "<?xml-stylesheet" };

    // The static SVG subset: paths, shapes, fills; no scripts, no external references.
    public static bool TryProbeSvg(byte[] content, out string reason)
    {
        if (content.Length > MaxSvgBytes) { reason = "larger than " + MaxSvgBytes / 1024 + " KiB"; return false; }
        string text;
        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        try { text = new UTF8Encoding(false, true).GetString(content, start, content.Length - start); }
        catch (DecoderFallbackException) { reason = "not valid UTF-8"; return false; }
        try
        {
            // Said first and precisely: a file that opens with a DOCTYPE is refused for that, not for "no root element".
            if (text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            { reason = "a DOCTYPE declaration is refused (it can define entities and load external files); remove the <!DOCTYPE ...> line"; return false; }
            if (!SvgRoot.IsMatch(text)) { reason = "no <svg> root element (only an XML declaration and comments may come before it)"; return false; }
            var lower = text.ToLowerInvariant();
            foreach (var word in SvgForbidden)
                if (lower.Contains(word)) { reason = "contains '" + word + "', which the static subset does not allow"; return false; }
            foreach (Match m in HrefAttr.Matches(text))
                if (!m.Groups[1].Value.StartsWith('#')) { reason = "external reference (href)"; return false; }
            foreach (Match m in UrlFunc.Matches(text))
                if (!m.Groups[1].Value.StartsWith('#')) { reason = "external reference (url())"; return false; }
            if (EventAttr.IsMatch(text)) { reason = "event handler attribute"; return false; }
        }
        catch (RegexMatchTimeoutException) { reason = "too complex to validate"; return false; }
        reason = "";
        return true;
    }

    // sfnt header and table directory of a static TrueType/OpenType font. `prefix` holds at least the directory.
    public static bool TryProbeFont(byte[] prefix, long length, out string reason)
    {
        if (length > MaxFontBytes) { reason = "larger than " + MaxFontBytes / (1024 * 1024) + " MiB"; return false; }
        if (prefix.Length < 12 || length < 12) { reason = "too short to be a font"; return false; }
        var version = U32(prefix, 0);
        var tag = Encoding.ASCII.GetString(prefix, 0, 4);
        if (version != 0x00010000 && tag != "true" && tag != "OTTO") { reason = "not a TrueType/OpenType font (collections and web fonts are not read)"; return false; }
        int tables = (prefix[4] << 8) | prefix[5];
        if (tables == 0 || tables > 128) { reason = "implausible table count"; return false; }
        if (prefix.Length < 12 + 16 * tables) { reason = "table directory truncated"; return false; }
        var hasHead = false; var hasOutlines = false;
        for (var i = 0; i < tables; i++)
        {
            var o = 12 + 16 * i;
            var name = Encoding.ASCII.GetString(prefix, o, 4);
            var offset = U32(prefix, o + 8);
            var size = U32(prefix, o + 12);
            if ((long)offset + size > length) { reason = "table '" + name + "' lies outside the file"; return false; }
            if (name == "fvar") { reason = "variable font (instance it offline)"; return false; }
            if (name == "head") hasHead = true;
            if (name is "glyf" or "CFF ") hasOutlines = true;
        }
        if (!hasHead || !hasOutlines) { reason = "no head table or outlines"; return false; }
        reason = "";
        return true;
    }

    public static int FontPrefixSize => 12 + 16 * 128;

    private static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    private static readonly uint[] CrcTable = BuildCrc();
    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
