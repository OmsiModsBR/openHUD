# Uninstalling

This page shows how to stop using openHUD for one session, for a profile, for every session, and how to remove it completely. Nothing the module does needs to be undone: it writes nothing to your OMSI installation and keeps no data of its own.

Related pages: [enabling the module](enabling-the-module.md), [session profiles](session-profiles.md), [installation](../getting-started/installation.md), [safety](../concepts/safety.md#recovery).

## For one session

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /no-module:openhud
```

Or simply do not request it: the activation is `requested`.

## For a profile

Set `enabled: false` in the preset's `modules` block, or leave the block out ([session profiles](session-profiles.md#keeping-the-module-out-of-a-preset)).

## For every session

Remove `enabled=true` (or set `enabled=false`) in the `[openhud]` section of `<OMSI root>\.omsilaunch\modules.ini`:

```ini
[openhud]
enabled=false
```

## Removing the module

1. Close OMSI and any OmsiLaunch session.
2. Delete the folder `<OMSI root>\plugins\OmsiLaunch.Modules\openhud`.
3. Optionally delete the `[openhud]` section of `modules.ini` and the `openhud` entries of your session profiles. Left in place they are harmless: a preset that says `enabled: true` for a module that is not installed makes its plan not runnable (`OL_E_MODULE_NOT_FOUND`), and settings alone are ignored with `OL_W_MODULE_SETTING_IGNORED`.

There is no data directory to clear: `/clear-module-data:openhud` has nothing to delete, because the module does not write there.

## What stays

Map packages (`openomsi-realmap`, `openomsi-pausemenu`) are part of your maps and stay; OMSI ignores them. OMSI's pause menu is unchanged at all times: openHUD only hides it while a session runs.

Related: [enabling the module](enabling-the-module.md) · [installation](../getting-started/installation.md)
