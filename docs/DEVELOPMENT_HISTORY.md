# OSRS Agility Overlay v51 - Persistent clock tick sampling

This build stops the internal OSRS tick clock from expiring during normal play.  Once the app has a good visual lock, it keeps the internal 600ms clock alive for the session and only uses the RuneLite tick bar for small filtered corrections.

Changes:
- After first good tick sync lock, the internal game clock no longer expires just because the last visual edge/sample is old.
- Missed/failed tick-bar reads no longer clear external sync or force raw Windows click timing.
- Tick bar is now sampled roughly once per OSRS tick instead of only near marker appearance.
- Samples are taken shortly after the predicted boundary so the low-opacity tick marker is easier to read.
- Visual samples still use the v50 stable filter: tiny corrections only, medium corrections require repeated agreement, suspicious corrections ignored.
- Tick sync area changes / manual reset still clear the clock intentionally so it can relock from the new setup.
- Version tag: `v51-persistent-clock-tick-sampling`.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

# OSRS Agility Overlay v50 - Stable tick clock filter

This build keeps the internal 600ms OSRS tick clock as the authority once sync is locked. Visual tick-bar detections only make tiny filtered corrections so low-opacity/noisy samples cannot drag the clock by 50-100ms over time.

Changes:
- Internal tick clock no longer directly jumps to every detected tick edge after initial lock.
- Visual corrections under ~15ms are treated as stable/no-op.
- Small corrections are applied as tiny steps only, capped around 3ms per observed edge.
- Medium corrections must repeat in the same direction before a slow correction is allowed.
- Suspicious large corrections are ignored.
- Debug CSV now includes `clock_sync_wanted_ms`, `clock_sync_applied_ms`, and `clock_sync_filter`.
- Version tag: `v50-stable-tick-clock-filter`.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

## v48 minimal performance mode

- Normal perfect marker clicks now schedule the next marker from the marker's own `ReadyAt` tick, not from the closest external tick boundary at mouse-down time.
- Queued early clicks still schedule from `ReadyAt`.
- Late clicks schedule from `ReadyAt + displayed late ticks`, so late clicks do not carry sub-tick mouse timing into the next marker.
- READY prompts still use the external tick clock/fallback click time because their `ReadyAt` is just when the prompt appeared, not an in-game tick boundary.
- Fixed tick sync area selection so it exits selection mode before snapping/screenshots. After you release the mouse, the selection form closes, then the app snaps/stores the area and shows editor status.
- Version tag: `v48-minimal-performance-mode`.

Build:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

## v44 queue safety minimal mode

- Added configurable `WorldLagMs` to `markers.json` and the editor. Default is `28`.
- If you click a marker slightly before `ReadyAt` but within `WorldLagMs`, the click is accepted as a queued perfect click.
- Queued early clicks schedule the next marker from the marker's `ReadyAt` tick, not the raw mouse-down time.
- This is intended for ping/client-server timing where a click in the last few ms before the tick can still land on the next OSRS server tick.
- Debug CSV now records `click_queued_early`, `click_ms_before_ready`, and `world_lag_ms`.
- Version tag: `v44-queue-safety-minimal-mode`.

Build:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

## v42 test expectation fix

- Updated regression tests that still expected the old next-boundary click behaviour. v41/v42 use closest-boundary click snapping, so normal slightly-after-ready clicks stay on the ready tick rather than being pushed one tick later.
- No gameplay/timing code changed from v41.

## v41 persistent tick clock

This build removes the last-second marker timing corrections.

Main behaviour:
- Tick sync now maintains the external OSRS tick clock only.
- The app tracks tick edges from the RuneLite tick bar, but does not adjust `ReadyAt` when the click timing UI appears.
- Every accepted marker click is converted to the closest tracked OSRS tick boundary when sync is fresh.
- The next marker countdown is scheduled from that action tick, not from raw Windows mouse-down time.
- Marker 2/lap start should no longer get large correction jumps caused by starting from raw click time.
- `LapOverheadTicks` was removed. Best possible time is now just the sum of marker delay ticks.
- Default `markers.json` has the provided 7-marker setup and tick-sync area.

Build:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

## v37 compile fix

- Added the missing `_suppressTickSyncDebugDrawing` field used by the v36 tick-area snap screenshot suppression logic.
- No timing or snapping behaviour changed from v36.

# v37 compile fix

Changes from v35:
- Tick area snapping now detects the full black-bordered RuneLite tick box instead of the moving line/bottom corner.
- Snap capture suppresses the overlay's own green/yellow debug rectangles before taking the screenshot.
- The detector is now colour-first: it finds the large green/red filled tick lane, then expands to the surrounding black border.
- Selection text now tells you to drag around the whole tick bar.
- Snap padding reduced so the saved green area should sit tight around the real box.

Use `Area +` only if the game tick bar moves slightly or the selected search area needs a little more room. Use `Area -` if it is too wide.


Changes from v34:
- Fixed the editor tick-sync preview bar being visually out of phase by making it display the external tick clock directly instead of anchoring the preview to the lap/session start.
- Tick sync area selection now auto-snaps to the detected tick bar inside the dragged rectangle.
- Added `Snap area`, `Area -`, and `Area +` buttons so the saved search rectangle can be re-snapped, tightened, or expanded without re-dragging.
- The selected search area is still drawn green in edit mode, and the latched detected inner bar is drawn yellow after detection.
- Added `Ctrl+I` and an editor/tray toggle to hide/show the info/stat overlay during normal play. Markers remain visible.

# v34 prestart tick lock

Changes from v33:
- Fixes marker 2 usually needing a large correction after starting a lap from marker 1 READY.
- While any marker is showing READY, the app now quietly tracks the RuneLite tick-bar edge in the background.
- READY tracking only updates the internal game-tick clock; it does not move the READY marker or start a lap.
- When marker 1 READY is clicked, the lap now starts from the closest tracked OSRS tick boundary instead of raw Windows mouse-down time.
- Marker 2 is therefore scheduled from the same game tick that should process the marker 1 click, which should stop marker 2 from being the main big-adjust marker.
- Stats lap start now uses the engine action tick when external sync is fresh, so lap timing matches the marker schedule.
- UI can show PRESTART / PRESTART LOCK while waiting on a READY marker.

# v33 edge tick tracker

Changes from v32:
- Stops using a single arbitrary tick-bar position as an automatic correction source.
- During a short pre-click window, samples the latched tick-bar line and waits for the moving marker to wrap/reset.
- Treats that wrap/reset as the reliable OSRS tick boundary.
- Applies the marker correction at the start of the click timing UI from the tracked boundary, not from one possibly biased phase sample.
- Allows larger corrections only when a real boundary edge was observed with high confidence.
- UI now reports edge tracking, edge age, and edge adjustment messages.

# v32 calibrated tick sync

Changes from v31:

- The one-shot tick sync still only runs once per marker at the -2 tick timing-bar point.
- The detector now timestamps the actual screen capture and uses the capture midpoint as the observed tick-bar time.
- Capture and scan timings are shown in the UI, for example `cap 12ms scan 1ms`.
- The marker correction now uses the sample timestamp instead of the older overlay timer tick time.
- Large repeated-looking corrections are treated as suspicious instead of being applied every marker.
- Corrections above about 0.14 ticks are ignored. At 0.6s/tick this is about 0.084s.
- Applied corrections are capped to about 0.083 ticks. At 0.6s/tick this is about 0.050s.
- The UI reports suspicious corrections as `suspicious ignored +0.160s` instead of clamping them into the countdown.

This is intended to fix the regular `+0.12s clamped from +0.16s` behaviour by removing capture-time bias and refusing corrections large enough to cause a visible countdown jump.


# v30 latched tick bar sync

Changes from v29:

- The one-shot sync still only runs once per marker at the -2 tick timing-bar point.
- The screen check now latches onto the real tick box inside the selected area.
- The detector analyzes a single horizontal sample line through that latched bar instead of scoring the whole selected rectangle.
- Phase is calculated between the detected left/right tick-box borders, so an oversized selection should not skew timing.
- The UI now shows the correction amount, e.g. `adjust +0.083s`, when the clock offset is changed.
- In edit mode the selected sync area is green and the latched inner bar is drawn yellow after the first successful latch.

Tick sync is no longer sampled constantly. When screen tick sync is enabled, the app waits until the current marker is about to show the click timing bar, exactly 2 ticks before ready, then performs one screen check for that marker. If the moving tick line is found, the current marker ready time is corrected to the OSRS tick phase and the click timing UI starts from that corrected timing. The app then waits for the next marker before checking again.

# OSRS Agility Overlay v16 Refactor

This version breaks the old giant `Program.cs` into manageable files and adds unit tests for the important timing/stat functions.

## Main changes

- Split code into `Models`, `Services`, `Rendering`, `Input`, and `Forms`.
- Added `MarkerSequenceEngine` for pure timing/marker logic.
- Added `StatsService` for combo/best/lost/lap stats.
- Added `ConfigService` for config loading/saving/backups.
- Added `ClickDetector` for click-radius tests.
- Added xUnit tests.
- Added `BuildAndTest.ps1`.

## Build and test

Run this first:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

This runs tests, then builds:

```text
FinalExe\OSRSAgilityOverlay.exe
FinalExe\markers.json
```

## Build only

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildFinalExe.ps1
```

## Run from source

```powershell
powershell -ExecutionPolicy Bypass -File .\RunFromSource.ps1
```

## Tests included

- Marker 1 is ready immediately after reset.
- Early/orange clicks do not advance.
- Late clicks do not shift future marker timing.
- Perfect clicks record the target tick, not `-1`.
- Best time uses marker delay ticks only.
- Perfect combo increments.
- Late click resets combo.
- Lost time increments by 0.6s chunks.
- Click radius uses `Radius + GlobalClickExtraRadius`.

## Config

The app uses:

```text
markers.json
```

A backup is created on save:

```text
markers.backup.json
```


## v17 project fix

v16 had the test folder inside the main project folder. SDK-style C# projects include all `.cs` files under the project folder by default, so the app tried to compile the xUnit tests as part of the overlay app.

v17 fixes that by adding this exclusion to `OSRSAgilityOverlay.csproj`:

```xml
<Compile Remove="OSRSAgilityOverlay.Tests\**\*.cs" />
```

Use:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildFinalExe.ps1
```

For tests:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```


## v18 crash fix

Fixed crash:

```text
System.InvalidOperationException: Nullable object must have a value
MarkerSequenceEngine.TryClick()
```

Cause:
- `TryClick()` returned `LastClickSliderPosition.Value`.
- In some click paths that nullable could still be empty.
- v18 now calculates a local `sliderPosition` and returns that directly.

Also added a regression test for the first marker click path.


## v19 compile fix

Fixed compile error:

```text
MarkerSequenceEngine.cs(144,38): error CS1002: ; expected
```

Cause:
- v18 patch accidentally duplicated the method name:
  `public bool CheckLapCompleted    public bool CheckLapCompleted(DateTime now)`

v19 corrects it to:

```csharp
public bool CheckLapCompleted(DateTime now)
```


## v20 version/debug build

This version adds:
- visible version tag in the overlay: `v20-version-debug`
- window title/tray tooltip includes the version
- crash logging to `FinalExe\crash.log`

If the crash dialog path mentions an older folder such as `v17_project_fix`, you are still running an old EXE/process.

Recommended clean run:

```powershell
Get-Process OSRSAgilityOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
.\FinalExe\OSRSAgilityOverlay.exe
```

If it crashes, send `FinalExe\crash.log`.


## v21 build fix

Fixed:
- duplicate `DrawVersionTag(Graphics g)` compile error
- build scripts now delete stale `FinalExe` before building
- build scripts now stop on dotnet errors instead of saying Done after a failed build
- scripts kill old `OSRSAgilityOverlay` processes before building

Use:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndTest.ps1
```

Do not run `FinalExe\OSRSAgilityOverlay.exe` unless the build says Done without errors.


## v22 test namespace fix

Fixed test compile errors:

```text
Point could not be found
Rectangle does not exist in current context
```

Cause:
- `ClickDetectorTests.cs` was missing `using System.Drawing;`.

v22 adds that using and marks the test project with `UseWindowsForms=true` to keep Windows drawing types available.


## v23 late resync fix

Fixed the bug where if you missed a marker by a huge number of ticks, the next marker stayed hugely late too.

Old behaviour:
- Marker 2 is +200 late.
- Click marker 2.
- Marker 3 still uses the original lap schedule, so it also appears massively late.

New behaviour:
- Perfect clicks stay on the fixed lap tick grid.
- Late clicks resync the marker schedule from the actual click/action.
- Stats still keep the real lost time/lap time separately.

Added regression test:
- `VeryLateClick_ResyncsNextMarkerCountdown`


## v24 test alignment fix

v23 changed the real app behaviour correctly: late clicks resync the next marker countdown from the actual click/action.

One old unit test still expected the previous fixed-grid behaviour:

```text
LateClick_DoesNotShiftFutureMarkerSchedule
```

That old test was now wrong. v24 updates it to:

```text
LateClick_ResyncsFutureMarkerSchedule
```

Expected behaviour now:
- Perfect click: stays on fixed tick grid.
- Late click: next marker countdown starts from the actual late click.
- Huge late click: no more +200 carry-over.

## v25 ready manual start

Changed:
- Manual marker selection now arms the selected marker as an immediate `READY` prompt.
- Previous (F7), Next (F8), Restart selected timer, and editor marker selection can start from that marker without waiting for its delay timer.
- `READY` is drawn inside the active marker circle instead of below it.
- Starting halfway through the course does not start or complete a real lap.
- Partial-route clicks are ignored by stats until marker 1 is clicked to begin a normal full lap.
- Lost-time tracking is paused while the sequence is not inside a real running lap.

Timing behaviour kept:
- Accepted clicks during a real lap now schedule the next marker from the snapped OSRS action tick.
- Very late clicks still do not carry extra late ticks into the next marker.

Added regression tests:
- `ReadyPrompt_StaysReadyUntilFirstClick`
- `SelectMarkerReady_MakesAnyMarkerReadyImmediately`
- `PartialRouteClick_DoesNotStartLap_AndNextMarkerCountsFromActualClick`
- `PartialRouteWrapToMarkerOne_DoesNotCompleteLap`
- `CancelCurrentLap_StopsLapWithoutCountingBest`


## v26 timing stats fix

Fixed:
- Manual Next/Previous/Restart still arms the selected marker as `READY`, but after any accepted click the next marker now schedules its normal delay countdown instead of accidentally re-arming marker 1 as `READY` on partial-route wraparound.
- A 1-tick-late click now records `+1` instead of `+2` in the timing/debug lap window.
- The timing window bottom row now says `last lap:` instead of `lap:`.
- Lost time now uses the same clock-style format as total time.
- Added perfect click totals: total, highest, and current.
- The click timing bar now keeps the clicked position visible for 3 ticks.
- A compact click timing bar now appears under the active marker from 2 ticks before the marker is ready.

Behaviour kept:
- Half-route starts still do not count as a real lap.
- Marker 1 only starts a real lap once you click marker 1.
- Accepted clicks during a real lap now schedule the next marker from the snapped OSRS action tick.

Added/updated regression tests:
- `LateClick_RecordsCorrectLateTick_NotOneExtra`
- `LastClickSlider_HoldsClickedPositionForThreeTicks`
- `PartialRouteWrapToMarkerOne_CountsDownInsteadOfReadyPrompt`
- `PartialRouteMarkerOneClick_StartsRealLapAfterCountdown`


## v27 compile fix

Fixes a compile error in `Rendering/MarkerRenderer.cs` caused by a `DrawString` call missing the brush argument for the floating Perfect text. No timing behaviour was intentionally changed from v26.

## v28 tick sync area

Added visual OSRS/RuneLite tick sync:
- New editor button: `Set tick sync area` lets you drag a rectangle around the on-screen tick bar.
- New editor checkbox: `Use screen tick sync` enables/disables visual sync.
- New small overlay status UI shows `LOCKED`, `SEARCHING`, `NO AREA`, or `OFF`, plus sync age/confidence.
- The selected tick area is saved in `markers.json`.
- If the overlay is anchored to RuneLite and the selected area is inside RuneLite, the tick area is saved relative to the RuneLite window.

Timing change:
- When screen tick sync is locked, accepted clicks schedule the next marker from the next detected game tick boundary instead of raw `DateTime.Now`.
- Manual Next/Previous/Restart still shows `READY` first.
- After a ready/perfect/late click, the next marker still shows its normal delay countdown.
- If tick sync is lost or disabled, timing falls back to the existing internal/manual-offset timer.

Added regression tests:
- `ExternalTickSync_ClickSchedulesFromNextGameTickBoundary`
- `ExternalTickSync_PartialRouteUsesNextGameTickBoundaryButDoesNotStartLap`
- `TickClockTests`
## v44-queue-safety-minimal-mode

v41 keeps the tracked OSRS tick clock usable across the whole marker delay and reduces debug-log lag.

- Debug timing logging is off by default in final release; press `Ctrl+L` for testing logs.
- Logs are written to `Logs/OverlayTimingLog_YYYYMMDD_HHMMSS.csv` beside the running EXE / source output.
- The editor has an **Open logs** button; logging is toggled with `Ctrl+L`.
- The log records timer frames, tick-sync samples/edges/status changes, every mouse down, accepted/rejected marker clicks, lap starts, lap completes, manual nudges, manual marker selection, saves, and app close.
- Useful columns to check first:
  - `event`
  - `marker`
  - `state`
  - `ms_to_ready`
  - `ms_until_next_tick`
  - `action_delta_ms`
  - `next_marker`
  - `next_delay_ticks`
  - `next_ms_to_ready`
  - `external_fresh`
  - `edge_age_ms`
  - `ticksync_status`

For testing, run a short lap or two, then send the newest CSV from the `Logs` folder.


## v41 closest-boundary click timing

The log showed the clock was fresh, but every normal click was being pushed to the next tick boundary by 0.37-0.54s. v41 changes accepted-click scheduling to use the closest tracked tick boundary, so a normal slightly-after-READY click stays on the READY tick instead of adding almost a full tick to the next marker.


## v44

- Added QueueSafetyMs. Effective early click queue is WorldLagMs + QueueSafetyMs.
- Added minimal mode toggle with Ctrl+M and editor controls.
- Minimal mode hides the per-click timing panel/large slider and shows only a thin green outline for the next marker until 3 ticks before ready.

## v46-minimal-preview-cleanup

- Minimal mode preview circle now uses a non-transparency-key green so the thin next-marker outline remains visible.
- Minimal mode suppresses the small timing bar under the active marker.
- Under-marker timing bars no longer draw the held black previous-click marker, fixing short-delay obstacles briefly showing the previous marker click line.


## v47-area-select-minimal-polish

- Minimal mode no longer draws the top-right version tag.
- Tick sync area selection now snaps/stores synchronously after mouse release instead of queueing the snap after the dialog.
- Selection overlay now hides before closing, captures/release mouse properly, and right-click/Esc cancels.
- Added `TICK_AREA_STORED` debug log rows showing the saved tick sync area values.


## v48-minimal-performance-mode

- Debug timing CSV logging only runs while Ctrl+L logging mode is enabled.
- The timer loop avoids debug sample/status/frame log work while logging is off.
- Editor log label now shows `Log: paused in minimal mode`.
- Note: the current tick bar detector still depends on visible green/red fill and black tick/border pixels. A RuneLite window at about 10% opacity is not expected to be reliable for live tick detection because Windows screen capture sees the composited/movie-blended pixels, not the original RuneLite colours.

## v58 final release cleanup

- Debug CSV logging is off by default and no longer controlled by `markers.json`.
- Press `Ctrl+L` to enable logging mode for testing. Press `Ctrl+L` again to disable it.
- `Ctrl+I` now toggles both minimal mode and the info overlay together.
- Removed old pixel help/search-margin controls from the editor.
- The quit button and `Ctrl+Q` now exit the whole app even while the editor window is open.
- Tick pixel selection immediately saves, enables sync, and starts the sync warmup.
- The top info box is wider so the perfect total/highest/current line is not cut off.
