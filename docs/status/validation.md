# Validation status

openHUD `0.1.0` is built and covered by offline tests that never start OMSI ([test coverage](test-coverage.md)). Its rounds in a running OMSI are scheduled by the owner and have not been run. This page states that plainly and lists what those rounds will confirm. Nothing below is a known failure; it is a list of behavior that only a running game can confirm.

Related pages: [test coverage](test-coverage.md), [known limitations](../reference/known-limitations.md), [compatibility](../reference/compatibility.md).

## What exists today

- The managed core, the pause menu and the module are built and covered by gates, including sessions run against a fake of the OmsiLaunch bridge.
- The native draw layer is built and covered by native tests, including a Direct3D 9 backend tested against a device when one can be created and compared with a software rasterizer.
- The two package readers are covered by fixtures, including hostile and randomly corrupted input.
- OmsiLaunch itself documents the pause-menu take-over and the input capture as validated offline only.

## What the rounds in a running OMSI will confirm

| Behavior | What will be confirmed |
| --- | --- |
| The take-over of the native pause menu | That `ui.pause.take-over` hides the native menu, `ui.pause.action` runs the native handlers (resume, new game, save, options, quit) and `ui.pause.restore-native` shows it again, on the OMSI builds in use; that the game stays paused throughout |
| Input capture in the real window | That the keyboard and mouse events of the real game window are captured while the menu is open and released before native dialogs, and that no key leaks to the game; the key-release wait before resuming |
| The Present and Reset hooks on the real device | That the draw layer's hooks install on the game's real Direct3D 9 device, that drawing appears at every frame, that a resolution change (`Reset`) is survived, and that the second device of the vehicle selection form is ignored |
| The background picture | That copying the back buffer gives the right picture for the menu's background |
| The units and position conventions the model infers | That `ground_speed` is in meters per second; that a tile's grid position times the tile size plus the local position gives the map's metres; that the orientation quaternion's forward axis gives the heading clockwise from north; that the tile table always contains the player's tile; that a package's `grid` matches the game's tile size |
| Drawing quality | That text, the symbol and the map read well at real resolutions and scales |
| Cost | That the tick and the draw stay within budget in a real session on a large map (`tick_max_ms`, `slow_ticks`) |
| Coexistence | That the hooks stack with the product's own Direct3D hooks and with other modules' |

## How results will be recorded

Results will be added to this page with the date, the OMSI build, the map and what was observed. Until then every statement in this documentation about game behavior is a statement about what the code asks OmsiLaunch for and what OmsiLaunch documents, not an observation of the game.

Related: [test coverage](test-coverage.md) · [known limitations](../reference/known-limitations.md)
