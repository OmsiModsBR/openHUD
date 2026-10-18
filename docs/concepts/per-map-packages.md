# Per-map packages

openHUD works on any map, and it can look like the map when the map ships two optional packages: a **realmap** package (a pre-built picture of the map for the radar and the map page) and a **pause-menu** package (the title, symbol, colors, legend and fonts of the pause menu). This page explains what they are, where they live, who reads them, what the module does with each piece, and what happens when they are missing, partly wrong or hostile. The formats are in the [realmap package reference](../reference/realmap-package.md) and the [pause-menu package reference](../reference/pause-menu-package.md); making them is in the guides for [pause-menu packages](../guides/making-a-pause-menu-package.md) and [realmap packages](../guides/using-a-realmap-package.md).

Related pages: [the radar](the-radar.md), [the pause menu](the-pause-menu.md), [fonts, symbols and licensing](fonts-symbols-and-licensing.md), [safety](safety.md), [troubleshooting](../guides/troubleshooting.md).

## The two packages

| | `openomsi-realmap` | `openomsi-pausemenu` |
| --- | --- | --- |
| What it carries | Water, parks, built-up areas, relief, named streets with their speed limits, place names, icons, municipalities, lines | The pause menu's title, symbols, colors, legend texts, fonts and per-language texts |
| Required file | `manifest.cfg` | `manifest.cfg` |
| Made by | The map author's tools | The map author, by hand |
| Used for | The radar and the Map page | The pause menu and the radar's colors |

Both live inside the map's own folder, next to its `global.cfg`:

```text
<OMSI root>\maps\<Map folder>\global.cfg
<OMSI root>\maps\<Map folder>\openomsi-realmap\...
<OMSI root>\maps\<Map folder>\openomsi-pausemenu\...
```

OMSI itself ignores both folders, so a map made for several games ships one set of files and loses nothing. They are data from third parties: the module reads them read-only, treats every value as untrusted and never writes to them.

## Who finds them

The realmap package is a *content input* of the module (`realmap-package`, not required). OmsiLaunch's bridge locates the folder of the current map, checks that it is inside the installation, free of links and within its bounds (50,000 files, 4 GiB), and hands the module its directory and a fingerprint. The module looks for the pause-menu package in the **same map folder** (the sibling of the realmap folder; when the map has no realmap package it works out the map folder from the module's own location in the installation). The folder `openomsi-pausemenu` is looked for nowhere else; a package for a map you do not own goes inside that map's folder, as a mod does.

The module notices the map from `map.read` (a file name of the form `maps\<folder>\global.cfg`) and starts loading the packages on a worker thread, never inside the tick. At most eight maps are loaded in one session; a map change replaces the packages, and the previous map's pictures are dropped from the draw layer.

## What happens without them

| Situation | Result |
| --- | --- |
| No realmap package | Not an error. The radar draws the ring, you, the vehicles around you and the clock; the Map page shows the same, and the Lines page says "No lines to show". |
| No pause-menu package | Not an error. The generic OMSI 2 menu: the title is the map's folder name, no symbol (a neutral disc with a pause mark is drawn), the default blue accent, the system UI font, the built-in legend texts. |
| A package folder without `manifest.cfg` | Same as absent. |
| A package with an unknown `format`, or an unreadable manifest | The whole package is ignored with one log line; the generic behavior applies. One notice is shown ("A map package could not be used and was ignored."). |
| A bad entry inside a package (a picture too big, a color that is not a color, a path that leaves the folder) | That entry is dropped with a reason; the rest of the package stays. |
| A layer of the realmap package is missing or corrupt | That layer is off; the others stay. A corrupt chunk of `surface.oms` loses that chunk only. |
| Reading a package fails unexpectedly | The package is ignored and the session goes on. |

Nothing a package contains can stop the module: every reader ends in "use the generic default".

## What the module uses

The readers read everything the formats define and validate it. The module in version `0.1.0` draws a part of it; the rest is read, checked and kept for later releases.

| From | Used in 0.1.0 | Read but not drawn yet |
| --- | --- | --- |
| Pause-menu `identity.cfg` | `title` (and `title.<LANG>`), `symbol` / `symbol.<theme>` / `symbol.openomsi` (PNG pictures; the choice follows `theme` and `layout`), `color.accent`, `color.line`, `color.group.<id>`, `legend.<kind>` (and per language) | `stop_icon`, `marker_icon.<kind>`, `mode_icon.<mode>`, `color.brt`; a symbol that is an SVG (it is validated, but the generic mark is drawn) |
| Pause-menu `symbols/` | The map's symbol picture | `omsi2-logo.png`, `openomsi.png` |
| Pause-menu `fonts/` | All five roles, registered with the draw layer | |
| Pause-menu `strings/` | The `title` and `legend.<kind>` keys | Other keys |
| Pause-menu `manifest.cfg` | `format`, `name` (for the log) | `author`, `credit` |
| Realmap `surface.oms` | Fills, rivers, rails, borders, contour lines, streets (at close zoom), place labels | Street names and speed limits, which are read (`StreetAt`) but not shown yet |
| Realmap `markers.cfg`, `icons/` | The markers with their icons (PNG, up to 128 pixels, 48 pictures) | |
| Realmap `regions.cfg` | The names with a readable label box, as faint watermarks | The "you are in" lookup |
| Realmap `lines.lin` | The lines valid on the session's date, on the Lines page | Routes and stops on the map |
| Realmap `manifest.cfg` | `grid`, `north` | Colors (`water`, `green`, ...), `player*`, `brt_lines`, `*_vehicles`, `alignment`, `credit` |

The radar's colors are the module's own, except the accent and the line color that the pause-menu package may set.


## Caching and change

The module reads each package once per map, outside the tick, and keeps an immutable snapshot. The fingerprint that identifies a package is a cache key (the relative path, size and write time of every file): it is never an integrity check or a trust decision. To see a change in a package, restart the session.

## Reports

When the packages of a map are loaded, the module emits the event `module.openhud.packages` with the map, the status of each package (`Absent`, `Ignored`, `Loaded`), the number of fonts and pictures sent to the draw layer and, when there is something to say, the first diagnostics (`Dropped: ...`, `Ignored: ...`), bounded to 700 characters. A package problem that cannot be shown as a status is `module.openhud.package-problem` ([operations and events](../reference/operations-and-events.md#events)).

Related: [pause-menu package](../reference/pause-menu-package.md) · [realmap package](../reference/realmap-package.md) · [fonts, symbols and licensing](fonts-symbols-and-licensing.md)
