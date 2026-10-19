# The pause menu

When OMSI pauses, it shows its native pause menu. openHUD can take that menu over: it asks OMSI, through OmsiLaunch, to hide the native menu, shows its own, and when you choose something it asks OMSI to run the native action. This page explains how that works, the states the module goes through, what happens when something fails (and why the native menu is then left alone), the pages of the menu and how it is sized and read with the mouse. The keys are in [controls](../reference/controls.md); the identity a map can give the menu is in [per-map packages](per-map-packages.md).

Related pages: [first session](../getting-started/first-session.md), [per-map packages](per-map-packages.md), [safety](safety.md), [troubleshooting](../guides/troubleshooting.md#the-pause-menu-is-the-native-one), [errors and degradation](../reference/errors-and-degradation.md), [validation status](../status/validation.md).

## What the module asks OMSI for

All of it goes through four operations of OmsiLaunch's public catalog, which the manifest declares (capability `ui.pause-menu.replace`, optional):

| Operation | Meaning |
| --- | --- |
| `ui.pause.state` | Is the game paused? Is the native menu visible? Is a native dialog busy? What was the result of the last action (`resumed`, `game-started`, `returned`, `quit-requested`)? |
| `ui.pause.take-over` | Hide the native menu (OMSI stays paused, the simulation stays held). |
| `ui.pause.action` | Run a native action: `resume`, `new-game`, `save`, `options` or `quit`. OMSI's own handler runs; its dialogs are the native ones. |
| `ui.pause.restore-native` | Show the native menu again. |

The module does not click the native menu, post messages to it or move its window: OmsiLaunch calls OMSI's handlers directly. The module also needs the game's keyboard and mouse while its menu is open (`bridge.input`); OmsiLaunch records those events and keeps them from reaching OMSI, and the module reads them as plain events (virtual key, position, button, wheel).

## When the native menu stays

The module takes the pause menu over only when all of these hold; otherwise the native menu is shown exactly as without openHUD, and the radar is unaffected.

| Condition | Where it is decided | What `pause.state` says when it fails |
| --- | --- | --- |
| The `pause_menu` setting is true | Settings | `turned off in the settings` |
| The draw layer is available | Arm | `no draw layer` |
| The capability `ui.pause-menu.replace` is available on this build | Arm | `ui.pause-menu.replace is not available in this build` |
| Input capture is available | Arm | `input capture is not available` |

These are checked once, when the module is armed. During the session the pause menu can still fail and give up, see [when something fails](#when-something-fails).

## The states

The pause menu is a state machine driven by the module's tick:

| State | Meaning |
| --- | --- |
| `Running` | The game is not paused (or the menu is not ours). Each tick asks `ui.pause.state`. |
| `TakeOverPending` | The game is paused and the native menu is visible: asking OMSI to hide it. A "busy" answer is retried for up to one second. |
| `Open` | The menu is shown over a copy of the game's last picture; the keyboard and the mouse are captured. |
| `Resuming` | You chose "back to the game" (or pressed Esc): waiting until the keys are released (at most 750 ms), then the capture is given back and the resume action is sent. |
| `Modal` | You chose new game, save, options or quit: the capture is given back and OMSI shows its native dialog; the module draws nothing and waits. |
| `Ending` | OMSI reported that it is quitting. |
| `Provisional` | Something failed: the native menu is visible and only the map is drawn behind it. |
| `Stopped` | The module stopped. |

Transitions, in short:

```text
Running --paused and native menu visible--> TakeOverPending --taken over--> Open
Open --Esc or "back to the game"--> Resuming --keys released, resume sent, game running--> Running
Open --new game / save / options / quit--> Modal
Modal --back, still paused--> Open (the keyboard is taken again)
Modal --a new game started, or the game is running--> Running
Modal --OMSI is quitting--> Ending
any state --a failed call--> Provisional
Stop --from any state--> everything given back
```

## Rules the machine keeps

- **Nothing is drawn while a native dialog is in front.** When OMSI reports a busy dialog (a save dialog, its options), the module hides its scene and releases the keyboard.
- **The capture is released before any action that can open a native dialog.** Native dialogs need real input.
- **The native menu is restored only while the game is still paused.** Restoring it on a running game would pause it again. If the game was resumed by someone else, the module just lets go.
- **A key still down when you resume would fire as a new press in the game** (OMSI also reads the keys directly), so the module waits for the keys to be released, up to 750 ms, and then resumes anyway. The timeouts are counted (`key_release_timeouts` in `pause.state`).
- **One thread.** The machine belongs to the thread of its first call; a call from any other thread is refused.
- **Cleanup is idempotent.** Stopping gives back the capture, the background picture and the native menu, and repeats whatever could not be done (for example a restore refused while a dialog was open) every 250 ms.

## When something fails

Any failed call to a `ui.pause.*` operation, a capture that cannot start, a background picture that cannot be taken, a state that cannot be read, or a port that throws, leads to the same place:

1. everything the module holds is given back (capture, background, and the native menu if it was hidden and the game is still paused);
2. if the game is still paused, the state becomes `Provisional`: **OMSI's native menu is visible and works as always; the module draws only the map behind it**. This way you never end up with no menu at all;
3. the failure is kept as `pause_last_error` (`OL_E_UI_BUSY`, `OL_E_UI_NOT_PAUSED`, `OL_E_UI_ALREADY_TAKEN_OVER`, `OL_E_CAPABILITY_UNRESOLVED`, `OL_E_RUNTIME_OPERATION_FAILED`, or the raw code) and the next pause starts again from `Running`.

Special cases:

| Answer | What the machine does |
| --- | --- |
| `OL_E_UI_BUSY` while taking over | Retries for up to one second (OMSI may be inside its frame), then fails as above. |
| `OL_E_UI_NOT_PAUSED` | The game is running: nothing to take over, back to `Running`. |
| `OL_E_UI_ALREADY_TAKEN_OVER` | The game holds a take-over the module does not know of: the module gives it back with the failure cleanup. |
| `OL_E_CAPABILITY_UNRESOLVED` | Latched: the module stops asking the game about the pause menu for the rest of the session. |

If the pause menu keeps failing (five faults in a row, five scenes that cannot be published, or the draw layer failing), the module gives up on it for the session: it hands everything back, emits `pause-degraded` and shows one notice ("openHUD's pause menu is not available; OMSI's own menu stays."). The radar goes on ([errors and degradation](../reference/errors-and-degradation.md)).

## The pages

| Page | Content |
| --- | --- |
| Menu | Five buttons in this order: back to the game, new game, save game, options, quit OMSI; and the legend of the map's markers on the right. |
| Map | The whole map, north up, centered on you; you pan and zoom it. It uses the realmap package when there is one. |
| Lines | The lines of the map that run on the session's date, one row each (number, name, color). Selecting a row has no further effect in 0.1.0. |

The header shows the map's title and symbol (a picture or an SVG drawing) and, at the right, the OMSI 2 mark and the openOMSI mark when the package carries them (otherwise a neutral tag); the legend shows the package's icon of each marker kind in place of the swatch; the Lines page shows the package's icon of the line's mode; the footer shows a hint with the keys on the left and, on the right, the versions of openHUD and OMSI and, when the package has one, its credit. The text of the menu comes from the interface language ([internationalization](internationalization.md)); the title, the symbol, the accent color, the line color and the legend come from the map's pause-menu package, or are the generic ones.

## Size and the mouse

The menu is designed as a block of 904 x 600 units. It is drawn at a scale that is never below the window's own scale and grows with `ui_scale_percent` as far as the window allows (it keeps a margin of about 2 % at the sides and 4 % above and below). The scene's canvas is the virtual 1920 x 1080 one, so the menu stays in proportion at any resolution.

The mouse arrives in client pixels of the game's view. The module asks the draw layer for the real size of the view it draws into and maps pixels back to the canvas and then to the menu's units, so a click lands on what is drawn. The map page's pan and zoom use the same mapping.

Related: [controls](../reference/controls.md) · [per-map packages](per-map-packages.md) · [errors and degradation](../reference/errors-and-degradation.md) · [validation status](../status/validation.md)
