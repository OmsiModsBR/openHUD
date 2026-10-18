# Architecture

openHUD is an OmsiLaunch runtime module (bridge protocol 3). It reads the game only through OmsiLaunch's catalog operations, decides what to draw in managed code, and hands the result to a small native layer that rasterizes it on the game's Direct3D 9 device. Status: built and tested offline against a fake bridge; not yet run in OMSI.

## The three parts

| Part | Where | Does |
| --- | --- | --- |
| Managed module | `src/OpenHud.Module` (+ `OpenHud.Core`, `OpenHud.Pause`) | Entry `OpenHud.HudModule`. Reads the game, builds the radar, the full map and the pause menu as scenes, runs the pause state machine, publishes scenes. Loads inside OMSI (x86, .NET 6). |
| Native draw layer | `src/OpenHud.Draw` (C++, x86) | Parses scenes, rasterizes them (fonts, images, clips, transforms) and draws on the game device from the `Present` hook; holds the pause background snapshot. Knows nothing about the game. |
| Session program (optional) | `src/OpenHud.Host` | Reserved for work that must not run inside OMSI. Not used by the current module. |

`OpenHud.Core` has no OMSI, Direct3D or OmsiLaunch dependency: model, packages, scene builders, i18n, settings. `OpenHud.Pause` is the pause menu's model and state machine behind small ports. Only `OpenHud.Module` references `OmsiLaunch.Runtime.Modules`; its adapters turn the bridge into those ports.

## The scene protocol

Managed and native meet at one JSON document, the scene (a virtual 1920x1080 canvas and a list of primitives), plus a few operations (`scene`, `image.put`, `font.put`, `measure`, `background.capture`, `background.release`, `visible`, `stats`). The native layer draws the last scene it was given at every `Present`. Full contract: [scene-protocol.md](scene-protocol.md). The native layer is also the source of text metrics (`measure`) and of the size of the game view (`stats`).

## Data flow of a tick (every 50 ms, on OMSI's UI thread)

1. Adopt the packages a worker finished (an immutable swap) and send a few queued fonts and pictures to the native layer, within a few milliseconds.
2. Every second, ask the layer for `stats`: the view size for mapping the mouse, and whether it failed.
3. `FrameSource` reads the catalog (`time`, `map`, `camera`, player and road vehicles, tile table) into an immutable `FrameSnapshot`. A failed read makes that part unavailable and nothing else.
4. When the map's `maps\<folder>\global.cfg` is first seen, a background job locates the map's packages through the content API, reads them and starts a query worker for the map.
5. The pause machine takes one step (see below).
6. The scene to show is chosen: while the machine is not `Running` it is what the machine published (the menu, the provisional map, or nothing); otherwise the radar.
7. The radar is rebuilt only if a fingerprint of what it shows changed (position to 25 cm, heading to a degree, vehicles, clock, map data, zoom), at most every 100 ms, and sent only if the encoded scene differs from the last one. Pause scenes are sent at once when they change.

Map geometry never blocks a tick: the worker answers window queries of the realmap package (areas, lines, streets, labels, markers) from disk, drops features too small to see at the requested scale, keeps the largest up to a budget, and turns them into radar features in the display plane. The tick draws the newest finished set and asks for a new window when the view leaves the current one. The package's `north` turns the whole plane so real north is up; vehicles and the player go through the same transform.

## Pause menu

`PauseMachine` (RUNNING, TAKE_OVER_PENDING, OPEN, RESUMING, MODAL, ENDING, PROVISIONAL, STOPPED) drives the `ui.pause.*` operations and shows openHUD's own menu in place of OMSI's:

- When the game is paused with its native menu visible, the machine takes the menu over, snapshots the background, starts the input capture and publishes the menu. The module does not capture Esc by itself; OMSI pauses on its own.
- Captured keyboard and mouse events (client coordinates of the view) drive the menu. Esc or the resume item waits for the keys to be released, lets go of the capture and asks for `resume`.
- New game, save, options and quit can open a native modal dialog: the capture is released first, nothing is drawn while the game is busy, and the menu returns when the dialog ends with the game still paused.
- Any failure of a `ui.pause.*` call leads to the provisional mode: the native menu stays visible and only the full map is drawn behind it. A capability that does not resolve stops the machine from asking again.

The module fills the menu's `map` region with the full map (viewport from the menu's pan and zoom, drawn through a clip and a translation) and its lines page with the package's transit lines. The module creates the machine only when the setting is on, a draw layer exists, `ui.pause-menu.replace` is available and input capture is available.

## Failure and degradation

Nothing throws out of a callback. Each piece of a tick runs under a guard; a fault is counted and reported once as an event, and repeated faults switch that piece off:

- The radar is switched off after repeated build or publish failures (one tray notice); `radar.toggle` with `visible=true` brings it back.
- If the draw layer reports a failure, the pause machine is stopped first (the native menu is restored, the capture and background released), then the layer is asked to draw again; if it keeps failing the radar is off too.
- If scenes cannot be published while the menu is open, the pause machine is stopped the same way.
- No draw layer, no capture, or a missing capability turns the affected part off with a reason (`pause.state`, `status`); the rest keeps working.
- `Stop` hides the layer, gives back the native menu (also when a lost answer left the game paused with its menu hidden), releases the capture and the background, and is idempotent. A stopped module makes no further calls.
- Unusable packages are ignored with a diagnostic (one `packages` event, one notice); the radar then draws without them. A pause-menu package supplies the title, symbol, colours, legend and fonts; without one the menu is generic.

## What each package provides

- `openomsi-realmap` (located by the bridge, `content_inputs`): the cartography (areas, lines, streets with limits, labels), markers with icons, municipalities and transit lines. Read in managed code as untrusted data; the fingerprint is only a cache key.
- `openomsi-pausemenu` (next to it in the map's folder): the menu's title per language, symbol, accent and line colours, legend texts, strings and fonts. PNG symbols and icons are decoded by a strict decoder (size limits, checksums, no guessing) and sent as RGBA; SVG symbols are not rasterized in this version and the generic mark is used, with the reason logged. Fonts are registered by validated absolute path; no font and no trademark logo ships with the module.

## Operations of the module

Declared in `module.template.json`: `status` (settings in effect, package states, counters, tick times), `radar.zoom` (`direction` in or out), `radar.toggle` (optional `visible`), `pause.state` (machine state and last typed error). The catalog operations the module calls are listed in `runtime_operations`.
