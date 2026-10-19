# First session

This page walks through a first session with openHUD, step by step, with the exact command lines: starting OMSI with the module, what the radar should look like, how to open the pause menu and use it, the keys, and how to read the module's own events to know what it decided. It assumes the module is installed ([installation](installation.md)). Nothing here needs a map package; what changes with one is said at each step.

Related pages: [overview](overview.md), [enabling the module](../guides/enabling-the-module.md), [controls](../reference/controls.md), [troubleshooting](../guides/troubleshooting.md), [operations and events](../reference/operations-and-events.md).

The examples use an invented map folder, `maps\Example City\global.cfg`. Use one of your own; `OmsiLaunch.exe /list:Maps` lists them.

## Step 1. Plan the session

A plan builds the session without starting OMSI. Run it with the module requested:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /plan
```

You want `Plan: READY`. If the plan is not runnable, the message names the code: `OL_E_MODULE_NOT_FOUND` (the folder is not where OmsiLaunch looks), `OL_E_MODULE_INTEGRITY` (a file of the module changed), `OL_E_MODULE_CAPABILITY_UNRESOLVED` (this OMSI build cannot give the module something it requires), `OL_E_MODULE_PROTOCOL_UNSUPPORTED` (OmsiLaunch is too old). See [troubleshooting](../guides/troubleshooting.md#the-module-is-not-listed-or-not-loaded).

## Step 2. Start the session

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud
```

OmsiLaunch starts OMSI with the world you chose. The module is armed before the world starts and becomes active when the game is ready to play. The module has no window and no splash: until the game is playing you see only OMSI.

To start with different settings for this one session, add `/module-setting` flags; for example a larger radar in the top-right corner:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_corner=top-right /module-setting:openhud.radar_size_percent=130
```

## Step 3. The radar

Once you are in the driver's seat the radar appears in its corner (bottom-left by default), 90 % opaque. What you should see:

- a round map centered on your vehicle, with your vehicle in the middle as an arrow;
- the map turns as you turn: your heading is up. A small red mark and the letter on the ring show where north is;
- the other vehicles around you as small colored shapes (up to 48);
- the scale of the circle at its bottom edge (for the default zoom, `400 m`: the radius of the circle);
- your speed and the game clock in a strip under the radar;
- with a realmap package: water, parks, built-up areas, rail, relief, streets and municipality names, and the package's icons ([the radar](../concepts/the-radar.md)). Without one, the circle is empty apart from your vehicle and the vehicles around you.

The radar draws only when the game knows where you are: you need a player vehicle, and the tile you are on must be in the map's tile table. If you are in a free camera away from any vehicle, or the map has just changed, the radar waits and draws nothing. That is normal; the reason is in `status` (step 6).

## Step 4. The pause menu

Pause OMSI the way you always do. If the take-over works on your build, you see openHUD's menu over a dimmed picture of the game instead of OMSI's native menu:

- the header shows the map's title (the generic menu shows the map folder's name, here `Example City`) and its symbol if the map ships a package;
- three tabs: **Menu**, **Map**, **Lines**;
- on the Menu page five buttons: back to the game, new game, save game, options, quit OMSI; and, on the right, the legend of the map's markers.

If you see OMSI's native menu instead, openHUD did not take it over; the radar is unaffected. Which of the conditions failed is in `pause.state` (step 6) and in [the pause menu](../concepts/the-pause-menu.md#when-the-native-menu-stays).

## Step 5. The keys

The pause menu is controlled by the keyboard and the mouse; the radar has no keys. The most used:

| Key or mouse | Does |
| --- | --- |
| Esc | Back to the game (from any page) |
| Tab / Shift+Tab | Next / previous page |
| Up, Down | Move between the buttons (Menu page); pan the map (Map page); select a line (Lines page) |
| Enter, Space | Press the focused button |
| Wheel | Zoom (Map page); scroll (Lines page) |
| Left drag | Pan the map |
| Double-click | Center the map on you |

The complete table is in [controls](../reference/controls.md). Buttons that open an OMSI dialog (new game, save game, options, quit OMSI) give the keyboard back to OMSI first; the module draws nothing while the dialog is in front and takes the keyboard again when you return to the paused game.

## Step 6. Ask the module what it did

The module reports through OmsiLaunch's runtime events and exposes four operations. While the session runs, from another console:

```text
OmsiLaunch.exe events read
OmsiLaunch.exe /runtime:module.openhud.status
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

`events read` lists the runtime events, among them `module.openhud.armed` (with `draw` and `pause_menu` saying `available` or `unavailable`, `on` or `off: <reason>`), `module.openhud.packages` (which packages were found for the map) and `module.openhud.gameplay-ready`. `status` returns the module's state: whether the radar is on, its zoom, the packages, counters and the timing of its tick. `pause.state` returns the pause menu's state and the last error it handled. Both are described in [operations and events](../reference/operations-and-events.md).

You can also change the radar while you play:

```text
OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=in
OmsiLaunch.exe /runtime:module.openhud.radar.toggle
```

## Step 7. End the session

Quit OMSI normally. The module stops with the session: it stops drawing, gives back the keyboard and the background picture and, if it still held OMSI's pause menu, hands it back ([safety](../concepts/safety.md#stopping)).

## If something does not look right

| You see | Read |
| --- | --- |
| No radar | [The radar does not appear](../guides/troubleshooting.md#the-radar-does-not-appear) |
| OMSI's native pause menu | [The pause menu is the native one](../guides/troubleshooting.md#the-pause-menu-is-the-native-one) |
| Strange characters in the text | [Text shows wrong characters](../guides/troubleshooting.md#text-shows-wrong-characters) |
| The wrong language | [The wrong language](../guides/troubleshooting.md#the-wrong-language) |

Related: [overview](overview.md) · [controls](../reference/controls.md) · [enabling the module](../guides/enabling-the-module.md)
