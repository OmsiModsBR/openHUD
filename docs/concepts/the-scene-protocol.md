# The scene protocol in plain words

The scene protocol is the one interface between the part of openHUD that decides what to draw and the part that draws it. This page explains it without the byte-level detail: what a scene is, what can be in one, how it gets to the screen, what the limits are and what happens when something is wrong. The exact format, every command and every limit are in the [scene protocol reference](../scene-protocol.md), which is the normative text.

Related pages: [the three parts](the-three-parts.md), [the radar](the-radar.md), [the pause menu](the-pause-menu.md), [performance](performance.md), [fonts, symbols and licensing](fonts-symbols-and-licensing.md).

## A scene is a drawing, not a program

A scene is a list of simple commands that are drawn in order, on a canvas that is always 1920 by 1080 units:

| Command | Draws |
| --- | --- |
| `rect`, `circle` | A rectangle (with rounded corners if asked) or a circle, filled and/or outlined |
| `line`, `arc` | A segment or an arc, with a width and round or flat ends |
| `poly` | A polygon or a polyline |
| `text` | One line of text, with a size, a font role, a color, an alignment and an optional maximum width |
| `image` | A registered picture, with an opacity, a tint and a rotation |
| `clip`, `unclip` | Restrict drawing to a rectangle or a circle, and lift the restriction |
| `push`, `pop` | Move, rotate or scale everything drawn until the matching `pop` |

Colors are text like `#14171cd9` (red, green, blue and, optionally, opacity). Angles are in degrees, 0 pointing up, clockwise. There is no loop, no condition and no script: a scene is data, and the same scene is always drawn the same way.

For example, a panel with a title:

```json
{"v":1,"w":1920,"h":1080,"background":false,"cmds":[
 {"t":"rect","x":10,"y":20,"w":300,"h":120,"fill":"#14171cd9","r":12},
 {"t":"text","x":24,"y":40,"s":"openHUD","size":22,"font":"bold","color":"#ffffff","base":"mid"}
]}
```

## From a scene to the screen

1. The core builds a scene (the radar, or the pause menu with the map in it) and the module sends it to the draw layer as one call.
2. The draw layer checks the document. A good one replaces the previous scene as a whole; a bad one is refused with a code and the previous scene stays.
3. At every frame, when the game presents its picture, the draw layer draws the current scene on top of it, scaled to the real size of the game's view (`min(width/1920, height/1080)`, centered).

Because the canvas is virtual, the same scene looks the same at any resolution, and the pause menu can map the mouse back from real pixels to canvas units ([the pause menu](the-pause-menu.md#size-and-the-mouse)).

## Pictures and fonts

A picture is registered once, by an id, as raw pixels; scenes then name it. A map package's symbol and the realmap package's icons travel this way, after being decoded outside the tick. Fonts are registered by role (`regular`, `medium`, `bold`, `black`, `condensed`) from a file the module has already validated; a role without a font uses the system UI font. A character a font lacks is drawn with the system UI font, never as a question mark.

## Limits, and why they exist

| Limit | Value |
| --- | --- |
| Commands in one scene | 8,192 |
| Size of one scene | 1 MiB of JSON |
| Nesting of clips / transforms | 8 / 16 |
| Images registered | 128, at most 4 MiB of pixels each |
| Text in one command | 4,096 bytes |

The core never produces a scene over a limit: when a map has more features than the budget, the radar keeps the most visible ones (large, on important layers) and drops the rest ([performance](performance.md)). The draw layer enforces the same limits on whatever it receives, because it does not trust its caller either.

## What happens when it goes wrong

| Situation | What happens |
| --- | --- |
| A scene is malformed or over a limit | The draw layer answers with a code (for example `too_large`, `json_syntax`) and keeps drawing the previous scene. The module counts it and, after five failed radar scenes in a row, switches the radar off with one notice. |
| A command is unknown, or lacks a required member | That command is skipped and counted; the rest is drawn. This lets a newer core talk to an older layer. |
| The draw layer fails while drawing (an exception inside the game's frame) | It stops drawing, says why in an event (`draw_failed`) and waits to be told to try again. The module retries twice; if it keeps failing, it switches the radar off and gives the native pause menu back. |
| The game's device is reset (a resolution change) | The layer releases its device objects before the reset and recreates them afterwards; nothing is lost. |
| The module stops | The layer is told to stop drawing, the scene is emptied, and from then on every call is refused. |

Related: [scene protocol reference](../scene-protocol.md) · [the three parts](the-three-parts.md) · [errors and degradation](../reference/errors-and-degradation.md)
