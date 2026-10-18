# Using a realmap package with the radar

A realmap package gives the radar a real map to draw: water, parks, built-up areas, relief, the name and the speed limit of the street you are on, the icons of stations and depots, the municipality you are in, and the lines of the map. This guide shows how a map author lays out a package, with a small complete example whose text files are printed in full and checked by the test suite, and how a player sees the difference. The format is in the [realmap package reference](../reference/realmap-package.md).

Related pages: [the radar](../concepts/the-radar.md), [per-map packages](../concepts/per-map-packages.md), [radar and interface settings](radar-and-interface-settings.md), [troubleshooting](troubleshooting.md), [making a pause-menu package](making-a-pause-menu-package.md).

The example is for an invented map, "Example City", with an invented "Line 12".

## For players: what you do

Nothing. If the map you play ships an `openomsi-realmap` folder next to its `global.cfg`, the module finds it through OmsiLaunch and the radar uses it; if not, the radar draws without it. To check that the module found the package, read the module's events at the start of the session ([troubleshooting](troubleshooting.md#reading-the-modules-events-and-notices)).

## For map authors: step by step

### Step 1. Create the folder

```text
<OMSI root>\maps\Example City\
  global.cfg
  openomsi-realmap\
    manifest.cfg
    surface.oms
    markers.cfg
    regions.cfg
    lines.lin
    icons\
      terminal.png
      depot.png
```

The folder is named `openomsi-realmap`, in lower case. Only `manifest.cfg` is required. The files are made beforehand by your own tools from whatever data you have; the module only reads them.

### Step 2. Write `manifest.cfg`

#### `openomsi-realmap/manifest.cfg`

```ini
# Realmap package of the Example City map
format = 1
name = Example City
grid = 300
credit = Map data by Example Maps Studio. Fitted to the map's own roads.
north = 0
alignment = fitted
terrain_offset = 0

# Colors of the dark look; the light_ twin is the light look
water = #7fb2e5
river = #7fb2e5
green = #9ccf8f
built = #2a2a2a
light_water = #5a98d1

# The player's marker
player = triangle
player_free = #ffffff
player_line = #3a7bd5
player_brt = #1e7b34
brt_lines = 12, 15

# Which vehicles are drawn as trams, metro trains and trains
tram_vehicles = vehicles/tram_example/
metro_vehicles = vehicles/metro_example/
rail_vehicles = vehicles/train_example/
```

`format` and `grid` are required. `grid` is the size in metres of the tiles in the numbers you use for positions: 300 for a plain map, another number for a map laid out otherwise. A wrong `grid` puts the player in the wrong place on the radar, because the module converts the game's tile numbers to your metres with it.

### Step 3. Build `surface.oms`

`surface.oms` is a binary file written by your tools ([the file layout](../reference/realmap-package.md#surfaceoms)). The example package has one chunk, `(0, 0)`, with origin `(0, 0)` and chunks of 2000 m:

| Content | Value |
| --- | --- |
| Water (fill class 0) | one triangle with corners `(100, 100)`, `(400, 100)`, `(100, 400)` |
| Park (fill class 1) | one triangle with corners `(1500, 1500)`, `(1700, 1500)`, `(1500, 1700)` |
| River (line class 0) | a line of 6 m width through `(0, 500)`, `(900, 520)`, `(1900, 500)`, named "Example River" |
| Street (line class 3, rank 2) | `(200, 200)` to `(800, 200)`, named "Main Street" |
| Street with limits (line class 4) | `(200, 300)` to `(800, 300)`, limit 50, heavy-vehicle limit 40, named "River Avenue" |
| Label (kind 0, district, rank 200) | at `(500, 500)`, "Old Town" |
| Label (kind 1, terminal, rank 255) | at `(1200, 840)`, "Central Terminal" |

### Step 4. Add icons and `markers.cfg`

Two PNG files in `icons`, up to 1024 x 1024 pixels: `icons/terminal.png` (64 x 64) and `icons/depot.png` (96 x 96) in the example.

#### `openomsi-realmap/markers.cfg`

```ini
# icon | width in pixels | x | y | name
marker = icons/terminal.png | 32 | 1200.5 | 840.0 | Central Terminal
marker = icons/depot.png | 48 | 650.0 | 410.5 | East Depot
```

### Step 5. Add `regions.cfg`

#### `openomsi-realmap/regions.cfg`

```ini
# region = name | label x | label y | box width m | box height m | polygon
region = Old Town | 750 | 750 | 600 | 120 | 0,0 1500,0 1500,1500 0,1500
region = Harbor District | 1700 | 400 | 0 | 0 | 1500,0 2000,0 2000,800 1500,800
```

A box width or height of 0 means the name does not fit anywhere readable and is not drawn; the district is still known for the "you are in" row.

### Step 6. Add `lines.lin`

Fields are separated by one TAB character; positions are relative to the `ORIGEM` record. The block below uses real tab characters: copy it with them.

#### `openomsi-realmap/lines.lin`

```text
# lines of Example City
RLIN	1
ORIGEM	0.0	0.0
L	12	EXP	Old Town - East Depot	bus	0	0
R	4.2	9	14	0	Every day	Articulated	Central Terminal - East Depot
P	1200.5 840.0 900.0 700.0 650.0 410.5
S	1200.5 840.0 650.0 410.5
R	4.3	9	15	0	Every day	Articulated	East Depot - Central Terminal
P	650.0 410.5 900.0 700.0 1200.5 840.0
S	650.0 410.5 1200.5 840.0
L	12	EXP	Old Town - East Depot (until 2019)	bus	0	20191231
R	3.8	8	12	0	Mon-Fri	Standard	Old Town - East Depot
P	1200.5 840.0 650.0 410.5
S	1200.5 840.0 650.0 410.5
L	T1	TRM	Harbor Tram	tram	19900101	0
R	2.0	5	8	1	Every day	Tram	Harbor loop
P	1700.0 400.0 1900.0 400.0 1900.0 600.0 1700.0 600.0
S	1700.0 400.0 1900.0 600.0
```

A line form has one or two routes (a route is a direction). The panel shows the forms valid on the session's date: with `from` and `to` as `yyyymmdd` (`0` = open), the first form of Line 12 runs in every year and the second one only until the end of 2019.

### Step 7. Add credit and licence

Put the licence of your data in `openomsi-realmap/LICENSE.txt`. The reader does not open it; it is for the people who receive the package.

## What the example looks like to the reader

When the example above is read, the package is loaded with all of its layers (surface, markers, regions and transit lines), and the reader reports no rejected entry. In the example:

- the manifest has `grid = 300`, `alignment = fitted`, a triangle player marker and the colors above;
- the marker file gives two markers with their two icons;
- the region file gives two municipalities, the first with a readable label box and the second without;
- the line file gives three line forms: two of Line 12 (a bus line) and one tram line; on a date in 2021 the forms valid are the current Line 12 and the tram, and on a date in 2019 both forms of Line 12 and the tram.

## Mistakes the reader reports

Each of these drops one entry; the rest of the package stays. The messages are those of the reader (the line is `Dropped: <file>:<line>: <reason>`):

| You wrote | The log says |
| --- | --- |
| `marker = icons/terminal.png \| 32 \| 1200` (missing fields) | `markers.cfg:<line>: expected icon \| width \| x \| y \| name` |
| `marker = elsewhere/x.png \| 30 \| 1 \| 1` | `icons are PNG files under icons/` |
| `marker = icons/missing.png \| 30 \| 1 \| 1` | `icon icons/missing.png: not found` |
| an icon larger than 1024 x 1024 | `icon icons/huge.png: larger than 1024x1024` |
| `region = Broken \| 1 \| 1 \| 1 \| 1 \| 1,1 2,2` | `polygon needs 3 to 20000 points` |
| `L` with a kind of vehicle that does not exist (`hovercraft`) | `unknown kind of vehicle 'hovercraft'` |
| `grid = 0` in the manifest | `Ignored: openomsi-realmap: manifest grid is missing or not a plausible tile size` |

Related: [realmap package reference](../reference/realmap-package.md) · [the radar](../concepts/the-radar.md) · [troubleshooting](troubleshooting.md)
