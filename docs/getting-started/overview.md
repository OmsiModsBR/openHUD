# What openHUD is

openHUD (module id `openhud`, version `0.1.0`) is an in-game HUD and pause menu for OMSI 2, packaged as an OmsiLaunch runtime module. It draws a round radar (a minimap) over the game view and, where the OMSI build allows it, replaces OMSI's native pause menu with its own, which has a menu page, a full-screen map page and a page with the lines of the map. It is made for maps that ship the optional openOMSI packages (a real map for the radar and an identity for the pause menu), and it works without them. This page says what it does, what it draws, what it replaces, what it leaves alone and which parts it is made of. Requirements and installation are in [installation](installation.md); the first session is in [first session](first-session.md).

Related pages: [installation](installation.md), [first session](first-session.md), [the three parts](../concepts/the-three-parts.md), [the radar](../concepts/the-radar.md), [the pause menu](../concepts/the-pause-menu.md), [settings](../reference/settings.md), [known limitations](../reference/known-limitations.md), [FAQ](../reference/faq.md).

## What it draws

| Element | When | What you see |
| --- | --- | --- |
| The radar | While you drive and the game is running | A round map in a corner of the screen, centered on your vehicle and turning with it ("heading up"), with a north mark, the other vehicles around you, the scale of the circle, and your speed and the game clock under it. With a realmap package it also shows water, parks, built-up areas, relief, municipality names and the map's icons ([the radar](../concepts/the-radar.md)). |
| The pause menu | While OMSI is paused and its native pause menu would be shown | A panel over a dimmed copy of the game picture, with three pages: **Menu** (back to the game, new game, save game, options, quit OMSI, and the map's legend), **Map** (the whole map, which you pan and zoom) and **Lines** (the lines of the map for the session's date) ([the pause menu](../concepts/the-pause-menu.md)). |
| A tray balloon | Rarely | When a part had to be switched off (the draw layer, the radar, the pause menu or a map package), OmsiLaunch's tray shows one short notice. |

Everything is drawn by the module itself into OMSI's Direct3D 9 view. Nothing is added to OMSI's own windows.

## What it replaces and what it leaves alone

- It replaces OMSI's native pause menu **only** when the build of OMSI in use lets OmsiLaunch hand it over (the optional capability `ui.pause-menu.replace`), when the input capture works and when the draw layer is available. In every other case the native pause menu stays exactly as it is, and the radar still works ([the pause menu](../concepts/the-pause-menu.md#when-the-native-menu-stays)).
- It does not touch OMSI's other windows, its settings, its files or its add-ons. It never writes to the OMSI installation ([safety](../concepts/safety.md)).
- The actions of the menu (new game, save, options, quit) are OMSI's own: the module asks OMSI to run its native handler and OMSI shows its native dialogs. The module draws nothing while a native dialog is in front.

## What it needs and what is optional

| | Needed? | Notes |
| --- | --- | --- |
| OmsiLaunch with bridge protocol 3 | Yes | The module is a runtime module of OmsiLaunch ([installation](installation.md#requirements)). |
| A Direct3D 9 view in OMSI | Yes | The draw layer hooks the game device's `Present` and `Reset`. |
| Keyboard and mouse capture (`bridge.input`) | Yes | Needed for the pause menu's own input. |
| `ui.pause-menu.replace` | Optional | Without it the native pause menu stays; the radar works. |
| A realmap package for the map | Optional | Without it the radar draws only you, the vehicles around you and the scale ([per-map packages](../concepts/per-map-packages.md)). |
| A pause-menu package for the map | Optional | Without it the menu is the generic OMSI 2 menu. |
| Fonts | Optional | Without a package font the draw layer uses the system UI font. |

## The three parts

| Part | Project | What it does |
| --- | --- | --- |
| The core | `OpenHud.Core` (C#) | Reads the game through OmsiLaunch's operations, keeps the model of the game, reads the settings and the two map packages, and builds the radar and the full map as a scene of simple shapes. |
| The pause menu | `OpenHud.Pause` (C#) | The state machine that takes over OMSI's native pause menu and gives it back, and the model of the menu itself (pages, focus, keyboard and mouse rules, layout). |
| The draw layer | `openhud-draw.dll` (C++, x86) | Draws the scene into the game's Direct3D 9 view at every `Present`. It knows nothing about the game, the menu or the map. |

A thin entry, `OpenHud.dll` (the module project), connects the three to OmsiLaunch: it is the managed component the bridge loads, and it owns the tick. The repository also holds a placeholder for an optional session program, `OpenHud.Host`; the manifest declares no host component, so none is started ([known limitations](../reference/known-limitations.md)). [The three parts](../concepts/the-three-parts.md) explains why the work is split this way.

## What it does not do

- It is not a performance feature. Drawing is bounded (a scene has at most 8,192 commands and 1 MiB of JSON) and the module's own work per tick is measured and reported, but it is work on top of the game's, not instead of it ([performance](../concepts/performance.md)).
- It does not read OMSI's memory by address, simulate keyboard or mouse input, automate OMSI's windows or write to OMSI's files. Everything it knows about the game comes through OmsiLaunch's public operations ([safety](../concepts/safety.md)).
- It does not change add-ons, licenses or ownership of any content.
- It has no settings window. Its settings are OmsiLaunch module settings: command-line `/module-setting`, `modules.ini` and session-profile presets ([enabling it](../guides/enabling-the-module.md)).

## Status

openHUD `0.1.0` is built and covered by offline tests that never start OMSI ([test coverage](../status/test-coverage.md)). Its rounds in a running OMSI are scheduled by the owner and have not been done yet; [validation status](../status/validation.md) lists what they will confirm. The runtime-module feature of OmsiLaunch is `EXPERIMENTAL`.

The module is not part of OmsiLaunch and is not distributed with it. It is free software under the GNU General Public License, version 3; it uses OmsiLaunch, which is licensed under the GNU Lesser General Public License, version 3 only (see `NOTICE.txt`).

## Next

[Installation](installation.md) · [First session](first-session.md) · [The radar](../concepts/the-radar.md) · [The pause menu](../concepts/the-pause-menu.md)
