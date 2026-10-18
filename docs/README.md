# openHUD Documentation

This is the English documentation for openHUD `0.1.0` (module id `openhud`), the in-game HUD and pause-menu module for OMSI 2, an OmsiLaunch runtime module. Every page describes what the current code does; when a page and the code disagree, the code wins and the page is a bug. The pages mirror the structure of the OmsiLaunch documentation: getting started, concepts, guides, reference and status.

The module is a runtime module of OmsiLaunch with bridge protocol 3. OmsiLaunch's own documentation (its Runtime Modules reference, CLI reference and session-profile reference) is the normative source for everything the module relies on from OmsiLaunch: where modules live, integrity, activation, `/module-setting`, `modules.ini`, presets. These pages link to it by name and repeat only what a user of this module needs.

Vocabulary used throughout: the module is built and covered by offline tests; its rounds in a running OMSI are scheduled by the owner ([validation status](status/validation.md)); the runtime-module feature of OmsiLaunch is `EXPERIMENTAL`.

## Who reads what

| Audience | Start here | Then |
| --- | --- | --- |
| Players | [Overview](getting-started/overview.md), [Installation](getting-started/installation.md), [First session](getting-started/first-session.md) | [Radar and interface settings](guides/radar-and-interface-settings.md), [Controls](reference/controls.md), [FAQ](reference/faq.md) |
| Players who tune or use profiles | [Settings](reference/settings.md), [Enabling the module](guides/enabling-the-module.md) | [Session profiles](guides/session-profiles.md), [Uninstalling](guides/uninstalling.md) |
| Players who have a problem | [Troubleshooting](guides/troubleshooting.md) | [Operations and events](reference/operations-and-events.md), [Errors and degradation](reference/errors-and-degradation.md), [Coexisting](guides/coexisting.md) |
| Map and content authors | [Per-map packages](concepts/per-map-packages.md), [Making a pause-menu package](guides/making-a-pause-menu-package.md) | [Pause-menu package format](reference/pause-menu-package.md), [Using a realmap package](guides/using-a-realmap-package.md), [Realmap package](reference/realmap-package.md), [Fonts, symbols and licensing](concepts/fonts-symbols-and-licensing.md) |
| Integrators and maintainers | [The three parts](concepts/the-three-parts.md), [Architecture](architecture.md), [Scene protocol](scene-protocol.md) | [Compatibility](reference/compatibility.md), [Safety](concepts/safety.md), [Known limitations](reference/known-limitations.md), [Test coverage](status/test-coverage.md), [Changelog](../CHANGELOG.md) |

## Navigation

### Getting started

| Page | Purpose |
| --- | --- |
| [Overview](getting-started/overview.md) | What openHUD is, what it draws, what it replaces and leaves alone, what it needs, its three parts. |
| [Installation](getting-started/installation.md) | Requirements, building the module folder, installing it into an OmsiLaunch installation, verifying it, upgrading. |
| [First session](getting-started/first-session.md) | A first session step by step with the exact command lines, what you should see, the keys, and reading what the module did. |

### Concepts

| Page | Purpose |
| --- | --- |
| [The three parts and why](concepts/the-three-parts.md) | The core, the pause menu and the draw layer, what each knows, and one tick. |
| [The scene protocol in plain words](concepts/the-scene-protocol.md) | What a scene is, how it reaches the screen, its limits and failure behavior. |
| [The radar](concepts/the-radar.md) | What it shows, zoom, orientation, positions, size, and when it is empty. |
| [The pause menu](concepts/the-pause-menu.md) | How it takes over OMSI's native menu, its states, what happens when something fails, its pages. |
| [Per-map packages](concepts/per-map-packages.md) | The realmap and pause-menu packages: where they live, what is used, what happens when missing. |
| [Fonts, symbols and licensing](concepts/fonts-symbols-and-licensing.md) | What the module ships (nothing), who is responsible, how text is drawn. |
| [Internationalization](concepts/internationalization.md) | Interface languages, how the language is chosen, where each text comes from. |
| [Performance and the tick/draw split](concepts/performance.md) | The tick, the draw, the bounds and how to see the numbers. |
| [Safety](concepts/safety.md) | What the module never does, what it touches, how it fails and stops. |

### Guides

| Page | Purpose |
| --- | --- |
| [Enabling the module](guides/enabling-the-module.md) | Per session, permanently, from a profile; precedence; checking with a plan. |
| [Radar and interface settings](guides/radar-and-interface-settings.md) | Every setting with a worked example. |
| [Using it with session profiles](guides/session-profiles.md) | Presets that carry the module's settings or switch it off. |
| [Making a pause-menu package](guides/making-a-pause-menu-package.md) | Step by step with a complete example package and the log lines of rejected entries. |
| [Using a realmap package](guides/using-a-realmap-package.md) | Laying out a realmap package with a complete example. |
| [Troubleshooting](guides/troubleshooting.md) | The radar, the pause menu, text, language, CPU, packages; reading events and notices. |
| [Coexisting with other modules and plugins](guides/coexisting.md) | Direct3D hooks, input capture, the pause menu, overlays. |
| [Uninstalling](guides/uninstalling.md) | For one session, a profile, every session, and for good. |

### Reference

| Page | Purpose |
| --- | --- |
| [Settings](reference/settings.md) | Every key with type, default, bounds and an example. |
| [Operations and events](reference/operations-and-events.md) | The four operations and every event and notice, with data. |
| [Pause-menu package format](reference/pause-menu-package.md) | Every file, key, type, default, limit and error, with a template. |
| [Realmap package](reference/realmap-package.md) | What the radar reads from a realmap package. |
| [UI language keys](reference/ui-language-keys.md) | Every `UI_` key in three languages. |
| [Controls](reference/controls.md) | Keyboard and mouse of the pause menu. |
| [Compatibility and capabilities](reference/compatibility.md) | Bridge features, capabilities, operations and what happens without them. |
| [Errors and degradation](reference/errors-and-degradation.md) | What can go wrong and what the module does. |
| [Known limitations](reference/known-limitations.md) | What it does not do, and the differences found. |
| [FAQ](reference/faq.md) | Short answers. |
| [Scene protocol](scene-protocol.md) | The normative scene format between the core and the draw layer. |
| [Architecture](architecture.md) | The design for developers. |

### Status

| Page | Purpose |
| --- | --- |
| [Test coverage](status/test-coverage.md) | The gates by group and what they prove. |
| [Validation status](status/validation.md) | What is covered offline and what the rounds in a running OMSI will confirm. |
| [Changelog](../CHANGELOG.md) | What `0.1.0` contains. |

## How this documentation is kept in sync

`tools\Check-Docs.ps1` runs from the repository root and checks that every relative link and anchor in `docs\**\*.md`, `README.md` and `CHANGELOG.md` resolves, that every setting of `module.template.json` is in the [settings reference](reference/settings.md) with its type, default and bounds and has its own section, and that every page is listed above. The test gates `docs.*` rebuild the example packages printed in the guides from the page text and read them with the real readers, and run every setting example through the real settings parser ([test coverage](status/test-coverage.md)). They do not check prose, so a page can still be wrong about behavior; report that as a bug against the page.

## License

Copyright © 2026 Leo Monteiro / Portal OmsiModsBR. openHUD is free software under the GNU General Public License, version 3; it uses OmsiLaunch, which is licensed under the GNU Lesser General Public License, version 3 only. See the repository `LICENSE` and `NOTICE.txt`.
