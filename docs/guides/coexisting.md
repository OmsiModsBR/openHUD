# Coexisting with other modules and plugins

openHUD hooks two methods of the game's Direct3D 9 device, captures the keyboard and mouse while its pause menu is open, and can hide OMSI's native pause menu. This page says what that means next to other OmsiLaunch modules, OMSI plugins, overlays and tools. The rules of the hook registry and of conflicts are those of the OmsiLaunch Runtime Modules reference.

Related pages: [safety](../concepts/safety.md), [compatibility](../reference/compatibility.md), [errors and degradation](../reference/errors-and-degradation.md), [troubleshooting](troubleshooting.md).

## Declared conflicts

The manifest declares none: openHUD does not refuse to run beside any OMSI plugin.

## Other modules that hook the Direct3D device

Hooks are recorded in one registry with their owner, and hooks of different modules on the same device method stack and are removed last-in first-out. Each module's `Present` detour runs in turn, so two overlays both draw. Two things can go wrong:

- **The device method was already changed by something outside the registry** (another injector, a screen-capture overlay). The bridge refuses the hook (result `4`) and never overwrites it. openHUD then has no `hooks` event, draws nothing, and the `status` operation shows `draw: available` with no scenes reaching the screen. Read OmsiLaunch's events for the refusal; it names the owner.
- **A component writes the device's original methods back** (observed by OmsiLaunch on one installation shortly after gameplay starts). A device hook then stops being called and cannot be removed; cleanup reports `OL_E_MODULE_CLEANUP_INCOMPLETE`.

openHUD draws only on the game's own device; the vehicle selection form creates a second device with the same methods and the draw layer ignores it.

## Other modules that capture input

One module captures at a time. Only one module's pause menu can be open; if another module holds the capture, openHUD's capture fails, the pause menu goes to its provisional state and OMSI's native menu stays visible.

## Other modules that replace the pause menu

Two modules cannot both take over the native menu. The second to ask receives `OL_E_UI_ALREADY_TAKEN_OVER`; openHUD then gives back what it holds and leaves the native menu to the other one.

## OMSI plugins and overlays

Plugins that only read or write OMSI's files are unaffected. Overlays that hook the same Direct3D methods behave as above. Overlays that draw in a corner (frame counters, chat) can cover the radar: move it with `radar_corner`.

## Tools that edit your files

openHUD reads packages and writes nothing, so it does not interfere with editors. It reads a package once per map; to see an edit, restart the session.

## Several maps and mods

The packages are looked up per map folder. A mod that adds a pause-menu or realmap package to a map you own goes into that map's folder as any mod does; openHUD uses the one that is there at the start of the session.

Related: [safety](../concepts/safety.md) · [compatibility](../reference/compatibility.md) · [troubleshooting](troubleshooting.md)
