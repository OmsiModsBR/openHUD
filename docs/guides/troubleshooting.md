# Troubleshooting

This page helps with the usual problems: the module does not load, the radar does not appear, the pause menu is OMSI's own, text shows wrong characters, the language is wrong, the game feels slow. For each it says how to find out what the module decided and what to change. The module explains itself through OmsiLaunch's runtime events, two read-only operations and the tray notices; the last section shows how to read them.

Related pages: [operations and events](../reference/operations-and-events.md), [errors and degradation](../reference/errors-and-degradation.md), [known limitations](../reference/known-limitations.md), [making a pause-menu package](making-a-pause-menu-package.md), [FAQ](../reference/faq.md).

## Reading the module's events and notices

While a session runs, from another console:

```text
OmsiLaunch.exe events read
OmsiLaunch.exe events watch
OmsiLaunch.exe /runtime:module.openhud.status
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

`events read` lists the recent runtime events; `events watch` prints each new one until you press Ctrl+C. The module's events are named `module.openhud.<name>`; the ones you will look for first:

| Event | Says |
| --- | --- |
| `armed` | The module started. Data: `omsi_version`, `draw` (`available` or `unavailable`) and `pause_menu` (`on`, or `off: <reason>`). |
| `settings-issue` | A setting could not be used and its default was taken: `radar_zoom=big (not one of near, medium, far)`. |
| `hooks` | The draw layer's hooks on the game's Direct3D device are installed (`present` and `reset` `installed`). Sent by the native component. |
| `gameplay-ready` | The world is running; ticks begin. |
| `packages` | The map's packages were read: status of each, fonts and pictures sent, and the first rejected entries. |
| `package-problem` | Loading the packages failed unexpectedly; the packages were ignored. |
| `read-problem` | A part of the game could not be read (`player: player-vehicle.read answered OL_E_...`). Each kind is said once. |
| `draw-failed`, `draw_failed` | The draw layer reported a failure and stopped drawing. |
| `pause-degraded` | The pause menu gave up for the session and handed OMSI's menu back. |
| `fault` | A part of the module threw an exception (it was contained). Data: the area and the type. |
| `notice` | A message for the user; OmsiLaunch's tray shows it as a balloon titled "openHUD". |
| `stopping` | The module is stopping. |

The tray notices are short, in your interface language:

| Notice | When |
| --- | --- |
| The draw layer is not available, so openHUD shows nothing. | The native component could not be used. |
| The radar was turned off after repeated errors. | Five failures in a row. `radar.toggle` turns it on again. |
| openHUD's pause menu is not available; OMSI's own menu stays. | The pause menu gave up. |
| A map package could not be used and was ignored. | A package is `Ignored`, or loading failed. |

Each notice is shown once per session.

## The module is not listed or not loaded

Run `OmsiLaunch.exe "C:\OMSI 2" /list:RuntimeModules`. If `openhud` is not there, or is marked invalid:

| Cause | What to check |
| --- | --- |
| Wrong location or name | The folder must be `<OMSI root>\plugins\OmsiLaunch.Modules\openhud\` and contain `module.json` ([installation](../getting-started/installation.md#installing)). |
| A file changed | `OmsiLaunch.exe /verify-module:"<folder>"` exits `7` and names the file whose pin does not match (`OL_E_MODULE_INTEGRITY`). Reinstall the folder. |
| Bridge protocol | The module needs protocol 3; an older OmsiLaunch answers `OL_E_MODULE_PROTOCOL_UNSUPPORTED`. |
| Capabilities | The module requires `bridge.events`, `bridge.runtime`, `bridge.tick`, `bridge.hooks.d3d9` and `bridge.input`. If this OMSI build cannot give one (for example input capture, native capability `ui.input.capture`, or the Direct3D capability `d3d.texture`), the plan says `OL_E_MODULE_CAPABILITY_UNRESOLVED` and the module is not loaded ([compatibility](../reference/compatibility.md)). |

If it is listed but a session does not use it: openHUD's activation is `requested`; add `/module:openhud` or `enabled=true` in `modules.ini` ([enabling the module](enabling-the-module.md)). `/plan` shows what OmsiLaunch decided.

## The radar does not appear

Ask the module:

```text
OmsiLaunch.exe /runtime:module.openhud.status
```

Read `radar_state`:

| `radar_state` | What to do |
| --- | --- |
| `off` | The setting `radar` is false, or the radar was hidden with `radar.toggle`. Set `radar=true` or run `OmsiLaunch.exe /runtime:module.openhud.radar.toggle`. |
| `disabled` | Five failures in a row switched it off; `status` has `last_fault` and `native_problem`. Run `radar.toggle` to try again; if it fails again, send the events. |
| `no player vehicle` | You are not in a vehicle. Get into one. |
| `player unreadable` | The game did not give the vehicle's position; look for a `read-problem` event. |
| `the player's tile is not in the map's tile table` | The module could not place your vehicle on the map. It retries about every second. If it persists, the map's tiles could not be read; look for `read-problem` events about `map.tiles.list` ([the radar](../concepts/the-radar.md#positions-and-headings)). |
| `no-frame` | The game could not be read at all. Look for `read-problem` events. |
| `idle` | The module is armed but not yet in the running game. |
| `on` | It is drawing. See below. |

If `radar_state` is `on` and you still see nothing:

- `status` says `draw: unavailable`: the draw layer could not be used (the native component is missing or failed). Reinstall the folder and read the `armed` event.
- No `hooks` event: the draw layer could not hook the Direct3D device. Another component may have hooked the same method of the device; read OmsiLaunch's hook events for the refusal ([coexisting](coexisting.md)).
- `native_failures` rises: the draw layer refuses the scenes; `native_problem` has the code.
- The radar is under another overlay, or in a corner you do not look at: change `radar_corner`, `radar_size_percent`, `radar_opacity_percent`.
- The map has no realmap package: the circle is empty apart from you and nearby vehicles. See `packages` in `status` ([per-map packages](../concepts/per-map-packages.md#what-happens-without-them)).

## The pause menu is the native one

```text
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

| `pause_menu` | `pause_reason` / state | Meaning and fix |
| --- | --- | --- |
| `off` | `turned off in the settings` | `pause_menu=false`. |
| `off` | `no draw layer` | The draw layer is not available; see above. |
| `off` | `ui.pause-menu.replace is not available in this build` | This OMSI build cannot hand over the pause menu. The native menu stays; nothing to fix on your side. |
| `off` | `input capture is not available` | OmsiLaunch could not capture the game's input on this build. |
| `degraded` | | The menu gave up for the session after repeated failures; see `pause_last_error` and the `pause-degraded` event (its `reason`). Restart the session. |
| `on` | `Provisional` | A call to OMSI failed: the native menu is shown with the map behind it. `pause_last_error` names the code (`OL_E_UI_BUSY`, `OL_E_CAPABILITY_UNRESOLVED` ...). The next pause starts afresh. |
| `on` | `Running` | The game is not paused, or the take-over is waiting (a native dialog is open, or OMSI is inside its frame). |

If `pause_unavailable` is `true`, OmsiLaunch answered that the capability cannot be resolved; the module stopped asking for the rest of the session.

## Text shows wrong characters

| You see | Cause and fix |
| --- | --- |
| The map's title or legend with `?` or boxes | The package's font lacks the character and so does the system UI font, or no font is registered. Check `packages` (`fonts`): `0` means the package's fonts were refused; the diagnostics say why (`fonts/FONTS-LICENSE.txt is missing or empty` ...). |
| Accented letters turned into two odd characters | A `.cfg` file was saved in a code page (Windows-1252) instead of UTF-8. A file that is not valid UTF-8 is refused as a whole: `Dropped: identity.cfg: not valid UTF-8`. Save it as UTF-8 ([pause-menu package](../reference/pause-menu-package.md#how-the-files-are-read)). |
| Emoji or rare symbols blank | Characters outside the Basic Multilingual Plane are drawn as a blank. |
| Arabic, Hebrew or Indic text in the wrong order or joined wrongly | Right-to-left and shaped scripts are not supported ([known limitations](../reference/known-limitations.md)). |
| The font is not the one in the package | `FONTS-LICENSE.txt` missing, a font file refused (variable font, over 4 MiB, a `.ttc`), or a role not listed in `fonts.cfg` (it takes the nearest weight). See `Dropped:` lines in the `packages` event. |

## The wrong language

Run `status` and read `language` and `language_code`.

- The interface is in English though your OMSI is in another language: `language` is `auto`, and `auto` follows OMSI's language only where the module can read it. Set it explicitly: `language=pt-BR` or `language=de` ([internationalization](../concepts/internationalization.md)).
- The map's title is not translated: the package has no `title.<LANG>` for your code. `language_code` is the code the package is asked for (`PTB`, `DEU`, ...). Add the key, or a `strings/<LANG>.cfg` ([making a pause-menu package](making-a-pause-menu-package.md#step-6-add-per-language-texts-optional)).
- A language the interface does not have (`es`, `fr`): the interface text is English; only the map's own text can be in that language.
- A `settings-issue` event with `language=...`: the value is not a code (letters, `-` or `_`, at most 8 characters).

## High CPU or a slow game

`status` has the module's own timings: `tick_last_ms`, `tick_max_ms`, `slow_ticks`.

1. If `slow_ticks` stays at 0 and `tick_max_ms` is a few milliseconds, the module is not the cause; test by starting the same session with `/no-module:openhud`.
2. If ticks are slow, find what they wait for: `frames_with_problems` high means failing reads; `uploads_pending` high means pictures still being sent (a one-time cost per map).
3. Reduce the work: `radar=false`, a smaller `radar_size_percent`, or `radar_zoom=near` ([performance](../concepts/performance.md#what-costs-the-most-and-what-to-do)).
4. A very large realmap package costs reading time on a worker thread when you move into new areas; it does not block the tick.

## Package problems

A package that does not look right: read the `packages` event; each rejected entry is a `Dropped:` or `Ignored:` line with the file, the line and the reason. Compare with [what the log says for each rejected entry](making-a-pause-menu-package.md#what-the-log-says-for-each-rejected-entry) and the [realmap mistakes](using-a-realmap-package.md#mistakes-the-reader-reports). The event holds the first twelve lines (700 characters); for a long list fix the first ones and restart.

When all else fails, collect `events read`, `status` and `pause.state` outputs and the session plan (`/plan --json`) before asking for help.

Related: [operations and events](../reference/operations-and-events.md) · [errors and degradation](../reference/errors-and-degradation.md) · [FAQ](../reference/faq.md)
