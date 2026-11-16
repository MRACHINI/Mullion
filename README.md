# Mullion

Place windows on a grid with the mouse, on Windows 10 and 11.

A *mullion* is the bar that divides a window into panes. Mullion divides your
screen the same way: drag a window, right-click, and stretch it over the cells
you want.

![The grid overlay with a 2 × 2 selection](docs/overlay.png)

Mullion is a free, open-source take on the right-click-while-dragging gesture of
the discontinued WindowGrid by Joshua Wilding. It shares no code with it.

## How to use it

1. Start dragging a window by its title bar, as usual.
2. While still holding the left button, **press the right button**. The grid
   appears and the window returns to where it was.
3. Keep the right button down to move the first corner; **release it** to fix
   that corner.
4. Move the mouse to stretch the selection over more cells.
5. **Release the left button.** The window fills the selection.

A quick right-click followed by releasing the left button puts the window on the
single cell under the cursor.

While the grid is showing:

| Input | Effect |
|---|---|
| Mouse wheel | Makes the grid finer or coarser for this placement |
| Right-click again | Picks a new first corner |
| Esc | Cancels; the window stays where it was |

Ordinary dragging and Windows' own edge snapping are untouched unless you
right-click.

### Move on the grid without resizing

Hold **Ctrl** while dragging a window and it snaps to the nearest grid line when
you let go, keeping its size. The key can be changed or switched off in the
settings.

### Zones and keyboard shortcuts

A zone is a part of the screen with a keyboard shortcut. Press the shortcut and
the active window moves there, without touching the mouse.

Open **Settings… → Zones → Add zone…**, then:

1. Choose the zone's grid. Each zone has its own, so "left third" (3 × 1) and
   "top-right quarter" (2 × 2) can live side by side.
2. Drag across the picture to select the cells.
3. Click the shortcut box and press the keys, for example `Ctrl+Alt+1`. A
   shortcut needs Ctrl, Alt or the Windows key.

You can have as many zones as you like. With more than one monitor, pressing a
shortcut again moves the window to the same zone on the next monitor; this can
be switched off on the Zones tab.

If another program has already registered the shortcut you choose, Mullion warns
you and then takes it over: while Mullion is running, the shortcut moves the
window and the other program does not receive it. The zone list marks such
shortcuts. Pausing or exiting Mullion gives the shortcut back.

## Settings

Right-click the Mullion icon in the notification area and choose **Settings…**
(or double-click the icon).

- **Grid**: number of columns and rows, from 1 to 100 each (the same grid is
  used on every monitor); the gap between neighbouring windows and the gap at
  the screen edges (zero means windows sit flush); and the key for moving on
  the grid.
- **Zones**: the saved zones and their shortcuts.
- **Appearance**: colour and opacity of the tint, the grid lines and the
  selection.
- **General**: start Mullion when you sign in to Windows.

The menu also has **Pause**, which switches the gestures off until you untick it.

## Portable

Mullion is a single executable. It keeps `settings.json` (and `mullion.log`, if
something goes wrong) in the folder it runs from, so put it somewhere you can
write to. The only thing stored elsewhere is the optional start-with-Windows
entry in the registry.

Two versions are published:

| File | Size | Needs |
|---|---|---|
| `Mullion.exe` | small | the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| `Mullion-full.exe` | large | nothing |

## Good to know

- Run only one grid tool at a time. Another tool that reacts to the same mouse
  gesture (the original WindowGrid, for example) will fight Mullion for it.
- Windows started "as administrator" can only be moved if Mullion itself runs as
  administrator.
- Some windows refuse certain sizes; Mullion moves those as close as they allow.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build
dotnet test tests/Mullion.Core.Tests
dotnet run --project src/Mullion
```

`build.ps1` runs the tests and writes both release executables to `dist\`.

`tests/Mullion.GestureProbe` checks the app end to end by driving the real mouse
and keyboard against a window of its own. It takes over the mouse for a few
seconds.

- Gestures: start Mullion with default settings, then run the probe.
- Zone shortcuts: copy `tests/Mullion.GestureProbe/zones.settings.json` to
  `settings.json` next to Mullion.exe, start Mullion, then run the probe with
  the argument `zones`.

### Layout

| Path | Contents |
|---|---|
| `src/Mullion.Core` | Grid arithmetic, settings and the gesture logic; no Windows dependencies |
| `src/Mullion` | The Windows app: mouse hook, overlay, tray icon, settings window |
| `tests/Mullion.Core.Tests` | Unit tests for the core |
| `tests/Mullion.GestureProbe` | Live end-to-end check |
| `tools/make-icon.ps1` | Regenerates the icon |

## Roadmap

Planned, roughly in this order: a step-through mode that offers zones for each
open window in turn, a panel opened by a hotkey, swapping two windows, arranging
all windows, named scenes, remembering where a window belongs, program
exclusions, remappable mouse input and automatic updates.

## License

[MIT](LICENSE)
