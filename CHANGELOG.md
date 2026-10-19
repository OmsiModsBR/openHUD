# Changelog

All notable changes to openHUD (module id `openhud`) are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the version numbers follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

Finishes what the first release left open ([known limitations](docs/reference/known-limitations.md)).

### Added

- SVG symbols are drawn: a hardened converter turns the static subset of SVG (shapes, paths with every command, transforms, colors, opacity) into scene shapes, with hard limits and a refusal or a skip with a reason for scripts, style sheets, entities, external references, `use`, filters and the like ([the SVG subset](docs/reference/pause-menu-package.md#the-svg-subset)). A `DOCTYPE` is still refused, now with its own message.
- The package slots that were only read are drawn: `stop_icon` (in place of the dot at markers without a picture), `marker_icon.<kind>` (legend rows), `mode_icon.<mode>` (the Lines page), `symbols/omsi2-logo` and `symbols/openomsi` as PNG or SVG (the menu header), `color.brt` and the `brt_lines` of the realmap manifest, the package `credit` (footer, next to the versions); the realmap manifest's colors and the player marker keys `player` and `player_free`.
- The radar's strip shows the speed next to the clock, and `units` now chooses km/h or mph and meters/kilometers or feet/miles for the speed and the scales of the radar and the map. The speed unit of `ground_speed` (metres per second) stays an assumption for the runtime round.
- `map_queries` and `map_builds` in `status`; `vector_symbols` in the `packages` event; `tessellations` and `replays` in the draw layer's `stats`.

### Changed

- Less work per tick: a coarser change detector for the radar (a third of a pixel at the current zoom), a hash of the scene instead of comparing encoded text, a cached pause map and its encoding, feature culling by bounding box, builders and a validator that allocate far less, a faster number writer. Measured offline on this machine: hovering the pause map from 8.7 ms to 0.4 ms per tick (p50), a radar rebuilt on every tick from 2.1 to 1.0 ms (p50) and from 9-14 ms to about 3 ms (p99), the longest pan of the map from about 20 ms to about 10 ms.
- The draw layer tessellates a scene once and replays the batches at every `Present` until the scene, an image, a font, the view or the device changes; a circular clip tests each triangle against the circle first (a new radar scene is about five times cheaper to tessellate).
- The map page of the pause menu draws at most 6,900 commands (it was 7,200) so that the header's drawings fit the scene limit; the title in the header stops before the marks.

## 0.1.0 - unreleased

First version of openHUD as an OmsiLaunch runtime module (bridge protocol 3, activation `requested`, failure policy `degrade`). It is built and covered by offline tests; its rounds in a running OMSI are scheduled by the owner ([docs/status/validation.md](docs/status/validation.md)).

### Added

- A round radar (minimap) over the game view, heading up, with the vehicles around the player, a north mark, a scale and the game clock; with a realmap package it draws water, parks, built-up areas, relief, rails, borders, streets, place names and icons ([docs/concepts/the-radar.md](docs/concepts/the-radar.md)).
- A pause menu that takes over OMSI's native one where the build allows it, with three pages (menu, map, lines), a legend, keyboard and mouse control, and a provisional mode that leaves OMSI's menu in front whenever something fails ([docs/concepts/the-pause-menu.md](docs/concepts/the-pause-menu.md)).
- A native draw layer (x86) that draws a JSON scene into the game's Direct3D 9 view at every `Present`, with a software backend for tests ([docs/concepts/the-scene-protocol.md](docs/concepts/the-scene-protocol.md)).
- Nine module settings: `radar`, `pause_menu`, `ui_scale_percent`, `language`, `units`, `radar_zoom`, `radar_corner`, `radar_size_percent`, `radar_opacity_percent` ([docs/reference/settings.md](docs/reference/settings.md)).
- Four module operations (`status`, `radar.zoom`, `radar.toggle`, `pause.state`) and runtime events and notices ([docs/reference/operations-and-events.md](docs/reference/operations-and-events.md)).
- Readers for the per-map `openomsi-pausemenu` and `openomsi-realmap` packages, path-safe, bounded, read-only, dropping bad entries one by one ([docs/reference/pause-menu-package.md](docs/reference/pause-menu-package.md), [docs/reference/realmap-package.md](docs/reference/realmap-package.md)).
- Interface text in English, Portuguese (Brazil) and German ([docs/reference/ui-language-keys.md](docs/reference/ui-language-keys.md)).
- `tools/Build-Module.ps1`, which assembles the pinned module folder without installing anything.
- Offline tests (managed and native) and user documentation under `docs/` with a link checker, `tools/Check-Docs.ps1`.

### Known limitations

See [docs/reference/known-limitations.md](docs/reference/known-limitations.md): among others, `language=auto` resolves to English, the next stop, line and delay have no source in the catalog, and a few keys of the package formats (`author`, `player_line`, `player_brt`, `light_*` colors, the `*_vehicles` lists) are read but not drawn.
