# Radar and interface settings, with an example each

This guide goes through the nine settings of openHUD, one by one, with a worked example for each: what to write, where, and what you see. Each example works as a command-line flag for one session, as a line in `modules.ini` for every session and as an entry in a session-profile preset ([enabling the module](enabling-the-module.md), [session profiles](session-profiles.md)). The complete table with types, bounds and defaults is the [settings reference](../reference/settings.md).

Related pages: [the radar](../concepts/the-radar.md), [the pause menu](../concepts/the-pause-menu.md), [internationalization](../concepts/internationalization.md), [troubleshooting](troubleshooting.md).

The examples start the invented map `maps\Example City\global.cfg`; the part that matters is the `/module-setting:` flag. Add `/module:openhud` to request the module when it is not already requested.

## Radar on and off

`radar` (default `true`) draws the round radar. Turn it off if you only want the pause menu:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar=false
```

You see no radar; the pause menu is unaffected. While you play you can also hide and show the radar without restarting with the module's `radar.toggle` operation ([operations and events](../reference/operations-and-events.md#radartoggle)), but it cannot show a radar that the setting turned off.

## Radar size

`radar_size_percent` (50 to 200, default 100) scales the radar. At 100 the circle has a radius of 160 units on the 1920 x 1080 canvas (about 30 % of the screen height across). At 150 the radius is 240; at 50 it is 80:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_size_percent=150
```

The size also follows `ui_scale_percent`, and the radius never goes below 40 or above 520 units. A larger radar shows the same distance (the zoom decides that) on more pixels, so streets and names are easier to read.

## Radar corner

`radar_corner` (`top-left`, `top-right`, `bottom-left`, `bottom-right`, default `bottom-left`) chooses where the radar sits:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_corner=top-right
```

The radar keeps 24 units from the edges; at the bottom corners it also leaves room for the clock strip. Pick the corner that does not cover your instruments or your map overlay.

## Radar opacity

`radar_opacity_percent` (20 to 100, default 90) makes the radar more or less transparent. Everything it draws is multiplied by this: map, ring, arrow and strip.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_opacity_percent=60
```

At 60 the road ahead shows through the radar. At 100 it is opaque. The value cannot go below 20, so the radar never disappears by accident.

## Radar zoom

`radar_zoom` (`near`, `medium`, `far`, default `medium`) is the distance the radar shows when the session starts:

| Value | Radius of the circle |
| --- | ---: |
| `near` | 200 m |
| `medium` | 400 m |
| `far` | 800 m |

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_zoom=far
```

The scale at the bottom edge of the circle says the radius. During the session `radar.zoom` moves through six steps from 100 m to 3.2 km:

```text
OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=out
```

## Units

`units` (`metric` or `imperial`, default `metric`) is the system of units for speed and distance on the HUD:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.units=imperial
```

In version 0.1.0 nothing the module draws is a speed or a distance in a unit that depends on this setting: the radar's scale label is in meters and kilometers, and the strip shows the clock. The value is accepted, validated and reported by `status` (`units`), and the texts for imperial units already exist (`UI_UNIT_MPH`, `UI_UNIT_FT`, `UI_UNIT_MI`), so a profile can set it now and it takes effect when the HUD shows speed ([known limitations](../reference/known-limitations.md)).

## Language

`language` (up to 8 characters, default `auto`) chooses the interface language. A code like `en`, `ENG`, `pt-BR` or `de` selects it; `auto` follows OMSI's language where the module can read it ([internationalization](../concepts/internationalization.md)).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.language=pt-BR
```

You see the pause menu and the radar's labels in Brazilian Portuguese. In `modules.ini`:

```ini
[openhud]
language=de
```

A value that is not a code (a number, a word with spaces) is reported as a settings issue (event `module.openhud.settings-issue`) and `auto` is used. A code the interface has no translation for (`es`) uses English for the interface, while the map's own title and legend still use the Spanish variants of its package.

## UI scale

`ui_scale_percent` (50 to 200, default 100) scales openHUD's interface: the radar's radius and the pause menu.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.ui_scale_percent=130
```

For the radar, 130 makes the circle 30 % larger on top of `radar_size_percent`. For the pause menu, the menu grows up to 130 % of its normal size as far as the window allows (it always keeps a margin) and is never smaller than normal: values under 100 shrink the radar only. Above 150 the menu uses the `@2x` version of the map's symbol when the package has one.

## The pause menu

`pause_menu` (default `true`) lets the module replace OMSI's native pause menu where the build allows it. Set it to `false` to keep the native menu in every case:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.pause_menu=false
```

The radar still works. `module.openhud.armed` then says `pause_menu: off: turned off in the settings`.

## A combination, in a file

For a player who wants a bigger, fainter radar in the top-right corner, in Portuguese, with the pause menu on:

```ini
[openhud]
enabled=true
radar_corner=top-right
radar_size_percent=140
radar_opacity_percent=70
radar_zoom=medium
language=pt-BR
ui_scale_percent=110
pause_menu=true
```

Related: [settings reference](../reference/settings.md) · [enabling the module](enabling-the-module.md) · [session profiles](session-profiles.md) · [the radar](../concepts/the-radar.md)
