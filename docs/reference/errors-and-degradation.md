# Errors and degradation

openHUD is built to fail small: its failure policy is `degrade`, and each part keeps working when another cannot. This page lists what can go wrong, what shows it (an OmsiLaunch code, an event or a log line), what the module does about it and what you can do. The codes are OmsiLaunch's; the page says what they mean for this module.

Related pages: [operations and events](operations-and-events.md), [troubleshooting](../guides/troubleshooting.md), [the pause menu](../concepts/the-pause-menu.md#when-something-fails), [compatibility](compatibility.md), [safety](../concepts/safety.md).

## The policy

Under `degrade`, a failure leaves the module in the session: the failing call returns its code, the bridge reports `module.degraded` once per code, and the module stays active. The module adds its own containment: every callback is guarded, so nothing escapes to the bridge, and what keeps failing is switched off, never retried in a loop. The session never fails because of openHUD.

## At planning (OmsiLaunch)

| Situation | Code | What happens | What to do |
| --- | --- | --- | --- |
| A file of the module folder changed, is missing or is reached through a link | `OL_E_MODULE_INTEGRITY` | Not loaded; with `/module:openhud` the plan is not runnable | Reinstall ([installation](../getting-started/installation.md)) |
| `module.json` missing or invalid, or the folder is not named `openhud` | `OL_E_MODULE_MANIFEST_INVALID`, `OL_E_MODULE_NOT_FOUND` | Not loaded | Reinstall |
| OmsiLaunch does not serve bridge protocol 3 | `OL_E_MODULE_PROTOCOL_UNSUPPORTED` | Not loaded | Use a newer OmsiLaunch |
| The OMSI build cannot give a required feature (`bridge.hooks.d3d9`, `bridge.input` ...) | `OL_E_MODULE_CAPABILITY_UNRESOLVED` | Not loaded | See [compatibility](compatibility.md) |
| A setting value is invalid or the key does not exist | `OL_E_MODULE_SETTING_INVALID` | The plan is not runnable | Correct the value |
| A `modules.ini` entry cannot be used | `OL_W_MODULE_SETTING_IGNORED` | The default is used | Correct the line |
| A preset and the command line set the same key | `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT` | Not runnable | Set it in one place |

## Inside OMSI

| Situation | What the module does | Shown by |
| --- | --- | --- |
| The native component cannot be used | The module runs without drawing; nothing is shown | Notice "The draw layer is not available, so openHUD shows nothing."; `armed` `draw: unavailable` |
| The Direct3D hooks cannot be installed | Same: no scenes reach the screen | No `hooks` event; OmsiLaunch's hook refusal event |
| A setting is unusable | The default is used | `settings-issue` |
| A part of the game cannot be read | That part of the snapshot is unavailable; the others are unaffected | `read-problem` (once per kind) |
| The player's vehicle or tile is unknown | The radar waits | `radar_state` in `status` |
| A scene is refused by the draw layer | The previous scene stays; five refusals in a row switch the radar off | `scene_failures`; notice "The radar was turned off after repeated errors." |
| The draw layer fails while drawing | It stops drawing; the module retries twice, then switches the radar off and gives the native pause menu back | `draw-failed`, `draw_failed`; notice |
| A fault in the radar | Counted and said once; five in a row switch the radar off | `fault`; notice |
| A pause-menu call fails | Everything is given back; the native menu is shown with only the map behind it | `pause_state` `Provisional`, `pause_last_error` |
| The pause menu keeps failing | It gives up for the session | `pause-degraded`; notice "openHUD's pause menu is not available; OMSI's own menu stays." |
| `OL_E_CAPABILITY_UNRESOLVED` from a `ui.pause.*` call | The module stops asking | `pause_unavailable` `true` |
| A package cannot be used | The package is ignored; the generic behavior applies | `packages` diagnostics; notice "A map package could not be used and was ignored." |
| A bad entry in a package | The entry is dropped | `Dropped:` line in `packages` |
| Loading packages throws | The packages are ignored | `package-problem` |
| A tick throws, or returns late | Reported by the bridge once per code; skipped ticks are not queued | `module.degraded` |
| The module stops | Everything is released ([safety](../concepts/safety.md#stopping)) | `stopping` |

## Package log lines

A package problem is a line `Severity: entry: reason`. `Dropped` is one entry left out, `Ignored` a whole package or feature not used, `Info` a note (such as `N more entries not listed` after 200 lines). Every reason is listed with its file in the [pause-menu package](pause-menu-package.md) and [realmap package](realmap-package.md) references.

## Codes of the pause operations

| Code | Meaning for openHUD |
| --- | --- |
| `OL_E_UI_NOT_PAUSED` | The game is not paused: nothing to take over; back to Running |
| `OL_E_UI_BUSY` | A native dialog is open, or OMSI is inside its frame; take-over retried for one second |
| `OL_E_UI_ALREADY_TAKEN_OVER` | Something holds the take-over; the module hands it back |
| `OL_E_CAPABILITY_UNRESOLVED` | This build cannot do it; latched |
| `OL_E_RUNTIME_OPERATION_FAILED` | The operation failed; failure cleanup |

Related: [troubleshooting](../guides/troubleshooting.md) · [operations and events](operations-and-events.md) · [compatibility](compatibility.md)
