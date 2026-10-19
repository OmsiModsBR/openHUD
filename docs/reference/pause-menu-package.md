# Pause-menu package format

This page is the normative reference for the `openomsi-pausemenu` package: the folder a map author ships next to a map to give the pause menu its identity (title, symbols, colors, legend, fonts and texts). It describes what `src/OpenHud.Core/Packages/PauseMenu/PauseMenuPackageReader.cs` reads, key by key, with every type, default, limit and the log line of every rejected entry. A step-by-step guide with a complete example is in [making a pause-menu package](../guides/making-a-pause-menu-package.md); what happens when a package is missing is in [per-map packages](../concepts/per-map-packages.md).

Format version: `1`. Related pages: [realmap package](realmap-package.md), [UI language keys](ui-language-keys.md), [settings](settings.md), [errors and degradation](errors-and-degradation.md), [troubleshooting](../guides/troubleshooting.md).

## Where the package lives

```text
<OMSI root>\maps\<Map folder>\
  global.cfg
  openomsi-pausemenu\             the package (fixed name, lower case)
    manifest.cfg                  required: its presence makes the package count
    identity.cfg                  optional: title, symbols, colors, legend
    symbols\                      optional: the pictures the identity names
    fonts\                        optional: font files, fonts.cfg and FONTS-LICENSE.txt
    strings\                      optional: translations of the package's own texts
    LICENSE.txt                   optional: the licence of the package's assets (for people)
    CREDITS.txt                   optional: credits (for people)
```

- OMSI does not read this folder; openHUD does, read-only. A map therefore ships one package for both and loses nothing.
- The names above are fixed. A missing file means that feature is off; it is never an error. Without `manifest.cfg` the package does not exist for openHUD.
- Paths written inside the package are relative to the `openomsi-pausemenu` folder, use forward slashes, and cannot leave it (see [path rules](#path-rules)).
- openHUD never writes to the package.
- `LICENSE.txt` and `CREDITS.txt` are not read by the reader; they are for the people who receive the package.

## How the files are read

All the `.cfg` files share one reader.

| Rule | Behavior |
| --- | --- |
| Encoding | UTF-8, with or without a byte-order mark. Anything else (invalid UTF-8) or a file with a NUL character is refused as a whole: `not valid UTF-8`, `contains a NUL character`. |
| Line ends | CRLF, LF or CR. |
| Size | A `.cfg` file larger than 65,536 bytes (64 KiB) is refused: `larger than 65536 bytes`. At most 20,000 lines are read and a line longer than 8,192 characters is dropped (`line longer than 8192`). |
| Lines | `key = value`. Blank lines and lines whose first non-blank character is `#` are comments. A line without `=` is dropped: `not a key = value line`. |
| Inline comments | A `#` that follows whitespace ends the value (`stop_icon = symbols/stop.svg  # note`). A value that starts with `#` is a color, not a comment. |
| Keys | Not case-sensitive (`Title` is `title`). A repeated key is dropped, the first one counts: `duplicate key 'title' (the first one counts)`. |
| Control characters | A control character (other than a tab) in a value drops the line: `control character in the value of '<key>'`. |

Unknown keys, unknown files and unknown `<kind>` or `<mode>` names are ignored without a message, so a package written for a later release of the format still loads.

### Path rules

Every path in the package (a picture, a font file) must pass these checks before the file is opened. A path that fails invalidates that entry only, never the package.

| Check | Message (`unsafe path: ...`) |
| --- | --- |
| Not empty, at most 240 characters | `empty path`, `path too long` |
| Relative, forward slashes only | `absolute path`, `backslash (use forward slashes)` |
| No drive letter or stream | `drive letter or stream` |
| No control characters or `* ? " < > |` | `invalid character` |
| No empty, `.` or `..` segment | `empty path segment`, `'.' or '..' segment` |
| No segment ending in a dot or a space | `segment ends with a dot or space` |
| No Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9`, with or without an extension) | `reserved device name` |

After the textual check, every component below the package root is verified not to be a link (junction or symbolic link); the folder itself must not be a link either. A file that is or goes through a link is refused (`is or goes through a link`), and so is a path that resolves outside the package (`resolves outside the package`).

## `manifest.cfg`

| Key | Type | Required | Default | Meaning |
| --- | --- | --- | --- | --- |
| `format` | integer | yes | none | Must be `1`. Any other value (or none) ignores the whole package. |
| `name` | text | yes | none | The package's name, for the log. A blank name ignores the package. |
| `author` | text | no | none | Who made the package. |
| `credit` | text | no | none | A short credit line. |

`author`, `credit` and `name` are cut at 512 characters. The files have fixed names, so the manifest does not list them.

| Situation | Log line |
| --- | --- |
| No `openomsi-pausemenu` folder, or no `manifest.cfg` in it | nothing (the package is absent) |
| `manifest.cfg` larger than 64 KiB, unreadable or a link | `Ignored: manifest.cfg: <reason>` |
| `manifest.cfg` is not valid text | `Dropped: manifest.cfg: not valid UTF-8`, then `Ignored: openomsi-pausemenu: manifest.cfg is unusable` |
| `format` is missing or not `1` | `Ignored: openomsi-pausemenu: unknown package format '<value>'` |
| `name` is missing or blank | `Ignored: openomsi-pausemenu: manifest has no name` |

## `identity.cfg`

Every key is optional. A key left out takes its default; a key that cannot be used is dropped with one log line and its default is used. If `identity.cfg` carries its own `format` and it is not `1`, the whole file is skipped: `Dropped: identity.cfg: unknown identity format '<value>'; defaults used`. Keys are written in lower case in the log, with the line number: `identity.cfg:7 symbol.green`.

### Texts

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `title` | text, 1 to 512 characters | the name of the map's folder | The name shown in the menu header. |
| `title.<LANG>` | text | `title` | The same in one language. `<LANG>` is OMSI's three-letter language code (`ENG`, `DEU`, `ESP`, `PTB` ...), any case. |
| `legend.<kind>` | text | the built-in text of that kind ([UI language keys](ui-language-keys.md)) | The legend text of a marker kind. `<kind>` is 1 to 32 characters of `a-z 0-9 _ -`. |
| `legend.<kind>.<LANG>` | text | `legend.<kind>` | The same in one language. |

Errors: `malformed text key` (a key that is not one of the four forms, or a language code that is not three letters); `empty or longer than 512 characters`.

The built-in legend kinds are `station`, `terminal`, `garage`, `stop` and `landmark` (keys `UI_LEGEND_*` in [UI language keys](ui-language-keys.md)). The menu lists these five kinds and every other kind the package adds with a `legend.<kind>` key; an entry's swatch has the color of `color.group.<kind>` when the package defines it, otherwise the accent.

### Symbols and icons

A symbol value is one of:

- `@omsi2`, `@simple` or `@none`: a mark openHUD draws itself, no file. `@omsi2` is the simplified OMSI 2 mark, `@simple` a plain dot, `@none` nothing. Any other `@name` is dropped: `unknown internal symbol '@name'`.
- `symbols/<file>.png` or `symbols/<file>.svg`: a picture directly inside the `symbols` folder (no sub-folders). See [pictures](#pictures).

| Key | Default | Meaning |
| --- | --- | --- |
| `symbol` | none | The map's symbol in the menu header. |
| `symbol.<theme>` | `symbol` | The symbol for one theme (`green`, `dark` ...). `<theme>` is 1 to 32 characters of `a-z 0-9 _ -`; anything else is dropped: `invalid theme name`. |
| `symbol.openomsi` | `symbol` | The symbol for the openHUD layout. |
| `stop_icon` | `@simple` | The icon of a stop: drawn in place of the dot at every marker of the map that has no picture of its own (on the radar and on the map page), and in the legend row of the `stop` kind when `marker_icon.stop` is absent. |
| `marker_icon.<kind>` | the color swatch | The icon of a marker kind (`station`, `terminal` ...): drawn in place of the swatch in the legend row of that kind. An invalid kind: `invalid marker kind`. |
| `mode_icon.<mode>` | none | The icon of a vehicle mode: drawn at the start of the rows of the Lines page for the lines of that mode. `<mode>` is `bus`, `tram`, `metro` or `rail`; another name is ignored without a message. |
| `theme` | `blue` | The default theme name (`a-z 0-9 _ -`, up to 32). An invalid name: `invalid theme name; default used`. |
| `layout` | `classic` | `classic` or `openhud`. Another value: `layout must be classic or openhud; default used`. |

### Colors

A color is `#rrggbb` or `#rrggbbaa` (straight alpha), in any case. An invalid value is dropped: `not a #rrggbb colour; default used`.

| Key | Default | Meaning |
| --- | --- | --- |
| `color.line` | the theme's | The color of the roads on the radar and the map, and of the menu's lines. |
| `color.brt` | the accent | The color of the lines that the realmap manifest lists in `brt_lines`, on the Lines page. |
| `color.accent` | the theme's | The accent color of the menu; also the radar's accent, route and line chip. |
| `color.group.<id>` | the theme's | The color of one line group (the lines of that group on the Lines page, the legend swatch of that kind). `<id>` is 1 to 32 characters of `a-z 0-9 _ -`; anything else: `invalid group id`. |

### Pictures

A picture is validated by its header and its size only; it is decoded later, in the draw layer. A picture that fails is dropped, with the reason, and the key it was named by keeps its default.

| Format | Limits and checks |
| --- | --- |
| PNG | At most 2 MiB; at most 2048 x 2048 pixels; a valid signature and header chunk (the header's checksum is verified), a valid color type and bit depth combination, standard compression, filter and interlace methods. Messages: `larger than 2 MiB`, `too short to be a PNG`, `not a PNG (signature)`, `PNG header chunk missing`, `PNG header checksum mismatch`, `PNG is 3000x10, the limit is 2048x2048`, `PNG colour type/bit depth combination is invalid`, `PNG compression/filter/interlace method unknown`. |
| SVG | At most 256 KiB, valid UTF-8, a root `<svg` element (an XML declaration, comments and a byte order mark may come before it; a `DOCTYPE` may not). The static subset: no `<script`, `<foreignobject`, `<!doctype`, `<!entity`, `<iframe`, `<image`, `<embed`, `<object`, `javascript:`, `@import` or `<?xml-stylesheet`; every `href` and every `url(...)` must point to an internal `#id`; no `on...=` event attribute. Messages: `a DOCTYPE declaration is refused (it can define entities and load external files); remove the <!DOCTYPE ...> line`, `contains '<script', which the static subset does not allow`, `external reference (href)`, `external reference (url())`, `event handler attribute`, `no <svg> root element (only an XML declaration and comments may come before it)`. |
| Anything else | `only PNG and SVG pictures are read` |

Other messages for a symbol value: `pictures live directly in symbols/` (the path is not `symbols/<file>`), `file not found in symbols/ (or beyond the folder limit)`, `unsafe path: <reason>`.

**Folder limit.** `symbols/` admits its first 64 files and 16 MiB, in name order (case-sensitive, ordinal). Each later file is dropped with `Dropped: symbols/<file>: beyond the folder limit of 64 files / 16 MiB` and cannot be used. Files in sub-folders of `symbols` are not seen.

**High-resolution siblings.** For a picture `symbols/name.png` the reader also looks for `symbols/name@2x.png` (the same extension) and, if it validates, keeps it next to the picture for a high interface scale. A sibling that fails is dropped under its own name (`symbols/name@2x.png`) and the main picture stays.

### The SVG subset

An SVG that passes the checks above is converted, on the worker thread when the map's packages load, into the shapes of the scene protocol (rectangles, circles, lines and polygons), fitted into the slot with its proportion kept. Nothing is rasterized; the draw layer needs nothing for it. The converter is stricter than the checks: it refuses the whole file for what it must not read, skips what it cannot draw, and enforces hard limits.

| | What it does |
| --- | --- |
| Drawn | `svg` (with `viewBox`, or `width` and `height`), `g`, `a`, `rect` (with `rx`, `ry`), `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path` with `M L H V C S Q T A Z` in absolute and relative form |
| Properties | `fill`, `stroke`, `stroke-width`, `opacity`, `fill-opacity`, `stroke-opacity`, `stroke-linecap` (round or butt), `color` (for `currentColor`), also in a `style` attribute; inherited down the groups, opacities multiply |
| Transforms | `translate`, `scale`, `rotate` (with a center), `skewX`, `skewY`, `matrix`, on a group or a shape, at most 16 per attribute |
| Colors | `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()` and `rgba()` (numbers or percentages), `none`, `transparent`, `currentColor`, and about sixty named colors |
| Skipped with a reason | `use`, `filter` and the `filter` attribute, `mask`, `clipPath`, `pattern`, gradients (a `fill="url(#id)"` is none), `text`, `marker`, `symbol`, `switch`, nested `svg`, animation; `display="none"` and hidden elements are simply not drawn |
| Refused as a whole | `script`, `style`, `foreignObject`, `iframe`, `embed`, `object`, `image`; a `DOCTYPE` or an entity; an external reference in `href` or `url()`; an event attribute; an `xml-stylesheet`; malformed XML |

Curves and arcs are flattened to polylines within 0.15 % of the view box's larger side, with at most 48 segments per curve and 64 per arc. Each sub-path of a path becomes one polygon, filled on its own, so a hole in a path (`fill-rule="evenodd"`) is filled; draw a hole as a separate shape in the background color if you need one. Dashes are drawn solid. A path with an error in its data is drawn up to the error.

| Limit | Value |
| --- | --- |
| File | 256 KiB |
| Elements / nesting depth | 2,000 / 32 |
| Attributes on one element | 64 |
| Numbers | at most 40 characters, absolute value at most 1,000,000; `NaN` and infinities are errors |
| Points after flattening | 4,000 in a drawing and 1,500 in one shape for the symbol and the two marks, 600 and 300 for each icon |
| Scene commands per drawing | 160 for the symbol and the two marks, 32 for each icon (stop, marker kind, mode); more and the drawing is refused with `needs more than N drawing commands (the per-symbol budget); simplify the drawing` |
| Pictures from a package | 24 PNGs in all; 16 marker kinds and 16 modes |

A refused drawing is replaced by the generic mark of its slot and the reason is logged, for example `symbols/stop.svg (stop_icon): needs more than 32 drawing commands ...; the generic mark is drawn`. Skipped parts are listed (the first few) in the same log.

### The two marks

| File | Purpose | Fallback when missing or invalid |
| --- | --- | --- |
| `symbols/omsi2-logo.png` or `.svg` | The OMSI 2 logo, at the right of the menu header. openHUD ships no copy of the trademark logo. | the package's `symbol`, then a neutral outlined tag with a steering wheel (openHUD writes no brand text) |
| `symbols/openomsi.png` or `.svg` | The openOMSI mark, next to it. | none |

Both follow the picture rules; when both a `.png` and a `.svg` exist, the PNG is used. The package author is responsible for the right to use the logo in the package ([fonts, symbols and licensing](../concepts/fonts-symbols-and-licensing.md)).

## `fonts/`

openHUD ships no font file. A package may carry the fonts its look needs; without them the draw layer uses the system UI font.

| File | Required | Meaning |
| --- | --- | --- |
| `fonts/*.ttf`, `fonts/*.otf` | no | The font files, directly in `fonts` (no sub-folders). Static TrueType or OpenType only. At most 12 files, in name order; later ones: `beyond the limit of 12 font files`. |
| `fonts/FONTS-LICENSE.txt` | **yes, when any font file is present** | The licence of every font file. At least one byte. The reader does not interpret it. |
| `fonts/fonts.cfg` | **yes, when any font file is present** | Which file serves which role. |

Without `FONTS-LICENSE.txt` (or with an empty one) or without `fonts.cfg`, no font of the package is used and one line says why, for example `Dropped: fonts/: fonts/FONTS-LICENSE.txt is missing or empty; the system font is used`.

`fonts/fonts.cfg`:

| Key | Value | Default weight | Meaning |
| --- | --- | ---: | --- |
| `format` | `1` | | Optional; another value refuses the fonts: `unknown fonts format '<value>'`. |
| `regular` | `<file> [ \| <weight> [ \| <note> ] ]` | 400 | Body text. |
| `medium` | same | 500 | Tabs and captions. |
| `bold` | same | 700 | Titles, buttons. |
| `black` | same | 900 | Heavy numbers. |
| `condensed` | same | 700 | Narrow text (hints, times, codes). |

- `<file>` is a file name directly in `fonts` (`unsafe path: ...`, `font files live directly in fonts/`, `font file not found (or beyond the limit)`).
- `<weight>` is a whole number from 100 to 900 (`weight must be 100..900; the role's default is used`). It decides which face stands in for a role you leave out; it does not change the file.
- `<note>` is free text for people.
- A font file must be a static TrueType or OpenType file of at most 4 MiB with a valid table directory: `larger than 4 MiB`, `not a TrueType/OpenType font (collections and web fonts are not read)`, `variable font (instance it offline)`, `table directory truncated`, `no head table or outlines`.
- A role left out takes the nearest weight among the roles present (a tie goes to the lighter one). No usable role at all: `no usable font in fonts/fonts.cfg; the system font is used`.
- Unknown role names are ignored.

Fonts are registered privately for the session by the draw layer (never installed in Windows). A character a font lacks is drawn with the system UI font, never as `?`.

## `strings/`

`strings/<LANG>.cfg` (`ENG.cfg`, `PTB.cfg`, `DEU.cfg` ...; `<LANG>` is exactly three letters, any case) holds the package's own texts for one language, as `key = value`.

| Rule | Value |
| --- | --- |
| Files | at most 32; later ones: `beyond the limit of 32 files` |
| Size | 64 KiB per file |
| Keys | `a-z 0-9 _ . -`, 1 to 64 characters, lower-cased: `malformed key` |
| Values | 1 to 512 characters: `empty or longer than 512` |
| Strings per file | at most 512: `more than 512 strings; the rest is not read` |
| `format` | accepted and ignored |

The menu asks for the key `title` and for `legend.<kind>`. For each one the text comes from, in order:

1. `strings/<LANG>.cfg` of the language in use, key `title` or `legend.<kind>`;
2. `identity.cfg`, `title.<LANG>` or `legend.<kind>.<LANG>`;
3. `identity.cfg`, `title` or `legend.<kind>`;
4. openHUD's built-in text (for a title: the map folder's name).

`<LANG>` is the language setting's code when `language` is an explicit code, otherwise OMSI's own language code ([internationalization](../concepts/internationalization.md)). Other keys of a `strings` file are read and kept.

## Limits at a glance

| What | Limit |
| --- | --- |
| `.cfg` file | 64 KiB, 20,000 lines, 8,192 characters per line |
| Any text value | 512 characters |
| `symbols/` | 64 files, 16 MiB; one picture 2 MiB and 2048 x 2048 (PNG), 256 KiB (SVG) |
| `fonts/` | 12 font files, 4 MiB each |
| `strings/` | 32 files, 512 strings each |
| The package log | 200 lines; beyond that `N more entries not listed` |

## Template

Copy these files into `openomsi-pausemenu` and change the values. They name pictures and fonts that you supply: the file names are the ones of the [worked example](../guides/making-a-pause-menu-package.md#the-binary-files) and every line is accepted by the reader. Keep only the keys you need.

#### `template/manifest.cfg`

```ini
# openomsi-pausemenu/manifest.cfg
format = 1
name = Example City pause menu
author = Example Maps Studio
credit = Map by Example Maps Studio
```

#### `template/identity.cfg`

```ini
# openomsi-pausemenu/identity.cfg - every key is optional
format = 1

# Header
title = Example City
title.ENG = Example City
title.PTB = Cidade Exemplo
title.DEU = Beispielstadt

# Symbols: a picture under symbols/, or @omsi2, @simple, @none
symbol = symbols/map-symbol.png
symbol.green = symbols/map-symbol-green.png
symbol.openomsi = symbols/map-symbol-wide.png

# Look
theme = green
layout = classic
color.line = #3a7bd5
color.brt = #1e7b34
color.accent = #24589a
color.group.express = #c0392b

# Map icons
stop_icon = symbols/stop.svg
marker_icon.station = symbols/station.png
marker_icon.terminal = symbols/terminal.png
marker_icon.garage = @simple
mode_icon.bus = symbols/mode-bus.svg
mode_icon.tram = symbols/mode-tram.svg

# Legend texts
legend.station = Station
legend.station.PTB = Estação
legend.station.DEU = Bahnhof
legend.terminal = Terminal
legend.garage = Depot
```

#### `template/fonts/fonts.cfg`

```ini
# openomsi-pausemenu/fonts/fonts.cfg
# role = file | weight | note
format = 1
regular = ExampleSans-Regular.ttf | 400
medium = ExampleSans-Regular.ttf | 500 | tabs and captions
bold = ExampleSans-Bold.ttf | 700
black = ExampleSans-Bold.ttf | 900
condensed = ExampleSans-Regular.ttf | 400 | narrow text
```

#### `template/strings/PTB.cfg`

```ini
# openomsi-pausemenu/strings/PTB.cfg
title = Cidade Exemplo
legend.terminal = Terminal
```

Next to them go `fonts/FONTS-LICENSE.txt` (the licence of your font files; required whenever `fonts` holds a font), the pictures named above, and optionally `LICENSE.txt` and `CREDITS.txt`.

Related: [making a pause-menu package](../guides/making-a-pause-menu-package.md) · [per-map packages](../concepts/per-map-packages.md) · [realmap package](realmap-package.md) · [UI language keys](ui-language-keys.md) · [errors and degradation](errors-and-degradation.md)
