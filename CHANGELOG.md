# Changelog

All notable changes to openHUD (module id `openhud`) are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the version numbers follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

See [docs/reference/known-limitations.md](docs/reference/known-limitations.md): among others, `units` has no visible effect yet, SVG symbols are validated but not drawn, `language=auto` resolves to English, and parts of the package formats are read but not yet drawn.
