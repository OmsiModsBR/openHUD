# Safety

openHUD runs inside OMSI with the rights of the user who started the session, like any OMSI plugin. This page lists what the module never does, what it does touch and how, how it fails and what happens when it stops. These are properties of the code, enforced by its structure and checked by the offline tests where a test can check them; they are not promises about OMSI itself.

Related pages: [the three parts](the-three-parts.md), [the pause menu](the-pause-menu.md), [per-map packages](per-map-packages.md), [errors and degradation](../reference/errors-and-degradation.md), [coexisting](../guides/coexisting.md), [uninstalling](../guides/uninstalling.md).

## What the module never does

| It never | How this is true |
| --- | --- |
| Contains a memory address, an offset, a signature or a pointer of OMSI | Everything it knows about the game comes from OmsiLaunch's public operations (`time.read`, `map.read`, `player-vehicle.read`, `ui.pause.*` and the others in the manifest). Exact-build addresses belong to OmsiLaunch's build profiles, not to modules. The managed code receives text, never a pointer; the native code receives a device pointer only to compare it with the game's and to draw on it. |
| Sends a key or a mouse click | There is no call to send input in the code. The pause menu reads the captured events and asks OMSI to run its handlers through OmsiLaunch's `ui.pause.action`, which calls them directly. |
| Automates OMSI's windows | No window is found, clicked, moved or read. The native pause menu is hidden and shown again by OmsiLaunch's operations. |
| Writes to a file of the OMSI installation | The module has no write path: it reads packages and writes nothing. It does not even use its data directory. |
| Writes to a package | Package readers open files read-only and refuse links. |
| Installs anything in OMSI when it is built | `tools\Build-Module.ps1` writes to the repository's `artifacts` folder only. Installing is a copy you make ([installation](../getting-started/installation.md)). |
| Changes an add-on, a license or an entitlement | It reads none of them. |
| Opens a network connection or a program | It has no network code and starts no process. |

## What it does touch

| What | How | How long |
| --- | --- | --- |
| The game's Direct3D 9 device | Hooks `Present` and `Reset` through OmsiLaunch's hook registry (stacked, never overwriting a hook it did not make) and draws on the game's device only | Until the module stops; the hooks are removed by the bridge, and the DLL stays loaded, inert, until OMSI exits |
| The back buffer | While the pause menu is open, the draw layer copies the last picture of the game into a texture to use as the menu's background | Released when the menu closes and at stop |
| The keyboard and mouse messages of the game's window | While the pause menu is open (`bridge.input`): OmsiLaunch records the events and removes them from the queue so OMSI does not act on them | Released before any native dialog opens, when the menu closes and at stop |
| OMSI's native pause menu | Hidden and shown again through `ui.pause.take-over` and `ui.pause.restore-native`; its actions run through `ui.pause.action` | Given back at any failure and at stop, as long as the game is still paused |
| Fonts | Registered for this process only | Unregistered at stop |
| Memory | Pictures (at most 32 MiB), glyph atlases and the packages' parsed data | Released at stop |

## Package safety

The two package readers treat a package as hostile data. Every path is checked before a file is opened (no absolute path, no `..`, no drive, no device name, no link anywhere on the way, nothing outside the folder), every size and count is bounded, every number is checked to be finite and plausible, and a file that fails is dropped alone. A reader bug cannot take the pause menu down: an unexpected exception ends in the generic menu. See [pause-menu package](../reference/pause-menu-package.md#how-the-files-are-read) and [realmap package](../reference/realmap-package.md).

Text that comes from a package or from the game is data. It is bounded, stripped of control characters and drawn; nothing in it is run, opened or followed.

## How it fails

The failure policy in the manifest is `degrade`: a failure costs the part that failed and the module stays. Every part is wrapped so that nothing escapes a callback:

- a game read that fails makes that part of the snapshot "unavailable" and leaves the others alone;
- a fault in the radar is counted and said once; five in a row switch the radar off with one notice;
- a pause-menu failure gives the native menu back and leaves only the map behind it ([the pause menu](the-pause-menu.md#when-something-fails));
- a draw-layer failure is reported, retried twice and then switches the radar off and gives the native menu back;
- a package problem ignores the package.

The bridge applies its own limits on top: a tick or an operation that returns late is a failure of that call, reported once per code, and a module that never returns cannot be interrupted by the bridge ([errors and degradation](../reference/errors-and-degradation.md)).

## Stopping

When the session ends (or the module fails), the bridge calls `Stop()`. The module:

1. tells the draw layer to stop drawing;
2. stops the pause menu: releases the capture, releases the background picture and, if it still hides OMSI's native menu and the game is still paused, restores it; any piece that could not be done at once (for example while a native dialog is open) is retried every 250 ms;
3. replaces the scene with an empty one and releases the background;
4. emits `module.openhud.stopping`.

After the module stopped, every call to the draw layer is refused, its detours pass through to the original functions and its fonts and pictures are gone.

## Recovery

The module holds no installation file, so there is nothing to recover after a crash: its memory, hooks and captured input end with the OMSI process. A crash in the middle of a pause leaves OMSI as OMSI is; the next session starts clean.

Related: [the pause menu](the-pause-menu.md) · [per-map packages](per-map-packages.md) · [coexisting](../guides/coexisting.md)
