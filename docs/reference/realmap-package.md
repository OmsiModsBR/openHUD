# Realmap package

The `openomsi-realmap` package is an optional, pre-built picture of a map that sits next to the map: its water, parks, built-up areas, relief, named streets and speed limits, the icons of its stations, its municipalities and its lines. openHUD's radar uses it to draw a real map around the player instead of an empty circle. This page lists what `src/OpenHud.Core/Packages/Realmap/` reads and how it treats what it reads. The format is made by the map author's tools; the page is for whoever makes the files and for whoever wants to know what the radar can rely on. A worked example is in [using a realmap package with the radar](../guides/using-a-realmap-package.md).

Format version: `1`. Related pages: [the radar](../concepts/the-radar.md), [per-map packages](../concepts/per-map-packages.md), [pause-menu package](pause-menu-package.md), [compatibility and capabilities](compatibility.md), [errors and degradation](errors-and-degradation.md).

## Where it is and who finds it

```text
<OMSI root>\maps\<Map folder>\openomsi-realmap\
  manifest.cfg      required: its presence makes the package count
  surface.oms       the pre-built surface, streets and relief (binary)
  markers.cfg       optional: icons on the map
  regions.cfg       optional: municipalities
  lines.lin         optional: the lines (bus, tram, metro, rail)
  icons\            optional: the PNG pictures that markers.cfg names
  LICENSE.txt       optional: the licence of the data, for people (not read)
```

The module does not search for the folder itself. It declares the content input `realmap-package` (`required: false`) and OmsiLaunch's bridge locates the folder of the current map, checks that reading it is safe and hands the module its directory and a fingerprint. OmsiLaunch's rules apply: the package must be inside the installation, no part of it may be a link, and it is bounded to 50,000 files and 4 GiB; otherwise the module gets `OL_E_MODULE_CONTENT_INPUT_INVALID` and ignores the package. A map without a package is not an error: the radar draws without it ([the radar](../concepts/the-radar.md#without-a-realmap-package)).

The module asks for the package of the map the game reports (`map.read`, file name of the form `maps\<folder>\global.cfg`). If the game does not report such a name, no package is looked for.

All data in the package is untrusted. Every value is checked, an entry that fails is dropped with a line in the package log (`Dropped: <file>:<line>: <reason>`), and a layer whose file is missing or corrupt is switched off alone: the other layers stay. Nothing in the reader throws out to the radar.

The `.cfg` files follow the same text rules as the pause-menu package (UTF-8, `key = value`, `#` comments, duplicate keys: the first counts), see [how the files are read](pause-menu-package.md#how-the-files-are-read). Paths use the same [path rules](pause-menu-package.md#path-rules).

## `manifest.cfg`

Up to 256 KiB.

| Key | Type | Required | Default | Meaning |
| --- | --- | --- | --- | --- |
| `format` | integer | yes | none | Must be `1`. Otherwise the package is ignored: `Ignored: openomsi-realmap: unknown package format '<value>'`. |
| `grid` | number | yes | none | The author's tile size in metres, more than 1 and at most 100,000. A missing or implausible value ignores the package: `Ignored: openomsi-realmap: manifest grid is missing or not a plausible tile size`. |
| `name` | text | no | none | The package's name. |
| `credit` | text | no | none | The credit line (at most 400 characters). |
| `north` | number | no | `0` | Degrees clockwise from the map's own up (+Y) to real north, at most 3600 in absolute value; stored modulo 360. Invalid: `Dropped: manifest.cfg north: not a number of degrees; 0 used`. |
| `alignment` | `exact`, `fitted` | no | not stated | `exact`: the real-world layers lie where they are in reality; `fitted`: they were bent to the map's own roads. Informational. Another value: `must be exact or fitted`. |
| `terrain_offset` | number | no | `0` | Metres added to the height of every contour line; at most 20,000 in absolute value. |
| `player` | `triangle` | no | the default arrow | `triangle` draws the player as a triangle. |
| `player_free` | color | no | the default | Triangle color with no timetable. |
| `player_line` | color | no | the default | Triangle color on a normal line. |
| `player_brt` | color | no | the default | Triangle color on a line named in `brt_lines`. |
| `brt_lines` | comma list | no | none | Line numbers drawn as the BRT kind (at most 64, each up to 120 characters). |
| `tram_vehicles`, `metro_vehicles`, `rail_vehicles` | comma list | no | none | Parts of vehicle file paths (lower case, `/`), up to 64 of 120 characters. A rail vehicle whose path contains one is drawn as a tram, a metro train or a train. |
| `land`, `water`, `river`, `green`, `grass`, `forest`, `sport`, `farm`, `built`, `rail`, `rail_edge`, `border`, `contour` | color | no | the radar's own | The colors of the dark look. |
| `light_land` ... `light_contour` | color | no | the radar's own | The same for the light look (`light_` plus the key). |

A color is `#rrggbb` or `#rrggbbaa`. An invalid color is dropped: `Dropped: manifest.cfg <key>: not a #rrggbb colour; the game's own is used`. A position is never stored with latitude or longitude: a map may be bent, shortened or fictional.

## Coordinates

Positions in the package are metres of the author's tile grid: `x = tile_column * grid + position_in_tile`, `y = tile_row * grid + position_in_tile`, X east, Y north. They are the same numbers OMSI shows for the map's objects. The module places the player on the same grid: it reads the tile table of the game (`map.tiles.list`), multiplies each tile's column and row by the package's `grid` and adds the position inside the tile ([the radar](../concepts/the-radar.md#positions-and-headings)). This is why `grid` is required: a map whose tiles are not 300 m (for example 585.795 m) would otherwise be off by kilometres far from the origin.

## `surface.oms`

A binary file, little-endian, written by the author's tools. The radar reads it by chunks, only the ones near the player.

| Part | Content |
| --- | --- |
| Header | magic `OMSF`; `u32` version (must be `1`); `f64` origin X and Y; four `f32` bounds (informational, not trusted); `f32` chunk size in metres (1 to 1,000,000) |
| String table | `u32` count (at most 4,000,000), then each name as `u16` length and UTF-8 bytes |
| Chunk index | `u32` count (at most 1,000,000), then per chunk `i32` cx, `i32` cy, `u64` offset, `u32` length |
| Chunk payload | fills: `u32` count, each `u8` class, `u32` triangle count, `f32[6 * n]`; lines: `u32` count, each `u8` class, `u8` flags, `f32` width, `u32` name, `u32` point count, `f32[2 * n]`; labels: `u32` count, each `u8` kind, `u8` rank, `u32` name, `f32` x, `f32` y |

Coordinates in a chunk are offsets from the origin; the reader adds the origin. A chunk `(cx, cy)` covers `[cx * size, (cx + 1) * size)` from the origin on each axis. A name is an index into the string table (`u32` maximum = none). Whole-map layers (borders, labels) are repeated in every chunk they touch.

Classes:

| Fill class | Meaning | | Line class | Meaning | `width` | `flags` |
| ---: | --- | --- | ---: | --- | --- | --- |
| 0 | water | | 0 | river | width in metres | 0 |
| 1 | green (parks) | | 1 | rail | | 0 |
| 2 | built-up | | 2 | border | | 0 |
| 3 | grass | | 3 | street (not drawn; looked up for the name and rank) | | rank 0 to 5 |
| 4 | forest | | 4 | street with limits | speed limit, km/h (0 = none) | limit of heavy vehicles, km/h (0 = the same) |
| 5 | sport | | 5 | contour line | height above sea level, m | 0 |
| 6 | farm | | 6 | index contour line (every fifth) | height | 0 |

Label kinds: `0` district, `1` terminal, `2` town, `3` landmark, `4` water; `rank` 0 to 255 is the label's importance.

Rules of the reader:

- Unknown classes and kinds are skipped, the rest of the chunk is kept.
- A coordinate that is not finite or exceeds 10,000,000 drops that triangle, line or label.
- A chunk is at most 64 MiB. A chunk that cannot be parsed (a count that lies, a truncated payload) is unreadable: it is left out of the picture and counted, and the other chunks are drawn. The header, the string table or the index being unusable switches the whole surface layer off: `Dropped: surface.oms: <reason>` (`too short`, `bad magic`, `implausible origin`, `implausible chunk size`, `string table larger than the file`, `chunk index larger than the file`, `header unreadable: ...`); an unknown version is `Ignored: surface.oms: unknown version <n>`. Index entries outside the file or repeated are counted: `N chunk index entries are outside the file or repeated`.
- At most 256 chunks answer one query; if the window is larger, the chunks nearest its centre are used and the picture is marked as truncated. The reader keeps the 24 most recently used parsed chunks.
- Contour lines get `terrain_offset` added to their height.

### Level of detail

The radar asks for a level of detail with each query.

| Level | When | Returned |
| --- | --- | --- |
| Overview | far zoom | fills, water, rails, borders, labels of rank 160 or more, markers; lines simplified with a tolerance of 6 m; no contour lines |
| Normal | normal zoom | adds index contour lines and labels of rank 64 or more; tolerance 1.5 m |
| Close | close zoom | everything: streets with names and limits, every contour line, every label; no simplification |

## `markers.cfg` and `icons/`

One marker per line, any number of `marker` lines (the key may repeat):

```text
marker = icons/terminal.png | 32 | 1200.5 | 840.0 | Central Terminal
```

| Field | Rule |
| --- | --- |
| `icon` | a PNG directly or below `icons/`, up to 1024 x 1024 pixels and 2 MiB (straight alpha); unsafe, missing, not under `icons/` or not a PNG: dropped |
| `width` | width in pixels at interface size 1, 1 to 1024 |
| `x`, `y` | metres of the author's grid, at most 10,000,000 in absolute value |
| `name` | optional, for people |

At most 20,000 markers; the file is at most 4 MiB. Lines with fewer than four fields, or any bad field, are dropped one by one: `Dropped: markers.cfg:<line>: expected icon | width | x | y | name`, `unsafe icon path: <reason>`, `icons are PNG files under icons/`, `icon <path>: <reason>` (the picture's own error, for example `larger than 1024x1024`), `icon width must be 1..1024 pixels`, `position is not a plausible pair of metres`. An icon that fails once is not tried again for the next lines that name it (`icon unusable (see above)`).

## `regions.cfg`

```text
region = Old Town | 750.0 | 750.0 | 600.0 | 120.0 | 0,0 1500,0 1500,1500 0,1500
```

Fields: `name | label x | label y | box width | box height | polygon`, where the polygon is `x,y` pairs separated by spaces (3 to 20,000 points). The region says which municipality the player is in (the smallest polygon that contains the point wins when they nest); the box is where the name fits as a faint watermark; a box width or height of 0 means there is no readable room and the name is not drawn. At most 2,000 regions, file at most 4 MiB. Errors: `expected name | label x | label y | width | height | polygon`, `label position or box is not plausible`, `polygon needs 3 to 20000 points`, `polygon has a point that is not an x,y pair of plausible metres`.

## `lines.lin`

A tab-separated text file of at most 32 MiB; `#` starts a comment line. Positions are relative to the `ORIGEM` record. The file must start with `RLIN`, tab, `1`.

| Record | Fields (tab-separated) | Meaning |
| --- | --- | --- |
| `RLIN` | `1` | The version. Anything else: `Ignored: lines.lin: unknown version`. |
| `ORIGEM` | x, y | The origin of the positions that follow (default 0, 0). |
| `L` | number, group, name, [kind], [from], [to] | A line form. `kind` is `bus` (default), `tram`, `metro` or `rail`; `from` and `to` are dates `yyyymmdd`, `0` = open. |
| `R` | km, stops, minutes, circular (`0`/`1`), days, model, itinerary | A route (a direction) of the form; at most two per form. |
| `P` | x y x y ... | The way of the route, relative to `ORIGEM`. |
| `S` | x y x y ... | The stops of the route. |

At most 5,000 line forms. A form that is valid on a date is shown on that date (`from <= date <= to`). Unknown records are ignored, so a later version can add records. Errors: `line record needs number, group, name`, `unknown kind of vehicle '<kind>'`, `from/to must be yyyymmdd or 0`, `route record needs km, stops, minutes, circular, days, model, itinerary`, `a line form has at most two routes; extra ones dropped`, `coordinates are not pairs of plausible metres`.

## What each layer gives the radar

| Layer | Without it |
| --- | --- |
| `surface.oms` | no map picture: only the player, nearby vehicles and markers |
| streets in `surface.oms` | no street name and no speed limit |
| `markers.cfg` | no icons |
| `regions.cfg` | no municipality row or watermark |
| `lines.lin` | no line list |
| `manifest.cfg` colors and `player*` | the radar's own colors and arrow |

Which of these layers the module draws in version 0.1.0 is listed in [per-map packages](../concepts/per-map-packages.md#what-the-module-uses).

Related: [using a realmap package](../guides/using-a-realmap-package.md) · [the radar](../concepts/the-radar.md) · [pause-menu package](pause-menu-package.md)
