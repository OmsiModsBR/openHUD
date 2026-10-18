using System.Buffers.Binary;
using System.Text;

namespace OpenHud.Packages.Realmap;

// Little-endian reader over a stream whose total length is known. Every read checks what is left
// first, so a count or size taken from the data can never run past the end or allocate beyond it.
internal sealed class LeReader
{
    private readonly Stream stream;
    private readonly byte[] scratch = new byte[8];

    public LeReader(Stream stream, long length)
    {
        this.stream = stream;
        Length = length;
    }

    public long Length { get; }
    public long Position { get; private set; }
    public long Remaining => Length - Position;

    public void Need(long bytes)
    {
        if (bytes < 0 || bytes > Remaining) throw new FormatException("data runs past the end");
    }

    private Span<byte> Take(int count)
    {
        Need(count);
        var span = scratch.AsSpan(0, count);
        ReadFully(span);
        Position += count;
        return span;
    }

    private void ReadFully(Span<byte> buffer)
    {
        var done = 0;
        while (done < buffer.Length)
        {
            var n = stream.Read(buffer[done..]);
            if (n <= 0) throw new EndOfStreamException();
            done += n;
        }
    }

    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public byte[] Bytes(int count)
    {
        Need(count);
        var buffer = new byte[count];
        ReadFully(buffer);
        Position += count;
        return buffer;
    }

    // count floats; the caller has already checked that count * 4 fits what is left.
    public float[] Floats(int count)
    {
        var bytes = Bytes(checked(count * 4));
        var result = new float[count];
        for (var i = 0; i < count; i++) result[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return result;
    }
}

internal readonly record struct ChunkKey(int X, int Y);
internal readonly record struct ChunkEntry(long Offset, int Length);

internal sealed class ParsedChunk
{
    public List<AreaFeature> Areas { get; } = new();
    public List<LineFeature> Lines { get; } = new();
    public List<LineFeature> Streets { get; } = new();
    public List<LabelFeature> Labels { get; } = new();
}

// surface.oms (OMSF version 1): header, string table, chunk index (kept in memory), and on-demand
// chunk payloads. Everything is bounds-checked; a chunk that cannot be parsed is just unreadable.
internal sealed class SurfaceFile
{
    public const int MaxChunkBytes = 64 * 1024 * 1024;
    public const int MaxStrings = 4_000_000;
    public const int MaxChunks = 1_000_000;
    public const float MaxCoordinate = 1e7f;
    private const int CacheSize = 24;

    private readonly string path;
    private readonly string[] strings;
    private readonly Dictionary<ChunkKey, ChunkEntry> index;
    private readonly double terrainOffset;
    private readonly object gate = new();
    private readonly Dictionary<ChunkKey, (ParsedChunk? Chunk, long Stamp)> cache = new();
    private long stamp;

    public double OriginX { get; }
    public double OriginY { get; }
    public double ChunkSize { get; }
    public int ChunkCount => index.Count;

    private SurfaceFile(string path, double ox, double oy, double chunkSize, string[] strings, Dictionary<ChunkKey, ChunkEntry> index, double terrainOffset)
    {
        this.path = path; OriginX = ox; OriginY = oy; ChunkSize = chunkSize; this.strings = strings; this.index = index; this.terrainOffset = terrainOffset;
    }

    // Null and a diagnostic when the header, string table or index cannot be trusted.
    public static SurfaceFile? Open(string path, double terrainOffset, DiagnosticSink sink)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            var r = new LeReader(file, file.Length);
            if (r.Length < 4 + 4 + 16 + 16 + 4 + 4 + 4) { sink.Dropped("surface.oms", "too short"); return null; }
            if (Encoding.ASCII.GetString(r.Bytes(4)) != "OMSF") { sink.Dropped("surface.oms", "bad magic"); return null; }
            var version = r.U32();
            if (version != 1) { sink.Ignored("surface.oms", "unknown version " + version); return null; }
            var ox = r.F64(); var oy = r.F64();
            for (var i = 0; i < 4; i++) r.F32(); // min/max bounds: informational, not trusted
            var chunkSize = r.F32();
            if (!double.IsFinite(ox) || !double.IsFinite(oy) || Math.Abs(ox) > 1e9 || Math.Abs(oy) > 1e9) { sink.Dropped("surface.oms", "implausible origin"); return null; }
            if (!float.IsFinite(chunkSize) || chunkSize < 1 || chunkSize > 1e6f) { sink.Dropped("surface.oms", "implausible chunk size"); return null; }

            var nStrings = r.U32();
            if (nStrings > MaxStrings || nStrings * 2L > r.Remaining) { sink.Dropped("surface.oms", "string table larger than the file"); return null; }
            var strings = new string[nStrings];
            var decoder = new UTF8Encoding(false, false);
            for (var i = 0; i < strings.Length; i++)
            {
                var len = r.U16();
                strings[i] = decoder.GetString(r.Bytes(len));
            }

            var nChunks = r.U32();
            if (nChunks > MaxChunks || nChunks * 20L > r.Remaining) { sink.Dropped("surface.oms", "chunk index larger than the file"); return null; }
            var entries = new List<(ChunkKey Key, long Offset, uint Length)>((int)nChunks);
            for (var i = 0; i < nChunks; i++) {
                var cx = r.I32(); var cy = r.I32(); var offset = r.U64(); var length = r.U32();
                entries.Add((new ChunkKey(cx, cy), offset > long.MaxValue ? long.MaxValue : (long)offset, length));
            }
            var dataStart = r.Position;
            var index = new Dictionary<ChunkKey, ChunkEntry>((int)nChunks);
            var bad = 0;
            foreach (var (key, offset, length) in entries)
            {
                if (offset < dataStart || length == 0 || length > MaxChunkBytes || offset > r.Length || length > r.Length - offset) { bad++; continue; }
                if (!index.TryAdd(key, new ChunkEntry(offset, (int)length))) bad++;
            }
            if (bad > 0) sink.Dropped("surface.oms", bad + " chunk index entries are outside the file or repeated");
            return new SurfaceFile(path, ox, oy, chunkSize, strings, index, terrainOffset);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or EndOfStreamException or OverflowException)
        {
            sink.Dropped("surface.oms", "header unreadable: " + exception.Message);
            return null;
        }
    }

    // Chunks (that exist in the index) whose cell meets the window, at most `limit`, nearest to the window centre first.
    public List<ChunkKey> ChunksIn(MapRect window, int limit, out bool truncated)
    {
        truncated = false;
        var x0 = Cell(window.MinX - OriginX); var x1 = Cell(window.MaxX - OriginX);
        var y0 = Cell(window.MinY - OriginY); var y1 = Cell(window.MaxY - OriginY);
        var found = new List<ChunkKey>();
        var cells = (double)(x1 - x0 + 1) * (y1 - y0 + 1);
        if (cells <= index.Count * 2L + 64)
        {
            for (var cx = x0; cx <= x1; cx++)
                for (var cy = y0; cy <= y1; cy++)
                    if (index.ContainsKey(new ChunkKey((int)cx, (int)cy))) found.Add(new ChunkKey((int)cx, (int)cy));
        }
        else
        {
            foreach (var key in index.Keys) if (key.X >= x0 && key.X <= x1 && key.Y >= y0 && key.Y <= y1) found.Add(key);
        }
        if (found.Count > limit)
        {
            truncated = true;
            var mx = ((window.MinX + window.MaxX) / 2 - OriginX) / ChunkSize - 0.5;
            var my = ((window.MinY + window.MaxY) / 2 - OriginY) / ChunkSize - 0.5;
            found = found.OrderBy(k => (k.X - mx) * (k.X - mx) + (k.Y - my) * (k.Y - my)).Take(limit).ToList();
        }
        return found;
    }

    private long Cell(double offset)
    {
        var cell = Math.Floor(offset / ChunkSize);
        return (long)Math.Clamp(cell, int.MinValue, int.MaxValue);
    }

    // Null when the chunk is corrupt or unreadable. Parsed chunks are cached (including failures).
    public ParsedChunk? Load(ChunkKey key)
    {
        lock (gate)
        {
            if (cache.TryGetValue(key, out var hit)) { cache[key] = (hit.Chunk, ++stamp); return hit.Chunk; }
        }
        ParsedChunk? chunk = null;
        if (index.TryGetValue(key, out var entry)) chunk = Parse(entry);
        lock (gate)
        {
            if (cache.Count >= CacheSize)
            {
                var oldest = cache.OrderBy(x => x.Value.Stamp).First().Key;
                cache.Remove(oldest);
            }
            cache[key] = (chunk, ++stamp);
        }
        return chunk;
    }

    private ParsedChunk? Parse(ChunkEntry entry)
    {
        try
        {
            byte[] payload;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1))
            {
                if (entry.Offset + entry.Length > file.Length) return null;
                file.Seek(entry.Offset, SeekOrigin.Begin);
                payload = new byte[entry.Length];
                var done = 0;
                while (done < payload.Length)
                {
                    var n = file.Read(payload, done, payload.Length - done);
                    if (n <= 0) return null;
                    done += n;
                }
            }
            using var memory = new MemoryStream(payload, false);
            var r = new LeReader(memory, payload.Length);
            var chunk = new ParsedChunk();
            ReadFills(r, chunk);
            ReadLines(r, chunk);
            ReadLabels(r, chunk);
            return chunk;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or EndOfStreamException or OverflowException)
        {
            return null;
        }
    }

    private float ToMap(float offset, double origin) => (float)(origin + offset);

    private static bool Sane(float v) => float.IsFinite(v) && Math.Abs(v) <= MaxCoordinate;

    private void ReadFills(LeReader r, ParsedChunk chunk)
    {
        var nFills = r.U32();
        if (nFills * 5L > r.Remaining) throw new FormatException("fill count");
        for (var f = 0; f < nFills; f++)
        {
            var cls = r.U8();
            var nTri = r.U32();
            if (nTri * 24L > r.Remaining) throw new FormatException("triangle count");
            var raw = r.Floats((int)nTri * 6);
            if (cls > (byte)FillClass.Farm) continue; // unknown class: skipped, structure still consumed
            var kept = new List<float>(raw.Length);
            for (var t = 0; t < nTri; t++)
            {
                var o = t * 6;
                var ok = true;
                for (var k = 0; k < 6; k++) ok &= Sane(raw[o + k]);
                if (!ok) continue;
                for (var k = 0; k < 6; k += 2) { kept.Add(ToMap(raw[o + k], OriginX)); kept.Add(ToMap(raw[o + k + 1], OriginY)); }
            }
            if (kept.Count > 0) chunk.Areas.Add(new AreaFeature((FillClass)cls, kept.ToArray()));
        }
    }

    private void ReadLines(LeReader r, ParsedChunk chunk)
    {
        var nLines = r.U32();
        if (nLines * 14L > r.Remaining) throw new FormatException("line count");
        for (var l = 0; l < nLines; l++)
        {
            var cls = r.U8(); var flags = r.U8(); var width = r.F32(); var name = r.U32(); var nPts = r.U32();
            if (nPts * 8L > r.Remaining) throw new FormatException("point count");
            var raw = r.Floats((int)nPts * 2);
            if (cls > (byte)LineClass.IndexContour || nPts < 2 || !float.IsFinite(width)) continue;
            var ok = true;
            foreach (var v in raw) ok &= Sane(v);
            if (!ok) continue;
            var points = new float[raw.Length];
            for (var i = 0; i < raw.Length; i += 2) { points[i] = ToMap(raw[i], OriginX); points[i + 1] = ToMap(raw[i + 1], OriginY); }
            var lineClass = (LineClass)cls;
            if (lineClass is LineClass.Contour or LineClass.IndexContour) width = (float)(width + terrainOffset);
            var feature = new LineFeature(lineClass, flags, width, Name(name), points);
            (feature.IsStreet ? chunk.Streets : chunk.Lines).Add(feature);
        }
    }

    private void ReadLabels(LeReader r, ParsedChunk chunk)
    {
        var nLabels = r.U32();
        if (nLabels * 14L > r.Remaining) throw new FormatException("label count");
        for (var i = 0; i < nLabels; i++)
        {
            var kind = r.U8(); var rank = r.U8(); var name = r.U32(); var x = r.F32(); var y = r.F32();
            if (kind > (byte)LabelKind.Water || !Sane(x) || !Sane(y)) continue;
            chunk.Labels.Add(new LabelFeature((LabelKind)kind, rank, Name(name), ToMap(x, OriginX), ToMap(y, OriginY)));
        }
    }

    private string? Name(uint id) => id < strings.Length && strings[id].Length > 0 ? strings[id] : null;
}
