# Noot

Beautiful little reminders that stay where you put them. C# / WinUI 3 / Windows
Composition, with native text editing and tactile paper. No browser runtime,
accounts, cloud services or added package dependencies.

## Run the everyday app

```powershell
.\proto\run.ps1
```

Use `-Build` to rebuild after closing this build. Requires .NET 9 and the Windows
build tools. Executable: `proto/artifacts/paper-polish-checked/Noot.Proto.exe`.

- **New note**, or **Ctrl+Alt+N** from anywhere while Noot is running. New notes
  focus the editor immediately; typing never waits for an animation.
- Drag the top edge; resize with the lower-right corner.
- Right-click the top edge for six paper colours, three motion personalities,
  reduced motion, optional always-on-top, and discard.
- **Ctrl+N** creates another note from a note. **Ctrl+Shift+Delete** discards it.
  **Ctrl+Shift+Z**, or **Undo last discard** in Noot, restores the latest discard.
  Ordinary Ctrl+Z stays available for undoing text edits.
- Discard from the top-edge menu for a quick crumple-and-throw. Keyboard discard
  and reduced motion stay immediate. Undo can interrupt the effect. The note is
  saved as recoverable before any animation begins.
- Minimise the Noot window while working. **Show notes** brings your notes forward.
  Closing the Noot control window saves and quits; notes return on next launch.
  Closing an individual note discards it recoverably.

Playful paper is the default. Notes are unpinned by default. A shortcut conflict
is shown in the control window; the New note button and local shortcut still work.
The global shortcut requires Noot to be running.

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
notes. Paper verification writes `verification.json` and leaves the bench open for
inspection. These are programmatic native integration checks, not mouse-gesture
or screenshot assertions. See `proto/VERIFICATION.md` for evidence and limitations.

## Next

- Refine the crumple silhouette, diagonal creases and shadows after feel testing.
  The current bundle is a stylised approximation without paper self-collision.
- Better contact shadows and resting material; human feel testing over days.
- Transparent-padding pass-through and deformed-silhouette hit testing.
- Verify the frameless popup treatment on Windows 11 and mixed-DPI monitors; the
  white outline is removed on the tested Windows 10 desktop.
- Launch-at-startup option and a smaller background control surface.
- Multi-monitor, DPI changes, sustained typing, long-session memory and frame-pacing
  measurements. No production memory budget or GPU-performance claim yet.
