# Scrunch

Beautiful little reminders that stay where you put them. C# / WinUI 3 / Windows
Composition, with native text editing and tactile paper. No browser runtime,
accounts or cloud services. Native deletion graphics use Vortice Direct3D 11.

## Run the everyday app

```powershell
.\proto\run.ps1
```

Use `-Build` to rebuild after closing this build. Requires .NET 9 and the Windows
build tools. Executable: `proto/artifacts/scrunch-compact/Scrunch.exe`.

- **New note**, or **Ctrl+Alt+N** from anywhere while Scrunch is running. New notes
  focus the editor immediately; typing never waits for an animation.
- Drag the top edge; resize with the lower-right corner.
- Right-click the top edge for six paper colours, three motion personalities,
  reduced motion, optional always-on-top, and discard.
- **Ctrl+N** creates another note from a note. **Ctrl+Shift+Delete** discards it.
  **Ctrl+Shift+Z**, or **Undo last discard** in Scrunch, restores the latest discard.
  Ordinary Ctrl+Z stays available for undoing text edits.
- Discard from the top-edge menu for a quick crumple-and-throw. Keyboard discard
  and reduced motion stay immediate. Undo can interrupt the effect. The note is
  saved as recoverable before any animation begins.
- Search the home panel by any text in a note; select a row to bring its native
  window forward. **Ctrl+F** focuses search; **Escape** clears it. **Ctrl+N** also
  works in the home panel. Rows stay in newest-created order as you edit.
- **Settings** opens a separate native view for new-note colour, always-on-top
  and reduced-motion defaults. Existing notes retain their individual settings.
  Windows' animation preference is still respected. Settings also shows shortcut
  availability, version, and **Open data folder**.
- Minimise the Scrunch home window while working. **Open Scrunch** in any note's
  top-edge menu restores it. **Quit**, or closing the home window, saves and exits;
  active notes return on next launch. Closing an individual note discards it
  recoverably. There is no tray icon or hidden background mode in this milestone.

The home panel is a 400-DIP-wide native utility that fits its content (329 DIP
high for three notes, plus a 32-DIP integrated caption), with a bounded scrolling
note list and tuned native Desktop Acrylic
Thin where Windows supports it. Inter Variable defines the shell and
Drawably Pen defines paper content, with Windows script/emoji fallbacks. Floating paper
notes remain the workspace. Playful paper, yellow and unpinned remain the initial
defaults. A shortcut conflict is shown in the home panel and Settings; local
creation still works. The global shortcut requires Scrunch to be running.

See [shell typography, material and sizing](proto/SHELL-POLISH.md) for font notices,
fallback architecture and native desktop verification details.

## Naming and compatibility

Product UI, window titles, executable (`Scrunch.exe`), assembly metadata and
manifest display names now use **Scrunch**. The checkout folder, source project
`proto/Noot.Proto/Noot.Proto.csproj`, `Noot_Proto` namespace, check-project names
and internal **NootFX** subsystem/assets deliberately retain their technical
names. This avoids churn in compiled XAML, graphics assets and existing tools.
Package identity and icons are unchanged; a branded icon is a later task.

Saved notes deliberately remain in `%LOCALAPPDATA%\Noot`. No files are moved,
so existing notes, recovery backups and the exclusive writer lock still work.
Optional new-note defaults are stored in the same version-1 document; files from
before this milestone load with the original defaults. Historical verification
sections retain their original build paths and product names as evidence.

## Saving and recovery

Data lives in `%LOCALAPPDATA%\Noot\notes.json`. Text, colour, personality, reduced
motion, pinning, paper size and physical window position are saved in bounded
350ms batches. The timer stops after saving. A normal quit flushes pending edits;
an abrupt process kill or power loss can lose the latest unsaved batch.

Each save flushes a temporary file before atomic replacement, keeping the previous
successful snapshot in `notes.json.bak`. A damaged primary can recover from that
backup with a visible warning; the damaged input is preserved on the next save.
Unreadable data without a usable backup, or a newer file schema, is left untouched.
Save failures are shown with a retry action and prevent normal quit/discard.
An exclusive session lock prevents simultaneous writers.

Discarded notes remain in the local file and can be restored, including after a
restart. There is no automatic expiry or permanent-delete UI in this build.
If a monitor is missing, startup placement is clamped to a nearby work area so
the paper remains reachable. Full mixed-DPI and monitor docking tests are pending.

## Native NootFX deletion slice

Floating notes now use a shared, lazy D3D11 renderer for animated menu discard:
the actual XAML note is captured, one of three Scrunch-owned prepared paper bakes
deforms it on the GPU, and a restrained throw finishes the discard. Corner Crush,
Side Scrunch and Centre Collapse have distinct fold trajectories. An explicit
seed selects geometry-only reflections, timing and release variation. Note text
is never mirrored. Normal editing and the existing
pickup/peel renderer remain native WinUI. Keyboard discard and reduced motion
remain immediate. Graphics failures fall back to the saved, recoverable discard.
Filtered fold shadows, 4x edge smoothing and a single fade of the assembled paper
keep the crumple readable. Playback takes 722–798ms: gather, brief hold, then throw.

```powershell
.\proto\run.ps1 -Build -FxLab     # DEBUG-only, isolated notes, slow/scrub controls
.\proto\verify-fx-seeds-ui.ps1 -AppPid <pid> -Record -Aspects
.\proto\run.ps1 -VerifyFx        # DEBUG-only native lifecycle/performance checks
dotnet run --project proto/Noot.FxChecks/Noot.FxChecks.csproj
```

The lab offers seed input, previous/next seed, next QA seed, selected family/orientation,
exact replay, production speed, slow playback and held deformation. Its Delete
latest test note command uses the ordinary saved-discard path. Hold deformation
and the slider inspect the bake; uncheck Hold to finish.
Matte fill keeps opposing folds lighter, while the main collapse has 19% more
reading time without increasing total playback. The lab displays gather, hold
and exit durations; the QA button cycles the fixed 18-seed suite.
The hold automatically ends after 60 seconds. None of these controls appears in
the everyday app or Release builds. See [NootFX implementation and verification](proto/NOOTFX.md)
for architecture, provenance, measured results and outstanding verification.
The lab also offers six size/colour/content samples and screen-corner placement.
`proto/fx-golden-seeds.json` defines 18 fixed QA seeds, including every family and
reflection plus the timing extremes. The seed verification script captures those
poses, six paper samples and actual production-speed discard/Undo recordings.
`node tools/nootfx-assets/author.mjs --check` verifies reproducible owned bakes.

## Paper comparison bench

```powershell
.\proto\run.ps1 -Bench
```

The original bench remains separate. Its floating test notes are **temporary** and
never enter your saved notes. Compare Stationery, Playful paper and Animated using
Pick up / Put down, drag interruption, the bend-inspection slider, colours, light
and dark backgrounds, and reduced motion. Windows' animation setting is respected.
Use **Crumple and throw** to replay completion, **Inspect the crumple** to hold a
pose, and **Centre** to restore the paper. The crumple deforms the actual text
texture into a corrugated bundle; it does not swap in a pre-rendered ball. Its
190ms gather overlaps a 180ms throw starting at 100ms, for a 280ms effect. Opening a note's menu
prepares its snapshot, and discard starts after the menu finishes closing. Focus
restoration cannot cancel it. A cold capture gets up to 1.5 seconds before falling
back to immediate discard; keyboard discard never waits for capture.

The renderer uses 288 connected projective patches with cylindrical bending,
asymmetric flex, diagonal crumple creases, smoothly blended directional lighting
and unprinted back faces. Its drawing surface includes the surrounding padding
so moving paper can extend beyond its flat bounds. A cached snapshot
of the native editor is shared across patches during motion and refreshed when
content changes. Geometry callbacks stop when the spring settles. This is a bent
paper approximation, not a cloth solver.

## Verify

```powershell
dotnet run --project proto/Noot.StorageChecks/Noot.StorageChecks.csproj
dotnet run --project proto/Noot.GeometryChecks/Noot.GeometryChecks.csproj
.\proto\run.ps1 -VerifyProduct
.\proto\run.ps1 -Verify
```

Product verification uses a unique `product-check-*` data folder beside the
executable, writes `product-verification.json`, and exits. It never opens your real
notes. For an isolated, restartable manual shell session, use `.\proto\run.ps1 -ShellPreview`
(Debug only). Its data stays beside the executable in `product-check-shell-preview`.
Paper verification writes `verification.json` and leaves the bench open for
inspection. These are programmatic native integration checks, not mouse-gesture
or screenshot assertions. See `proto/VERIFICATION.md` for evidence and limitations.

## Next

- Everyday feel testing of the three seeded discard families. The current
  bundles deliberately use a stylised approximation without self-collision.
- Better contact shadows and resting material; human feel testing over days.
- Transparent-padding pass-through and deformed-silhouette hit testing.
- Verify the frameless popup treatment on Windows 11 and mixed-DPI monitors; the
  white outline is removed on the tested Windows 10 desktop.
- Next product milestone: tray access and deliberate background/close behaviour,
  followed by opt-in launch at sign-in. Keep the current small home panel.
- Multi-monitor, DPI changes, sustained typing, long-session memory and frame-pacing
  measurements. No production memory budget or GPU-performance claim yet.
