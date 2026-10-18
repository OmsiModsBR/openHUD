# Using it with predefined session profiles

An OmsiLaunch session profile is a YAML package that fixes a map, an entrypoint and up to 99 presets. A preset can request openHUD and carry its settings, so that each map or way of playing starts with the HUD that suits it: a small radar for commuting, a large one for sightseeing, only the pause menu, or no HUD at all. This page shows how to write them, how they combine with the command line and `modules.ini`, and which mistakes OmsiLaunch rejects. The profile format itself is in the OmsiLaunch session-profile reference (`docs\reference\session-profiles.md` of OmsiLaunch).

Related pages: [enabling the module](enabling-the-module.md), [radar and interface settings](radar-and-interface-settings.md), [settings reference](../reference/settings.md), [uninstalling](uninstalling.md).

## The `modules` block of a preset

A preset has an optional `modules` mapping, one entry per module id. For this module the entry is `openhud`, with two keys:

| Key | Meaning |
| --- | --- |
| `enabled` | `true` requests the module for the session; `false` keeps it out. Not set: the module is loaded only if the command line or `modules.ini` asks for it (its activation is `requested`). |
| `settings` | The module's settings by key, with the values of the [settings reference](../reference/settings.md). Checked against the manifest when the session is planned. |

```yaml
modules:
  openhud:
    enabled: true
    settings:
      radar_corner: top-right
      radar_zoom: far
```

Values may be YAML numbers, booleans or strings; OmsiLaunch converts them as for the command line and refuses a value outside the manifest's bounds when the session is planned (`OL_E_MODULE_SETTING_INVALID`). A setting key must match `[a-z][a-z0-9_]{0,31}` and the module id `[a-z0-9][a-z0-9-]{1,47}`, or the profile is invalid (`OL_E_SESSION_PROFILE_INVALID`). If the module is not installed, `enabled: true` is reported at planning as `OL_E_MODULE_NOT_FOUND`, and settings alone as `OL_W_MODULE_SETTING_IGNORED`.

## A complete example

`<OMSI root>\.omsilaunch\session-profiles\example-city-hud\profile.yaml`: four presets of the same map, each with a different HUD.

```yaml
schema: omsilaunch.session-profile/v1
id: example-city-hud
name: Example City with openHUD
author: You
version: "1.0"
compatibility:
  maps:
    - maps\Example City\global.cfg
new:
  map: maps\Example City\global.cfg
  entrypoint-index: 1
presets:
  - index: 1
    id: commuter
    name: Commuter (small radar)
    modules:
      openhud:
        enabled: true
        settings:
          radar_size_percent: 80
          radar_opacity_percent: 70
          radar_zoom: near
  - index: 2
    id: sightseeing
    name: Sightseeing (large radar, far zoom)
    modules:
      openhud:
        enabled: true
        settings:
          radar_corner: top-right
          radar_size_percent: 150
          radar_zoom: far
          language: pt-BR
  - index: 3
    id: pause-menu-only
    name: Pause menu only
    modules:
      openhud:
        enabled: true
        settings:
          radar: false
  - index: 4
    id: plain
    name: OMSI as it is
    modules:
      openhud:
        enabled: false
```

Start one with:

```text
OmsiLaunch.exe /predefined-profile:example-city-hud /predefined-profile-index:2 /new
```

The `id` must equal the folder name. The keys outside the `modules` block (`compatibility`, `new`, `settings`, ...) are defined by the OmsiLaunch reference; the example only shows where the block goes. In the example the map and the entrypoint are fixed by the profile, so the command line carries nothing else.

## How a preset combines with the rest

- **A key the preset sets cannot also be set on the command line.** `/predefined-profile-index:2` together with `/module-setting:openhud.radar_zoom=near` is `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`. Set the key in the preset or on the command line, not both. Keys the preset does not set can still be given on the command line.
- **`enabled` in a preset conflicts with the opposite flag.** A preset with `enabled: false` and `/module:openhud` is rejected; so is `enabled: true` with `/no-module:openhud`.
- **Preset values win over `modules.ini`**, which wins over the manifest default.

| Preset sets | Command line | `modules.ini` | Result for `radar_zoom` |
| --- | --- | --- | --- |
| `far` | (nothing about it) | `near` | `far` |
| (nothing) | `/module-setting:openhud.radar_zoom=far` | `near` | `far` |
| `far` | `/module-setting:openhud.radar_zoom=near` | | rejected: `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT` |

## Keeping the module out of a preset

A preset that should always be plain can say so:

```yaml
    modules:
      openhud:
        enabled: false
```

This keeps the module out even when `modules.ini` has `enabled=true`.

## Listing and checking

```text
OmsiLaunch.exe "C:\OMSI 2" /list:SessionProfiles
OmsiLaunch.exe /predefined-profile:example-city-hud /predefined-profile-index:1 /new /plan
```

The second line builds the plan of the first preset without starting OMSI.

Related: [enabling the module](enabling-the-module.md) · [radar and interface settings](radar-and-interface-settings.md) · [settings reference](../reference/settings.md)
