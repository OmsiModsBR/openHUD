<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/logo-dark.svg">
    <img src="assets/brand/logo-light.svg" alt="OmsiLaunch openHUD" height="96">
  </picture>
</p>

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

Copyright © 2026 Leo Monteiro / Portal OmsiModsBR - https://github.com/OmsiModsBR

openHUD is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License, version 3, as published by the Free Software Foundation. See [LICENSE](LICENSE).
