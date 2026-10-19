# Known limitations

This page lists, from the code, what openHUD `0.1.0` does not do, does only partly, or does at an accepted risk, so that you do not build expectations on behavior it does not provide. Where the manifest, the notice, the specification drafts and the code disagree, the code wins; the differences found are listed at the end.

Related pages: [validation status](../status/validation.md), [compatibility](compatibility.md), [errors and degradation](errors-and-degradation.md), [FAQ](faq.md).

## Validation

| Limitation | Detail |
| --- | --- |
| Not yet run in a real OMSI | The module is built and covered by offline tests only. The take-over of the native pause menu, the input capture in the real window, the Direct3D hooks on the real device and the position and unit conventions the model infers are confirmed in rounds that the owner schedules ([validation status](../status/validation.md)). |
| Runtime modules are `EXPERIMENTAL` | The feature of OmsiLaunch that carries this module is experimental. |

## The radar

| Limitation | Detail |
| --- | --- |
| No next stop, line or delay on the strip | The strip shows the speed and the clock. The catalog gives no source for the next stop, the line or the delay, so those slots stay empty (the interface table has their texts). |
| The speed unit is an assumption | The catalog's `ground_speed` is read as metres per second, which is inferred, not confirmed (one named constant, `FrameSource.GroundSpeedToMetresPerSecond`). The speed on the strip is only as right as that assumption until the runtime round confirms it ([validation status](../status/validation.md)). |
| Heading up only | The radar has no north-up mode setting; the Map page is north up. |
| Needs a resolved player tile | Without the player's tile in the map's tile table the radar draws nothing. |
| Vehicles are one shape | Every vehicle around you is drawn with the same kind (no bus, tram or truck distinction). |
| No route | The route of the active trip is not drawn. |

## Packages

| Limitation | Detail |
| --- | --- |
| Parts of the formats are read but not drawn | Pause-menu: `author` (for people). Realmap: `player_line` and `player_brt` (they need the player's active line, which the catalog does not give), the `light_*` colors (the radar is always dark), the `*_vehicles` lists, street names and speed limits, the municipality row, `alignment`, the manifest's `credit` ([per-map packages](../concepts/per-map-packages.md#what-the-module-uses)). |
| SVG is a static subset | Rectangles, circles, ellipses, lines, polygons and paths with flat colors are drawn as scene shapes. Gradients, patterns, filters, masks, clip paths, text, `use` and animation are skipped with a reason; `evenodd` holes in a path are filled; dashes are drawn solid; a drawing over its budget of commands is refused and the generic mark is drawn ([the SVG subset](pause-menu-package.md#the-svg-subset)). |
| `theme` and `layout` only choose the symbol | They do not change the menu's layout or colors. |
| The pause-menu package is looked up in the map's own folder only | A package placed in another content root is not found. |
| A package is read once per map | Edits show after a restart of the session. At most eight maps are loaded in one session. |
| At most 48 marker icons and one symbol | Further icons are left out (a note in the `packages` event). |

## The pause menu

| Limitation | Detail |
| --- | --- |
| Needs three conditions at the start | The draw layer, `ui.pause-menu.replace` and input capture; otherwise the native menu stays. |
| Lines page is a list | Selecting or activating a line does nothing further. |
| Mouse mapping needs the view size | The size comes from the draw layer after its first frame; before that the menu assumes 1920 x 1080. |
| One menu at a time | Another module that captures input or replaces the pause menu prevents it ([coexisting](../guides/coexisting.md)). |

## Text and language

| Limitation | Detail |
| --- | --- |
| Interface in three languages | English, Portuguese (Brazil) and German. |
| `language=auto` resolves to English | The module resolves the language from the setting only; it does not yet read OMSI's language. Set `language` explicitly ([internationalization](../concepts/internationalization.md)). |
| Left-to-right, no shaping, no kerning | Right-to-left and complex scripts are not supported; characters outside the Basic Multilingual Plane are drawn blank. |
| Glyphs come from GDI | Fonts are registered process-private; a font that Windows refuses is reported and the system font is used. |

## Scope

| Limitation | Detail |
| --- | --- |
| No session program | `OpenHud.Host` in the repository is an empty placeholder; the manifest declares no host component. |
| No data directory | The module keeps nothing between sessions. |
| No settings window or in-game settings | Settings are OmsiLaunch's module settings. |

## Differences found between the pieces

| Where | Difference | Which wins |
| --- | --- | --- |
| `module.template.json` says `language` `auto` "follows OMSI's language" | The module passes no OMSI language to the resolver, so `auto` is English | The code |
| `NOTICE.txt` says `OpenHud.Host.exe` "is started by OmsiLaunch as a session program" | The manifest has no host component, and the build does not package it | The manifest |
| The pause-menu package draft says the folder may be in another content root | The module looks in the map's folder only | The code |
| The draft says the fallback of the OMSI 2 mark is the text "OMSI 2" | The generic mark is a neutral outlined tag with a steering wheel; openHUD writes no brand text | The code |
| The draft says the footer shows only the versions and the credit | The footer also shows the hint with the keys | The code |
| The draft says the fallback font is Segoe UI | The draw layer uses the font Windows uses for message boxes (Segoe UI on current Windows) | The code |
| The draft says unknown package files are ignored and `@2x` is used above scale 1.5 | The module uses the `@2x` picture above `ui_scale_percent` 150 | The code |
| `PauseStrings` (the menu's catalogue) and the interface table both define some `UI_PAUSE_*` keys with different texts ("Back to the game" and "Resume") | The menu shows the catalogue's | The code ([UI language keys](ui-language-keys.md)) |

Related: [validation status](../status/validation.md) · [per-map packages](../concepts/per-map-packages.md) · [FAQ](faq.md)
