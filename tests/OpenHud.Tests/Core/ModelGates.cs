using OpenHud.Model;
using Fault = OpenHud.Tests.Core.InjectingReader.Fault;

namespace OpenHud.Tests.Core;

internal static class ModelGates
{
    public static void Register()
    {
        Gates.Run("model.angles-and-quaternion-heading", AnglesGate);
        Gates.Run("model.radar-projection-properties", ProjectionProperties);
        Gates.Run("model.snapshot-from-synthetic-drive", SnapshotHappy);
        Gates.Run("model.replay-follows-the-drive-across-tiles", ReplayDrive);
        Gates.Run("model.replay-reader", ReplayReader);
        Gates.Run("model.paged-lists", Paging);
        Gates.Run("model.list-cursor-loops-are-cut", CursorLoops);
        Gates.Run("model.vehicle-read-budget", VehicleBudget);
        Gates.Run("model.slow-parts-refresh-rarely", SlowRefresh);
        Gates.Run("model.map-change-resets-learned-state", MapChange);
        Gates.Run("model.no-player-vehicle", NoPlayer);
        Gates.Run("model.failure-per-part", FailurePerPart);
        Gates.Run("model.failure-kinds", FailureKinds);
        Gates.Run("model.stale-vehicle-handles", StaleHandles);
        Gates.Run("model.unknown-tile-is-flagged", UnknownTile);
        Gates.Run("model.timetable-logs-option", TimetableLogs);
        Gates.Run("model.fuzzed-replies-never-throw", Fuzz);
    }

    private static (FrameSource Source, ReplayGameReader Reader, SyntheticDrive Drive) Make(Action<SyntheticDrive>? tune = null, FrameSourceOptions? options = null)
    {
        var drive = new SyntheticDrive();
        tune?.Invoke(drive);
        var reader = new ReplayGameReader(drive.Generate());
        return (new FrameSource(reader, options), reader, drive);
    }

    // ------------------------------------------------------------------ coordinates

    private static void AnglesGate()
    {
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var a = (random.NextDouble() - 0.5) * 20000;
            var n = Angles.Normalize(a);
            Expect.True(n >= 0 && n < 360, "Normalize range for " + a);
            Expect.Near(0, Math.Sin((n - a) * Math.PI / 180), 1e-9, "Normalize keeps the direction");
            var b = (random.NextDouble() - 0.5) * 20000;
            var d = Angles.Delta(a, b);
            Expect.True(d > -180 - 1e-9 && d <= 180 + 1e-9, "Delta range");
            Expect.Near(0, Math.Sin((a + d - b) * Math.PI / 180), 1e-9, "Delta turns a into b");
        }
        Expect.Equal(0.0, Angles.Normalize(double.NaN), "NaN normalises to 0");
        Expect.Equal(0.0, Angles.Normalize(double.PositiveInfinity), "infinity normalises to 0");
        Expect.Equal(0.0, Angles.Normalize(360), "360 is 0");
        Expect.Near(350, Angles.Normalize(-10), 1e-9, "-10 is 350");
        Expect.Near(-20, Angles.Delta(10, 350), 1e-9, "shortest turn across north");
        Expect.Near(180, Angles.Delta(0, 180), 1e-9, "half turn is +180");

        // Yaw about the vertical axis: counter-clockwise theta is heading -theta.
        for (var i = 0; i < 2000; i++)
        {
            var theta = (random.NextDouble() - 0.5) * 720;
            var yaw = Quat.AxisAngle(0, 0, 1, theta);
            Expect.Near(0, Math.Abs(Angles.Delta(Angles.HeadingFromQuaternion(yaw.X, yaw.Y, yaw.Z, yaw.W)!.Value, -theta)), 1e-6, $"heading of a yaw of {theta}");
            // Pitch and roll before the yaw leave the heading alone while the nose is not vertical.
            var pitch = Quat.AxisAngle(1, 0, 0, (random.NextDouble() - 0.5) * 120);
            var both = Quat.Mul(yaw, pitch);
            Expect.Near(0, Math.Abs(Angles.Delta(Angles.HeadingFromQuaternion(both.X, both.Y, both.Z, both.W)!.Value, -theta)), 1e-6, "pitch does not change the heading");
            // Scale does not matter.
            var k = 0.1 + random.NextDouble() * 50;
            Expect.Near(0, Math.Abs(Angles.Delta(Angles.HeadingFromQuaternion(yaw.X * k, yaw.Y * k, yaw.Z * k, yaw.W * k)!.Value, -theta)), 1e-6, "non-unit quaternion");
        }
        Expect.Near(0, Angles.HeadingFromQuaternion(0, 0, 0, 1)!.Value, 1e-9, "identity faces north");
        Expect.Near(90, Angles.HeadingFromQuaternion(0, 0, Math.Sin(-Math.PI / 4), Math.Cos(-Math.PI / 4))!.Value, 1e-9, "clockwise quarter turn faces east");
        Expect.True(Angles.HeadingFromQuaternion(0, 0, 0, 0) is null, "zero quaternion has no heading");
        Expect.True(Angles.HeadingFromQuaternion(double.NaN, 0, 0, 1) is null && Angles.HeadingFromQuaternion(0, 0, double.PositiveInfinity, 1) is null, "NaN/Infinity have no heading");
        var up = Quat.AxisAngle(1, 0, 0, 90);
        Expect.True(Angles.HeadingFromQuaternion(up.X, up.Y, up.Z, up.W) is null, "a vertical nose has no heading");
    }

    private readonly record struct Quat(double X, double Y, double Z, double W)
    {
        public static Quat AxisAngle(double x, double y, double z, double degrees)
        {
            var h = degrees * Math.PI / 360;
            var s = Math.Sin(h);
            return new Quat(x * s, y * s, z * s, Math.Cos(h));
        }

        public static Quat Mul(Quat a, Quat b) => new(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    }

    private static void ProjectionProperties()
    {
        var random = new Random(11);
        for (var i = 0; i < 5000; i++)
        {
            var cx = (random.NextDouble() - 0.5) * 100000; var cy = (random.NextDouble() - 0.5) * 100000;
            var rotation = (random.NextDouble() - 0.5) * 1000;
            var px = cx + (random.NextDouble() - 0.5) * 4000; var py = cy + (random.NextDouble() - 0.5) * 4000;
            var projection = new RadarProjection(cx, cy, rotation);
            var (rx, ry) = projection.WorldToRadar(px, py);
            var (bx, by) = projection.RadarToWorld(rx, ry);
            Expect.Near(px, bx, 1e-6, "round trip x");
            Expect.Near(py, by, 1e-6, "round trip y");
            Expect.Near(Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)), Math.Sqrt(rx * rx + ry * ry), 1e-6, "rotation preserves distance");
            Expect.Near(projection.DistanceFromCenter(px, py), Math.Sqrt(rx * rx + ry * ry), 1e-6, "DistanceFromCenter");
            // The centre is the origin.
            var (ox, oy) = projection.WorldToRadar(cx, cy);
            Expect.True(Math.Abs(ox) < 1e-9 && Math.Abs(oy) < 1e-9, "centre maps to the origin");
            // What lies ahead of the rotation heading is straight up; what lies to its right is to the right.
            var h = projection.RotationDegrees * Math.PI / 180;
            var dist = 10 + random.NextDouble() * 500;
            var (ax, ay) = projection.WorldToRadar(cx + dist * Math.Sin(h), cy + dist * Math.Cos(h));
            Expect.True(Math.Abs(ax) < 1e-6 && Math.Abs(ay - dist) < 1e-6, "ahead is up");
            var (qx, qy) = projection.WorldToRadar(cx + dist * Math.Cos(h), cy - dist * Math.Sin(h));
            Expect.True(Math.Abs(qx - dist) < 1e-6 && Math.Abs(qy) < 1e-6, "right is right");
            // Pixels: Y flips, scale applies.
            var (pxl, pyl) = RadarProjection.ToPixels(rx, ry, 0.5, 100, 200);
            Expect.Near(100 + rx * 0.5, pxl, 1e-9, "pixel x");
            Expect.Near(200 - ry * 0.5, pyl, 1e-9, "pixel y is flipped");
            var (wx, wy) = projection.WorldToPixels(px, py, 0.5, 100, 200);
            Expect.True(Math.Abs(wx - pxl) < 1e-9 && Math.Abs(wy - pyl) < 1e-9, "WorldToPixels composes");
            // An object's heading on the radar: the rotation heading is straight up.
            Expect.Near(0, Angles.Delta(projection.ObjectAngle(projection.RotationDegrees), 0), 1e-9, "object ahead has angle 0");
        }
        // North up: no rotation, so the radar is the world delta.
        var north = new RadarProjection(10, 20, 0);
        var (nx, ny) = north.WorldToRadar(13, 24);
        Expect.True(nx == 3 && ny == 4, "north-up is a translation");
        // Heading 90 (east): a point east of the centre is straight up.
        var east = new RadarProjection(0, 0, 90);
        var (ex, ey) = east.WorldToRadar(100, 0);
        Expect.True(Math.Abs(ex) < 1e-9 && Math.Abs(ey - 100) < 1e-9, "east-up");
        var (sx, sy) = east.WorldToRadar(0, -50);
        Expect.True(Math.Abs(sx - 50) < 1e-9 && Math.Abs(sy) < 1e-9, "with east up, south is on the right");
        Expect.Near(0, new RadarProjection(0, 0, 720).RotationDegrees, 1e-9, "rotation normalised");
        Expect.Near(0, new RadarProjection(0, 0, double.NaN).RotationDegrees, 1e-9, "NaN rotation is 0");
    }

    // ------------------------------------------------------------------ snapshots

    private static void SnapshotHappy()
    {
        var (source, _, drive) = Make(null, new FrameSourceOptions { NearbyRadiusMetres = 400 });
        var s = source.Read();
        Expect.Equal(1L, s.Sequence, "first sequence");
        Expect.True(!s.Problems.Any(), "nothing is wrong: " + string.Join("; ", s.Problems));
        var t = s.Time.Value!;
        Expect.True(t.Hour == 8 && t.Minute == 30 && Math.Abs(t.Second) < 1e-9 && t.DateNumber == 19900615, "time and date");
        Expect.Near(8 * 3600 + 30 * 60, t.SecondsOfDay, 1e-9, "seconds of day");
        var m = s.Map.Value!;
        Expect.True(m.Loaded && m.FriendlyName == "Synthetic Map" && m.LeftHandTraffic == false && m.LoadedTiles == 4 && m.YearStart == 1980, "map");
        Expect.Equal(@"Maps\Synthetic Map\global.cfg", m.RealmapIdentity, "realmap identity from the file name");
        Expect.True(s.Camera.Value!.Family == CameraFamily.Driver && s.Camera.Value.FieldOfView == 60, "camera");
        var p = s.PlayerVehicle!;
        Expect.True(s.Player.Value!.Present && p.WorldResolved, "player pose resolved through the tile table");
        Expect.Near(100, p.WorldX, 1e-9, "player world x");
        Expect.Near(100, p.WorldY, 1e-9, "player world y");
        Expect.Near(90, p.HeadingDegrees!.Value, 1e-6, "player heading");
        Expect.Near(36, p.SpeedKmh, 1e-9, "10 m/s is 36 km/h");
        Expect.True(p.Handle == "rv-000000" && p.TrafficType == 1 && p.AiEnabled == false, "player basics");
        var near = s.Vehicles.Value!;
        Expect.Equal(drive.AiVehicles + 1, near.KnownCount, "all vehicles in the list are known (player included)");
        var expected = drive.Ai().Where(v => Math.Sqrt((v.WorldX - 100) * (v.WorldX - 100) + (v.WorldY - 100) * (v.WorldY - 100)) <= 400).Select(v => v.Handle).OrderBy(x => x).ToList();
        Expect.True(expected.Count > 0 && expected.Count < drive.AiVehicles, "the test drive has vehicles on both sides of the radius");
        Expect.True(near.Vehicles.Select(v => v.Handle!).OrderBy(x => x).SequenceEqual(expected), "exactly the vehicles inside the radius, the player excluded");
        var distances = near.Vehicles.Select(v => Math.Sqrt((v.WorldX - 100) * (v.WorldX - 100) + (v.WorldY - 100) * (v.WorldY - 100))).ToList();
        Expect.True(distances.SequenceEqual(distances.OrderBy(x => x)), "nearest first");
        var truth = drive.Ai().ToDictionary(v => v.Handle);
        foreach (var v in near.Vehicles)
        {
            Expect.True(Math.Abs(v.WorldX - truth[v.Handle!].WorldX) < 1e-6 && Math.Abs(v.WorldY - truth[v.Handle!].WorldY) < 1e-6, "vehicle world position through its tile");
            Expect.Near(0, Math.Abs(Angles.Delta(v.HeadingDegrees!.Value, truth[v.Handle!].Heading)), 1e-6, "vehicle heading");
        }
        var tt = s.Timetable.Value!;
        Expect.True(tt.Invalid == false && tt.Tracks == 5 && tt.Lines == 3 && tt.Logs.Count == 0, "timetable counts");
        var w = s.Weather.Value!;
        Expect.True(w.Temperature == 18.5 && w.WindDirection == 270 && w.CloudTypeName == "scattered" && w.Snow == false, "weather");
        Expect.Equal(300.0, s.TileMetres, "the tile edge is carried");
    }

    private static void ReplayDrive()
    {
        var (source, reader, drive) = Make();
        var previous = source.Read().PlayerVehicle!;
        var tiles = new HashSet<int> { previous.Tile };
        do
        {
            var current = source.Read();
            if (reader.FrameIndex == 0) continue;
            var p = current.PlayerVehicle!;
            tiles.Add(p.Tile);
            var (ex, ey) = drive.Player(reader.FrameIndex);
            Expect.True(Math.Abs(p.WorldX - ex) < 1e-6 && Math.Abs(p.WorldY - ey) < 1e-6, "world position at frame " + reader.FrameIndex);
            Expect.Near(10, Math.Sqrt((p.WorldX - previous.WorldX) * (p.WorldX - previous.WorldX) + (p.WorldY - previous.WorldY) * (p.WorldY - previous.WorldY)), 1e-6, "the step stays continuous across the tile boundary");
            Expect.True(p.WorldResolved, "resolved at frame " + reader.FrameIndex);
            previous = p;
        } while (reader.Advance());
        Expect.True(tiles.Count >= 2, "the drive crosses a tile boundary");
        Expect.True(source.Read().Sequence > 40, "sequence keeps counting");
    }

    private static void ReplayReader()
    {
        Expect.Throws<ArgumentException>(() => new ReplayGameReader(Array.Empty<ReplayFrame>()), "an empty replay is refused");
        var a = new ReplayFrame().Add("op", GameReply.Ok(("v", "1")));
        var b = new ReplayFrame().Add("op", GameReply.Ok(("v", "2")));
        b.Add("op", GameReply.Ok(("v", "arg")), ("b", "2"), ("a", "1"));
        var r = new ReplayGameReader(new[] { a, b });
        Expect.Equal("1", r.Read("op").Values["v"], "frame 0");
        Expect.True(!r.Read("missing").Succeeded && r.Read("missing").ErrorCode == "OL_E_RUNTIME_OPERATION_UNAVAILABLE", "an operation the frame lacks is unavailable");
        Expect.True(r.Advance() && !r.Advance(), "advance stops at the end");
        Expect.Equal("2", r.Read("op").Values["v"], "frame 1");
        Expect.Equal("arg", r.Read("op", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }).Values["v"], "argument order does not matter");
        Expect.Equal("op?a=1&b=2", ReplayGameReader.Key("op", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }), "key is sorted");
        Expect.Equal(5, r.Calls, "calls are counted");
    }

    private static void Paging()
    {
        // Vehicles over several pages, and the tile table too.
        var (source, reader, drive) = Make(d => { d.AiVehicles = 25; d.ListPageSize = 7; d.TilePageSize = 2; }, new FrameSourceOptions { NearbyRadiusMetres = 5000, VehicleReadBudget = 100 });
        var s = source.Read();
        Expect.True(s.Vehicles.IsAvailable && s.Vehicles.Value!.KnownCount == 26, "all pages of the vehicle list were followed");
        Expect.True(s.PlayerVehicle!.WorldResolved, "all pages of the tile table were followed (the player's tile was on a later page)");
        Expect.Equal(25, s.Vehicles.Value!.Vehicles.Count, "every other vehicle was read");
        // The page cap bounds the work: 4 pages of 7 = 28 handles seen at most.
        var (capped, _, _) = Make(d => { d.AiVehicles = 100; d.ListPageSize = 7; }, new FrameSourceOptions { MaxListPages = 2, VehicleReadBudget = 200, NearbyRadiusMetres = 5000 });
        Expect.Equal(14, capped.Read().Vehicles.Value!.KnownCount, "MaxListPages bounds the list");
        _ = reader; _ = drive;
    }

    private static void CursorLoops()
    {
        // A list that keeps returning the same cursor must not loop forever.
        var frame = new ReplayFrame();
        frame.Add("time.read", GameReply.Ok(("hour", "1"), ("minute", "2")));
        frame.Add("player-vehicle.read", GameReply.Ok(SyntheticDrive.VehicleReply("rv-000000", 0, 5, 5, 0, 0, player: true)));
        var page = new Dictionary<string, string> { ["vehicle.0.handle"] = "rv-000001", ["truncated"] = "true", ["next_cursor"] = "same" };
        frame.Add("road-vehicles.list", GameReply.Ok(page), ("limit", "1000"));
        frame.Add("road-vehicles.list", GameReply.Ok(page), ("cursor", "same"), ("limit", "1000"));
        frame.Add("road-vehicle.read", GameReply.Ok(SyntheticDrive.VehicleReply("rv-000001", 0, 50, 50, 0, 0)), ("handle", "rv-000001"));
        var reader = new ReplayGameReader(new[] { frame });
        var s = new FrameSource(reader, new FrameSourceOptions { MaxListPages = 50 }).Read();
        Expect.True(reader.Calls < 40, "the repeated cursor ended the paging (" + reader.Calls + " calls)");
        Expect.Equal(1, s.Vehicles.Value!.KnownCount, "the handle was read once");
        Expect.True(s.Time.IsAvailable && !s.Camera.IsAvailable && !s.Map.IsAvailable, "parts the recording lacks are unavailable, others fine");
    }

    private static void VehicleBudget()
    {
        var (source, reader, drive) = Make(d => { d.AiVehicles = 100; d.Frames = 40; }, new FrameSourceOptions { VehicleReadBudget = 10, NearbyRadiusMetres = 5000, MaxNearbyVehicles = 1000 });
        var counting = new InjectingReader(reader);
        var src = new FrameSource(counting, new FrameSourceOptions { VehicleReadBudget = 10, NearbyRadiusMetres = 5000, MaxNearbyVehicles = 1000 });
        var known = new List<int>();
        for (var frame = 0; frame < 14; frame++)
        {
            counting.Calls.Clear();
            var s = src.Read();
            Expect.True(counting.Calls.GetValueOrDefault("road-vehicle.read") <= 10, "at most the budget per frame");
            known.Add(s.Vehicles.Value!.Vehicles.Count);
            reader.Advance();
        }
        Expect.True(known.Zip(known.Skip(1), (a, b) => b >= a).All(x => x), "knowledge grows monotonically");
        Expect.Equal(100, known[^1], "after enough frames every vehicle has been read (player excluded)");
        Expect.True(known[0] <= 10, "the first frame reads at most the budget");
        // A zero budget reads no vehicle but still answers.
        var (zero, _, _) = Make(null, new FrameSourceOptions { VehicleReadBudget = 0 });
        Expect.True(zero.Read().Vehicles.IsAvailable && zero.Read().Vehicles.Value!.Vehicles.Count == 0, "zero budget");
        // MaxNearbyVehicles truncates and says so.
        var (few, _, _) = Make(d => d.AiVehicles = 40, new FrameSourceOptions { NearbyRadiusMetres = 5000, VehicleReadBudget = 100, MaxNearbyVehicles = 5 });
        var f = few.Read().Vehicles.Value!;
        Expect.True(f.Vehicles.Count == 5 && f.Truncated, "capped list is flagged");
        _ = drive;
    }

    private static void SlowRefresh()
    {
        var drive = new SyntheticDrive { Frames = 60 };
        var replay = new ReplayGameReader(drive.Generate());
        var counting = new InjectingReader(replay);
        var source = new FrameSource(counting, new FrameSourceOptions { SlowRefreshFrames = 20, ListRefreshFrames = 10, TileRefreshFrames = 1000 });
        for (var i = 0; i < 41; i++) { source.Read(); replay.Advance(); }
        Expect.Equal(3, counting.Calls["map.read"], "map is read at frames 1, 21, 41");
        Expect.Equal(3, counting.Calls["weather.read"], "weather likewise");
        Expect.Equal(3, counting.Calls["timetable.read"], "timetable likewise");
        Expect.Equal(41, counting.Calls["time.read"], "time every frame");
        Expect.Equal(41, counting.Calls["camera.read"], "camera every frame");
        Expect.Equal(41, counting.Calls["player-vehicle.read"], "player every frame");
        Expect.Equal(1, counting.Calls["map.tiles.list"], "the tile table once while it resolves the player");
        Expect.True(counting.Calls["road-vehicles.list"] <= 5, "the handle list is refreshed rarely");
        // Between refreshes the reused reading is still served.
        var s = source.Read();
        Expect.True(s.Map.IsAvailable && s.Weather.IsAvailable && s.Timetable.IsAvailable, "reused readings");
    }

    private static void MapChange()
    {
        var drive = new SyntheticDrive { Frames = 6 };
        var frames = drive.Generate();
        // From frame 3 on, another map: other file name, and a tile table that is gone.
        for (var i = 3; i < frames.Count; i++)
        {
            frames[i].Add("map.read", GameReply.Ok(("loaded", "true"), ("filename", "Maps\\Other\\global.cfg")));
            frames[i].Replies.Remove("map.tiles.list?limit=256");
        }
        var reader = new ReplayGameReader(frames);
        var source = new FrameSource(reader, new FrameSourceOptions { SlowRefreshFrames = 1 });
        for (var i = 0; i < 3; i++) { Expect.True(source.Read().PlayerVehicle!.WorldResolved, "resolved on the first map"); reader.Advance(); }
        var after = source.Read();
        Expect.Equal(@"Maps\Other\global.cfg", after.Map.Value!.RealmapIdentity, "new map identity");
        Expect.True(!after.PlayerVehicle!.WorldResolved, "the old map's tile table is not applied to the new map");
    }

    private static void NoPlayer()
    {
        var frame = new ReplayFrame();
        frame.Add("time.read", GameReply.Ok(("hour", "23"), ("minute", "59")));
        frame.Add("player-vehicle.read", GameReply.Ok(("present", "false")));
        var s = new FrameSource(new ReplayGameReader(new[] { frame })).Read();
        Expect.True(s.Player.IsAvailable && !s.Player.Value!.Present && s.PlayerVehicle is null, "no player vehicle is a valid answer");
        Expect.True(s.Vehicles.IsAvailable && s.Vehicles.Value!.Vehicles.Count == 0, "no vehicles around nobody");
        var unknown = new ReplayFrame().Add("player-vehicle.read", GameReply.Ok(("tile", "3")));
        var s2 = new FrameSource(new ReplayGameReader(new[] { unknown })).Read();
        Expect.True(!s2.Player.IsAvailable && !s2.Vehicles.IsAvailable, "a player reply without 'present' is unavailable, and so are the vehicles around it");
    }

    // ------------------------------------------------------------------ failure injection

    private static readonly (string Operation, Func<FrameSnapshot, bool> Available)[] Parts =
    {
        ("time.read", s => s.Time.IsAvailable), ("map.read", s => s.Map.IsAvailable), ("camera.read", s => s.Camera.IsAvailable),
        ("player-vehicle.read", s => s.Player.IsAvailable), ("timetable.read", s => s.Timetable.IsAvailable), ("weather.read", s => s.Weather.IsAvailable),
    };

    private static void FailurePerPart()
    {
        foreach (var fault in new[] { Fault.ErrorCode, Fault.Throw, Fault.Timeout, Fault.Garbage, Fault.Empty, Fault.NullReply, Fault.NullValues })
        {
            foreach (var (operation, _) in Parts)
            {
                var replay = new ReplayGameReader(new SyntheticDrive().Generate());
                var injecting = new InjectingReader(replay) { Rule = (op, _) => op == operation ? fault : Fault.None };
                var s = new FrameSource(injecting).Read();
                foreach (var (other, available) in Parts)
                {
                    var wantAvailable = other != operation;
                    // Garbage may leave a few fields readable (a lucky "-7"); the part is then allowed to survive but never to be nonsense.
                    if (fault == Fault.Garbage && other == operation) continue;
                    Expect.Equal(wantAvailable, available(s), $"{fault} on {operation}: part {other}");
                }
                if (operation == "player-vehicle.read")
                    Expect.True(!s.Vehicles.IsAvailable && s.Problems.Any(p => p.StartsWith("player")), "without the player the vehicles are unavailable, and the problem is reported");
                else if (fault != Fault.Garbage)
                    Expect.True(s.Vehicles.IsAvailable, $"{fault} on {operation}: vehicles unaffected");
                if (fault != Fault.Garbage) Expect.True(s.Problems.Count(p => !p.StartsWith("vehicles")) >= 1, "the failure is reported in Problems");
            }
        }
    }

    private static void FailureKinds()
    {
        // Vehicle list and tile table failing degrade their own parts only.
        foreach (var fault in new[] { Fault.ErrorCode, Fault.Throw, Fault.Timeout, Fault.Garbage, Fault.Empty })
        {
            var replay = new ReplayGameReader(new SyntheticDrive().Generate());
            var listFails = new FrameSource(new InjectingReader(replay) { Rule = (op, _) => op == "road-vehicles.list" ? fault : Fault.None }).Read();
            Expect.True(listFails.Time.IsAvailable && listFails.Player.IsAvailable && listFails.Map.IsAvailable, fault + " on the vehicle list leaves the other parts alone");
            if (fault != Fault.Garbage) Expect.True(!listFails.Vehicles.IsAvailable || listFails.Vehicles.Value!.Vehicles.Count == 0, fault + " on the list: no vehicles invented");
            var tilesFail = new FrameSource(new InjectingReader(replay) { Rule = (op, _) => op == "map.tiles.list" ? fault : Fault.None }).Read();
            Expect.True(tilesFail.Player.IsAvailable && tilesFail.Time.IsAvailable, fault + " on the tile table: the player is still there");
            if (fault != Fault.Garbage) Expect.True(!tilesFail.PlayerVehicle!.WorldResolved, fault + " on the tile table: the pose says it is unresolved");
        }
        // A timeout during one frame does not poison the next.
        var drive = new SyntheticDrive();
        var r = new ReplayGameReader(drive.Generate());
        var flaky = new InjectingReader(r);
        var source = new FrameSource(flaky);
        flaky.Rule = (op, _) => op == "time.read" ? Fault.Timeout : Fault.None;
        Expect.True(!source.Read().Time.IsAvailable, "frame with a timeout");
        flaky.Rule = (_, _) => Fault.None;
        r.Advance();
        Expect.True(source.Read().Time.IsAvailable, "the next frame recovers");
        // Reply values that parse but make no sense are refused one by one.
        var odd = new ReplayFrame();
        odd.Add("time.read", GameReply.Ok(("hour", "24"), ("minute", "0")));
        Expect.True(!new FrameSource(new ReplayGameReader(new[] { odd })).Read().Time.IsAvailable, "hour 24 is not a time");
        var odd2 = new ReplayFrame().Add("time.read", GameReply.Ok(("hour", "-1"), ("minute", "0")));
        Expect.True(!new FrameSource(new ReplayGameReader(new[] { odd2 })).Read().Time.IsAvailable, "hour -1");
        var odd3 = new ReplayFrame().Add("time.read", GameReply.Ok(("hour", "12"), ("minute", "30"), ("second", "NaN"), ("day", "40"), ("month", "13"), ("year", "-5")));
        var t = new FrameSource(new ReplayGameReader(new[] { odd3 })).Read().Time.Value!;
        Expect.True(t.Second == 0 && t.Day is null && t.Month is null && t.Year is null && t.DateNumber is null, "a bad date is dropped, the clock stays");
        var odd4 = new ReplayFrame().Add("time.read", GameReply.Ok(("hour", "3.0"), ("minute", "7.0")));
        Expect.True(new FrameSource(new ReplayGameReader(new[] { odd4 })).Read().Time.Value is { Hour: 3, Minute: 7 }, "whole numbers printed as floats are accepted");
        // A pose with NaN or without position is not a pose; a pose without a usable rotation has no heading.
        var pose = SyntheticDrive.VehicleReply("rv-000000", 0, 50, 50, 0, 0, player: true);
        pose["position_x"] = "NaN";
        Expect.True(!new FrameSource(new ReplayGameReader(new[] { new ReplayFrame().Add("player-vehicle.read", GameReply.Ok(pose)) })).Read().Player.IsAvailable, "NaN position");
        pose = SyntheticDrive.VehicleReply("rv-000000", 0, 50, 50, 0, 0, player: true);
        pose["rotation_w"] = "0"; pose["rotation_z"] = "0";
        var noHeading = new FrameSource(new ReplayGameReader(new[] { new ReplayFrame().Add("player-vehicle.read", GameReply.Ok(pose)) })).Read();
        Expect.True(noHeading.Player.IsAvailable && noHeading.PlayerVehicle!.HeadingDegrees is null, "a zero rotation: pose without heading");
        pose["ground_speed"] = "1e30";
        Expect.Equal(0.0, new FrameSource(new ReplayGameReader(new[] { new ReplayFrame().Add("player-vehicle.read", GameReply.Ok(pose)) })).Read().PlayerVehicle!.SpeedMetresPerSecond, "an impossible speed reads as 0");
        // Camera: an unknown family is not a camera.
        var cam = new ReplayFrame().Add("camera.read", GameReply.Ok(("family", "9")));
        Expect.True(!new FrameSource(new ReplayGameReader(new[] { cam })).Read().Camera.IsAvailable, "unknown camera family");
    }

    private static void StaleHandles()
    {
        // Half of the handles answer a stale-handle error: they are dropped, the rest is served, nothing fails.
        var replay = new ReplayGameReader(new SyntheticDrive { AiVehicles = 20 }.Generate());
        var injecting = new InjectingReader(replay)
        {
            Rule = (op, args) => op == "road-vehicle.read" && args!["handle"].EndsWith('1') ? Fault.ErrorCode : Fault.None,
        };
        var s = new FrameSource(injecting, new FrameSourceOptions { NearbyRadiusMetres = 5000, VehicleReadBudget = 100 }).Read();
        Expect.True(s.Vehicles.IsAvailable, "the vehicles part survives stale handles");
        Expect.True(s.Vehicles.Value!.Vehicles.Count is > 5 and < 20 && s.Vehicles.Value.Vehicles.All(v => !v.Handle!.EndsWith('1')), "only readable vehicles are reported");
        // When every read fails, the part says so instead of reporting an empty road.
        var allFail = new InjectingReader(new ReplayGameReader(new SyntheticDrive().Generate())) { Rule = (op, _) => op == "road-vehicle.read" ? Fault.Throw : Fault.None };
        var f = new FrameSource(allFail).Read();
        Expect.True(!f.Vehicles.IsAvailable && f.Player.IsAvailable, "every vehicle read failing marks the vehicles unavailable only");
    }

    // A player reply whose local position is exactly (lx, ly) inside the given tile.
    private static GameReply LocalPlayer(int tile, double lx, double ly)
    {
        var d = SyntheticDrive.VehicleReply("rv-000000", tile, 0, 0, 0, 0, player: true);
        d["position_x"] = SyntheticDrive.F(lx); d["position_y"] = SyntheticDrive.F(ly);
        return GameReply.Ok(d);
    }

    private static void UnknownTile()
    {
        var frame = new ReplayFrame();
        frame.Add("player-vehicle.read", LocalPlayer(99, 12, 34));
        frame.Add("map.tiles.list", GameReply.Ok(("tile.0.x", "0"), ("tile.0.y", "0"), ("tile.0.tile_index", "0"), ("tile.1.x", "1"), ("tile.1.y", "zz"), ("tile.1.tile_index", "1"),
            ("tile.2.x", "5"), ("tile.2.y", "5"), ("tile.2.tile_index", "2"), ("tile.2.state", "unavailable")), ("limit", "256"));
        var s = new FrameSource(new ReplayGameReader(new[] { frame })).Read();
        var p = s.PlayerVehicle!;
        Expect.True(!p.WorldResolved && p.WorldX == 12 && p.WorldY == 34 && p.Tile == 99, "an unknown tile keeps the local position and says it is unresolved");
        // The table row with a bad coordinate or an unavailable state is skipped, the good one is used.
        var f2 = new ReplayFrame().Add("player-vehicle.read", LocalPlayer(0, 12, 34));
        f2.Replies["map.tiles.list?limit=256"] = frame.Replies["map.tiles.list?limit=256"];
        var p2 = new FrameSource(new ReplayGameReader(new[] { f2 }), new FrameSourceOptions { TileMetres = 585.795 }).Read().PlayerVehicle!;
        Expect.True(p2.WorldResolved && p2.WorldX == 12 && p2.WorldY == 34, "tile (0,0) resolved");
        var f3 = new ReplayFrame().Add("player-vehicle.read", LocalPlayer(1, 12, 34));
        f3.Replies["map.tiles.list?limit=256"] = frame.Replies["map.tiles.list?limit=256"];
        Expect.True(!new FrameSource(new ReplayGameReader(new[] { f3 })).Read().PlayerVehicle!.WorldResolved, "a tile row with a bad coordinate is not used");
        // The grid size comes from the options (the realmap package's grid).
        var f4 = new ReplayFrame();
        f4.Replies["player-vehicle.read"] = LocalPlayer(7, 10, 20);
        f4.Replies["map.tiles.list?limit=256"] = GameReply.Ok(("tile.0.x", "2"), ("tile.0.y", "3"), ("tile.0.tile_index", "7"));
        var p4 = new FrameSource(new ReplayGameReader(new[] { f4 }), new FrameSourceOptions { TileMetres = 585.795 }).Read().PlayerVehicle!;
        Expect.True(Math.Abs(p4.WorldX - (2 * 585.795 + 10)) < 1e-9 && Math.Abs(p4.WorldY - (3 * 585.795 + 20)) < 1e-9, "world = tile cell * grid + local");
    }

    private static void TimetableLogs()
    {
        var frame = new SyntheticDrive().Frame(0);
        frame.Add("timetable.logs.read", GameReply.Ok(("log.0.bus_stop", "Stop A"), ("log.0.estimated_arrival", "100"), ("log.0.actual_arrival", "110"), ("log.0.arrival_ok", "1"),
            ("log.1.bus_stop", "Stop B"), ("log.1.estimated_arrival", "x"), ("log.2.arrival_ok", "0"), ("returned_count", "3")), ("limit", "256"));
        var off = new FrameSource(new ReplayGameReader(new[] { frame })).Read();
        Expect.Equal(0, off.Timetable.Value!.Logs.Count, "logs are not read unless asked");
        var on = new FrameSource(new ReplayGameReader(new[] { frame }), new FrameSourceOptions { ReadTimetableLogs = true }).Read();
        var rows = on.Timetable.Value!.Logs;
        Expect.True(rows.Count == 3 && rows[0].BusStop == "Stop A" && rows[0].ActualArrival == 110 && rows[0].ArrivalOk == true && rows[1].EstimatedArrival is null && rows[2].ArrivalOk == false, "rows parsed defensively");
        // The operation failing leaves the counts.
        var broken = new SyntheticDrive().Frame(0);
        var failing = new FrameSource(new ReplayGameReader(new[] { broken }), new FrameSourceOptions { ReadTimetableLogs = true }).Read();
        Expect.True(failing.Timetable.IsAvailable && failing.Timetable.Value!.Logs.Count == 0 && failing.Timetable.Value.Tracks == 5, "log failure keeps the counts");
    }

    private static void Fuzz()
    {
        var random = new Random(1017);
        var junk = new[] { "NaN", "abc", "", " ", "1e999", "-1e999", "0", "-0", "1", "-1", "3.5", "999999999999", "true", "false", "\u0000", "rv-1", "rv-000001", "∞", new string('x', 5000), "\ud800", "1,5", "0x1F", "300", "-300", "1e-320" };
        var keys = new[] { "hour", "minute", "second", "day", "month", "year", "loaded", "name", "filename", "friendly_name", "family", "field_of_view", "present", "tile", "position_x", "position_y", "position_z",
            "rotation_x", "rotation_y", "rotation_z", "rotation_w", "ground_speed", "traffic_type", "ai_enabled", "handle", "tracks", "invalid", "temperature", "wind_speed", "snow", "cloud_type_name",
            "vehicle.0.handle", "vehicle.1.handle", "vehicle.99999999999.handle", "tile.0.x", "tile.0.y", "tile.0.tile_index", "tile.1.x", "tile.1.y", "next_cursor", "truncated", "count", "limit" };
        var operations = new[] { "time.read", "map.read", "camera.read", "player-vehicle.read", "road-vehicles.list", "road-vehicle.read", "timetable.read", "weather.read", "map.tiles.list", "timetable.logs.read" };
        var good = new ReplayGameReader(new SyntheticDrive().Generate());
        var source = new FrameSource(new FuzzReader(random, junk, keys, operations, good), new FrameSourceOptions { ReadTimetableLogs = true, VehicleReadBudget = 50 });
        for (var frame = 0; frame < 1500; frame++)
        {
            var s = source.Read();
            if (s.Time.Value is { } t) Expect.True(t.Hour is >= 0 and <= 23 && t.Minute is >= 0 and <= 59 && double.IsFinite(t.Second), "time invariants");
            foreach (var v in (s.Vehicles.Value?.Vehicles ?? Array.Empty<VehicleState>()).Concat(s.PlayerVehicle is { } pv ? new[] { pv } : Array.Empty<VehicleState>()))
            {
                Expect.True(double.IsFinite(v.WorldX) && double.IsFinite(v.WorldY) && double.IsFinite(v.LocalZ) && double.IsFinite(v.SpeedMetresPerSecond), "finite pose");
                Expect.True(v.HeadingDegrees is null || (v.HeadingDegrees >= 0 && v.HeadingDegrees < 360), "heading range");
            }
            if (s.Camera.Value is { } c) Expect.True(c.Family != CameraFamily.Unknown, "no unknown camera family");
            _ = s.Problems.ToList();
        }
    }

    private sealed class FuzzReader : IGameReader
    {
        private readonly Random random; private readonly string[] junk, keys, operations; private readonly IGameReader good;
        public FuzzReader(Random random, string[] junk, string[] keys, string[] operations, IGameReader good) { this.random = random; this.junk = junk; this.keys = keys; this.operations = operations; this.good = good; }

        public GameReply Read(string operation, IReadOnlyDictionary<string, string>? arguments = null)
        {
            switch (random.Next(6))
            {
                case 0: return GameReply.Fail("OL_E_RUNTIME_OPERATION_FAILED");
                case 1: throw new IOException("injected");
                case 2:
                {
                    var d = new Dictionary<string, string>();
                    for (var i = random.Next(0, 14); i > 0; i--) d[keys[random.Next(keys.Length)]] = junk[random.Next(junk.Length)];
                    return GameReply.Ok(d);
                }
                case 3: return GameReply.Ok(new Dictionary<string, string>());
                default: return good.Read(operation, arguments);
            }
        }
    }
}
