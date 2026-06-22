# OSRS Agility Overlay

Windows visual timing overlay for Old School RuneScape agility practice.

OSRS Agility Overlay shows timing markers over RuneLite so you can practise manually clicking agility obstacles on the correct game tick. It is designed for manual play only: you still do every click yourself.

![Overlay preview](docs/images/overlay-preview.png)

## Features

* Default marker setup for the **Ardougne Agility Course**.
* **F10** editor for creating or adjusting markers for your own screen/layout.
* Tick-based countdowns, ready markers, lap stats, combo tracking, and best time tracking.
* Optional tick pixel sync using RuneLite's Visual Ticks plugin.
* World lag and queue safety settings for adjusting timing to your server/client feel.
* Minimal/info toggle so the marker overlay can stay clean while you play.
* Designed to sit over RuneLite while RuneLite is low-opacity, so you can watch something behind the game window.

## Important notice

This is an unofficial fan-made tool and is not affiliated with, endorsed by, or supported by Jagex, Old School RuneScape, or RuneLite.

This app is a **visual overlay only**. It does not automate clicks, move the mouse, press keys, send inputs, read game memory, inspect network traffic, modify RuneLite, modify the OSRS client, or interact with Jagex game worlds. You are responsible for making sure your use complies with the game rules.

## Download

1. Go to the GitHub repo.
2. Click **Releases**.
3. Click **v0.1.0**.
4. Download **OSRSAgilityOverlay-win-x64.zip**.
5. Extract the ZIP.
6. Set up RuneLite and the overlay using the sections below.
7. Run **OSRSAgilityOverlay.exe**.

After setup is complete, press **Ctrl+I** to activate minimal mode and start clicking.

Minimal mode removes the extra UI and shows only the next marker. The marker stays as an empty green circle until 3 ticks before it is ready.

## Required RuneLite setup

Set this up before configuring the overlay:

1. Install/enable the **Visual Ticks** plugin.
2. Move the Visual Ticks display into a corner.
3. Put both Visual Ticks circles on the same spot using negative horizontal spacing.
4. Do one run of the course and make sure you can click every obstacle without moving the camera.

Do **not** enable Blindfold yet. Blindfold disables the normal game display, so only enable it later when you are already in position and ready for video mode.

## Marker setup

The overlay includes default markers for the **Ardougne Agility Course**. If you are using another course, or your layout does not match the default Ardougne setup, create your own markers:

1. Stand at the end of the course, where you would stand after finishing the last obstacle.
2. Press **F10** to open the editor.
3. Delete all markers if you are not using the Ardougne Agility Course defaults.
4. Move your cursor to the middle of the next obstacle clickbox.
5. Press **F5** to add a marker at your cursor.
6. Press **F10** to leave edit mode.
7. Click that obstacle.
8. Repeat the marker placement and obstacle click steps until you have mapped the whole course.
9. Press **F10** to reopen the editor.
10. Manually adjust the tick delay for each marker until the timing is correct. This is usually guess-and-check.
11. Drag markers in the editor if their positions need adjusting.
12. Press **F11** to save.

## Tick pixel sync setup

1. Run **OSRSAgilityOverlay.exe**.
2. Press **F10** to open the editor.
3. Set **World lag ms** using the ping grapher plugin for your server.
4. Click **Set tick pixel**.
5. Click the Visual Ticks circle/pixel you want the overlay to sync from.
6. Wait about 10 seconds for the overlay to sync.
7. Press **F11** to save.
8. Press **F10** again to leave edit mode.

If tick pixel sync does not start after about 10 seconds, close the app with **Ctrl+Q**, open it again, and the selected tick pixel should load properly.

## Watching videos while training

Only do this after your markers and tick pixel sync are already set up.

1. Move to your start tile at the end of the course.
2. Turn off **Tile Indicators**.
3. Turn off **Screen Markers** if you use them.
4. Minimise the minimap.
5. Enable **Blindfold** to turn off the OSRS display.
6. Enable **Entity Hider** to hide your player, other players, pets, and anything else that displays on screen.
7. Turn off the normal Agility plugin, or disable clickboxes and disable Mark of Grace highlighting.
8. In the **RuneLite** plugin settings, change opacity to **10%**.
9. Move a video behind the RuneLite client. Make sure no video pixels overlap the Visual Ticks sync pixel.
10. Start your video, then switch back to RuneLite.
11. Run **OSRSAgilityOverlay.exe**.
12. Start clicking the markers manually.
13. Press **Ctrl+I** to activate minimal mode once you have confirmed marker tick timing is correct.

If you want to watch a video that overlaps the sync pixel: First ensure it is synced, then untick the "Use tick pixel" checkbox.
If you feel it get out of sync relaunch the app and sync manually.

## Hotkeys

| Hotkey | Action                               |
| ------ | ------------------------------------ |
| F10    | Open/close editor                    |
| F5     | Add marker at cursor                 |
| Delete | Delete selected marker while editing |
| F7     | Previous marker                      |
| F8     | Next marker                          |
| F6     | Reset lap sequence                   |
| F11    | Save markers/settings                |
| F12    | Reload markers/settings              |
| Ctrl+R | Reset stats                          |
| Ctrl+I | Toggle minimal/info overlay          |
| Ctrl+L | Toggle debug logging                 |
| Ctrl+Q | Quit app                             |

## Building from source

Most users should download the release ZIP instead of building from source.

Requirements:

* Windows 10 or Windows 11
* .NET 8 SDK

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
