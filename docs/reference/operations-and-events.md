# Operations and events

openHUD exposes four operations while a session runs and reports through runtime events and tray notices. This page lists each with its arguments, results and errors, with example calls, and every event with its data. Operations are reached with `/runtime:module.openhud.<operation>` and `/runtime-arg:<name>=<value>`; events are read with `OmsiLaunch.exe events read` or `events watch`. The rules of operations, events and notices are those of the OmsiLaunch Runtime Modules reference.

Related pages: [first session](../getting-started/first-session.md), [troubleshooting](../guides/troubleshooting.md), [errors and degradation](errors-and-degradation.md), [performance](../concepts/performance.md).

## Operations

The operations exist only in a session that loaded the module; otherwise OmsiLaunch answers `OL_E_MODULE_NOT_LOADED` or `OL_E_MODULE_OPERATION_UNKNOWN`. Undeclared arguments are refused (`OL_E_RUNTIME_ARGUMENT_REQUIRED`). An operation the module does not know answers `OL_E_MODULE_OPERATION_UNKNOWN`. After the module stopped or failed every call answers `OL_E_MODULE_DISABLED`.

| Operation | Kind | Arguments | Result |
| --- | --- | --- | --- |
| `status` | read | none | the module's state (below) |
| `radar.zoom` | action | `direction` (required): `in` or `out` | `radar_zoom_step`, `radar_radius_m` |
| `radar.toggle` | action | `visible` (optional): `true`, `false`, `1`, `0` | `radar_visible`, `radar_state` |
| `pause.state` | read | none | the pause menu's state (below) |

### `status`

```text
OmsiLaunch.exe /runtime:module.openhud.status
```

| Key | Meaning |
| --- | --- |
| `module_state` | `new`, `armed`, `running` or `stopped` |
| `draw` | `available` or `unavailable` (the native draw layer) |
| `radar_setting`, `radar_visible`, `radar_state` | the `radar` setting; whether `radar.toggle` left it visible; why it draws or not ([the radar](../concepts/the-radar.md#when-the-radar-shows-nothing)) |
| `radar_zoom_step`, `radar_radius_m` | the current zoom (0 to 5) and its radius in meters |
| `radar_corner`, `units`, `language`, `language_code`, `ui_scale_percent` | the effective settings as the module uses them |
| `map` | the map identity the module is loading packages for (`maps\<folder>\global.cfg`), empty before a map is seen |
| `packages` | `none`, `loading` or `loaded` |
| `realmap_status`, `pausemenu_status` | `Absent`, `Ignored` or `Loaded` |
| `radar_features`, `fullmap_features` | features in the newest radar window and map window |
| `fonts_registered`, `images_registered`, `uploads_pending`, `uploads_failed` | what was sent to the draw layer |
| `pause_menu`, `pause_state` | `off`, `on` or `degraded`; the state machine's state |
| `map_queries`, `map_builds` | windows of map data asked for from the worker; pause-menu maps built (a hovered menu does not rebuild its map) |
| `scenes_published`, `scenes_skipped`, `scene_failures` | scenes sent, skipped as unchanged, refused |
| `ticks`, `tick_last_ms`, `tick_max_ms`, `slow_ticks` | tick count and timings ([performance](../concepts/performance.md)) |
| `frames_with_problems`, `faults`, `last_fault` | game reads that failed in a tick; contained exceptions and the last one (`area: Type`) |
| `native_calls`, `native_failures`, `native_problem` | calls to the draw layer, failures and the last problem |
| `text_measure` | `native` (text widths from the draw layer) or `approximate` |
| `view` | size in pixels of the game's view as the draw layer reports it (`1920x1080`; `0x0` before the first frame) |
| `notices` | the notices shown so far (comma-separated keys) |

### `pause.state`

```text
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

| Key | Meaning |
| --- | --- |
| `pause_menu` | `off`, `on` or `degraded` |
| `pause_reason` | why it is off (`turned off in the settings`, `no draw layer`, `ui.pause-menu.replace is not available in this build`, `input capture is not available`), empty otherwise |
| `pause_state` | `Running`, `TakeOverPending`, `Open`, `Resuming`, `Modal`, `Ending`, `Provisional`, `Stopped`, or empty when off |
| `pause_last_error` | the last failure handled: a catalog code (`OL_E_UI_BUSY` ...) with detail, or empty |
| `pause_cleanup_complete` | `true` when nothing is held (capture, background, hidden native menu) |
| `pause_unavailable` | `true` after `OL_E_CAPABILITY_UNRESOLVED` (the module stopped asking) |
| `key_release_timeouts` | how many resumes did not wait for a stuck key |

### `radar.zoom`

Moves the radar one step. The six steps are 100, 200, 400, 800, 1600 and 3200 m; the call at an end changes nothing and answers the current step.

```text
OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=in
```

Errors: `OL_E_RUNTIME_ARGUMENT_REQUIRED` (no `direction`), `OL_E_RUNTIME_VALUE_INVALID` (not `in` or `out`).

### `radar.toggle`

Shows or hides the radar for the session; without `visible` it flips. Showing it also clears an automatic switch-off after repeated errors. It cannot show a radar that the `radar` setting turned off.

```text
OmsiLaunch.exe /runtime:module.openhud.radar.toggle /runtime-arg:visible=false
```

Error: `OL_E_RUNTIME_VALUE_INVALID` (`visible` not a boolean).

## Events

Events are named `module.openhud.<name>`, with a JSON object of strings as data.

| Name | When | Data |
| --- | --- | --- |
| `armed` | The module is armed, before the world starts | `omsi_version`, `draw`, `pause_menu` |
| `settings-issue` | A setting was unusable | `issues`: `key=value (reason); ...` |
| `hooks` | The draw layer installed its Direct3D hooks (native component) | `present`, `reset` (`installed`) |
| `gameplay-ready` | The world is running | none |
| `packages` | A map's packages were loaded | `map`, `realmap`, `pausemenu` (status), `fonts`, `images`, `vector_symbols` (SVG drawings from the pause-menu package), `diagnostics` (first lines, the pause-menu package's first, at most 700 characters) |
| `package-problem` | Loading the packages failed | `detail` |
| `read-problem` | A part of the game could not be read (each kind once, at most 12) | `problem` |
| `draw-failed` | The draw layer reported a failure | `detail` |
| `draw_failed` | Same, said by the native component when it fails | `code`, `detail` |
| `pause-degraded` | The pause menu gave up for the session | `reason`, `state` |
| `fault` | A contained exception (once per area) | `area`, `type`, `message` |
| `notice` | A message for the user | `title`, `text` |
| `stopping` | The module is stopping | none |

### Notices

A `notice` event is shown by OmsiLaunch's tray as a balloon titled "openHUD". Its text is in the interface language and each notice appears once per session: the draw layer is not available; the radar was turned off after repeated errors; the pause menu is not available and OMSI's own stays; a map package could not be used and was ignored ([troubleshooting](../guides/troubleshooting.md#reading-the-modules-events-and-notices)).

## What the module reads from OmsiLaunch

The module declares these catalog operations (`runtime_operations`) and runs them in process: `time.read`, `map.read`, `map.tiles.list`, `camera.read`, `player-vehicle.read`, `road-vehicles.list`, `road-vehicle.read`, `timetable.read`, `weather.read`, `ui.pause.state`, `ui.pause.take-over`, `ui.pause.restore-native` and `ui.pause.action`. The plan lists them, and the pause ones carry their mutation policy.

Related: [troubleshooting](../guides/troubleshooting.md) · [errors and degradation](errors-and-degradation.md) · [compatibility](compatibility.md)
