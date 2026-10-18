# Enabling the module, per session and permanently

openHUD's activation is `requested`: installing it does not turn it on. A session loads it only when something asks for it. This page shows the ways to ask, for one session, for every session and from a session profile, how to keep it out of a session, in what order OmsiLaunch combines the requests, and how to check what a session would do before starting OMSI. The rules are OmsiLaunch's; they are described in the OmsiLaunch Runtime Modules reference (`docs\reference\runtime-modules.md`, offline at `<OMSI root>\.omsilaunch\docs\reference\runtime-modules.md`) and repeated here only as far as openHUD needs.

Related pages: [installation](../getting-started/installation.md), [radar and interface settings](radar-and-interface-settings.md), [session profiles](session-profiles.md), [uninstalling](uninstalling.md), [settings reference](../reference/settings.md).

## For one session

Add `/module:openhud` to the command line:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud
```

Add settings for this session with `/module-setting:openhud.<key>=<value>`, as many as you like:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_zoom=far /module-setting:openhud.language=pt-BR
```

openHUD declares no flags of its own; `/module-setting` is the way. A value outside the module's bounds, or a key it does not declare, makes the plan not runnable with `OL_E_MODULE_SETTING_INVALID`. Quote the argument if it contains a character your shell treats specially.

## For every session

Create or edit `<OMSI root>\.omsilaunch\modules.ini`. A section per module id; `enabled=true` loads the module in every session, and `key=value` lines set its settings:

```ini
; one section per module id
[openhud]
enabled=true
radar_corner=top-right
radar_size_percent=120
language=auto
```

Lines that start with `;` or `#` are comments, and a comment cannot follow a value. OmsiLaunch only reads this file. An entry it cannot use is ignored with the warning `OL_W_MODULE_SETTING_IGNORED`.

With `enabled=true` in the file the module's activation is reported as `settings-file`. If the module then cannot be loaded (a changed file, a missing capability), the session still starts and the plan carries the warning `OL_W_MODULE_NOT_LOADED`; when you *request* it on the command line, a module that cannot be loaded makes the plan not runnable instead.

To keep settings without turning the module on for everyone, leave `enabled` out and write only the settings: they apply whenever a session loads the module by another route.

## From a session profile

A preset of an OmsiLaunch session profile can request the module and carry its settings, so that each map or way of playing starts with its own HUD. See [session profiles](session-profiles.md).

## Keeping it out of a session

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /no-module:openhud
```

`/no-module:openhud` keeps the module out of this session even if `modules.ini` or a preset asks for it. Enabling and disabling it in the same command is `OL_E_MODULE_SETTING_INVALID`. A preset with `enabled: true` or `false` conflicts with the opposite flag (`OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`).

## What wins

For whether the module is loaded: an explicit request on the command line or in a preset (`/module`, `/no-module`, `enabled:` in a preset) wins over the settings file. For a setting's value, in order:

1. the request: `/module-setting:openhud.<key>=<value>` or `modules.openhud.settings` of the selected preset (a key the preset sets cannot also be set on the command line: `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`);
2. the `[openhud]` section of `modules.ini`;
3. the manifest default.

| Request | `modules.ini` | Default | Effective `radar_zoom` |
| --- | --- | --- | --- |
| `/module-setting:openhud.radar_zoom=far` | `radar_zoom=near` | `medium` | `far` |
| (none) | `radar_zoom=near` | `medium` | `near` |
| (none) | (none) | `medium` | `medium` |

## Checking before you start

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /plan --json
```

The plan lists the planned modules. For openHUD you see its activation (`requested`, `settings-file`), its nine settings with the effective value and its source (`request`, `settings-file` or `default`), the operations it declared, and any warning. `/list:RuntimeModules` lists what is installed:

```text
OmsiLaunch.exe "C:\OMSI 2" /list:RuntimeModules
```

## Common messages

| Message | Meaning | Do |
| --- | --- | --- |
| `OL_E_MODULE_NOT_FOUND` | No valid `openhud` folder was found | [Installation](../getting-started/installation.md) |
| `OL_E_MODULE_INTEGRITY` | A file of the module changed or is reached through a link | Reinstall the folder |
| `OL_E_MODULE_SETTING_INVALID` | A requested value is outside the bounds, or the key does not exist | Correct it; the [settings reference](../reference/settings.md) has the bounds |
| `OL_W_MODULE_SETTING_IGNORED` | An entry of `modules.ini` was unusable | Correct or remove the line |
| `OL_W_MODULE_NOT_LOADED` | The module joined by `modules.ini` but could not be loaded | See the plan's reason ([troubleshooting](troubleshooting.md#the-module-is-not-listed-or-not-loaded)) |

Related: [radar and interface settings](radar-and-interface-settings.md) · [session profiles](session-profiles.md) · [troubleshooting](troubleshooting.md)
