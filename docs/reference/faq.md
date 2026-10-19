# FAQ

Short answers to the questions people ask most about openHUD. Each answer points to the page with the detail.

Related pages: [overview](../getting-started/overview.md), [known limitations](known-limitations.md), [troubleshooting](../guides/troubleshooting.md), [settings](settings.md).

## Does it change my OMSI files?

No. It reads packages and writes nothing, and the build never copies anything into OMSI ([safety](../concepts/safety.md)).

## Do I have to turn it on every time?

Its activation is `requested`. Ask for it per session with `/module:openhud`, for every session with `enabled=true` under `[openhud]` in `modules.ini`, or in a session-profile preset ([enabling the module](../guides/enabling-the-module.md)).

## Does it work on any map?

Yes. Without the map's optional packages the radar shows you and the vehicles around you, and the pause menu is the generic one ([per-map packages](../concepts/per-map-packages.md)).

## Why is my radar empty?

Probably because the map has no realmap package, or because the game has not told the module where your vehicle is yet. `status` says which ([troubleshooting](../guides/troubleshooting.md#the-radar-does-not-appear)).

## Where is the speed, the next stop, the timetable?

The speed is on the strip under the radar, next to the clock, in the unit of the `units` setting. The next stop, the line and the delay are not drawn: the catalog gives no source for them ([known limitations](known-limitations.md)).

## Does it replace OMSI's pause menu on every build?

Only where OmsiLaunch can hand it over (`ui.pause-menu.replace`) and the input capture and draw layer work. Otherwise OMSI's menu is shown as always ([the pause menu](../concepts/the-pause-menu.md#when-the-native-menu-stays)).

## What happens if the menu breaks while I am paused?

The module gives OMSI's native menu back and draws only the map behind it, so you are never left without a menu ([the pause menu](../concepts/the-pause-menu.md#when-something-fails)).

## Does it click OMSI's buttons for me?

No. It asks OMSI, through OmsiLaunch, to run the native handler of the action. No click, key or message is sent.

## Can I change the radar while I play?

Yes: `OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=in` and `radar.toggle` ([operations and events](operations-and-events.md)).

## How do I move or resize the radar?

`radar_corner`, `radar_size_percent`, `radar_opacity_percent` and `ui_scale_percent` ([radar and interface settings](../guides/radar-and-interface-settings.md)).

## Why is the interface in English?

`language=auto` resolves to English in 0.1.0. Set `language=pt-BR` or `language=de` ([internationalization](../concepts/internationalization.md)).

## Can I use my own language?

The interface exists in English, Portuguese (Brazil) and German. A map package can add its title and legend in any OMSI language code.

## How do I give my map its own look?

Add an `openomsi-pausemenu` folder to the map ([making a pause-menu package](../guides/making-a-pause-menu-package.md)) and, for the radar, an `openomsi-realmap` folder ([using a realmap package](../guides/using-a-realmap-package.md)).

## Does it include fonts or logos?

No. Fonts and logos come from the map's package, and the author is responsible for the right to ship them ([fonts, symbols and licensing](../concepts/fonts-symbols-and-licensing.md)).

## Why was my SVG symbol not drawn?

It is drawn when it fits the static subset and its budget ([the SVG subset](pause-menu-package.md#the-svg-subset)). When it is not, the generic mark is drawn instead and the reason is in the `packages` event (`diagnostics`) and in the package log: a `DOCTYPE` (remove that line), a script, a style sheet, an external reference, or a drawing with more shapes than the slot allows.

## Why does a package entry get dropped?

The log line says which and why; every message is in the references ([pause-menu package](pause-menu-package.md), [realmap package](realmap-package.md)).

## Does the `units` setting do anything?

Yes. It chooses km/h or mph for the speed on the strip and meters or feet for the scales ([settings](settings.md#units)).

## Does it slow the game?

It adds bounded work: a tick every 50 ms and a draw at each frame. Its cost in a running game has not been measured yet ([performance](../concepts/performance.md), [validation status](../status/validation.md)).

## Can it run with other overlays?

Usually, if they hook the Direct3D device through OmsiLaunch or not at all. A device method changed by something outside OmsiLaunch's registry is refused ([coexisting](../guides/coexisting.md)).

## How do I remove it?

Delete the `openhud` folder ([uninstalling](../guides/uninstalling.md)).

## Is it tested?

By offline tests that never start OMSI ([test coverage](../status/test-coverage.md)). Rounds in a running OMSI are scheduled by the owner ([validation status](../status/validation.md)).

Related: [known limitations](known-limitations.md) · [troubleshooting](../guides/troubleshooting.md)
