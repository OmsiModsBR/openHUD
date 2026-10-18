# Installation

openHUD is installed as a folder inside an OmsiLaunch installation. This page lists the requirements, says how to build the module folder from the repository, where to put it, how to check that OmsiLaunch accepts it and how to upgrade and remove it. Nothing in the process copies anything into your OMSI installation by itself: the build writes to the repository's `artifacts` folder, and you copy the finished folder yourself.

Related pages: [overview](overview.md), [first session](first-session.md), [enabling the module](../guides/enabling-the-module.md), [uninstalling](../guides/uninstalling.md), [compatibility](../reference/compatibility.md), [troubleshooting](../guides/troubleshooting.md).

## Requirements

| Requirement | Detail |
| --- | --- |
| Windows | The platforms OmsiLaunch supports (see the OmsiLaunch compatibility reference). The module's own tools target Windows x64; the components that run inside OMSI are 32-bit. |
| OMSI 2 | An installation that OmsiLaunch supports, with OmsiLaunch's permanent plugin installed (OmsiLaunch's installation guide). |
| OmsiLaunch | A build that serves runtime-module **bridge protocol 3**: the module declares `bridge_protocol` 3 to 3, because it uses the tick, the in-process operations, the input capture and the community content inputs. |
| Capabilities | The module cannot be loaded without the bridge features `bridge.events`, `bridge.runtime`, `bridge.tick`, `bridge.hooks.d3d9` and `bridge.input`. `ui.pause-menu.replace`, `camera.read`, `timetable.read` and `weather.read` are optional ([compatibility](../reference/compatibility.md)). |

Optional, per map: a realmap package and a pause-menu package ([per-map packages](../concepts/per-map-packages.md)). Neither is needed to install the module.

## Building the module folder

Building is only for people who work from the repository; a folder you received already built needs only the next section.

Tools: the .NET SDK, the Visual Studio C++ build tools (MSBuild with the x86 toolset) and a checkout of OmsiLaunch next to this repository (or its path given with `-OmsiLaunchRoot`).

```text
powershell -File tools\Build-Module.ps1 -OmsiLaunchRoot C:\src\OmsiLaunch
```

The script:

1. builds the draw layer `openhud-draw.dll` (C++, x86) and its native tests, and runs them;
2. builds the managed component (`OpenHud.dll`, with `OpenHud.Core.dll` and `OpenHud.Pause.dll`);
3. assembles `artifacts\modules\openhud\` with the files below and a `module.json` made from `module.template.json` in which every component is pinned by its SHA-256;
4. unless you pass `-SkipTests`, builds and runs the managed tests.

| File in `artifacts\modules\openhud\` | What it is |
| --- | --- |
| `module.json` | The manifest, with the pins of the components. |
| `OpenHud.dll`, `OpenHud.Core.dll`, `OpenHud.Pause.dll` | The managed component and the two libraries it uses. |
| `openhud-draw.dll` | The native draw layer. |
| `NOTICE.txt`, `LGPL-3.0.txt`, `GPL-3.0.txt` | The licence notice and the licence texts. |

The build never writes into an OMSI installation and never starts OMSI.

## Installing

Copy the whole `openhud` folder into the module folder of the permanent plugin:

```text
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\module.json
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\OpenHud.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\OpenHud.Core.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\OpenHud.Pause.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\openhud-draw.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\NOTICE.txt
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\LGPL-3.0.txt
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\GPL-3.0.txt
```

The folder name must be exactly `openhud`, the module's id. Do not edit the files: `module.json` pins the components by their SHA-256, and a changed file makes OmsiLaunch refuse the module (`OL_E_MODULE_INTEGRITY`).

The module's activation is `requested`: installing it does not turn it on. A session loads it only when you ask for it ([enabling the module](../guides/enabling-the-module.md)).

## Verifying

Check the folder without touching any installation:

```text
OmsiLaunch.exe /verify-module:"C:\OMSI 2\plugins\OmsiLaunch.Modules\openhud"
```

Exit code `0` means the manifest parses and every pinned file matches its pin; `7` means a pinned file does not match; `2` means the manifest does not parse or a file is missing ([errors and degradation](../reference/errors-and-degradation.md)).

Check that OmsiLaunch lists it in your installation:

```text
OmsiLaunch.exe "C:\OMSI 2" /list:RuntimeModules
```

The entry for `openhud` shows its version `0.1.0`, activation `requested`, its four operations (`status`, `radar.zoom`, `radar.toggle`, `pause.state`), its nine settings with type and default, and whether the folder is valid. If the entry is missing or invalid, see [troubleshooting](../guides/troubleshooting.md#the-module-is-not-listed-or-not-loaded).

Check what a session would do, without starting OMSI:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /plan
```

A runnable plan prints `Plan: READY`; with `--json` the plan lists the module among the planned modules with its settings and where each value comes from. A module that cannot be loaded makes the plan not runnable, with the module's `OL_E_MODULE_*` code.

## Upgrading

Close OMSI and OmsiLaunch's sessions, delete the old `openhud` folder and copy the new one in its place. Settings live in OmsiLaunch's own files (`modules.ini`, session profiles), not in the module folder, so they carry over. The module keeps no data of its own between sessions.

## Removing

[Uninstalling](../guides/uninstalling.md) covers removing the module for one session, for a profile and for good.

Related: [overview](overview.md) · [first session](first-session.md) · [enabling the module](../guides/enabling-the-module.md)
