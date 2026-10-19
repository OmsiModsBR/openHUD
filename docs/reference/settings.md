# Settings reference

openHUD declares 9 settings in its manifest. This page lists every key with its type, default and bounds, then describes what each does and shows an example. The summary table is checked against `module.template.json` (the source of the packaged `module.json`) by the test suite and by `tools\Check-Docs.ps1`, and the behavior described was checked against `src/OpenHud.Core/Settings/HudSettings.cs` and the module (`src/OpenHud.Module/HudModule.cs`).

Related pages: [radar and interface settings](../guides/radar-and-interface-settings.md), [enabling the module](../guides/enabling-the-module.md), [session profiles](../guides/session-profiles.md), [the radar](../concepts/the-radar.md), [internationalization](../concepts/internationalization.md), the OmsiLaunch Runtime Modules reference (settings, the settings file, precedence).

## How settings reach the module

The module has no settings file of its own and declares no command-line flags. Settings reach it through OmsiLaunch, which validates them against the manifest and refuses a value outside its bounds. The effective value of a setting is, in order:

1. the request: `/module-setting:openhud.<key>=<value>`, `LaunchSpec.Modules.Settings` (`"openhud.<key>": "<value>"`), or `modules.openhud.settings` of the selected session-profile preset (a key the preset sets cannot also be set on the command line: `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`);
2. the `[openhud]` section of `<OMSI root>\.omsilaunch\modules.ini` (`key=value`; comment lines start with `;` or `#`);
3. the manifest default.

OmsiLaunch normalizes values: booleans accept `true`/`false`, `yes`/`no`, `on`/`off`, `1`/`0`; choices are matched without regard to case; integers are checked against their bounds; text has no control characters and a length limit. An invalid requested value, or a key the module does not declare, makes the plan not runnable (`OL_E_MODULE_SETTING_INVALID`); an unusable entry of `modules.ini` is ignored with `OL_W_MODULE_SETTING_IGNORED`. `/list:RuntimeModules` shows each setting with its type and default.

The module checks the values a second time with the same rules. Parsing never throws: a missing key is the default, and a value it cannot use is the default plus a `settings-issue` event. `enabled` is not a setting of the module: OmsiLaunch switches modules on and off the same way for all of them ([enabling the module](../guides/enabling-the-module.md)).

## Summary

| Key | Type | Default | Range or choices |
| --- | --- | --- | --- |
| `radar` | boolean | `true` | `true` or `false` |
| `pause_menu` | boolean | `true` | `true` or `false` |
| `ui_scale_percent` | integer | `100` | 50 to 200 |
| `language` | text | `auto` | up to 8 characters |
| `units` | choice | `metric` | `metric`, `imperial` |
| `radar_zoom` | choice | `medium` | `near`, `medium`, `far` |
| `radar_corner` | choice | `bottom-left` | `top-left`, `top-right`, `bottom-left`, `bottom-right` |
| `radar_size_percent` | integer | `100` | 50 to 200 |
| `radar_opacity_percent` | integer | `90` | 20 to 100 |

In the examples the command-line form is for one session; the same key and value work in `modules.ini` (`key=value` under `[openhud]`) and in a preset (`modules.openhud.settings`). Add `/module:openhud` to request the module when nothing else does.

## The radar

### `radar`

Draws the round radar over the game view. When `false` the radar is never drawn and `radar.toggle` cannot show it; the pause menu is independent. `status` reports `radar_setting` and `radar_state` (`off`).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar=false
```

### `radar_zoom`

The distance the radar shows at the start: `near` is a radius of 200 m, `medium` 400 m, `far` 800 m. The operation `radar.zoom` moves through the six steps 100, 200, 400, 800, 1600 and 3200 m during the session. The level of detail of the map follows the radius.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_zoom=near
```

### `radar_corner`

The corner of the screen where the radar sits, 24 canvas units from the edges (the bottom corners leave room for the clock strip).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_corner=bottom-right
```

### `radar_size_percent`

The size of the radar: its radius is 160 canvas units x `radar_size_percent` / 100 x `ui_scale_percent` / 100, at least 40 and at most 520.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_size_percent=120
```

### `radar_opacity_percent`

The opacity of everything the radar draws, 20 to 100 percent. Applied to the radar only, not to the pause menu.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_opacity_percent=75
```

## The interface

### `pause_menu`

Replaces OMSI's pause menu with openHUD's where the build allows it. Even with `true`, the native menu stays when the draw layer, the capability `ui.pause-menu.replace` or the input capture is not available ([the pause menu](../concepts/the-pause-menu.md#when-the-native-menu-stays)); `false` keeps it in every case.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.pause_menu=false
```

### `ui_scale_percent`

Scale of the interface: the radar's radius and the pause menu. The menu is never drawn smaller than its normal size and grows as far as the window allows, so values under 100 affect the radar only. Above 150 the menu uses the `@2x` version of a map's symbol when there is one.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.ui_scale_percent=125
```

### `language`

The interface language. `auto` (or an empty value) is the default; otherwise a code of letters, `-` or `_`, at most 8 characters (`en`, `ENG`, `pt-BR`, `de`), stored in upper case. English, Portuguese (Brazil) and German exist; other codes use English for the interface and still select the language of a map package's texts ([internationalization](../concepts/internationalization.md)). A value with other characters is a `settings-issue` and `auto` is used.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.language=de
```

### `units`

`metric` or `imperial`, for speed and distance on the HUD. Metric shows the speed in km/h and the scales in meters and kilometers; imperial shows mph, feet and miles (the radar's scale reads 330 ft, 660 ft, 0.25 mi, 0.5 mi, 1 mi, 2 mi, and the map page's scale bar uses feet and miles). The speed comes from the vehicle's `ground_speed`, read as metres per second ([known limitations](known-limitations.md)). The effective value is reported by `status`.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.units=imperial
```

## A file with every setting

```ini
[openhud]
radar=true
pause_menu=true
ui_scale_percent=100
language=auto
units=metric
radar_zoom=medium
radar_corner=bottom-left
radar_size_percent=100
radar_opacity_percent=90
```

These are the defaults; writing them changes nothing.

Related: [radar and interface settings](../guides/radar-and-interface-settings.md) · [enabling the module](../guides/enabling-the-module.md) · [operations and events](operations-and-events.md)
