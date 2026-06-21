# OSRS Agility Overlay

Windows visual timing overlay for Old School RuneScape agility practice.

OSRS Agility Overlay shows numbered timing markers over RuneLite so you can practise clicking agility obstacles on the correct game tick. It is designed for manual play only: you still do every click yourself.

![Overlay preview](docs/images/overlay-preview.png)

## Features

- Default marker setup for the **Ardougne Agility Course**.
- F10 editor for creating or adjusting markers for your own screen/layout.
- Tick-based countdowns, ready markers, lap stats, combo tracking, and best time tracking.
- Optional tick pixel sync using RuneLite's Visual Ticks plugin.
- World lag and queue safety settings for adjusting timing to your server/client feel.
- Minimal/info toggle so the marker overlay can stay clean while you play.
- Designed to sit over RuneLite while RuneLite is low-opacity, so you can still watch something behind the game window.

## Important notice

This is an unofficial fan-made tool and is not affiliated with, endorsed by, or supported by Jagex, Old School RuneScape, or RuneLite.

This app is a **visual overlay only**. It does not automate clicks, move the mouse, press keys, send inputs, read game memory, inspect network traffic, modify RuneLite, modify the OSRS client, or interact with Jagex game worlds. You are responsible for making sure your use complies with the game rules.

## Download

1. Go to the GitHub repo.
2. Click **Releases**.
3. Click **v0.1.0**.
4. Download **OSRSAgilityOverlay-win-x64.zip**.
5. Extract the ZIP.
6. Set up RuneLite and the required plugins/settings below.
7. Run **OSRSAgilityOverlay.exe**.

## Required RuneLite setup

Set this up before running the overlay properly:

1. Install/enable the **Visual Ticks** plugin.
2. Move the Visual Ticks display into a corner.
3. Put both Visual Ticks circles on the same spot using negative horizontal spacing.
4. Enable **Blindfold** to turn off the OSRS display.
5. Enable **Entity Hider** to hide your player, other players, pets, and anything else that blocks clicks.
6. Turn off the normal Agility plugin, or disable clickboxes and disable Mark of Grace highlighting.
7. Turn off **Tile Indicators**.
8. Turn off **Screen Markers** if you use them.
9. Minimise the minimap.
10. Do one normal run of the course and make sure you can click every obstacle without moving the camera.

## First-time overlay setup

1. Open RuneLite and set up the plugins/settings above.
2. Extract **OSRSAgilityOverlay-win-x64.zip**.
3. Run **OSRSAgilityOverlay.exe**.
4. Press **F10** to open the marker editor.
5. Start at the end of the course.
6. Map the markers one by one to the exact place you click from the end of the previous movement.
7. For each marker, set the tick delay to what it should be for your route.
8. Adjust **World lag ms** until the timing feels correct for your server.
9. Click **Set tick pixel**.
10. Click the Visual Ticks circle/pixel you want the overlay to sync from.
11. Wait about 10 seconds for the overlay to sync.
12. Press **F11** to save.
13. Press **F10** again to leave edit mode.

If tick pixel sync does not start after about 10 seconds, close the app with **Ctrl+Q**, open it again, and it should load the selected tick pixel properly.

## Hotkeys

| Hotkey | Action |
|---|---|
| F10 | Open/close editor |
| F5 | Add marker at cursor |
| Delete | Delete selected marker while editing |
| F7 | Previous marker |
| F8 | Next marker |
| F6 | Reset lap sequence |
| F11 | Save markers/settings |
| F12 | Reload markers/settings |
| Ctrl+R | Reset stats |
| Ctrl+I | Toggle minimal/info overlay |
| Ctrl+L | Toggle debug logging |
| Ctrl+Q | Quit app |

## Building from source

Most users should download the release ZIP instead of building from source.

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK

Easy build:

```powershell
.\BuildFinalExe.ps1
```

Or double-click:

```text
BuildFinalExe.bat
```

The final build is created in:

```text
FinalExe\OSRSAgilityOverlay.exe
```

A ZIP is also created:

```text
OSRSAgilityOverlay-win-x64.zip
```

## Run from source

```powershell
.\RunFromSource.ps1
```

Or double-click:

```text
RunFromSource.bat
```

## Config

The app saves marker and timing settings in:

```text
markers.json
```

A backup is created when saving:

```text
markers.backup.json
```

## Release

Current release: **v0.1.0**

Source build base: **v60-overlay-info-layout**
