# Scene protocol (version 1)

The managed core decides **what** is drawn; the native draw layer only **rasterizes** it. They meet at one JSON document, the scene, and a few small operations. The native layer has no state of its own about the game, the menu or the map: it draws the last scene it was given at every `Present`.

## Why a scene of primitives

- All layout (radar, menu, text placement) is plain managed code, so it is tested offline without a Direct3D device.
- The native layer stays small and can be checked with golden scenes.
- A scene is immutable data: publishing a new one is one `execute` call, and the draw layer swaps it atomically.

## Operations of the native component (`execute`, JSON in, JSON out)

| Operation | Arguments | Meaning |
| --- | --- | --- |
| `scene` | the scene document (below) | replaces the scene drawn at every `Present`; `{}` or `{"v":1,"cmds":[]}` draws nothing |
| `image.put` | `{"id":"s1","w":64,"h":64,"rgba":"<base64>"}` | registers or replaces an image (straight-alpha RGBA, `w*h*4` bytes); at most 128 images, 4 MiB each |
| `image.drop` | `{"id":"s1"}` | removes an image |
| `font.put` | `{"role":"bold","path":"<absolute path of a .ttf/.otf the module validated>"}` | registers a font for a role (`regular`, `medium`, `bold`, `black`, `condensed`); private to the process |
| `background.capture` | `{}` | copies the current back buffer into the pause background texture (only while the pause menu is taken over) |
| `background.release` | `{}` | frees the pause background texture |
| `visible` | `{"on":true}` | `false` stops drawing at once (used on stop, on failure and while a native modal dialog is in front) |
| `measure` | `{"text":"Next stop","size":14,"font":"bold"}` | the width of one line of text in canvas pixels, laid out with the registered fonts the way a `text` command is (advances only, no kerning); answers `{"ok":true,"w":71.25}`. `size` is 1..512, `font` a role, `text` at most 2048 bytes. The module uses it to size chips and strips; it caches the answers |
| `stats` | `{}` | counters: scenes drawn, commands, images, fonts, skipped frames, device resets, last error, and `view_w`/`view_h`, the size in pixels of the target of the last frame (0 before the first); the module maps the mouse of the pause menu with it |

Answers are `{"ok":true,...}` or `{"ok":false,"code":"...","detail":"..."}`. An unknown operation or a malformed document never crashes OMSI: it answers `ok:false` and keeps the previous scene.

## Scene document

```json
{
  "v": 1,
  "w": 1920, "h": 1080,
  "background": false,
  "cmds": [ ... ]
}
```

- `w`, `h`: the virtual canvas in pixels. The layer scales it to the game view (the device's back buffer) by `min(width/w, height/h)` and centers it; coordinates are floats.
- `background`: when `true` the layer first draws the captured pause background (stretched to the whole view), then `cmds`.
- `cmds`: the commands, drawn in order. At most 8192 commands and 1 MiB of JSON per scene; beyond that the scene is refused.
- Colors are strings `#rrggbb` or `#rrggbbaa` (straight alpha). Angles are degrees, 0 = up, clockwise. All numbers are finite; `NaN`/`Infinity` refuse the scene.

### Commands

Every command has `"t"` and these members (optional ones in brackets):

| `t` | Members | Draws |
| --- | --- | --- |
| `rect` | `x y w h` `[fill] [stroke] [sw] [r]` | rectangle, corner radius `r` |
| `circle` | `cx cy r` `[fill] [stroke] [sw]` | circle |
| `arc` | `cx cy r a0 a1 color sw` `[cap]` | arc stroke from angle `a0` to `a1` (`cap`: `butt` or `round`) |
| `line` | `x1 y1 x2 y2 color sw` `[cap]` | segment |
| `poly` | `pts` (`[x,y,x,y,...]`) `[fill] [stroke] [sw] [closed]` | polygon or polyline |
| `text` | `x y s` `size` `[font] [color] [align] [base] [maxw] [outline]` | text. `font`: a role (default `regular`); `align`: `l` `c` `r`; `base`: `top` `mid` `alpha` (alphabetic); `maxw`: clip width (text is cut with `...`); `outline`: `#rrggbbaa` 1 px halo |
| `image` | `id x y w h` `[op] [tint] [rot]` | registered image, `op` opacity 0..1, `tint` color multiplied in, `rot` degrees about its center |
| `clip` | `kind` (`rect`/`circle`) and the shape members | pushes a clip (nesting depth at most 8) |
| `unclip` | — | pops one clip |
| `push` | `[tx ty] [rot] [sx sy]` | pushes a transform (depth at most 16): translate, then rotate, then scale |
| `pop` | — | pops one transform |

An unknown `t` is skipped and counted in `stats` (forward compatibility); a missing required member skips that command. Text is UTF-8; characters a font lacks fall back to the system UI font, never to `?`.

### Golden examples

`tests/vectors/scene-minimal.json`:

```json
{"v":1,"w":1920,"h":1080,"background":false,"cmds":[
 {"t":"rect","x":10,"y":20,"w":300,"h":120,"fill":"#14171cd9","r":12},
 {"t":"text","x":24,"y":40,"s":"openHUD","size":22,"font":"bold","color":"#ffffff","base":"mid"}
]}
```

`tests/vectors/scene-radar.json` (clip, transform, arc, polygon, image):

```json
{"v":1,"w":1920,"h":1080,"background":false,"cmds":[
 {"t":"circle","cx":1700,"cy":880,"r":160,"fill":"#0b151ee6","stroke":"#ffffff","sw":4},
 {"t":"clip","kind":"circle","cx":1700,"cy":880,"r":156},
 {"t":"push","tx":1700,"ty":880,"rot":-35},
 {"t":"poly","pts":[-60,-120,60,-120,60,120,-60,120],"stroke":"#5588c7","sw":6,"closed":false},
 {"t":"pop"},
 {"t":"unclip"},
 {"t":"arc","cx":1700,"cy":880,"r":158,"a0":-40,"a1":40,"color":"#00c94a","sw":5,"cap":"round"},
 {"t":"image","id":"player","x":1684,"y":864,"w":32,"h":32}
]}
```

`tests/vectors/scene-pause.json` (background and a button):

```json
{"v":1,"w":1920,"h":1080,"background":true,"cmds":[
 {"t":"rect","x":0,"y":0,"w":1920,"h":1080,"fill":"#000000a6"},
 {"t":"rect","x":760,"y":420,"w":400,"h":56,"fill":"#00c94a","r":10},
 {"t":"text","x":960,"y":448,"s":"Resume","size":18,"font":"bold","color":"#ffffff","align":"c","base":"mid"}
]}
```

## Rules both sides keep

- The core never sends pointers, handles or addresses; the layer never reads the game.
- A scene older than the one being drawn is never drawn after it (the layer keeps the latest only).
- Everything the layer allocates (textures, fonts, device objects) is released on `visible:false`-then-stop and before a device `Reset`; it is recreated after.
- Limits above are enforced by both: the core never produces a scene over them, the layer refuses one that is.
