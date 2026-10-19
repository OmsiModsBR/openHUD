# Fonts, symbols and licensing

openHUD carries no font file and no trademark logo of its own. Whatever typeface, symbol or logo a map's menu shows comes from the map's pause-menu package, and the person who makes the package is responsible for the right to ship it. This page explains what the module does with fonts and pictures, what it checks and what it leaves to the author, and how text is drawn when a font is missing or lacks a character.

Related pages: [pause-menu package](../reference/pause-menu-package.md#fonts), [making a pause-menu package](../guides/making-a-pause-menu-package.md), [per-map packages](per-map-packages.md), [internationalization](internationalization.md), [troubleshooting](../guides/troubleshooting.md#text-shows-wrong-characters).

## Who is responsible for what

| | The module | The package author |
| --- | --- | --- |
| Font files | Ships none. Uses a package's fonts only when `fonts/FONTS-LICENSE.txt` exists and is not empty. Does not read or interpret the licence text. | Chooses fonts whose licence allows redistribution inside the package, and writes their licences in `FONTS-LICENSE.txt`. |
| Symbols and logos | Ships a neutral disc with a pause mark and the internal marks `@omsi2`, `@simple` and `@none`, which are drawn by code. Ships no brand symbol and no copy of the OMSI 2 logo. | Owns the right to every picture in `symbols/`, including the OMSI 2 logo if the package includes one (`symbols/omsi2-logo.png`), and the brand symbol of a map. |
| Map data | Reads it read-only. | States its licence in `LICENSE.txt` (for people) and the credit in the manifest. |

The `LICENSE.txt` and `CREDITS.txt` files of a package are for people. The module never opens them. The licence check on fonts is a presence check, not a legal one: a package can contain a licence file that is wrong, and the module cannot tell.

## What the module does with a font

1. The reader finds the font files directly in `fonts/` (`.ttf`, `.otf`, at most 12) and requires `fonts.cfg` and a non-empty `FONTS-LICENSE.txt`. Without them no font of the package is used.
2. It checks each file named by `fonts.cfg`: a static TrueType or OpenType file, at most 4 MiB, with a valid table directory. Variable fonts and collections are refused.
3. It assigns the five roles (`regular`, `medium`, `bold`, `black`, `condensed`); a role you leave out takes the face with the nearest weight.
4. When the map's packages are loaded, the module asks the draw layer to register each font for its role. The draw layer checks the file again and registers it for this process only: it is never installed in Windows, never copied, never written to disk. When the session ends the draw layer unregisters every font.

## How text is drawn

The draw layer asks Windows' GDI to rasterize each glyph once, at the size needed, into a glyph atlas (a texture), and draws text as textured triangles. Consequences:

- **A role without a font** is drawn with the system UI font (the font Windows uses for message boxes, usually Segoe UI), in the weight of the role. If a package gives only some roles, the other roles use the `regular` font of the package before the system font.
- **A character the font lacks** is drawn with the system UI font; a character that font lacks too is drawn with that font's own missing-character glyph. It is never replaced by a question mark.
- **Characters outside the Basic Multilingual Plane** (most emoji) are drawn as a blank of half an em: GDI is asked glyph by glyph in UTF-16 units.
- **Advances only.** Text is laid out with each glyph's advance; there is no kerning and no shaping, so right-to-left and complex scripts are not supported ([known limitations](../reference/known-limitations.md)).
- The module asks the draw layer for the width of a text (`measure`) when it sizes chips and strips, so layouts follow the fonts that are actually registered; if the layer cannot answer, an approximation is used.

## Pictures

| Kind | What happens |
| --- | --- |
| PNG symbol | Validated by its header and size, then decoded on the worker thread (at most 512 pixels on the longest side after reduction) and registered with the draw layer as raw pixels. |
| SVG symbol | Validated against the static subset (no scripts, no `DOCTYPE`, no style sheets, no embedded images, no external references), then turned into shapes of the scene protocol on the worker thread. Nothing is rasterized and the draw layer needs nothing new. A drawing that does not fit the subset or its budget is replaced by the generic mark, with the reason in the log ([the SVG subset](../reference/pause-menu-package.md#the-svg-subset)). |
| `@omsi2`, `@simple`, `@none` | Drawn by openHUD itself. |
| Realmap icons | PNG, up to 1024 x 1024 in the package; reduced to at most 128 pixels; at most 48 distinct pictures. |

A picture that fails any check is dropped with a reason in the log and the default is used.

## Practical rules for authors

- Put the licence of every font file in `fonts/FONTS-LICENSE.txt`, naming the files it covers.
- Do not include a commercial font, or a font whose licence forbids redistribution, in a package you distribute.
- Do not include the OMSI 2 logo unless you have the right to; the generic `@omsi2` mark needs no permission.
- Never ship a brand symbol you do not own.
- Keep a `CREDITS.txt` for the people who made the pictures.

Related: [pause-menu package](../reference/pause-menu-package.md) · [making a pause-menu package](../guides/making-a-pause-menu-package.md) · [safety](safety.md)
