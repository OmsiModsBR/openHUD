# The three parts and why

openHUD is split in three parts that meet at one small interface: a document that says what to draw. This page explains what each part does, what it is not allowed to know, how a tick of the module moves information between them and why the work is divided this way. The design is described in more depth for developers in [architecture](../architecture.md); the document the parts exchange is defined in the [scene protocol](../scene-protocol.md).

Related pages: [the scene protocol in plain words](the-scene-protocol.md), [the radar](the-radar.md), [the pause menu](the-pause-menu.md), [performance](performance.md), [safety](safety.md).

## The parts

| Part | Language, bitness | Lives in | Knows | Does not know |
| --- | --- | --- | --- | --- |
| The core | C#, loaded by the 32-bit plugin | `OpenHud.Core` | The game as OmsiLaunch describes it (time, map, camera, the player's vehicle, the vehicles around it); the settings; the interface texts; the two map packages; how to lay out a radar and a map as shapes | Direct3D, windows, input, any address in OMSI |
| The pause menu | C#, same | `OpenHud.Pause` | The pause menu's states, its pages, focus and keyboard and mouse rules, its layout; how to talk to OMSI's pause through four small ports | The radar, the packages, the bridge |
| The draw layer | C++, x86 | `OpenHud.Draw` (`openhud-draw.dll`) | Triangles, glyphs, textures, the game's Direct3D 9 device; the scene format | The game, the menu, the map |

A fourth piece, `OpenHud.Module` (`OpenHud.dll`), is the glue: the managed component that OmsiLaunch's bridge loads. It adapts OmsiLaunch's context to the ports of the other parts, owns the tick and decides what to publish. It holds no logic of its own about layout.

## Why a scene

The core decides **what** is drawn; the draw layer only **rasterizes** it. They meet at one JSON document, the scene (a list of rectangles, circles, lines, polygons, text and images, in a virtual 1920 x 1080 canvas), and a few small operations to register images and fonts and to show or hide. Splitting the work this way has four consequences:

- **All layout is plain managed code.** The radar and the menu are built without a Direct3D device, so the tests run anywhere and a layout is checked against golden documents ([test coverage](../status/test-coverage.md)).
- **The native layer stays small and checkable.** It parses a document, draws it and answers `ok` or an error. It keeps no state about the game, so it cannot disagree with it.
- **A new scene replaces the old one atomically.** Publishing is one call; the layer keeps the latest scene only and draws it at every frame, so a late or slow tick never shows a half-built picture.
- **Failures are contained.** A scene the layer refuses leaves the previous one; a layer that fails draws nothing and says so, and the game goes on.

## One tick

The bridge calls the module every 50 ms on OMSI's UI thread. In each tick the module:

1. reads the game through OmsiLaunch's operations (`time.read`, `map.read`, `player-vehicle.read`, `road-vehicles.list` and the others the manifest declares). A part that cannot be read becomes "unavailable" without touching the other parts;
2. notices a change of map and starts loading the map's packages on a worker thread (never inside the tick);
3. gives the pause menu's state machine one step: it asks OMSI whether the game is paused, takes the native menu over or gives it back, and reads the keys and the mouse;
4. builds one scene, the radar while the game runs, or the menu (with the full map inside it) while it is paused;
5. sends the scene to the draw layer only when something visible changed.

The draw layer, meanwhile, is not tied to the tick. It draws the last scene it was given at every `Present` of the game's device, whatever thread presents.

## What each part may never do

- The core never sends pointers, handles or addresses to the layer, and the layer never reads the game.
- Neither managed part opens a window, sends a key or a click, or writes to a file of OMSI.
- No part contains an address, an offset or a signature of OMSI. What the game exposes comes from OmsiLaunch's catalog; OmsiLaunch resolves it per OMSI build and answers `OL_E_CAPABILITY_UNRESOLVED` where it cannot ([compatibility](../reference/compatibility.md)).

## Where the code is

| Folder | Content |
| --- | --- |
| `src/OpenHud.Core/Settings`, `I18n` | The settings parser and the interface texts. |
| `src/OpenHud.Core/Model` | The game model: `FrameSource` and the snapshot types. |
| `src/OpenHud.Core/Packages` | The two package readers. |
| `src/OpenHud.Core/Scene`, `Radar` | The scene types and codec; the radar and full-map builders. |
| `src/OpenHud.Pause` | The state machine and the menu. |
| `src/OpenHud.Draw` | The draw layer. |
| `src/OpenHud.Module` | The glue. |

Related: [the scene protocol in plain words](the-scene-protocol.md) · [architecture](../architecture.md) · [performance](performance.md)
