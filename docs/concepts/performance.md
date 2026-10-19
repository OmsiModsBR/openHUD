# Performance and the tick/draw split

openHUD runs in two rhythms: a **tick** every 50 ms that reads the game and builds a scene, and a **draw** at every frame that puts the last scene on the screen. This page explains what each costs, what is bounded and how, how the module protects the game from its own work, and how to see the numbers. It states what the code bounds; it does not state a frame-rate cost, because none has been measured in a running game yet ([validation status](../status/validation.md)).

Related pages: [the three parts](the-three-parts.md), [the scene protocol in plain words](the-scene-protocol.md), [the radar](the-radar.md), [troubleshooting](../guides/troubleshooting.md#high-cpu-or-a-slow-game), [known limitations](../reference/known-limitations.md).

## The tick

The bridge calls the module about every 50 ms (`tick_ms` is 50) on OMSI's UI thread. A tick that throws, returns late (over its interval) or never returns is a failure of the module under the bridge's rules; the module's failure policy is `degrade`, so a late tick is reported once and the module keeps running (missed ticks are skipped, never queued). The module keeps its own work well under 50 ms and measures it.

What one tick does, and the bounds:

| Work | Bound |
| --- | --- |
| Read the game | The clock and the camera every tick; the player's vehicle every tick; the map, weather and timetable every 20th tick; the tile table every 60th tick (and at once when the player's tile is unknown, at most every 10th tick); the list of vehicles every 5th tick |
| Read the vehicles around you | At most 24 `road-vehicle.read` calls per tick (the never-read ones first, then the ones nearest to you whose reading is oldest); only vehicles within 800 m are kept; at most 128 are reported; a vehicle not read for 40 ticks is dropped |
| Step the pause menu | One `ui.pause.state` call per tick while the game is not paused |
| Build the radar | At most every 100 ms, and only if something visible changed (position to a third of a pixel of the radar's scale, heading to 1 degree, the vehicles, the shown speed, the clock minute, the zoom, new map data); a build that comes out equal to the scene on the screen is not encoded or sent |
| Build the pause menu and the map | The menu when its state changed; the map under it only when the view, the data window, the player, the vehicles or the lines changed, so a hovered menu reuses the map and its encoded commands |
| Send pictures and fonts to the draw layer | A budget of 6 ms per tick; the rest waits for the next tick |
| Load a package | On a worker thread, never in the tick |
| Read map data | On a worker thread: the tick asks for a window and keeps drawing the previous one until the new one is ready |

All the game reads are in-process calls to OmsiLaunch's operations, run on the UI thread like OMSI's own code; each is small and bounded.

## What a scene costs

The core never produces more than the draw layer accepts:

| | Radar | Full map |
| --- | --- | --- |
| Commands | 6,000 (a part is reserved for vehicles, markers and the ring) | 7,000 to 7,200 |
| Polyline/polygon points | 24,000 | 30,000 |
| Vehicles / markers | 48 / 48 | 120 to 200 |
| Map features offered by a window | 40,000 triangles and lines | the same |

When a window holds more than the budget, the builder keeps the most visible features (large on the screen, from important layers: roads first, then water, built-up areas, green areas, relief) and drops the rest; the choice is deterministic, so the same input draws the same picture. Geometry is cut to the visible window and points that add no visible detail are dropped before it is counted against the budget.

The module sends a scene only if it differs from the last one it sent: it compares a hash of the commands, so an unchanged scene is neither encoded nor sent. A static radar costs nothing but the check. Features outside the view are skipped by their bounding box before any point is projected, the builders reuse their working lists between builds, and the checks a scene passes (`SceneBuilder`, the encoder) allocate nothing for a valid command; with these a radar build allocates about 0.45 MiB instead of 1.5 MiB, and the garbage collector runs far less often on the tick.

The package data is read a window at a time on the worker thread. The window around you is about twice the radius shown (at least 600 m) and is asked for again only when the view leaves it, so a drive through one window asks once (`map_queries` in `status`).

## The draw

The draw layer hooks the game device's `Present` and `Reset` through the bridge's hook registry, stacked on the product's own hooks. At each `Present` it draws the current scene on top of the game's picture, only on the game's own device (the vehicle selection form creates a second device with the same methods; the layer ignores it).

- **It never waits.** If a `Reset` or a stop owns the lock when `Present` arrives, that frame is skipped.
- **It batches.** Triangles are collected into a few draw calls; glyphs come from a texture atlas with a least-recently-used replacement, so the atlas stays bounded.
- **It tessellates a scene once.** A scene is drawn at every `Present` but replaced a few times a second, so the commands are turned into triangles once, when a scene is first drawn, and the same batches are replayed until something they depend on changes: a new scene, an image put or dropped, a font registered, a different view size or device, a device `Reset`. A frame that is not complete (a picture still missing, a glyph that did not fit, an atlas cell reused while drawing) is not kept. `stats` reports `tessellations` and `replays`. A circular clip, which the radar uses, tests each triangle against the circle before the general clipping, which made a new radar scene about five times cheaper to tessellate.
- **It restores what it changes.** The Direct3D state it sets is saved and restored; it also works when called inside the game's own scene.
- **Software and hardware backends.** The same scene is rasterized by a software backend (used by the tests) and by the Direct3D 9 backend, and a test compares them.
- **Memory.** Pictures are limited to 128 and 4 MiB each, 32 MiB in all.
- **Failure.** A failure while drawing (an exception caught at the frame boundary) switches drawing off and is reported; the layer does not try to draw again until the module asks.

## Seeing the numbers

`status` ([operations and events](../reference/operations-and-events.md#status)) reports the module's own measures:

| Key | Meaning |
| --- | --- |
| `ticks` | Ticks since the session started |
| `tick_last_ms`, `tick_max_ms` | Duration of the last tick and the longest one, in ms |
| `slow_ticks` | Ticks that took more than 40 ms |
| `scenes_published`, `scenes_skipped`, `scene_failures` | Scenes sent, ones skipped because nothing changed, ones the draw layer refused |
| `map_queries`, `map_builds` | Windows of map data asked for; maps built for the pause menu |
| `native_calls`, `native_failures` | Calls to the draw layer and how many failed |
| `uploads_pending`, `fonts_registered`, `images_registered` | Pictures and fonts waiting and registered |
| `frames_with_problems` | Ticks in which part of the game could not be read |

The draw layer's own counters (scenes, commands, triangles, draw calls, tessellations, replays, skipped frames, device resets, captures) are the `stats` operation of the native component; the module reads them every 20th tick and acts on a failure.

## What costs the most, and what to do

| If you want less work | Do |
| --- | --- |
| Less per tick | Turn the radar off (`radar=false`) when you only want the pause menu; a smaller `radar_size_percent` draws fewer shapes |
| Less map drawing | `radar_zoom=near` shows a small area at full detail; `far` shows more at less detail |
| Nothing at all | Do not request the module for that session ([enabling the module](../guides/enabling-the-module.md)) |

A heavy realmap package costs reading time on the worker thread, once per window; the picture is drawn from memory afterwards.

Related: [the three parts](the-three-parts.md) · [operations and events](../reference/operations-and-events.md) · [troubleshooting](../guides/troubleshooting.md)
