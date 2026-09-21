# Verification

## Scrunch product shell, 21 September 2026

Current default: `artifacts/scrunch-shell/Scrunch.exe`, via `run.ps1`.
All sections after this milestone are historical evidence for their named builds.
The pre-existing NootFX work was checkpointed separately as `bd4a409` before
shell implementation began.

### Implemented and checked

- Debug x64 build: **zero warnings and errors**. NuGet restore required network
  permission on this host; subsequent builds used `--no-restore`.
- **32 native product checks passed** in `product-verification.json`. These use
  actual WinUI controls and native automation providers: New note, Settings,
  back navigation, scrolling Settings, persisted defaults, instant text search,
  list-item invocation without duplicate windows, and shell Undo.
- Preview updates handle the native editor's carriage-return line endings.
  Active-note filtering, no-match results, colour, placement, autosave,
  recoverable discard, Undo races and fresh-edit animated menu discard passed.
  The final product run rendered **47 frames** for the real menu discard.
- The final verification session invoked the actual shell **Quit** button via
  its native provider and exited. Saved active notes were not marked discarded.
- **16 storage checks passed**, including new defaults across reopen, legacy
  version-1 files without defaults, null/unknown optional defaults, Unicode,
  exclusive writer lock, backup recovery and failed-save preservation.
- **62 native NootFX lifecycle checks passed** in `fx-verification.json`,
  including repeated discard, undo/cancellation, 13-note idle, real display
  capture, injected graphics failures, renderer recovery, reduced motion and
  closed-note/view collection. The FX code and prepared assets were unchanged.
- **25 native paper-renderer checks passed** in `verification.json`.
  The comparison bench was closed after its report completed.
- Headless geometry checks passed for 466,560 projected patches and 1,570,752
  crumple patches. Deterministic FX checks passed, including 10,000 seeds.

### Visual evidence and limits

`-VerifyProduct` writes native WinUI RenderTargetBitmap captures beside the exe:

- `shell-empty.png`
- `shell-notes.png` and `shell-notes-dark.png` (three actual test notes)
- `shell-search-empty.png`
- `shell-settings.png` and `shell-settings-bottom.png`

The populated light/dark home captures were visually inspected: compact header,
quiet search, light list rows, colour indicators, and anchored Undo/Settings/Quit.
Settings uses native controls and a scrollable body. These are rendered pixels
from the running XAML tree, with an explicit neutral capture background. They do
**not** validate native title-bar appearance, Mica or physical pointer gestures.
The capture follows the documented [WinUI pixel-buffer API](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.rendertargetbitmap.getpixelsasync?view=windows-app-sdk-1.8).

Computer Use read the actual accessibility tree, but Windows 10 capture returned
`SetIsBorderRequired ... 0x80004002`, and pointer input returned `coordinate input
geometry is unavailable`. Desktop screenshot fallbacks were occluded by other
windows and are not visual evidence for Scrunch. Native provider invocation is
reported separately from physical mouse/keyboard testing. The global shortcut
was occupied by another running app; the conflict fallback was verified, not a
new physical global-key delivery test.

Windows 11 Mica, mixed-DPI shell sizing, screen-reader announcements, physical
pointer feel and long-session resource budgets still need hands-on acceptance.
No new graphics-quality or performance claim is made by this shell milestone.

### Reproduce

```powershell
dotnet build proto/Noot.Proto/Noot.Proto.csproj -p:Platform=x64 -o proto/artifacts/scrunch-shell
dotnet run --project proto/Noot.StorageChecks/Noot.StorageChecks.csproj
dotnet run --project proto/Noot.GeometryChecks/Noot.GeometryChecks.csproj
dotnet run --project proto/Noot.FxChecks/Noot.FxChecks.csproj
.\proto\run.ps1 -VerifyProduct
.\proto\run.ps1 -VerifyFx
.\proto\run.ps1 -Verify
.\proto\run.ps1 -ShellPreview
```

Run the UI checks sequentially and inspect each report's `passed` and `error`;
`run.ps1` detaches after launch and its exit code alone does not prove a test pass.
The ordinary product/FX tests use unique isolated folders. `-ShellPreview` is a
Debug-only, restartable manual session using `product-check-shell-preview` beside
the executable; it never opens everyday notes.

Tray access, startup registration and a new app icon are deferred. Closing the
home window still saves and quits. The next milestone should define tray access
and background/close behaviour before adding opt-in sign-in startup.

## Paper motion polish, 8 September 2026

Current executable: `artifacts/paper-polish-checked/Noot.Proto.exe`, selected by
`run.ps1`. Sections below describe historical builds.

The mesh host now fills the padded window instead of the flat paper rectangle.
Window size and saved placement are unchanged. The 12 by 24 mesh uses diagonal
creases, staggered horizontal/vertical gathering and shared-vertex lighting blended
across each patch. The gather lasts 190ms; the 180ms throw starts at 100ms. The
shadow follows beneath the paper independently of its rotation. Reduced motion
and keyboard discard remain immediate.

- Build passed with zero warnings/errors; no new dependencies.
- Geometry checks passed for 466,560 bend patches and 1,570,752 crumple patches.
  Maximum bend seam error was 0.001638 DIP. Supported drag poses remain inside
  window padding, and crumple projective denominators stay positive.
- Final native renderer report passed all 25 entries, including padded viewport,
  actual temporary-note menu discard, texture reuse, cancellation and idle shutdown.
- Final native product report passed all 16 checks, including fresh-edit menu
  discard (10 rendered frames, Animated), undo races, recovery and clean quit.
- Actual native captures on a neutral temporary review window show the strong
  bend extending past the old paper bounds and held crumple stages at 35%, 70%
  and 100%. Capture files respectively:
  `C:/Users/Nick/AppData/Local/Temp/noot-polish-inspect-525456.png`,
  `C:/Users/Nick/AppData/Local/Temp/noot-polish-inspect-394652.png`,
  `C:/Users/Nick/AppData/Local/Temp/noot-polish-inspect-329146.png`,
  `C:/Users/Nick/AppData/Local/Temp/noot-polish-inspect-329210.png`.
  Reproduce with `--verify-product --inspect-material` (isolated data).

The crumple is still a stylised surface approximation without self-collision.
Held captures do not prove temporal motion quality; physical dragging, mixed DPI,
Windows 11 and sustained frame pacing still need hands-on checks. The short bench
idle sample reported 160.3 MB working set and 0.00% of one CPU core over three
seconds; it is not a production memory budget or GPU measurement.

## Discard-menu and white-outline correction

Current executable: `artifacts/window-fix-checked/Noot.Proto.exe`, selected by
`run.ps1`. Earlier sections below describe historical builds, not current defaults.

The direct renderer tests missed the actual flyout lifecycle and the temporary
bench-note path. The editor's GotFocus handler could cancel pending discard when
the native menu restored focus; the 200ms cold-capture timeout could also skip
animation. Menu opening now prewarms the texture, menu closing queues discard,
and focus restoration is ignored during discard. Explicit undo still cancels it.
A cold capture is allowed up to 1.5 seconds. Keyboard/reduced-motion discard stays
immediate. Temporary bench notes now animate from their menu too.

The floating HWND now uses a popup style without caption, thick frame or extended
edge styles. Non-client sizing/painting/activation do not draw a frame. DWM stays
enabled to preserve alpha; Windows 11 additionally requests DWMWA_COLOR_NONE.
The Windows 11 branch has not been exercised on this Windows 10 machine.

- Build: zero warnings/errors.
- Native product inspection report passed, including native MenuFlyoutItem provider
  invocation after fresh text edits: **12 rendered frames, Animated outcome**.
  Undo/discard races and saved-note checks remain covered. Pinning preserves the
  frameless styles.
- Actual screen capture of the pinned isolated test note confirms readable text,
  transparent surroundings, and removal of the white outer rectangle:
  `C:/Users/Nick/AppData/Local/Temp/codex-shot-2026-09-07_23-12-45.png`.
  Earlier captures were obscured or the note had already been discarded and do
  not support the border conclusion.
- Reports are in `artifacts/window-fix-checked/`. The opt-in
  `--verify-product --inspect-note` run leaves an isolated test note visible and
  writes its HWND in the report for targeted capture. Normal `--verify-product`
  still quits after checking preservation of saved notes.
- Final normal native product run: all 16 checks passed, including clean quit.
  Final renderer run passed, including the actual temporary-note menu. Bench
  windows are explicitly shown before activation, and menu tests await Loaded
  rather than attempting to open a flyout before it has a XAML root.

The native provider invocation tests the real menu event path, but not physical
mouse input. Human motion quality and Windows 11/mixed-DPI behaviour still require
hands-on checks. Crumple remains a stylised mesh approximation.

## Crumple-and-throw build

Current executable: `artifacts/discard-checked/Noot.Proto.exe` (also selected by
`run.ps1`). Right-click the note's top edge and choose Discard note. The mesh
contracts into a corrugated bundle and follows a short throw with a terminal fade.
The animation reuses the written note's snapshot; no replacement ball asset is used.
The purpose is completion feedback. Windows Composition transforms and opacity
drive the exit: 140ms strong ease-out contraction, overlapping 240ms ballistic
throw, 280ms total. Keyboard discard and reduced motion skip the effect.

- Final Debug x64 build: zero warnings/errors.
- Geometry checks: 272,160 existing bend patches plus 916,272 crumple patches across
  all three personalities, supported sizes and contraction progress. Crumple
  projective denominators stay positive and corner mappings agree within 0.1 DIP.
- Native product report: all 14 checks passed. Includes saving before animation,
  undo while animation is pending, reuse of the same window, discard after undo,
  recovery after the effect completes, and normal quit without discarding notes.
- Native renderer report: all 23 entries passed/reported. Includes actual discard
  frames, cached texture reuse, callback shutdown, held-pose idle, cancellation,
  reduced-motion interruption and the previous peel/editor regression checks.
- A per-window generation guards late animation continuations after undo/re-discard.
  Texture preparation has a 200ms deadline; slow preparation skips the effect.
- Closing the comparison bench now exits its process so temporary floating notes
  do not keep a hidden test session alive.

The crumple is a continuous stylised surface approximation, not simulated folds
with self-collision or self-shadowing. Its material/shape quality still needs a
hands-on feel test. The bench provides Crumple and throw, a held-pose slider, and
Centre to restore it. `--verify --inspect-crumple` selects a held pose after checks.

Desktop captures did not expose the held bench unobstructed while the desktop was
being used. They do not validate the crumple appearance. Inspection capture paths:
`C:/Users/Nick/AppData/Local/Temp/codex-shot-2026-09-07_22-55-15.png` and
`C:/Users/Nick/AppData/Local/Temp/codex-shot-2026-09-07_22-58-51.png`.
UIA slider input also failed with `Requested property was not in the CacheRequest`.
Automated smoothness, mouse gestures, mixed-DPI behaviour and long-session resource
use remain unverified. Reports: `artifacts/discard-checked/product-verification.json`
and `artifacts/discard-checked/verification.json`.

## Everyday product build

Executable: `artifacts/product-checked/Noot.Proto.exe`. Default launch now opens
the small Noot control window and restores local notes; `--bench` opens the paper
comparison. The earlier paper-only report below remains historical evidence.

- Debug x64 build: zero warnings and errors.
- 12 storage checks passed: Unicode/multiline text and preferences round-trip,
  negative monitor coordinates, exclusive writer lock, atomic replacement backup,
  restart-safe discard, interrupted temporary write, failed-save preservation,
  corrupt-primary recovery, damaged-file preservation, newer-schema refusal,
  invalid dimensions and missing required schema fields.
- Native `product-verification.json`: passed. Actual controls/window changes update
  the saved record; autosave drains; discard and undo restore identity and placement;
  quitting closes windows without marking saved notes discarded. Global hotkey
  registration succeeded; this check does not simulate physical keyboard delivery.
- Existing native renderer `verification.json`: passed for all personalities,
  texture refresh, interruption, reduced motion and return to idle.
- Geometry baseline: all 272,160 patches passed, maximum error 0.006764 DIP.

The native checks found and corrected two lifecycle issues: persistence wiring
must not depend on receiving an activation event, and programmatic discard must
commit explicitly rather than relying on the system-only AppWindow.Closing event.

Reports are in `artifacts/product-checked/`. Product test notes use an isolated
`product-check-*` directory there, not the real `%LOCALAPPDATA%\Noot` directory.
Storage checks retain evidence in a unique `noot-storage-check-*` temporary folder.

The renderer regression's short 3-second Debug sample was 156.8 MB / 8.29% of one
CPU core. It is too short and startup-adjacent to establish an idle budget. No
performance improvement is claimed from it; GPU utilisation remains unmeasured.

A separate 10-second sample of the everyday control window with **zero notes**
reported 116.6 MB working set and 0.00% of one CPU core (rounded). This is a narrow
Debug baseline, not evidence for performance with many notes. Saved as
`artifacts/product-checked/product-performance.json`.

Still needs hands-on testing: global-key delivery with other applications focused, continuous drag/resize,
multi-monitor and mixed-DPI restoration, perceived motion quality, and long sessions.
The computer-use accessibility tree exposed the new control window, but an indexed
button action failed with `element 9 is not available in cached app state`.
Screenshot capture also failed with `SetIsBorderRequired failed: No such interface
supported (0x80004002)`. An injected Ctrl+Alt+N chord succeeded: after asynchronous
dispatch, the control window count changed from zero to one and `notes.json` was
created. The first immediate accessibility snapshot preceded that dispatch.
No automated mouse-interaction or screenshot success is claimed for this pass.

## Earlier paper-only build

## Passed

- Debug x64 build: zero warnings and zero errors.
- Geometry checks: 272,160 patches across three personalities, three widths,
  three heights and combinations of bend, twist and peel. All projective corner
  mappings finite; adhesive edge fixed. Maximum measured mapping error:
  0.006764 DIP.
- The final `--verify` run reported `passed: true`. It checked texture creation
  for all personalities, no ticking during held inspection, return to idle,
  fresh textures after text and colour changes, immediate native editing,
  reduced-motion interruption, reset and floating-window creation.
- Actual screenshots confirmed readable native handwriting on the paper,
  curved text during a held bend, corrected patch seams, removal of the solid
  shadow rectangle, and transparent surroundings around the floating note.

## Resource sample

The final ten-second process sample reported **165.1 MB working set** and
**0.62% of one CPU core**. This is a Debug build with the comparison bench and
test-note activity, not a production resource budget. Short startup samples
varied considerably. GPU memory, GPU utilisation and frame pacing were not
measured. The app's own geometry callback stops at rest, but the diagnostic
readout and native UI still have work to do.

Machine/runtime observed: Windows 10 build 19045, .NET SDK 9.0.317.

Machine-readable reports are beside the checked executable:

- `artifacts/paper-bench-checked/verification.json`
- `artifacts/paper-bench-checked/performance.json`

## Limits and remaining manual checks

Native accessibility inspection worked, but the computer-use screenshot helper
failed with `SetIsBorderRequired failed: No such interface supported (0x80004002)`.
Its coordinate actions also reported `coordinate input geometry is unavailable`,
and its range-value action failed with a UIA cache error. Programmatic integration
checks are therefore not presented as successful automated mouse tests.

The screenshot fallback captured actual screen pixels. Some captures contain
overlapping windows; only visible Noot regions support the visual observations.
The final screenshot is at:
`C:/Users/Nick/AppData/Local/Temp/codex-shot-2026-09-07_16-24-12.png`.

A thin native outline is still visible around the floating window. Transparent
padding pass-through, continuous mouse dragging/resizing, mixed-DPI monitors and
perceived animation smoothness still require hands-on testing. The shadow is an
approximation, and the mesh does not simulate paper self-collision or crumpling.
Notes are temporary and are not persisted.
