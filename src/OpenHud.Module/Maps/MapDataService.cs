using OpenHud.Packages.Realmap;
using OpenHud.Radar;

namespace OpenHud.Maps;

internal enum MapQuery { Radar, FullMap }

// Answers window queries of one realmap package on a worker thread, so reading chunks from disk never
// happens inside a tick. The tick asks (latest request per kind wins, older ones are dropped) and picks
// up the newest finished FeatureSet whenever it likes; until one covers the view it keeps drawing the
// previous one. ProcessPending is the unit of work and is also callable directly (tests).
internal sealed class MapDataService
{
    private sealed record Request(long Id, MapRect Window, DetailLevel Detail, bool MinorContours, double MinExtent);

    // Features a tick projects and clips: the radar is a few hundred pixels wide and the full map a few hundred thousand.
    public const int RadarBudget = 2500;
    public const int FullMapBudget = 8000;

    private readonly RealmapPackage package;
    private readonly MapFrame frame;
    private readonly Func<string, string?> imageIdOf;
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0, int.MaxValue);
    private readonly Request?[] pending = new Request?[2];
    private readonly FeatureSet?[] results = new FeatureSet?[2];
    private long nextId;
    private long processed;
    private long failures;

    public MapDataService(RealmapPackage package, MapFrame frame, Func<string, string?> imageIdOf)
    {
        this.package = package;
        this.frame = frame;
        this.imageIdOf = imageIdOf;
    }

    public MapFrame Frame => frame;
    public long Processed => Interlocked.Read(ref processed);
    public long Failures => Interlocked.Read(ref failures);

    public FeatureSet? Latest(MapQuery kind) => Volatile.Read(ref results[(int)kind]);

    // Asks for a window; returns the id the answer will carry. A newer request replaces an unprocessed older one.
    public long Ask(MapQuery kind, MapRect window, DetailLevel detail, bool minorContours, double minExtent)
    {
        lock (gate)
        {
            var id = ++nextId;
            pending[(int)kind] = new Request(id, window, detail, minorContours, minExtent);
            signal.Release();
            return id;
        }
    }

    // Runs until cancelled; each wake-up serves what is pending.
    public void Run(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try { signal.Wait(stopping); }
            catch (OperationCanceledException) { return; }
            ProcessPending();
        }
    }

    public int ProcessPending()
    {
        var served = 0;
        foreach (var kind in new[] { MapQuery.Radar, MapQuery.FullMap })
        {
            Request? request;
            lock (gate) { request = pending[(int)kind]; pending[(int)kind] = null; }
            if (request is null) continue;
            try
            {
                var view = package.Query(request.Window, request.Detail);
                Volatile.Write(ref results[(int)kind], RealmapConverter.Convert(view, frame, request.Id, request.Window, request.Detail, imageIdOf, request.MinorContours, request.MinExtent, kind == MapQuery.Radar ? RadarBudget : FullMapBudget));
                served++;
            }
            catch (Exception)
            {
                // Query never throws by contract; a converter bug must cost this window, not the thread.
                Interlocked.Increment(ref failures);
                Volatile.Write(ref results[(int)kind], new FeatureSet(request.Id, request.Window, request.Detail, Array.Empty<RadarFeature>(), Array.Empty<RadarRegion>(), Array.Empty<RadarMarker>(), false, 0, request.MinExtent));
            }
            Interlocked.Increment(ref processed);
        }
        return served;
    }
}
