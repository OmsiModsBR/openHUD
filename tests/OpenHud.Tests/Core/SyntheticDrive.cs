using System.Globalization;
using OpenHud.Model;

namespace OpenHud.Tests.Core;

// Generates a recorded drive without any recorded data: a player vehicle driving east across a 2x2 block
// of 300 m tiles with AI vehicles standing around it. Every reply has the shape the catalog documents.
internal sealed class SyntheticDrive
{
    public const double TileMetres = 300;
    public int Frames = 40;
    public double StartX = 100, StartY = 100;
    public double HeadingDegrees = 90;
    public double SpeedMetresPerSecond = 10;
    public double SecondsPerFrame = 1;
    public int AiVehicles = 12;
    public int ListPageSize = 1000;
    public int TilePageSize = 256;
    // Tile index <-> grid cell: index = row * 2 + column (a deliberately non-trivial table order).
    public static readonly int[] TileOrder = { 3, 2, 1, 0 };

    public static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public (double X, double Y) Player(int frame)
    {
        var heading = HeadingDegrees * Math.PI / 180;
        var t = frame * SecondsPerFrame * SpeedMetresPerSecond;
        return (StartX + t * Math.Sin(heading), StartY + t * Math.Cos(heading));
    }

    // Deterministic AI placements, spread over the block; the last ones sit in a tile the map does not list.
    public (string Handle, int Tile, double WorldX, double WorldY, double Heading)[] Ai()
    {
        var list = new List<(string, int, double, double, double)>();
        for (var i = 0; i < AiVehicles; i++)
        {
            var wx = 20 + (i * 47) % 560;
            var wy = 30 + (i * 83) % 540;
            list.Add(($"rv-{i + 1:000000}", TileIndexOf(wx, wy), wx, wy, (i * 37) % 360));
        }
        return list.ToArray();
    }

    public static int TileIndexOf(double worldX, double worldY) => (int)Math.Floor(worldY / TileMetres) * 2 + (int)Math.Floor(worldX / TileMetres);

    public static Dictionary<string, string> VehicleReply(string? handle, int tile, double worldX, double worldY, double headingDegrees, double speed, bool player = false)
    {
        var col = tile % 2; var row = tile / 2;
        var theta = -headingDegrees * Math.PI / 180;
        var d = new Dictionary<string, string>
        {
            ["runtime_index"] = "1", ["tile"] = tile.ToString(CultureInfo.InvariantCulture), ["marked_for_killing"] = "false", ["traffic_type"] = "1",
            ["position_x"] = F(worldX - col * TileMetres), ["position_y"] = F(worldY - row * TileMetres), ["position_z"] = "1.5",
            ["rotation_x"] = "0", ["rotation_y"] = "0", ["rotation_z"] = F(Math.Sin(theta / 2)), ["rotation_w"] = F(Math.Cos(theta / 2)),
            ["steering"] = "0", ["tacho"] = "0", ["ground_speed"] = F(speed), ["kilometres"] = "12.5", ["ai_enabled"] = player ? "false" : "true",
        };
        if (handle is not null) d["handle"] = handle;
        if (player) { d["present"] = "true"; d["player_index"] = "0"; }
        return d;
    }

    public List<ReplayFrame> Generate()
    {
        var frames = new List<ReplayFrame>();
        for (var i = 0; i < Frames; i++) frames.Add(Frame(i));
        return frames;
    }

    public ReplayFrame Frame(int i)
    {
        var f = new ReplayFrame();
        var seconds = 8 * 3600 + 30 * 60 + i * SecondsPerFrame;
        f.Add("time.read", GameReply.Ok(("hour", ((int)(seconds / 3600)).ToString()), ("minute", ((int)(seconds / 60) % 60).ToString()), ("second", F(seconds % 60)), ("day", "15"), ("month", "6"), ("year", "1990")));
        f.Add("map.read", GameReply.Ok(("loaded", "true"), ("loaded_tiles", "4"), ("left_hand_traffic", "false"), ("name", "Synthetic"), ("filename", "Maps\\Synthetic Map\\global.cfg"),
            ("friendly_name", "Synthetic Map"), ("description", "test"), ("max_speed", "50"), ("year_start", "1980"), ("year_end", "2000")));
        f.Add("camera.read", GameReply.Ok(("family", "0"), ("name", "Driver"), ("field_of_view", "60"), ("normal_field_of_view", "60"), ("distance", "0")));
        f.Add("timetable.read", GameReply.Ok(("invalid", "false"), ("tracks", "5"), ("trips", "20"), ("bus_stops", "30"), ("station_links", "12"), ("lines", "3"), ("rv_files", "2")));
        f.Add("weather.read", GameReply.Ok(("temperature", "18.5"), ("wind_speed", "3"), ("wind_direction", "270"), ("fog_density", "0"), ("precipitation", "0"), ("cloud_type_name", "scattered"), ("snow", "false"), ("wet_ground", "false")));

        // Tile table, in a shuffled order.
        var rows = TileOrder.ToList();
        AddPages(f, "map.tiles.list", 256, TilePageSize, rows.Count, (page, n) =>
        {
            var tile = rows[n];
            page[$"tile.{n}.x"] = (tile % 2).ToString(); page[$"tile.{n}.y"] = (tile / 2).ToString(); page[$"tile.{n}.tile_index"] = tile.ToString();
            page[$"tile.{n}.loaded"] = "true"; page[$"tile.{n}.filename"] = $"tile_{tile}.map";
        });

        // Player.
        var (px, py) = Player(i);
        f.Add("player-vehicle.read", GameReply.Ok(VehicleReply("rv-000000", TileIndexOf(px, py), px, py, HeadingDegrees, SpeedMetresPerSecond, player: true)));

        // Road vehicles: the player is the first in the list.
        var ai = Ai();
        AddPages(f, "road-vehicles.list", 1000, ListPageSize, ai.Length + 1, (page, n) => page[$"vehicle.{n}.handle"] = n == 0 ? "rv-000000" : ai[n - 1].Handle);
        f.Add("road-vehicle.read", GameReply.Ok(VehicleReply("rv-000000", TileIndexOf(px, py), px, py, HeadingDegrees, SpeedMetresPerSecond)), ("handle", "rv-000000"));
        foreach (var v in ai) f.Add("road-vehicle.read", GameReply.Ok(VehicleReply(v.Handle, v.Tile, v.WorldX, v.WorldY, v.Heading, 0)), ("handle", v.Handle));
        return f;
    }

    // Registers the pages of a cursor-paged list: key "op?limit=L" for the first page, "op?cursor=C&limit=L" after.
    public static void AddPages(ReplayFrame frame, string operation, int limit, int pageSize, int total, Action<Dictionary<string, string>, int> row)
    {
        string? cursor = null;
        for (var start = 0; start < total || start == 0; start += pageSize)
        {
            var page = new Dictionary<string, string>();
            var end = Math.Min(total, start + pageSize);
            for (var n = start; n < end; n++) row(page, n);
            page["count"] = total.ToString(); page["returned_count"] = (end - start).ToString();
            page["truncated"] = end < total ? "true" : "false";
            string? next = end < total ? "c" + end : null;
            if (next is not null) page["next_cursor"] = next;
            var args = new List<(string, string)> { ("limit", limit.ToString()) };
            if (cursor is not null) args.Add(("cursor", cursor));
            frame.Add(operation, GameReply.Ok(page), args.ToArray());
            cursor = next;
            if (next is null) break;
        }
    }
}

// Wraps a reader and decides per call what happens to it.
internal sealed class InjectingReader : IGameReader
{
    public enum Fault { None, ErrorCode, Throw, Timeout, Garbage, Empty, NullReply, NullValues }

    private readonly IGameReader inner;
    public Func<string, IReadOnlyDictionary<string, string>?, Fault> Rule { get; set; } = (_, _) => Fault.None;
    public Dictionary<string, int> Calls { get; } = new();
    public Random Random { get; } = new(42);

    public InjectingReader(IGameReader inner) => this.inner = inner;

    public GameReply Read(string operation, IReadOnlyDictionary<string, string>? arguments = null)
    {
        Calls[operation] = Calls.GetValueOrDefault(operation) + 1;
        switch (Rule(operation, arguments))
        {
            case Fault.ErrorCode: return GameReply.Fail("OL_E_RUNTIME_OPERATION_FAILED");
            case Fault.Throw: throw new InvalidOperationException("injected");
            case Fault.Timeout: throw new TimeoutException("injected timeout");
            case Fault.Empty: return GameReply.Ok(new Dictionary<string, string>());
            case Fault.NullReply: return null!;
            case Fault.NullValues: return new GameReply(true, null, null!);
            case Fault.Garbage:
            {
                var real = inner.Read(operation, arguments);
                var junk = new[] { "NaN", "abc", "", " ", "1e999", "-1e999", "∞", "0x10", "1,5", "999999999999999999999", "\u0000", "true", "-7", new string('9', 400), "\ud800" };
                var d = new Dictionary<string, string>();
                foreach (var key in real.Values.Keys) d[key] = junk[Random.Next(junk.Length)];
                return GameReply.Ok(d);
            }
            default: return inner.Read(operation, arguments);
        }
    }
}
