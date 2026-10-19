# Making a pause-menu package for your map

This guide takes a map author from an empty folder to a finished `openomsi-pausemenu` package, step by step, with a complete example whose every text file is printed here in full. The example is checked by the test suite: the files below are read with the same reader the module uses and must produce no rejected entry ([test coverage](../status/test-coverage.md)). The format itself, key by key, is the [pause-menu package reference](../reference/pause-menu-package.md).

A package is optional. Without one, openHUD's pause menu is the generic OMSI 2 menu: the title is the folder name of the map, there is no map symbol, the font is the system UI font and the colors are the default blue theme ([per-map packages](../concepts/per-map-packages.md)). A package changes that menu to look like your map's.

Related pages: [reference: pause-menu package](../reference/pause-menu-package.md), [fonts, symbols and licensing](../concepts/fonts-symbols-and-licensing.md), [internationalization](../concepts/internationalization.md), [troubleshooting](troubleshooting.md), [realmap packages](using-a-realmap-package.md).

The example is for an invented map, "Example City", with an invented line "Line 12". Use your own names.

## Step 1. Create the folder

Inside your map's folder create `openomsi-pausemenu`, exactly that name in lower case:

```text
<OMSI root>\maps\Example City\
  global.cfg
  openomsi-pausemenu\
```

If you distribute the map as a mod, put the folder inside the map's folder in the archive, next to `global.cfg`, so that it installs with the map. OMSI itself ignores the folder; nothing in the game changes.

## Step 2. Write `manifest.cfg`

This is the only required file. Without it the package does not exist.

#### `openomsi-pausemenu/manifest.cfg`

```ini
# Pause-menu package of the Example City map
format = 1
name = Example City pause menu
author = Example Maps Studio
credit = Map by Example Maps Studio. Symbols by Example Maps Studio.
```

`format = 1` and a non-blank `name` are required. Everything else is optional.

## Step 3. Add the symbols

Create `symbols` and put your pictures directly in it (no sub-folders). PNG and SVG are read. The example uses these files:

### The binary files

| File | Kind | Size |
| --- | --- | --- |
| `symbols/map-symbol.png` | PNG | 256 x 256 |
| `symbols/map-symbol@2x.png` | PNG | 512 x 512 |
| `symbols/map-symbol-green.png` | PNG | 256 x 256 |
| `symbols/map-symbol-wide.png` | PNG | 512 x 128 |
| `symbols/station.png` | PNG | 64 x 64 |
| `symbols/terminal.png` | PNG | 64 x 64 |
| `symbols/omsi2-logo.png` | PNG | 300 x 120 |
| `symbols/openomsi.png` | PNG | 200 x 60 |
| `fonts/ExampleSans-Regular.ttf` | TrueType | static |
| `fonts/ExampleSans-Bold.ttf` | TrueType | static |

You make these pictures; the sizes are the sizes of the example. Rules to keep in mind:

- A PNG is at most 2048 x 2048 pixels and 2 MiB. Use RGBA with straight (non-premultiplied) alpha.
- Make a symbol square or give it the proportion you want; openHUD keeps the proportion and fits the picture in its slot, it never stretches it.
- `map-symbol@2x.png` is the optional high-resolution sibling of `map-symbol.png`: same name plus `@2x`, same extension, used when the interface scale is high.
- `omsi2-logo.png` and `openomsi.png` are optional. openHUD ships no copy of the OMSI 2 trademark logo; if you want it in the menu, you supply it here and you are responsible for the right to do so ([licensing](../concepts/fonts-symbols-and-licensing.md)).
- The fonts are real files that you obtain with a licence that allows redistribution inside your package. Never copy a commercial font into a package.

The three SVG files below are static and contain only shapes. An SVG may not contain a script, a `DOCTYPE`, an embedded image or any external reference; many editors add a `DOCTYPE` on export, so remove it.

#### `openomsi-pausemenu/symbols/stop.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <circle cx="12" cy="12" r="10" fill="#ffffff"/>
  <circle cx="12" cy="12" r="5" fill="#24589a"/>
</svg>
```

#### `openomsi-pausemenu/symbols/mode-bus.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <rect x="3" y="4" width="18" height="14" rx="3" fill="#ffffff"/>
  <rect x="5" y="7" width="14" height="5" fill="#24589a"/>
  <circle cx="8" cy="19" r="2" fill="#ffffff"/>
  <circle cx="16" cy="19" r="2" fill="#ffffff"/>
</svg>
```

#### `openomsi-pausemenu/symbols/mode-tram.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <rect x="5" y="3" width="14" height="15" rx="3" fill="#ffffff"/>
  <rect x="7" y="6" width="10" height="5" fill="#24589a"/>
  <path d="M8 21 L12 18 L16 21" fill="none" stroke="#ffffff" stroke-width="2"/>
</svg>
```

## Step 4. Write `identity.cfg`

The identity names the title, the symbols, the colors and the legend. Every key is optional; a key you leave out takes the generic default.

#### `openomsi-pausemenu/identity.cfg`

```ini
# Identity of the Example City pause menu
format = 1

# Header: the title in OMSI's language codes (ENG, PTB, DEU, ...)
title = Example City
title.ENG = Example City
title.PTB = Cidade Exemplo
title.DEU = Beispielstadt

# Symbols: pictures under symbols/ or internal marks (@omsi2, @simple, @none)
symbol = symbols/map-symbol.png
symbol.green = symbols/map-symbol-green.png
symbol.openomsi = symbols/map-symbol-wide.png

# Look
theme = green
layout = classic
color.line = #3a7bd5          # the map's lines
color.brt = #1e7b34           # the bus-rapid-transit group
color.accent = #24589a
color.group.express = #c0392b # Line 12 and the other express lines

# Map icons
stop_icon = symbols/stop.svg
marker_icon.station = symbols/station.png
marker_icon.terminal = symbols/terminal.png
marker_icon.garage = @simple
mode_icon.bus = symbols/mode-bus.svg
mode_icon.tram = symbols/mode-tram.svg

# Legend texts, with the language variants
legend.station = Station
legend.station.PTB = Estação
legend.station.DEU = Bahnhof
legend.terminal = Terminal
legend.garage = Depot
```

Things worth knowing:

- Comments start with `#` at the beginning of a line, or after a space at the end of a value (`color.line = #3a7bd5   # note`). A value that starts with `#` is a color.
- The language suffix is OMSI's three-letter code: `title.ENG`, `title.PTB`, `title.DEU`, `title.ESP`. The menu uses the variant of the language in use and `title` otherwise ([internationalization](../concepts/internationalization.md)).
- A picture path always starts with `symbols/`, uses forward slashes and names a file directly in that folder.
- `@simple`, `@omsi2` and `@none` are marks openHUD draws itself: no file needed.

## Step 5. Add fonts, with their licence (optional)

If your map's look needs a typeface, put static `.ttf` or `.otf` files in `fonts`, say which file serves which role in `fonts.cfg`, and include the licence of every font in `FONTS-LICENSE.txt`. Without a licence file the fonts are not used at all.

#### `openomsi-pausemenu/fonts/fonts.cfg`

```ini
# role = file | weight | note
format = 1
regular = ExampleSans-Regular.ttf | 400
medium = ExampleSans-Regular.ttf | 500 | tabs and captions
bold = ExampleSans-Bold.ttf | 700
black = ExampleSans-Bold.ttf | 900
condensed = ExampleSans-Regular.ttf | 400 | narrow text
```

#### `openomsi-pausemenu/fonts/FONTS-LICENSE.txt`

```text
ExampleSans-Regular.ttf and ExampleSans-Bold.ttf
Copyright (c) Example Type Foundry.
Distributed under the licence stated by the foundry, which allows
redistribution inside this package. Replace this text with the full
licence text of every font file you include.
```

The five roles are `regular`, `medium`, `bold`, `black` and `condensed`. A role you leave out takes the face with the nearest weight among the ones you listed. Variable fonts and font collections (`.ttc`) are not read: create static instances with a font tool first.

## Step 6. Add per-language texts (optional)

For languages where one file per language is more convenient than suffixed keys, add `strings/<LANG>.cfg`. The menu asks for `title` and `legend.<kind>`, and a `strings` file wins over `identity.cfg` for the language it names.

#### `openomsi-pausemenu/strings/ENG.cfg`

```ini
format = 1
title = Example City
legend.terminal = Terminal
```

#### `openomsi-pausemenu/strings/PTB.cfg`

```ini
format = 1
title = Cidade Exemplo
legend.terminal = Terminal da Linha 12
```

#### `openomsi-pausemenu/strings/DEU.cfg`

```ini
format = 1
title = Beispielstadt
legend.terminal = Endhaltestelle der Linie 12
```

## Step 7. Add licence and credits for people (optional)

The reader never opens these two files; they are for whoever receives the package.

#### `openomsi-pausemenu/LICENSE.txt`

```text
The pictures in symbols/ are (c) Example Maps Studio and may be
redistributed with the Example City map. The fonts have their own
licence in fonts/FONTS-LICENSE.txt.
```

#### `openomsi-pausemenu/CREDITS.txt`

```text
Example City map: Example Maps Studio
Symbols: Example Maps Studio
```

## The finished package

```text
openomsi-pausemenu\
  manifest.cfg
  identity.cfg
  LICENSE.txt
  CREDITS.txt
  symbols\
    map-symbol.png
    map-symbol@2x.png
    map-symbol-green.png
    map-symbol-wide.png
    station.png
    terminal.png
    omsi2-logo.png
    openomsi.png
    stop.svg
    mode-bus.svg
    mode-tram.svg
  fonts\
    fonts.cfg
    FONTS-LICENSE.txt
    ExampleSans-Regular.ttf
    ExampleSans-Bold.ttf
  strings\
    ENG.cfg
    PTB.cfg
    DEU.cfg
```

## Step 8. Check it

There is no separate checker program: the module's own reader is the check, and every problem it finds becomes one line of the package log ([troubleshooting](troubleshooting.md#reading-the-modules-events-and-notices)). Before you ship, go through this list, which is what the reader checks:

1. The folder is named `openomsi-pausemenu` and `manifest.cfg` exists, with `format = 1` and a `name`.
2. Every `.cfg` file is UTF-8 text of at most 64 KiB, with `key = value` lines.
3. No key appears twice in a file (the second one is dropped).
4. Every picture path starts with `symbols/`, uses `/`, and exists directly in `symbols`.
5. Every picture is a PNG of at most 2048 x 2048 and 2 MiB, or an SVG of at most 256 KiB without a script, `DOCTYPE`, embedded image or external reference.
6. `symbols` holds at most 64 files and 16 MiB.
7. Every color is `#rrggbb` or `#rrggbbaa`.
8. If `fonts` holds a font file: `fonts.cfg` and a non-empty `FONTS-LICENSE.txt` exist; every font is a static TrueType or OpenType file of at most 4 MiB; at most 12 files.
9. Every language code is three letters.
10. Nothing in the package is a link (junction or symbolic link) to somewhere else.

Then start a session with the module ([first session](../getting-started/first-session.md)) and read the log: a clean package prints no line about itself.

## What the log says for each rejected entry

A bad entry is dropped on its own and the rest of the package stays; the log line starts with `Dropped` (an entry was left out), `Ignored` (the whole package or a whole feature was not used) or `Info` (a note, such as the log being cut at 200 lines). The line shows the file and the line number, then the reason.

| You wrote | The log says |
| --- | --- |
| `symbol.green = symbols/missing.png` | `Dropped: identity.cfg:<line> symbol.green: file not found in symbols/ (or beyond the folder limit)` |
| `color.accent = blue` | `Dropped: identity.cfg:<line> color.accent: not a #rrggbb colour; default used` |
| `symbol = ../manifest.cfg` | `Dropped: identity.cfg:<line> symbol: unsafe path: '.' or '..' segment` |
| `symbol = C:/logo.png` | `Dropped: identity.cfg:<line> symbol: unsafe path: drive letter or stream` |
| `symbol = logo.png` | `Dropped: identity.cfg:<line> symbol: pictures live directly in symbols/` |
| `symbol = @brand` | `Dropped: identity.cfg:<line> symbol: unknown internal symbol '@brand'` |
| `title.EN = Hello` (a language code of two letters) | `Dropped: identity.cfg:<line> title.en: malformed text key` |
| `layout = fancy` | `Dropped: identity.cfg:<line> layout: layout must be classic or openhud; default used` |
| `title` twice in the file | `Dropped: identity.cfg:<line>: duplicate key 'title' (the first one counts)` |
| a line without `=` | `Dropped: identity.cfg:<line>: not a key = value line` |
| an SVG that starts with a `DOCTYPE` (only an XML declaration and comments may come before `<svg`) | `Dropped: identity.cfg:<line> stop_icon: a DOCTYPE declaration is refused (it can define entities and load external files); remove the <!DOCTYPE ...> line` |
| a PNG of 3000 x 10 pixels | `Dropped: identity.cfg:<line> symbol.green: PNG is 3000x10, the limit is 2048x2048` |
| a file named `.png` that is not a PNG | `Dropped: identity.cfg:<line> symbol.green: not a PNG (signature)` |
| fonts without `FONTS-LICENSE.txt` | `Dropped: fonts/: fonts/FONTS-LICENSE.txt is missing or empty; the system font is used` |
| a font weight of 1000 | `Dropped: fonts/fonts.cfg:<line> bold: weight must be 100..900; the role's default is used` |
| a variable font | `Dropped: fonts/fonts.cfg:<line> bold: variable font (instance it offline)` |
| a font file that is not in `fonts` | `Dropped: fonts/fonts.cfg:<line> bold: font file not found (or beyond the limit)` |
| a `strings` key with a space in it | `Dropped: strings/ENG.cfg:<line>: malformed key` |
| `format = 2` in `manifest.cfg` | `Ignored: openomsi-pausemenu: unknown package format '2'` |
| no `name` in `manifest.cfg` | `Ignored: openomsi-pausemenu: manifest has no name` |

The reasons not listed here are in the [reference](../reference/pause-menu-package.md): each key and file has its messages next to it.

Related: [pause-menu package reference](../reference/pause-menu-package.md) · [troubleshooting](troubleshooting.md) · [using a realmap package](using-a-realmap-package.md)
