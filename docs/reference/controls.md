# Controls

The radar has no keys. The pause menu is controlled by the keyboard and the mouse while it is open; the module captures those events (so OMSI does not act on them) only while the menu is open, and gives them back before any native dialog opens. This page lists every key and mouse action by page. It is taken from `src/OpenHud.Pause/PauseMenu.cs` and checked by the `pause.menu.*` gates.

Related pages: [first session](../getting-started/first-session.md), [the pause menu](../concepts/the-pause-menu.md), [operations and events](operations-and-events.md).

## Everywhere in the menu

| Input | Does |
| --- | --- |
| Esc | Back to the game (waits until keys are released, up to 750 ms) |
| Tab | Next page (Menu, Map, Lines, then Menu again) |
| Shift+Tab | Previous page |
| Click on a tab | That page |

Other keys are ignored; none reaches OMSI while the menu is open.

## Menu page

The five buttons are, in order: back to the game, new game, save game, options, quit OMSI.

| Input | Does |
| --- | --- |
| Down / Up | Next / previous button (wraps around) |
| Home / End | First / last button |
| Enter, Space | Press the focused button |
| Mouse move | Focuses the button under the pointer |
| Left click | Press and release on the same button activates it; releasing elsewhere cancels |
| Double-click on a button | Counts as a press; the release activates |

New game, save game, options and quit OMSI run OMSI's own handlers. The menu hides and gives the keyboard back while OMSI's dialog is open, and returns when it closes and the game is still paused.

## Map page

| Input | Does |
| --- | --- |
| Arrow keys | Pan by 40 menu units |
| `+`, numeric `+` | Zoom in one step |
| `-`, numeric `-` | Zoom out one step |
| Home | Center on you and reset the zoom |
| Mouse wheel over the map | Zoom (one step per notch) |
| Left button, drag | Pan the map |
| Double-click on the map | Center on you and reset the zoom |

The zoom has 21 steps (from 8 out to 12 in) around the default scale; each step changes the scale by a factor of 1.25. The map opens centered on your vehicle every time the menu opens.

## Lines page

| Input | Does |
| --- | --- |
| Down / Up | Select the next / previous line (the first one if none is selected) |
| Page Down / Page Up | Move by one page (9 rows) |
| Home / End | First / last line |
| Enter | Activate the selected line (no further effect in 0.1.0) |
| Mouse wheel over the list | Scroll, 3 rows per notch |
| Left click on a row | Select it |
| Double-click on a row | Select and activate it (no further effect in 0.1.0) |

## Notes

- Right and middle buttons are ignored.
- A key still held when you resume would be seen by OMSI as a new press, so the menu waits for release (at most 750 ms, then resumes anyway).
- Keys act on key-down; repeated key-downs repeat the action.

Related: [the pause menu](../concepts/the-pause-menu.md) · [first session](../getting-started/first-session.md)
