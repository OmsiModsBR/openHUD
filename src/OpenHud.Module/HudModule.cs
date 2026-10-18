using System.Diagnostics;
using System.Globalization;
using OmsiLaunch.Api;
using OmsiLaunch.Runtime.Modules;
using OpenHud.Bridge;
using OpenHud.Draw;
using OpenHud.I18n;
using OpenHud.Maps;
using OpenHud.Model;
using OpenHud.Packages;
using OpenHud.Packages.Realmap;
using OpenHud.Pause;
using OpenHud.Radar;
using OpenHud.Scene;
using OpenHud.Settings;

namespace OpenHud;

// The entry of the openHUD module. Everything it does goes through the context the bridge hands it
// (catalog operations, the content API, input capture, the native draw layer); it holds no address and
// no pointer. Each Tick reads the game, keeps the pause menu's state machine going and publishes one
// scene to the draw layer when something visible changed. Nothing here throws out of a callback: a
// failed part is switched off, said once in a notice, and the rest keeps working.
public sealed class HudModule : IRuntimeModule, ITickingModule
{
    internal const int MinRadarIntervalMs = 100;
    internal const int StatsIntervalTicks = 20;
    internal const int MaxMapLoads = 8;
    internal const int FailureLimit = 5;
    internal const double SlowTickMilliseconds = 40;
    private const int ReadProblemKinds = 12;

    private static readonly MapFrame IdentityFrame = new(0);

    private readonly IPauseClock clock;
    private readonly Counters counters = new();
    private readonly HashSet<string> noticed = new(StringComparer.Ordinal);
    private readonly HashSet<string> problemsSeen = new(StringComparer.Ordinal);
    private readonly UploadQueue uploads = new();

    private IRuntimeModuleContext? context;
    private HudSettings settings = HudSettings.Defaults;
    private UiLanguage ui = UiLanguage.English;
    private string languageCode = "ENG";

    private NativeLink native = new(null);
    private NativeTextMeasurer? measurer;
    private GameReaderAdapter? reader;
    private RealmapSourceAdapter? realmapSource;
    private FrameSource? frames;
    private RadarSceneBuilder radarBuilder = new();
    private FullMapSceneBuilder fullBuilder = new();
    private RadarOptions radarOptions = new();
    private RadarTexts texts = new();

    private PauseMenu? menu;
    private PauseMachine? machine;
    private PauseGameAdapter? pauseGame;
    private PauseScenePublisher? publisher;
    private string pauseReason = "";
    private bool pauseDegraded;
    private long nextPauseCleanupAt;
    private int pauseCleanupRetries;
    private PauseState lastPauseState = PauseState.Running;
    private readonly FullMapView fullView = new();

    private LoadedMap? map;
    private LoadedMap? pendingMap;
    private volatile string? mapLoadFailure;
    private string? requestedIdentity;
    private CancellationTokenSource? workerCts;
    private int mapSerial;
    private int mapLoads;
    private readonly MapRect[] asked = new MapRect[2];
    private readonly DetailLevel[] askedDetail = new DetailLevel[2];
    private readonly long[] askedAt = new long[2];
    private readonly double[] askedExtent = new double[2];
    private readonly LoadedMap?[] askedMap = new LoadedMap?[2];
    private int linesDate = int.MinValue;
    private int linesMapSerial = -1;
    private IReadOnlyList<RadarLine> radarLines = Array.Empty<RadarLine>();
    private (double X, double Y, double Heading) lastPlayer;

    private bool armed, ready, stopped, drawAvailable;
    private bool radarVisible = true, radarDisabled;
    private string radarState = "idle";
    private string lastJson = "";
    private string lastKind = "";
    private long lastRadarSignature;
    private long lastRadarSentAt = long.MinValue;
    private long lastPauseStamp = long.MinValue;
    private int sceneFailureRun;
    private int drawRetries;
    private int viewWidth, viewHeight;

    public HudModule() : this(new StopwatchClock()) { }

    internal HudModule(IPauseClock clock) => this.clock = clock;

    // ------------------------------------------------------------------ lifecycle

    public void Arm(IRuntimeModuleContext context)
    {
        if (this.context is not null) return;
        this.context = context;
        try { Setup(context); }
        catch (Exception exception) { Fault("arm", exception); }
        armed = true;
        Emit("armed", new Dictionary<string, string>
        {
            ["omsi_version"] = Safe(() => context.OmsiVersion, "unknown"),
            ["draw"] = drawAvailable ? "available" : "unavailable",
            ["pause_menu"] = machine is null ? "off: " + pauseReason : "on",
        });
    }

    private void Setup(IRuntimeModuleContext context)
    {
        var parsed = HudSettingsParser.Parse(context.Settings);
        settings = parsed.Settings;
        if (parsed.Issues.Count > 0)
            Emit("settings-issue", new Dictionary<string, string> { ["issues"] = Clip(string.Join("; ", parsed.Issues.Select(i => i.Key + "=" + i.Value + " (" + i.Reason + ")")), 400) });
        ui = LanguageResolver.Resolve(settings.Language, null);
        languageCode = LanguageResolver.PackageCode(settings.Language, null);
        texts = Look.Texts(ui);
        radarOptions = RadarOptionsFor(settings) with { ZoomStep = ZoomStepFor(settings.RadarZoom) };

        native = new NativeLink(Safe<IModuleNativeComponent?>(() => context.NativeComponent, null));
        drawAvailable = native.Available;
        if (drawAvailable) measurer = new NativeTextMeasurer(native);
        else Notice("draw", "UI_NOTICE_DRAW_UNAVAILABLE");

        reader = new GameReaderAdapter(context.Runtime);
        realmapSource = new RealmapSourceAdapter(context.Content);
        frames = new FrameSource(reader, new FrameSourceOptions());
        BuildSceneBuilders(null);

        var capability = Safe(() => context.IsCapabilityAvailable("ui.pause-menu.replace"), false);
        var input = Safe<IModuleInput?>(() => context.Input, null);
        if (!settings.PauseMenu) pauseReason = "turned off in the settings";
        else if (!drawAvailable) pauseReason = "no draw layer";
        else if (!capability) pauseReason = "ui.pause-menu.replace is not available in this build";
        else if (input is null || !Safe(() => input.IsAvailable, false)) pauseReason = "input capture is not available";
        else
        {
            publisher = new PauseScenePublisher();
            menu = new PauseMenu(Look.MenuLookup(ui), PauseIdentity.Generic, languageCode);
            menu.SetViewport(new MenuViewport(MenuScene.CanvasWidth, MenuScene.CanvasHeight, 1, settings.UiScale));
            pauseGame = new PauseGameAdapter(context.Runtime);
            machine = new PauseMachine(pauseGame, new PauseInputAdapter(input), new PauseBackgroundAdapter(native), publisher, clock, menu);
        }
    }

    public void GameplayReady()
    {
        if (context is null || stopped) return;
        ready = true;
        if (drawAvailable)
        {
            var answer = native.Call("visible", SceneOps.Visible(true));
            if (!answer.Ok) counters.NativeProblem = NativeLink.Describe(answer);
            Guard("native", PollNative);
        }
        Emit("gameplay-ready");
    }

    public void Stop()
    {
        if (context is null || stopped) return;
        stopped = true;
        ready = false;
        try { workerCts?.Cancel(); } catch (Exception) { /* already disposed */ }
        // Stop drawing before anything else is given back.
        if (drawAvailable) native.Call("visible", SceneOps.Visible(false));
        StopPause();
        if (drawAvailable)
        {
            native.Call("scene", SceneOps.EmptyScene());
            native.Call("background.release", "{}");
        }
        Emit("stopping");
    }

    // ------------------------------------------------------------------ tick

    public void Tick(TimeSpan elapsed)
    {
        if (context is null || !armed || stopped || !ready || !drawAvailable) return;
        var started = Stopwatch.GetTimestamp();
        try { TickCore(); }
        catch (Exception exception) { Fault("tick", exception); }
        var milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        counters.TickLastMs = milliseconds;
        if (milliseconds > counters.TickMaxMs) counters.TickMaxMs = milliseconds;
        if (milliseconds > SlowTickMilliseconds) counters.SlowTicks++;
    }

    private void TickCore()
    {
        counters.Ticks++;
        var now = clock.NowMilliseconds;
        Guard("map", AdoptPendingMap);
        if (uploads.Pending > 0) Guard("uploads", () => { if (uploads.Drain(native)) measurer?.FontsChanged(); });
        if (counters.Ticks % StatsIntervalTicks == 1) Guard("native", PollNative);

        FrameSnapshot? frame = null;
        Guard("frame", () => frame = ReadFrame());
        if (frame is not null)
        {
            Guard("map", () => WatchMap(frame));
            Guard("lines", () => RefreshLines(frame));
            if (RadarModels.PlayerFor(frame, out _) is { } pose) lastPlayer = (pose.WorldX, pose.WorldY, pose.HeadingDegrees ?? 0);
        }

        if (machine is not null && !pauseDegraded)
        {
            if (Guard("pause", () => StepPause())) counters.PauseFaultRun = 0;
        }
        else if (pauseDegraded) RetryPauseCleanup(now);

        var pausing = machine is not null && !pauseDegraded && machine.State != PauseState.Running;
        if (pausing) { if (Guard("pause-scene", () => ShowPause(frame, now))) counters.PauseFaultRun = 0; }
        else if (Guard("radar", () => ShowRadar(frame, now))) counters.RadarFaultRun = 0;
    }

    private FrameSnapshot? ReadFrame()
    {
        if (frames is null) return null;
        var frame = frames.Read();
        var problems = false;
        foreach (var problem in frame.Problems)
        {
            problems = true;
            if (problemsSeen.Count < ReadProblemKinds && problemsSeen.Add(problem))
                Emit("read-problem", new Dictionary<string, string> { ["problem"] = Clip(problem, 200) });
        }
        if (problems) counters.ProblemFrames++;
        return frame;
    }

    // ------------------------------------------------------------------ packages

    private void WatchMap(FrameSnapshot frame)
    {
        if (mapLoadFailure is { } failure)
        {
            mapLoadFailure = null;
            Notice("packages", "UI_NOTICE_PACKAGE_IGNORED");
            Emit("package-problem", new Dictionary<string, string> { ["detail"] = failure });
        }
        if (frame.Map.Value is not { Loaded: true } info || info.RealmapIdentity is not { } identity) return;
        if (string.Equals(identity, requestedIdentity, StringComparison.OrdinalIgnoreCase) || mapLoads >= MaxMapLoads || context is null || realmapSource is null) return;
        requestedIdentity = identity;
        mapLoads++;
        try { workerCts?.Cancel(); } catch (Exception) { /* already disposed */ }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Stopping);
        workerCts = cts;
        var moduleDirectory = Safe<string?>(() => context.ModuleDirectory, null);
        var source = realmapSource;
        var uiScale = settings.UiScalePercent;
        context.RunBackground("openhud-map", token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cts.Token);
            LoadedMap loaded;
            try { loaded = MapPackageLoader.Load(identity, source, moduleDirectory, uiScale, linked.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception exception) { mapLoadFailure = "loading the packages failed: " + exception.GetType().Name; return; }
            if (linked.IsCancellationRequested) return;
            Interlocked.Exchange(ref pendingMap, loaded);
            loaded.Data.Run(linked.Token);
        });
    }

    // On the tick thread: the packages the worker finished become the module's current ones.
    private void AdoptPendingMap()
    {
        var next = Interlocked.Exchange(ref pendingMap, null);
        if (next is null) return;
        map = next;
        mapSerial++;
        Array.Clear(askedMap);
        if (reader is not null) frames = new FrameSource(reader, new FrameSourceOptions { TileMetres = next.TileMetres });
        uploads.Replace(next.Uploads);
        menu?.SetIdentity(Look.Identity(next.PauseMenu.Identity, languageCode, ui, next.SymbolImage));
        BuildSceneBuilders(next);
        lastRadarSignature = 0;
        lastPauseStamp = long.MinValue;

        var data = new Dictionary<string, string>
        {
            ["map"] = next.Identity,
            ["realmap"] = next.Realmap.Status.ToString(),
            ["pausemenu"] = next.PauseMenu.Status.ToString(),
            ["fonts"] = next.FontCount.ToString(CultureInfo.InvariantCulture),
            ["images"] = next.ImageCount.ToString(CultureInfo.InvariantCulture),
        };
        var diagnostics = next.Realmap.Diagnostics.Select(d => "realmap " + d).Concat(next.PauseMenu.Diagnostics.Select(d => "pausemenu " + d)).Concat(next.Notes).Take(12).ToList();
        if (diagnostics.Count > 0) data["diagnostics"] = Clip(string.Join(" | ", diagnostics), 700);
        Emit("packages", data);
        if (next.Realmap.Status == PackageStatus.Ignored || next.PauseMenu.Status == PackageStatus.Ignored) Notice("packages", "UI_NOTICE_PACKAGE_IGNORED");
    }

    private void BuildSceneBuilders(LoadedMap? loaded)
    {
        var theme = Look.Theme(loaded?.PauseMenu.Identity);
        ITextMeasurer text = measurer is not null ? measurer : new ApproximateTextMeasurer();
        radarBuilder = new RadarSceneBuilder(theme, texts, text);
        fullBuilder = new FullMapSceneBuilder(theme, texts, text);
    }

    private void RefreshLines(FrameSnapshot frame)
    {
        var date = frame.Time.Value?.DateNumber ?? 0;
        if (linesMapSerial == mapSerial && date == linesDate) return;
        linesMapSerial = mapSerial;
        linesDate = date;
        var accent = map?.PauseMenu.Identity.AccentColor is { } a ? new HudColor(a.R, a.G, a.B).ToHex() : PauseIdentity.DefaultAccent;
        var (radar, entries) = RadarModels.Lines(map, date == 0 ? null : date, accent);
        radarLines = radar;
        menu?.SetLines(entries);
    }

    // The map data for a view: the newest finished set, and a request when the view is not covered.
    private FeatureSet? Features(MapQuery kind, double packageX, double packageY, double half, DetailLevel detail, bool minorContours, double minExtent, long now)
    {
        var current = map;
        if (current is null || current.Realmap.Status != PackageStatus.Loaded) return null;
        var set = current.Data.Latest(kind);
        if (set is not null && set.Detail == detail && set.MinExtent <= minExtent * 1.0001 && set.Covers(packageX, packageY, half)) return set;
        var k = (int)kind;
        var pending = asked[k];
        var covered = askedMap[k] == current && askedDetail[k] == detail && askedExtent[k] <= minExtent * 1.0001 && now - askedAt[k] < 3000 && pending.IsValid
            && packageX - half >= pending.MinX && packageX + half <= pending.MaxX && packageY - half >= pending.MinY && packageY + half <= pending.MaxY;
        if (!covered)
        {
            var window = MapRect.Around(packageX, packageY, Math.Max(half * 1.8, 600));
            current.Data.Ask(kind, window, detail, minorContours, minExtent);
            askedExtent[k] = minExtent; asked[k] = window; askedDetail[k] = detail; askedAt[k] = now; askedMap[k] = current;
        }
        return set;
    }

    // A feature under about a pixel and a half at the scale of the view cannot be seen: it is not worth projecting every tick.
    private static double MinVisible(double metresPerPixel) => 1.5 * metresPerPixel;

    private static DetailLevel DetailForRadius(double radiusMetres) => radiusMetres <= 800 ? DetailLevel.Close : radiusMetres <= 1600 ? DetailLevel.Normal : DetailLevel.Overview;
    private static DetailLevel DetailForScale(double metresPerPixel) => metresPerPixel <= 2 ? DetailLevel.Close : metresPerPixel <= 8 ? DetailLevel.Normal : DetailLevel.Overview;

    // ------------------------------------------------------------------ the radar

    private void ShowRadar(FrameSnapshot? frame, long now)
    {
        if (!settings.Radar || !radarVisible || radarDisabled) { radarState = radarDisabled ? "disabled" : "off"; SendEmpty(); return; }
        if (frame is null) { radarState = "no-frame"; SendEmpty(); return; }
        var player = RadarModels.PlayerFor(frame, out var reason);
        if (player is null) { radarState = reason; SendEmpty(); return; }
        radarState = "on";

        var radius = RadarOptions.ZoomRadiiMeters[RadarOptions.ClampZoom(radarOptions.ZoomStep)];
        var set = Features(MapQuery.Radar, player.WorldX, player.WorldY, radius * 1.15, DetailForRadius(radius), radius <= 200, MinVisible(radius / radarOptions.Radius), now);
        var sameKind = lastKind == "radar";
        if (sameKind && now - lastRadarSentAt < MinRadarIntervalMs) return;
        var signature = RadarModels.Signature(frame, player, set?.Id ?? 0, radarOptions.ZoomStep, mapSerial);
        if (sameKind && signature == lastRadarSignature) { counters.ScenesSkipped++; return; }

        var model = RadarModels.Build(frame, player, map?.Frame ?? IdentityFrame, set, null);
        var scene = SceneOpacity.Apply(radarBuilder.Build(model, radarOptions), settings.RadarOpacityPercent);
        var json = SceneJson.Encode(scene);
        if (sameKind && json == lastJson) { counters.ScenesSkipped++; lastRadarSignature = signature; lastRadarSentAt = now; return; }
        if (Send(json, "radar")) { lastRadarSignature = signature; lastRadarSentAt = now; }
    }

    private static RadarOptions RadarOptionsFor(HudSettings s)
    {
        const double margin = 24, strip = 44;
        var radius = Math.Clamp(160 * s.RadarSizePercent / 100.0 * s.UiScale, 40, 520);
        var width = MenuScene.CanvasWidth;
        var height = MenuScene.CanvasHeight;
        var cx = s.RadarCorner is RadarCorner.TopLeft or RadarCorner.BottomLeft ? margin + radius : width - margin - radius;
        var cy = s.RadarCorner is RadarCorner.TopLeft or RadarCorner.TopRight ? margin + radius : height - margin - strip - radius;
        return new RadarOptions { CanvasWidth = width, CanvasHeight = height, CenterX = cx, CenterY = cy, Radius = radius, Mode = RadarMode.HeadingUp, MaxCommands = 1500, MaxPoints = 6000 };
    }

    private static int ZoomStepFor(RadarZoom zoom) => zoom switch { RadarZoom.Near => 1, RadarZoom.Far => 3, _ => 2 };

    // ------------------------------------------------------------------ the pause menu

    private void StepPause()
    {
        var m = machine!;
        m.Tick();
        // The build cannot take the menu over: OMSI's own menu stays, which is worth telling once.
        if (m.Unavailable) Notice("pause", "UI_NOTICE_PAUSE_FALLBACK");
        if (m.State == PauseState.Open && lastPauseState != PauseState.Open)
        {
            var (x, y) = (map?.Frame ?? IdentityFrame).ToDisplay(lastPlayer.X, lastPlayer.Y);
            fullView.Reset(x, y);
        }
        lastPauseState = m.State;
    }

    private void ShowPause(FrameSnapshot? frame, long now)
    {
        var scene = publisher!.Current;
        if (scene.Mode == MenuSceneMode.None) { SendEmpty(); return; }
        var set = map?.Data.Latest(MapQuery.FullMap);
        var stamp = HashCode.Combine(publisher.Version, set?.Id ?? 0, mapSerial, radarLines.Count);
        if (stamp == lastPauseStamp && lastKind == "pause") return;

        MenuRect? area = null;
        if (scene.Mode == MenuSceneMode.MapBehind || menu!.Page == MenuPage.Map) area = scene.Region("map")?.Rect;

        SceneDocument? sub = null;
        if (area is { W: >= 16, H: >= 16 } rect)
        {
            try { sub = BuildFullMap(frame, rect, scene.Mode == MenuSceneMode.MapBehind, now); }
            catch (Exception exception) { Fault("map", exception); }
        }
        SceneDocument document;
        try { document = SceneComposer.Compose(scene, sub, area ?? default); }
        catch (SceneException) when (sub is not null) { document = SceneComposer.Compose(scene, null, default); }
        var json = SceneJson.Encode(document);
        if (json == lastJson && lastKind == "pause") { lastPauseStamp = stamp; return; }
        if (Send(json, "pause")) { lastPauseStamp = stamp; counters.PauseFailureRun = 0; }
        else if (++counters.PauseFailureRun >= FailureLimit) DegradePause("the scene could not be published");
    }

    private SceneDocument BuildFullMap(FrameSnapshot? frame, MenuRect area, bool provisional, long now)
    {
        var mapFrame = map?.Frame ?? IdentityFrame;
        var (displayX, displayY) = mapFrame.ToDisplay(lastPlayer.X, lastPlayer.Y);
        // The provisional map sits behind the native menu: centred on the player at the default scale, no panning.
        if (!provisional) fullView.Follow(menu!, menu!.Geometry.Metrics.CanvasScale, displayX, displayY);
        var view = provisional ? new FullMapViewport(displayX, displayY, FullMapView.DefaultMetersPerPixel) : fullView.Viewport;

        var width = (int)Math.Round(area.W);
        var height = (int)Math.Round(area.H);
        var (cx, cy) = mapFrame.ToPackage(view.CenterX, view.CenterY);
        var half = Math.Sqrt((double)width * width + (double)height * height) / 2 * view.MetersPerPixel * 1.1;
        var set = Features(MapQuery.FullMap, cx, cy, half, DetailForScale(view.MetersPerPixel), view.MetersPerPixel <= 1, MinVisible(view.MetersPerPixel), now);

        var pose = frame is null ? null : RadarModels.PlayerFor(frame, out _);
        var model = frame is not null && pose is not null
            ? RadarModels.Build(frame, pose, mapFrame, set, radarLines)
            : new RadarModel(new RadarPlayer(displayX, displayY, 0), set?.Features, set?.Regions, set?.Markers, Array.Empty<RadarVehicle>(), null, null, radarLines);
        var options = new FullMapOptions { CanvasWidth = width, CanvasHeight = height, Margin = 0, SidePanelWidth = 0, OverBackground = false, MaxCommands = 7200, MaxVehicles = 120, MaxMarkers = 120 };
        return fullBuilder.Build(model, view, options);
    }

    private void DegradePause(string why)
    {
        if (pauseDegraded) return;
        pauseDegraded = true;
        Notice("pause", "UI_NOTICE_PAUSE_FALLBACK");
        Emit("pause-degraded", new Dictionary<string, string> { ["reason"] = why, ["state"] = machine?.State.ToString() ?? "" });
        StopPause();
        lastKind = "";
    }

    // Gives back everything the pause machine holds; repeats the pieces a busy game refused.
    private void StopPause()
    {
        if (machine is null) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { machine.Stop(); }
            catch (Exception exception)
            {
                Fault("pause-stop", exception);
                ForcedPauseCleanup();
                return;
            }
            if (machine.CleanupComplete) break;
        }
        EnsureNativeMenu();
    }

    // The last line of defence: a take-over whose answer was lost (a call that failed after it took effect) is invisible to the
    // machine. If the game is still paused with its menu hidden and nothing is going on, the menu is given back.
    private void EnsureNativeMenu()
    {
        try
        {
            var state = pauseGame?.State();
            if (state is not { IsOk: true } || state.Value.State is not { Paused: true, NativeMenuVisible: false, Busy: false } snapshot || snapshot.LastResult == PauseLastResult.QuitRequested) return;
            pauseGame!.RestoreNative();
            Emit("pause-restored", new Dictionary<string, string> { ["why"] = "the game was paused with its menu hidden" });
        }
        catch (Exception) { /* nothing more can be done from here */ }
    }

    private void RetryPauseCleanup(long now)
    {
        if (machine is null || machine.CleanupComplete || now < nextPauseCleanupAt || pauseCleanupRetries >= 40) return;
        pauseCleanupRetries++;
        nextPauseCleanupAt = now + 250;
        try { machine.Stop(); } catch (Exception exception) { Fault("pause-stop", exception); }
    }

    // The machine could not run its own Stop (it belongs to one thread): the three things it holds, by hand.
    private void ForcedPauseCleanup()
    {
        try { context?.Input.Capture(false); } catch (Exception) { /* nothing more to try */ }
        try { native.Call("background.release", "{}"); } catch (Exception) { /* nothing more to try */ }
        EnsureNativeMenu();
    }

    // ------------------------------------------------------------------ the draw layer

    private void PollNative()
    {
        var stats = native.Stats();
        if (stats is null) { counters.StatsFailures++; return; }
        if (stats.ViewWidth > 0 && stats.ViewHeight > 0 && (stats.ViewWidth != viewWidth || stats.ViewHeight != viewHeight))
        {
            viewWidth = stats.ViewWidth; viewHeight = stats.ViewHeight;
            menu?.SetViewport(new MenuViewport(viewWidth, viewHeight, 1, settings.UiScale));
        }
        if (!stats.Failed) return;
        counters.DrawFailures++;
        Emit("draw-failed", new Dictionary<string, string> { ["detail"] = Clip(stats.LastError ?? "", 200) });
        // With the menu open the user would be left without one: give the native menu back first.
        if (machine is not null && !pauseDegraded && machine.State != PauseState.Running) DegradePause("the draw layer failed");
        if (drawRetries++ < 2)
        {
            native.Call("visible", SceneOps.Visible(true));
            lastJson = ""; lastKind = "";
        }
        else
        {
            radarDisabled = true;
            Notice("draw", "UI_NOTICE_DRAW_UNAVAILABLE");
            DegradePause("the draw layer keeps failing");
        }
    }

    private bool Send(string json, string kind)
    {
        var answer = native.Call("scene", json);
        if (answer.Ok)
        {
            lastJson = json; lastKind = kind; sceneFailureRun = 0;
            counters.ScenesPublished++;
            return true;
        }
        counters.SceneFailures++;
        counters.NativeProblem = NativeLink.Describe(answer);
        if (++sceneFailureRun >= FailureLimit && kind == "radar" && !radarDisabled)
        {
            radarDisabled = true;
            Notice("radar", "UI_NOTICE_RADAR_OFF");
        }
        return false;
    }

    private void SendEmpty()
    {
        var empty = SceneOps.EmptyScene();
        if (lastJson == empty) return;
        Send(empty, "empty");
    }

    // ------------------------------------------------------------------ operations of the module

    public IReadOnlyDictionary<string, string> Execute(string operation, IReadOnlyDictionary<string, string> arguments)
    {
        switch (operation)
        {
            case "status": return Status();
            case "radar.zoom": return Zoom(arguments);
            case "radar.toggle": return Toggle(arguments);
            case "pause.state": return PauseStatus();
            default: throw new ModuleOperationException(PublicErrorCodes.MODULE_OPERATION_UNKNOWN, operation);
        }
    }

    private IReadOnlyDictionary<string, string> Zoom(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("direction", out var direction) || string.IsNullOrWhiteSpace(direction))
            throw new ModuleOperationException(PublicErrorCodes.RUNTIME_ARGUMENT_REQUIRED, "direction is required (in or out)");
        switch (direction.Trim().ToLowerInvariant())
        {
            case "in": radarOptions = radarOptions.ZoomIn(); break;
            case "out": radarOptions = radarOptions.ZoomOut(); break;
            default: throw new ModuleOperationException(PublicErrorCodes.RUNTIME_VALUE_INVALID, "direction must be in or out");
        }
        lastRadarSignature = 0;
        return new Dictionary<string, string>
        {
            ["radar_zoom_step"] = radarOptions.ZoomStep.ToString(CultureInfo.InvariantCulture),
            ["radar_radius_m"] = RadarOptions.ZoomRadiiMeters[RadarOptions.ClampZoom(radarOptions.ZoomStep)].ToString(CultureInfo.InvariantCulture),
        };
    }

    private IReadOnlyDictionary<string, string> Toggle(IReadOnlyDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue("visible", out var text) && !string.IsNullOrWhiteSpace(text))
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "true": case "1": radarVisible = true; break;
                case "false": case "0": radarVisible = false; break;
                default: throw new ModuleOperationException(PublicErrorCodes.RUNTIME_VALUE_INVALID, "visible must be true or false");
            }
        }
        else radarVisible = !radarVisible;
        // Turning the radar on again is also the way back after repeated errors switched it off.
        if (radarVisible) { radarDisabled = false; sceneFailureRun = 0; counters.RadarFaultRun = 0; lastRadarSignature = 0; }
        return new Dictionary<string, string> { ["radar_visible"] = radarVisible ? "true" : "false", ["radar_state"] = radarState };
    }

    private IReadOnlyDictionary<string, string> PauseStatus() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pause_menu"] = machine is null ? "off" : pauseDegraded ? "degraded" : "on",
        ["pause_reason"] = pauseReason,
        ["pause_state"] = machine?.State.ToString() ?? "",
        ["pause_last_error"] = machine?.LastError is { } error ? error.CatalogCode + (string.IsNullOrEmpty(error.Detail) ? "" : ": " + Clip(error.Detail, 120)) : "",
        ["pause_cleanup_complete"] = machine is null || machine.CleanupComplete ? "true" : "false",
        ["pause_unavailable"] = machine is { Unavailable: true } ? "true" : "false",
        ["key_release_timeouts"] = (machine?.KeyReleaseTimeouts ?? 0).ToString(CultureInfo.InvariantCulture),
    };

    private IReadOnlyDictionary<string, string> Status()
    {
        var loaded = map;
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["module_state"] = stopped ? "stopped" : ready ? "running" : armed ? "armed" : "new",
            ["draw"] = drawAvailable ? "available" : "unavailable",
            ["radar_setting"] = settings.Radar ? "true" : "false",
            ["radar_visible"] = radarVisible ? "true" : "false",
            ["radar_state"] = radarState,
            ["radar_zoom_step"] = radarOptions.ZoomStep.ToString(inv),
            ["radar_radius_m"] = RadarOptions.ZoomRadiiMeters[RadarOptions.ClampZoom(radarOptions.ZoomStep)].ToString(inv),
            ["radar_corner"] = settings.RadarCorner.ToString(),
            ["units"] = settings.Units.ToString(),
            ["language"] = ui.ToString(),
            ["language_code"] = languageCode,
            ["ui_scale_percent"] = settings.UiScalePercent.ToString(inv),
            ["map"] = requestedIdentity ?? "",
            ["packages"] = loaded is null ? (requestedIdentity is null ? "none" : "loading") : "loaded",
            ["realmap_status"] = loaded?.Realmap.Status.ToString() ?? "",
            ["pausemenu_status"] = loaded?.PauseMenu.Status.ToString() ?? "",
            ["radar_features"] = (loaded?.Data.Latest(MapQuery.Radar)?.Features.Length ?? 0).ToString(inv),
            ["fullmap_features"] = (loaded?.Data.Latest(MapQuery.FullMap)?.Features.Length ?? 0).ToString(inv),
            ["fonts_registered"] = uploads.Fonts.ToString(inv),
            ["images_registered"] = uploads.ImageIds.Count.ToString(inv),
            ["uploads_pending"] = uploads.Pending.ToString(inv),
            ["uploads_failed"] = uploads.Failed.ToString(inv),
            ["pause_menu"] = machine is null ? "off" : pauseDegraded ? "degraded" : "on",
            ["pause_state"] = machine?.State.ToString() ?? "",
            ["scenes_published"] = counters.ScenesPublished.ToString(inv),
            ["scenes_skipped"] = counters.ScenesSkipped.ToString(inv),
            ["scene_failures"] = counters.SceneFailures.ToString(inv),
            ["ticks"] = counters.Ticks.ToString(inv),
            ["tick_last_ms"] = counters.TickLastMs.ToString("0.###", inv),
            ["tick_max_ms"] = counters.TickMaxMs.ToString("0.###", inv),
            ["slow_ticks"] = counters.SlowTicks.ToString(inv),
            ["frames_with_problems"] = counters.ProblemFrames.ToString(inv),
            ["faults"] = counters.Faults.ToString(inv),
            ["last_fault"] = counters.LastFault,
            ["native_calls"] = native.Calls.ToString(inv),
            ["native_failures"] = native.Failures.ToString(inv),
            ["native_problem"] = counters.NativeProblem,
            ["text_measure"] = measurer is { UsingNative: true } ? "native" : "approximate",
            ["view"] = viewWidth + "x" + viewHeight,
            ["notices"] = string.Join(",", noticed.Where(x => x.StartsWith("notice:", StringComparison.Ordinal)).Select(x => x[7..]).OrderBy(x => x, StringComparer.Ordinal)),
        };
    }

    // ------------------------------------------------------------------ helpers

    // Runs one piece of work; a fault is counted and said once, and the caller carries on. True when it ran clean.
    private bool Guard(string area, Action action)
    {
        try { action(); return true; }
        catch (Exception exception) { Fault(area, exception); return false; }
    }

    private void Fault(string area, Exception exception)
    {
        counters.Faults++;
        counters.LastFault = area + ": " + exception.GetType().Name;
        if (noticed.Add("fault:" + area))
            Emit("fault", new Dictionary<string, string> { ["area"] = area, ["type"] = exception.GetType().Name, ["message"] = Clip(exception.Message, 200) });
        // What cannot go on after repeated faults is switched off, never retried in a loop.
        if (area == "radar" && ++counters.RadarFaultRun >= FailureLimit && !radarDisabled)
        {
            radarDisabled = true;
            Notice("radar", "UI_NOTICE_RADAR_OFF");
        }
        else if (area is "pause" or "pause-scene" && ++counters.PauseFaultRun >= FailureLimit) DegradePause(area + " keeps failing");
    }

    private void Notice(string key, string textKey)
    {
        if (!noticed.Add("notice:" + key)) return;
        Emit("notice", new Dictionary<string, string> { ["title"] = Strings.Get("UI_NOTICE_TITLE", ui), ["text"] = Strings.Get(textKey, ui) });
    }

    private void Emit(string name, IReadOnlyDictionary<string, string>? data = null)
    {
        try { context?.Emit(name, data); } catch (Exception) { /* an event the bridge refuses is not worth a failure */ }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch (Exception) { return fallback; }
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];

    private sealed class Counters
    {
        public long Ticks, ScenesPublished, ScenesSkipped, SceneFailures, SlowTicks, ProblemFrames, Faults, StatsFailures, DrawFailures;
        public int RadarFaultRun, PauseFaultRun, PauseFailureRun;
        public double TickLastMs, TickMaxMs;
        public string LastFault = "", NativeProblem = "";
    }
}
