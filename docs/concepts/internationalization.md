# Internationalization

openHUD's own text (the buttons of the pause menu, the labels of the radar and the map, the notices) exists in three languages: English, Portuguese (Brazil) and German. A map's pause-menu package can add the map's title and legend in any language OMSI has a code for. This page explains how the language is chosen, where each text comes from, what a missing translation does and what is outside the module's reach. The keys and their texts are in [UI language keys](../reference/ui-language-keys.md); the `language` setting is in the [settings reference](../reference/settings.md#language).

Related pages: [pause-menu package](../reference/pause-menu-package.md), [fonts, symbols and licensing](fonts-symbols-and-licensing.md), [radar and interface settings](../guides/radar-and-interface-settings.md#language), [troubleshooting](../guides/troubleshooting.md#the-wrong-language).

## Two kinds of text

| Kind | Where it lives | Languages |
| --- | --- | --- |
| The interface | Embedded in the module (the `UI_` keys); nothing is read from disk | English, Portuguese (Brazil), German |
| The map's own text (title, legend, per-language strings) | The map's pause-menu package | Any language with an OMSI three-letter code (`ENG`, `DEU`, `ESP`, `PTB`, `FRA` ...) |

Texts are UTF-8 throughout. A map's title written in Polish or French shows in a Latin font; characters the chosen font lacks fall back to the system UI font ([fonts](fonts-symbols-and-licensing.md#how-text-is-drawn)).

## How the language is chosen

The `language` setting is either `auto` (the default) or a code. It accepts up to eight characters: letters, `-` or `_` (`en`, `ENG`, `pt-BR`, `de`). It is stored in upper case with `_` replaced by `-`. Anything else is reported as a settings issue and `auto` is used.

**The interface language** is resolved from the code: `ENG` or `EN` is English; `PTB` or `PT` (any `pt-...`) is Portuguese; `DEU`, `DE` or `GER` is German. A code with no translation (`es`, `FRA`) falls back to what OMSI reports, then to English. In version 0.1.0 `auto` gives English: the module does not yet read OMSI's language, so set `language` explicitly to get another one (`auto` is meant to follow OMSI's own language, and will once the module reads it).

**The package language code** is what the package's suffixed keys use (`title.PTB`). An explicit setting maps to the OMSI code (`en` to `ENG`, `pt` to `PTB`, `de` to `DEU`, `es` to `ESP`, `fr` to `FRA`, `it` to `ITA`, `nl` to `NLD`, `pl` to `POL`, `cs` to `CZE`, `ru` to `RUS`; a three-letter code is used as it is). With `auto`, OMSI's own code is meant to be used (in 0.1.0 the module has none to use and asks packages for `ENG`), even when the interface has no translation for it, so a package can still show its Spanish title to a player whose OMSI is Spanish while the interface text is English.

## Where each text comes from

| Text | Order of lookup |
| --- | --- |
| A button, a label, a notice | The interface table in the chosen language; if that language has no text for the key, the English text; an unknown key shows the key itself (so a missing translation is visible, never blank). |
| The pause menu's own labels | The pause menu's catalogue of 13 keys in the chosen language first, then the interface table, then English. |
| The map's title | `strings/<LANG>.cfg` (`title`), then `title.<LANG>` of `identity.cfg`, then `title`, then the map folder's name. |
| A legend entry | `strings/<LANG>.cfg` (`legend.<kind>`), then `legend.<kind>.<LANG>`, then `legend.<kind>`, then the built-in text of the kind (`UI_LEGEND_STATION` ...). |

Placeholders `{0}`, `{1}` in a text are filled by the module (`UI_RADAR_EARLY`: "{0} early"). A placeholder without a value is left as it is. Units are separate keys (`UI_UNIT_KMH`, `UI_UNIT_MI`) so a language can order them as it needs.

## What the tests guarantee

A gate checks that every key has a text in all three languages and that a text has the same placeholders in all three ([test coverage](../status/test-coverage.md)). It cannot check that a translation is good; corrections are welcome.

## Limits

- Interface text exists in three languages. Other languages need a translation added to the module; a map package cannot translate the interface (it can translate its own title and legend).
- Text is laid out left to right, one glyph after the other. Right-to-left languages and scripts that need shaping are not supported.
- The module does not change OMSI's own language or its native dialogs: the native dialogs that the menu's actions open (save, options) are OMSI's and keep OMSI's language.

Related: [UI language keys](../reference/ui-language-keys.md) · [pause-menu package](../reference/pause-menu-package.md) · [settings](../reference/settings.md)
