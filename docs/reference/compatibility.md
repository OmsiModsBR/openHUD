# Compatibility and capabilities

This page says what openHUD runs on and what it needs from OmsiLaunch: the bridge protocol, the bridge features it requires, the capabilities it uses or can do without, the operations it runs, and what it does when one is unavailable. The capability system is OmsiLaunch's (its capability reference lists them per OMSI build); this page says what each means for openHUD.

Related pages: [installation](../getting-started/installation.md), [errors and degradation](errors-and-degradation.md), [the pause menu](../concepts/the-pause-menu.md), [coexisting](../guides/coexisting.md), [known limitations](known-limitations.md), [validation status](../status/validation.md).

## At a glance

| | |
| --- | --- |
| Module id, version | `openhud`, `0.1.0` |
| Bridge protocol | 3 (`min` 3, `max` 3) |
| Activation | `requested` |
| Failure policy | `degrade` |
| Tick | every 50 ms |
| Components | managed `OpenHud.dll` (entry `OpenHud.HudModule`, .NET 6, loadable by the 32-bit plugin), native `openhud-draw.dll` (x86) |
| Content input | `realmap-package`, not required |
| OMSI | The builds OmsiLaunch supports; what works on a given build is decided by its capabilities, below |
| Windows | The platforms OmsiLaunch supports |
| Languages | Interface in English, Portuguese (Brazil), German; map text in any OMSI language code |

## Bridge features

| Feature | Required | Used for |
| --- | :---: | --- |
| `bridge.events` | yes | The events and notices of the module |
| `bridge.runtime` | yes | Running catalog operations in process |
| `bridge.tick` | yes | The 50 ms tick |
| `bridge.hooks.d3d9` | yes | Hooking `Present` and `Reset` of the game's Direct3D 9 device (needs the native capability `d3d.texture`) |
| `bridge.input` | yes | Capturing the keyboard and mouse for the pause menu (needs the native capability `ui.input.capture`) |

A required feature the running OMSI build cannot give makes the module not load: the plan says `OL_E_MODULE_CAPABILITY_UNRESOLVED`. For a requested module that makes the plan not runnable; OmsiLaunch's own capability reference says which builds resolve which capabilities.

## Optional capabilities

| Capability | If available | If not |
| --- | --- | --- |
| `ui.pause-menu.replace` | The module can take over the native pause menu | The native menu stays; the radar works. `armed` says `pause_menu: off: ui.pause-menu.replace is not available in this build` |
| `camera.read` | The camera is read each tick (not drawn in 0.1.0) | The camera part of the snapshot is unavailable |
| `timetable.read` | The timetable counts are read every 20th tick (not drawn in 0.1.0) | That part is unavailable |
| `weather.read` | The weather is read every 20th tick (not drawn in 0.1.0) | That part is unavailable |

## Operations the module runs

Declared in the manifest (`runtime_operations`), run in process on OMSI's UI thread:

| Operation | Used for | If it answers an error |
| --- | --- | --- |
| `time.read` | The clock in the radar's strip; the date that selects the lines | The strip has no clock; all lines are listed |
| `map.read` | Which map is playing (and the package lookup) | No packages are loaded; the radar draws without a map |
| `map.tiles.list` | The tile table that places vehicles on the map's grid | The player's position is not resolved; the radar waits (`the player's tile is not in the map's tile table`) |
| `player-vehicle.read` | Where you are, your heading and your speed | No radar (`no player vehicle` / `player unreadable`) |
| `road-vehicles.list`, `road-vehicle.read` | The vehicles around you | No vehicles on the radar |
| `camera.read`, `timetable.read`, `weather.read` | Read for the snapshot | That part is unavailable |
| `ui.pause.state`, `ui.pause.take-over`, `ui.pause.restore-native`, `ui.pause.action` | The pause menu | The pause menu falls back; see [the pause menu](../concepts/the-pause-menu.md#when-something-fails) |

A typed answer (`OL_E_CAPABILITY_UNRESOLVED`, `OL_E_RUNTIME_OPERATION_UNAVAILABLE`) keeps its code and makes only that part unavailable. The module never retries in a loop: a part that cannot be read is reported once (`read-problem`) and tried again at its normal pace.

## Operations the module offers

`status`, `radar.zoom`, `radar.toggle` and `pause.state` ([operations and events](operations-and-events.md)).

## Other modules and plugins

No plugin is declared as a conflict. See [coexisting](../guides/coexisting.md) for overlays that hook the same Direct3D methods and for modules that capture input or replace the pause menu.

## Maps

Any map works. The radar's map layers and the pause menu's identity need the map's optional packages ([per-map packages](../concepts/per-map-packages.md)). The module learns the map from `map.read`; a map whose file name is not of the form `maps\<folder>\global.cfg` gets no packages. A saved situation, a new map and the last state are all treated the same once the game is running.

Related: [installation](../getting-started/installation.md) · [errors and degradation](errors-and-degradation.md) · [known limitations](known-limitations.md)
