<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/logo-dark.svg">
    <img src="assets/brand/logo-light.svg" alt="OmsiLaunch openHUD" height="96">
  </picture>
</p>

<div align="center">

[![License: GPL v3](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
[![Release](https://img.shields.io/github/v/release/OmsiModsBR/openHUD?include_prereleases&label=release&sort=semver)](https://github.com/OmsiModsBR/openHUD/releases)
[![Last commit](https://img.shields.io/github/last-commit/OmsiModsBR/openHUD)](https://github.com/OmsiModsBR/openHUD/commits/main)
[![Issues](https://img.shields.io/github/issues/OmsiModsBR/openHUD)](https://github.com/OmsiModsBR/openHUD/issues)
![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2B-0078d6)
![Game: OMSI 2](https://img.shields.io/badge/game-OMSI%202-f78034)
[![Requires OmsiLaunch](https://img.shields.io/badge/requires-OmsiLaunch%200.1.0--beta4%2B-ff9918)](https://github.com/OmsiModsBR/OmsiLaunch)
![.NET 6](https://img.shields.io/badge/.NET-6-512bd4)
![Docs: EN / PT-BR](https://img.shields.io/badge/docs-EN%20%7C%20PT--BR-00c94a)

</div>

# openHUD

In-game HUD for OMSI 2: a live map and a new pause menu, running as an OmsiLaunch runtime module.

## Status

openHUD is built and tested offline: the module runs against a fake of the OmsiLaunch bridge (catalog, content, input and the native draw layer) and the draw layer against a software and a Direct3D 9 backend. It has not been run in OMSI yet. Nothing that depends on the real game (the radar over a live map, the pause menu take-over, the input capture, the Direct3D hooks) is proven; those wait for their runtime rounds, which are run separately and only by the owner.

What the module needs from OmsiLaunch (bridge protocol 3): the catalog operations it lists in `module.template.json`, `bridge.input`, `bridge.tick` and `bridge.hooks.d3d9`. Replacing the pause menu also needs the optional capability `ui.pause-menu.replace`; without it OMSI's own menu stays and the radar still works.

See [docs/architecture.md](docs/architecture.md) for how the parts fit together and [docs/scene-protocol.md](docs/scene-protocol.md) for the contract between the managed and the native side.

## Building

Needs the .NET SDK, Visual Studio with the C++ build tools (toolset v145, x86) and a checkout of OmsiLaunch.

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Module.ps1 -OmsiLaunchRoot <path to OmsiLaunch>
```

builds the draw layer (x86), its native tests, the managed module and the test gates, and assembles the module in `artifacts\modules\openhud` with every component pinned by its SHA-256. Nothing is copied into an OMSI installation.

## License

Copyright © 2026 LMonteiro - github.com/lmonteirotech and OmsiModsBR Team - github.com/OmsiModsBR

openHUD is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License, version 3, as published by the Free Software Foundation. See [LICENSE](LICENSE).

## Documentation

Complete documentation, with examples, in English and Brazilian Portuguese: [English index](docs/README.md) · [Índice em português](docs/pt-BR/README.md).

#### English

- **Getting started:** [Overview](docs/getting-started/overview.md) · [Installation](docs/getting-started/installation.md) · [First session](docs/getting-started/first-session.md)
- **Concepts:** [The three parts and why](docs/concepts/the-three-parts.md) · [The scene protocol in plain words](docs/concepts/the-scene-protocol.md) · [The radar](docs/concepts/the-radar.md) · [The pause menu](docs/concepts/the-pause-menu.md) · [Per-map packages](docs/concepts/per-map-packages.md) · [Fonts, symbols and licensing](docs/concepts/fonts-symbols-and-licensing.md) · [Internationalization](docs/concepts/internationalization.md) · [Performance and the tick/draw split](docs/concepts/performance.md) · [Safety](docs/concepts/safety.md)
- **Guides:** [Enabling the module](docs/guides/enabling-the-module.md) · [Radar and interface settings](docs/guides/radar-and-interface-settings.md) · [Using it with session profiles](docs/guides/session-profiles.md) · [Making a pause-menu package](docs/guides/making-a-pause-menu-package.md) · [Using a realmap package](docs/guides/using-a-realmap-package.md) · [Troubleshooting](docs/guides/troubleshooting.md) · [Coexisting with other modules and plugins](docs/guides/coexisting.md) · [Uninstalling](docs/guides/uninstalling.md)
- **Reference:** [Settings](docs/reference/settings.md) · [Operations and events](docs/reference/operations-and-events.md) · [Pause-menu package format](docs/reference/pause-menu-package.md) · [Realmap package](docs/reference/realmap-package.md) · [UI language keys](docs/reference/ui-language-keys.md) · [Controls](docs/reference/controls.md) · [Compatibility and capabilities](docs/reference/compatibility.md) · [Errors and degradation](docs/reference/errors-and-degradation.md) · [Known limitations](docs/reference/known-limitations.md) · [FAQ](docs/reference/faq.md) · [Scene protocol](docs/scene-protocol.md) · [Architecture](docs/architecture.md)
- **Status:** [Test coverage](docs/status/test-coverage.md) · [Validation status](docs/status/validation.md) · [Changelog](CHANGELOG.md)

#### Português (Brasil)

- **Primeiros passos:** [Visão geral](docs/pt-BR/getting-started/overview.md) · [Instalação](docs/pt-BR/getting-started/installation.md) · [Primeira sessão](docs/pt-BR/getting-started/first-session.md)
- **Conceitos:** [As três partes e por quê](docs/pt-BR/concepts/the-three-parts.md) · [O protocolo de cena em palavras simples](docs/pt-BR/concepts/the-scene-protocol.md) · [O radar](docs/pt-BR/concepts/the-radar.md) · [O menu de pausa](docs/pt-BR/concepts/the-pause-menu.md) · [Pacotes por mapa](docs/pt-BR/concepts/per-map-packages.md) · [Fontes, símbolos e licenciamento](docs/pt-BR/concepts/fonts-symbols-and-licensing.md) · [Internacionalização](docs/pt-BR/concepts/internationalization.md) · [Desempenho e a divisão tick/desenho](docs/pt-BR/concepts/performance.md) · [Segurança](docs/pt-BR/concepts/safety.md)
- **Guias:** [Ativando o módulo](docs/pt-BR/guides/enabling-the-module.md) · [Configurações do radar e da interface](docs/pt-BR/guides/radar-and-interface-settings.md) · [Usando com perfis de sessão](docs/pt-BR/guides/session-profiles.md) · [Criando um pacote de menu de pausa](docs/pt-BR/guides/making-a-pause-menu-package.md) · [Usando um pacote realmap](docs/pt-BR/guides/using-a-realmap-package.md) · [Solução de problemas](docs/pt-BR/guides/troubleshooting.md) · [Convivendo com outros módulos e plugins](docs/pt-BR/guides/coexisting.md) · [Desinstalação](docs/pt-BR/guides/uninstalling.md)
- **Referência:** [Configurações](docs/pt-BR/reference/settings.md) · [Operações e eventos](docs/pt-BR/reference/operations-and-events.md) · [Formato do pacote de menu de pausa](docs/pt-BR/reference/pause-menu-package.md) · [Pacote realmap](docs/pt-BR/reference/realmap-package.md) · [Chaves de idioma da interface](docs/pt-BR/reference/ui-language-keys.md) · [Controles](docs/pt-BR/reference/controls.md) · [Compatibilidade e capacidades](docs/pt-BR/reference/compatibility.md) · [Erros e degradação](docs/pt-BR/reference/errors-and-degradation.md) · [Limitações conhecidas](docs/pt-BR/reference/known-limitations.md) · [FAQ](docs/pt-BR/reference/faq.md) · [Protocolo de cena (em inglês)](docs/scene-protocol.md) · [Arquitetura](docs/pt-BR/architecture.md)
- **Status:** [Cobertura de testes](docs/pt-BR/status/test-coverage.md) · [Status de validação](docs/pt-BR/status/validation.md) · [Changelog](CHANGELOG.md)

