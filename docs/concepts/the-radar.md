# The radar

The radar is the round minimap in a corner of the screen. This page explains what it shows, how it decides where things are, its zoom steps and its two orientations, how big and how transparent it is, what changes with a realmap package and when it draws nothing. The settings that control it are in the [settings reference](../reference/settings.md) and the [guide to radar and interface settings](../guides/radar-and-interface-settings.md).

Related pages: [realmap package](../reference/realmap-package.md), [per-map packages](per-map-packages.md), [the scene protocol in plain words](the-scene-protocol.md), [performance](performance.md), [troubleshooting](../guides/troubleshooting.md#the-radar-does-not-appear).

## What it shows

From the center outwards:

| Element | Source | Notes |
| --- | --- | --- |
| Map layers | The realmap package | Water, green areas, built-up areas, relief, roads, rails, borders. Without a package: none. |
| Municipality and place names | The realmap package | A faint name where a region lies (up to 12 on the radar) and the package's labels. |
| Markers | The realmap package | The package's icons (stations, terminals, depots); a marker without a usable icon shows the pause-menu package's `stop_icon`, or a dot. |
| Vehicles around you | `road-vehicles.list` and `road-vehicle.read` | Up to 48 on the radar, the nearest first. Only vehicles within 800 m of you are read. |
| You | `player-vehicle.read` | An arrow in the middle: a chevron, or a plain triangle when the realmap manifest says `player = triangle`, in the color of `player_free` when it gives one. |
| The ring | | A thin ring with four marks (the red one is north) and a small red triangle and letter showing where north is. |
| The scale | | The radius of the circle at its bottom edge: meters and kilometers, or feet and miles with `units=imperial`. |
| The strip | `player-vehicle.read`, `time.read` | Under the radar (above it when there is no room below): your speed on the left, in km/h or mph by the `units` setting, and the game clock on the right. |

The speed is the vehicle's `ground_speed`, read as metres per second (an inference that the runtime round confirms) and shown as a whole number without a sign. The radar does not show routes, next stops, delays or a timetable: the catalog gives no source for them, so the next-stop and delay slots of the strip stay empty ([known limitations](../reference/known-limitations.md)).

## Zoom

The visible radius is one of six steps: 100, 200, 400, 800, 1600 and 3200 meters. The `radar_zoom` setting chooses the starting step:

| `radar_zoom` | Step | Radius shown |
| --- | ---: | ---: |
| `near` | 1 | 200 m |
| `medium` | 2 | 400 m |
| `far` | 3 | 800 m |

While you play, the operation `radar.zoom` moves one step in or out, across all six steps ([operations and events](../reference/operations-and-events.md#radarzoom)). The level of detail follows the radius: up to 800 m everything is drawn (streets, every label); up to 1600 m the normal level; beyond that the overview level (no minor contour lines, fewer labels, simplified lines) ([realmap package](../reference/realmap-package.md#level-of-detail)).

## Orientation

A radar can keep north up or keep your heading up. openHUD's radar keeps **your heading up**: the map turns as you turn, so what is ahead of you is always at the top, and the ring's north mark moves round to show where north is. The full map on the pause menu's Map page is north up.

If the realmap package states a `north` (the angle between the map's own up and real north), the picture is rotated by it, so that real north is up when the map is north up; vehicles and the player go through the same rotation and always agree with the map.

## Positions and headings

OMSI reports a vehicle's position as a tile number and a position inside the tile. The module converts it to the metres of the map's grid:

1. it reads the map's tile table (`map.tiles.list`) and learns each tile's column and row;
2. a position is `column x tile size + position in the tile`, and the same for the row; the tile size is the package's `grid` (300 m when there is no package);
3. a heading is read from the vehicle's orientation (a quaternion): the direction of its forward axis seen from above, in degrees clockwise from north.

Two things follow. First, the radar draws only when your vehicle's tile is in the table; when it is not (a tile that cannot be read, or the table not read yet), the radar waits and retries soon. Second, the size of the grid matters: a package whose `grid` is wrong puts the map off from the vehicles, farther the farther from the origin ([realmap package](../reference/realmap-package.md#coordinates)).

The units and conventions of the game's positions are the ones the model infers from OmsiLaunch's operations; they have not yet been confirmed in a running game ([validation status](../status/validation.md)).

## Size, corner, opacity

| Setting | Effect |
| --- | --- |
| `radar_corner` | One of the four corners of the 1920 x 1080 canvas, 24 units from the edges (the bottom corners leave 44 more for the strip). |
| `radar_size_percent` | The radius is 160 canvas units x size / 100 x `ui_scale_percent` / 100, at least 40 and at most 520. At 100 % and scale 100 %, the circle is 320 units across, about 30 % of the height of a 1080-pixel view. |
| `radar_opacity_percent` | Multiplies the opacity of everything the radar draws (map, ring, arrow, strip). 100 draws it opaque. |
| `ui_scale_percent` | Scales the radar together with the pause menu. |

Everything is positioned on the canvas, so the radar keeps its place and proportion when the window or the resolution changes.

## When the radar shows nothing

The radar is empty (and the scene is cleared) in these cases. `status` reports which in `radar_state` ([operations and events](../reference/operations-and-events.md#status)).

| `radar_state` | Meaning |
| --- | --- |
| `off` | The `radar` setting is false, or `radar.toggle` hid it. |
| `disabled` | It was switched off after repeated errors (five in a row); `radar.toggle` turns it on again. |
| `no-frame` | The game could not be read at all in this tick. |
| `no player vehicle` | You are not in a vehicle (or the game has none). |
| `player unreadable` | The player's vehicle exists but its position could not be read. |
| `the player's tile is not in the map's tile table` | See [positions and headings](#positions-and-headings). |
| `on` | It is drawing. |

While the pause menu is open the radar is not drawn; the menu has the map ([the pause menu](the-pause-menu.md)).

## Without a realmap package

The radar still works. It shows the ring, your arrow, the vehicles around you, the speed and the clock, over a plain dark disc. Everything that comes from the map is missing: no water, no streets, no names, no icons. A map author can add the package at any time ([using a realmap package](../guides/using-a-realmap-package.md)).

## How often it is drawn

The radar is rebuilt at most ten times a second and only when something visible changed: your position to a third of a pixel at the current zoom (about a meter at 400 m), your heading to a degree, the vehicles around you, the shown speed, the minute of the clock, the zoom, or new map data. A rebuilt radar that comes out identical to the one on the screen is not encoded or sent either. Map data is read on a worker thread, a window at a time around you, and the previous window stays on the screen until the new one is ready ([performance](performance.md)).

Related: [realmap package](../reference/realmap-package.md) · [radar and interface settings](../guides/radar-and-interface-settings.md) · [operations and events](../reference/operations-and-events.md)
