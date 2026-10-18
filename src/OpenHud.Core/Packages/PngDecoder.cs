using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenHud.Packages;

// Straight-alpha RGBA pixels, w*h*4 bytes.
public sealed record DecodedImage(int Width, int Height, byte[] Rgba);

// A small, strict PNG decoder for the pictures of the packages (symbols and marker icons). Every
// size is checked before memory is allocated: the picture is at most 2048x2048 and 2 MiB, the
// inflated data must match the size the header implies (a decompression bomb is cut at that
// size), chunk checksums must hold, and anything unusual is refused with a reason instead of
// guessed. Non-interlaced and Adam7 images of every colour type and bit depth are read.
public static class PngDecoder
{
    public static DecodedImage? Decode(byte[] file, out string reason)
    {
        try { return DecodeCore(file, out reason); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            reason = "damaged picture data";
            return null;
        }
    }

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static DecodedImage? DecodeCore(byte[] file, out string reason)
    {
        if (file is null || file.Length < 57) { reason = "too short to be a PNG"; return null; }
        if (file.Length > AssetProbe.MaxPictureBytes) { reason = "larger than " + AssetProbe.MaxPictureBytes / (1024 * 1024) + " MiB"; return null; }
        if (!file.AsSpan(0, 8).SequenceEqual(Signature)) { reason = "not a PNG (signature)"; return null; }
        if (!AssetProbe.TryProbePng(file, file.Length, out var width, out var height, out reason)) return null;

        int depth = file[24], colorType = file[25];
        bool interlaced = file[28] == 1;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        var position = 8;
        var sawEnd = false;
        var sawData = false;
        while (position + 12 <= file.Length && !sawEnd)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position));
            if (length > int.MaxValue || position + 12L + length > file.Length) { reason = "a chunk reaches past the end of the file"; return null; }
            var type = System.Text.Encoding.ASCII.GetString(file, position + 4, 4);
            var body = file.AsSpan(position + 8, (int)length);
            var expected = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position + 8 + (int)length));
            if (AssetProbe.Crc32(file.AsSpan(position + 4, 4 + (int)length)) != expected) { reason = "chunk " + type + " has a bad checksum"; return null; }
            switch (type)
            {
                case "IHDR":
                    if (position != 8) { reason = "second header chunk"; return null; }
                    break;
                case "PLTE":
                    if (sawData || palette is not null || length == 0 || length % 3 != 0 || length > 768) { reason = "invalid palette"; return null; }
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    if (sawData) { reason = "transparency after the picture data"; return null; }
                    transparency = body.ToArray();
                    break;
                case "IDAT":
                    sawData = true;
                    compressed.Write(body);
                    break;
                case "IEND":
                    sawEnd = true;
                    break;
                default:
                    // An unknown critical chunk (upper-case first letter) changes the meaning of the data.
                    if (type[0] is >= 'A' and <= 'Z' && type is not ("IHDR" or "PLTE" or "IDAT" or "IEND")) { reason = "unknown critical chunk " + type; return null; }
                    break;
            }
            position += 12 + (int)length;
        }
        if (!sawEnd || !sawData) { reason = "no picture data"; return null; }
        if (colorType == 3 && palette is null) { reason = "palette missing"; return null; }

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var bitsPerPixel = channels * depth;
        var pixelBytes = Math.Max(1, bitsPerPixel / 8);
        var passes = interlaced ? Adam7 : new[] { (0, 0, 1, 1) };

        long rawSize = 0;
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var pw = (width - x0 + dx - 1) / dx;
            var ph = (height - y0 + dy - 1) / dy;
            if (pw > 0 && ph > 0) rawSize += (long)ph * (1 + ((long)pw * bitsPerPixel + 7) / 8);
        }
        if (rawSize > 64L * 1024 * 1024) { reason = "picture data too large"; return null; }

        var raw = new byte[rawSize];
        compressed.Position = 0;
        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            var filled = 0;
            while (filled < raw.Length)
            {
                var n = inflate.Read(raw, filled, raw.Length - filled);
                if (n <= 0) break;
                filled += n;
            }
            if (filled != raw.Length) { reason = "the picture data is shorter than its header says"; return null; }
            if (inflate.ReadByte() != -1) { reason = "the picture data is longer than its header says"; return null; }
        }

        var rgba = new byte[checked(width * height * 4)];
        var offset = 0;
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var pw = (width - x0 + dx - 1) / dx;
            var ph = (height - y0 + dy - 1) / dy;
            if (pw <= 0 || ph <= 0) continue;
            var rowBytes = (int)(((long)pw * bitsPerPixel + 7) / 8);
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            for (var row = 0; row < ph; row++)
            {
                var filter = raw[offset++];
                Buffer.BlockCopy(raw, offset, current, 0, rowBytes);
                offset += rowBytes;
                if (!Unfilter(filter, current, previous, pixelBytes)) { reason = "unknown scanline filter"; return null; }
                for (var col = 0; col < pw; col++)
                {
                    var target = ((y0 + row * dy) * width + x0 + col * dx) * 4;
                    Pixel(current, col, colorType, depth, palette, transparency, rgba, target);
                }
                (previous, current) = (current, previous);
            }
        }
        reason = "";
        return new DecodedImage(width, height, rgba);
    }

    private static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7 =
    {
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
    };

    private static bool Unfilter(byte filter, byte[] row, byte[] up, int bpp)
    {
        switch (filter)
        {
            case 0: return true;
            case 1:
                for (var i = bpp; i < row.Length; i++) row[i] = (byte)(row[i] + row[i - bpp]);
                return true;
            case 2:
                for (var i = 0; i < row.Length; i++) row[i] = (byte)(row[i] + up[i]);
                return true;
            case 3:
                for (var i = 0; i < row.Length; i++) row[i] = (byte)(row[i] + (((i >= bpp ? row[i - bpp] : 0) + up[i]) >> 1));
                return true;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    int a = i >= bpp ? row[i - bpp] : 0, b = up[i], c = i >= bpp ? up[i - bpp] : 0;
                    var p = a + b - c;
                    int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    row[i] = (byte)(row[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                }
                return true;
            default: return false;
        }
    }

    // The value of sample `index` of a scanline, scaled so the maximum of its depth is 65535 for 16 bits and left raw otherwise.
    private static int Sample(byte[] row, int index, int depth)
    {
        switch (depth)
        {
            case 8: return row[index];
            case 16: return (row[index * 2] << 8) | row[index * 2 + 1];
            default:
                var bit = index * depth;
                var shift = 8 - depth - (bit & 7);
                return (row[bit >> 3] >> shift) & ((1 << depth) - 1);
        }
    }

    private static byte To8(int value, int depth) => depth switch
    {
        8 => (byte)value,
        16 => (byte)(value >> 8),
        _ => (byte)(value * 255 / ((1 << depth) - 1)),
    };

    private static void Pixel(byte[] row, int col, int colorType, int depth, byte[]? palette, byte[]? trns, byte[] rgba, int target)
    {
        switch (colorType)
        {
            case 0:
            {
                var v = Sample(row, col, depth);
                var g = To8(v, depth);
                var alpha = trns is { Length: >= 2 } && v == ((trns[0] << 8) | trns[1]) ? 0 : 255;
                rgba[target] = rgba[target + 1] = rgba[target + 2] = g;
                rgba[target + 3] = (byte)alpha;
                break;
            }
            case 2:
            {
                int r = Sample(row, col * 3, depth), g = Sample(row, col * 3 + 1, depth), b = Sample(row, col * 3 + 2, depth);
                var alpha = trns is { Length: >= 6 } && r == ((trns[0] << 8) | trns[1]) && g == ((trns[2] << 8) | trns[3]) && b == ((trns[4] << 8) | trns[5]) ? 0 : 255;
                rgba[target] = To8(r, depth); rgba[target + 1] = To8(g, depth); rgba[target + 2] = To8(b, depth); rgba[target + 3] = (byte)alpha;
                break;
            }
            case 3:
            {
                var index = Sample(row, col, depth);
                if (index * 3 + 2 >= palette!.Length) { rgba[target + 3] = 0; break; } // an index past the palette is transparent, not an error
                rgba[target] = palette[index * 3]; rgba[target + 1] = palette[index * 3 + 1]; rgba[target + 2] = palette[index * 3 + 2];
                rgba[target + 3] = trns is not null && index < trns.Length ? trns[index] : (byte)255;
                break;
            }
            case 4:
            {
                var g = To8(Sample(row, col * 2, depth), depth);
                rgba[target] = rgba[target + 1] = rgba[target + 2] = g;
                rgba[target + 3] = To8(Sample(row, col * 2 + 1, depth), depth);
                break;
            }
            default:
                rgba[target] = To8(Sample(row, col * 4, depth), depth);
                rgba[target + 1] = To8(Sample(row, col * 4 + 1, depth), depth);
                rgba[target + 2] = To8(Sample(row, col * 4 + 2, depth), depth);
                rgba[target + 3] = To8(Sample(row, col * 4 + 3, depth), depth);
                break;
        }
    }

    // Box-filters the picture down by the smallest whole factor that makes both sides at most maxSide
    // (alpha-weighted, so a transparent border does not darken the edge). A picture that fits is returned as is.
    public static DecodedImage Downscale(DecodedImage image, int maxSide)
    {
        if (maxSide < 1) throw new ArgumentOutOfRangeException(nameof(maxSide));
        var factor = (Math.Max(image.Width, image.Height) + maxSide - 1) / maxSide;
        if (factor <= 1) return image;
        var w = Math.Max(1, image.Width / factor);
        var h = Math.Max(1, image.Height / factor);
        var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                long r = 0, g = 0, b = 0, a = 0, count = 0;
                for (var sy = y * factor; sy < Math.Min(image.Height, (y + 1) * factor); sy++)
                {
                    for (var sx = x * factor; sx < Math.Min(image.Width, (x + 1) * factor); sx++)
                    {
                        var i = (sy * image.Width + sx) * 4;
                        var alpha = image.Rgba[i + 3];
                        r += image.Rgba[i] * alpha; g += image.Rgba[i + 1] * alpha; b += image.Rgba[i + 2] * alpha; a += alpha; count++;
                    }
                }
                var o = (y * w + x) * 4;
                if (a > 0) { output[o] = (byte)(r / a); output[o + 1] = (byte)(g / a); output[o + 2] = (byte)(b / a); }
                output[o + 3] = (byte)(count == 0 ? 0 : a / count);
            }
        }
        return new DecodedImage(w, h, output);
    }
}
