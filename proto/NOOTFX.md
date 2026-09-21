# NootFX native deletion vertical slice

Status: implemented and verified for this vertical slice. On 21 September 2026,
Nick confirmed that validation may use tools other than Codex Computer Use.
Native WinApp UI Automation, inspected application screenshots, build checks
and repeated-effect measurements satisfy that revised verification requirement.
The Computer Use capture failure remains recorded below as a tooling limitation.

## Existing application and scope

Inspected before changes: a .NET 9 WinUI 3 application, Windows App SDK
1.8.260317003, target Windows SDK 26100, minimum 17763, unpackaged with a
self-contained Windows App SDK. There is one application project and independent
storage/geometry check projects, no solution file. Each sticky is a borderless,
transparent `NoteWindow` containing the native editable `NoteView`. The control
window owns records, note windows, autosave and undo. `NativeTransparency` and
`TransparentBackdrop` provide the existing Windows 10 transparent window setup.
The original Composition patch renderer remains for pickup, peel and the bench.

The store persists a recoverable discard **before** visual work. NootFX only
delays closing that window. Undo cancels playback and restores editing. Keyboard
discard, reduced motion and ordinary window-close discard remain immediate.
No application framework migration, realtime cloth or additional paper interaction
was added. User data was not used for verification: all runs used isolated folders.

## Graphics and lifecycle

`NootFxService.Shared` owns at most one lazy `D3DRenderer`, one D3D11 hardware
device and one hidden reusable HWND. Additional ordinary notes allocate no D3D
devices. Concurrent requests receive an immediate fallback rather than starting
another renderer. Feature level 11.0 is required; unsupported hardware falls back.

The note captures its actual `Paper` XAML subtree with `RenderTargetBitmap`,
including background, grain, native text, wrapping and visible controls. Capture
is bounded by 1.5 seconds. BGRA pixels are uploaded once per effect; the source
remains visible until the native overlay has rendered. A cancelled late capture
cannot hide the note. The existing persistent record is never reconstructed from
rendering data. Deleted source visuals stay hidden until window close, avoiding
a flash of the editor after the throw.

Rendering uses **DXGI composition swap chain → DirectComposition visual → small
native overlay HWND**. The swap chain uses BGRA, premultiplied alpha and flip
sequential presentation. The overlay is non-activating, click-through, hidden from
Alt+Tab, and padded beyond the note. `ClientToScreen` plus the source XAML DPI
scale determines physical placement, including negative desktop coordinates.

This host avoids changes to the editable WinUI tree and avoids original-note
clipping. The actual 1.8 `Microsoft.UI.Composition.Interop.h` was inspected:
its `ICompositorInterop` exposes `CreateGraphicsDevice`, **not** the older
`Windows.UI.Composition` swap-chain method. Its newer composition-texture path
has newer OS requirements. Native DirectComposition works on Noot's Windows 10
baseline without introducing SwapChainPanel into every note.

Shaders, bake/UV/index buffers, states and the device are reused. Surface/depth
resources resize when necessary; per-note texture/views are explicitly released
after each effect. An effect-scoped `DispatcherQueueTimer` schedules drawing and
`Present(1)` synchronizes presentation with DWM. It is stopped and unsubscribed
on success, cancellation, timeout and failure. There is **no idle FX frame loop**.
WinUI's CompositionTarget.Rendering measured about 31ms between callbacks here;
the final 8ms timer interval measured around 15.6ms on this desktop. This is a
measured local scheduling choice, not a universal refresh-rate claim.

The shader separately handles interpolated mesh deformation, perspective,
whole-object throw, double-sided diffuse paper lighting and an analytic soft
shadow. Production playback is 680ms: about 524ms for crumpling, then a short
translation/rotation/fade. Initialization adds cold-start capture/upload costs.

All initialization and rendering exceptions enter a safe fallback; faulty device
state is disposed and the next effect may initialize again. Product deletion
still closes the already-persisted note. Capture is bounded, playback has a
watchdog, and cancellation unhooks drawing independently of frame delivery.

## Assets and dependencies

Vortice.Direct3D11, Vortice.D3DCompiler and Vortice.DirectComposition **3.8.3**.
DXGI, DirectX, Direct2D bindings and SharpGen runtime are transitive dependencies.
No Direct2D rendering engine or browser is instantiated.

The prepared animation is derived from the MIT-licensed
[nagasawa / ITEM Inc. Codrops demo](https://github.com/item-develop/paper-crumple-demo),
pinned at `f84648b001dd13becda7a629ab5398cecfe3a252`. It has 3,500 vertices,
6,762 triangles and 38 moving frames, occupying 4,365,160 bytes. Source-format
decoding occurs only in `tools/nootfx-assets`; Three.js there is an offline parser,
not an application dependency. Runtime data is immediately suitable for indexed
GPU drawing and structured-buffer animation lookup. Notices and hashes ship in
`Assets/NootFX`. No fictional demo paper designs are shipped.

## Important files

- `Noot.Proto/NootFX/NootFxService.cs`: capture/playback/cancellation/fallback and diagnostics.
- `Noot.Proto/NootFX/D3DRenderer.cs`, `FxOverlay.cs`, `Paper.hlsl`: native resources, host and rendering.
- `Noot.Proto/NootFX/PaperBake.cs`, `Assets/NootFX`: validated prepared data and notices.
- `NoteView.xaml.cs`, `NoteWindow.xaml.cs`, `ProductWindow.cs`: small integration points and deterministic native event cleanup.
- `ProductWindow.FxChecks.cs`, `Noot.FxChecks`, `verify-fx-ui.ps1`: isolated native, asset and UI checks.
- `tools/nootfx-assets`: pinned, reproducible offline conversion.

Repeated-use testing also uncovered existing closed-note retention through native
callbacks. Closing now detaches menu, keyboard, control, AppWindow and XamlRoot
subscriptions, releases the Composition subtree and clears window content. The
weak-reference regression explicitly checks that closed windows are collectible.

## Development controls and reproduction

```powershell
.\proto\run.ps1 -Build -FxLab
# Pass the PID printed by WinApp:
.\proto\verify-fx-ui.ps1 -AppPid <pid>

.\proto\run.ps1 -VerifyFx
.\proto\run.ps1 -VerifyProduct
.\proto\run.ps1 -Verify
dotnet run --project proto/Noot.FxChecks/Noot.FxChecks.csproj
dotnet run --project proto/Noot.StorageChecks/Noot.StorageChecks.csproj
dotnet run --project proto/Noot.GeometryChecks/Noot.GeometryChecks.csproj
```

The lab is DEBUG-only and uses fresh isolated storage. Slow playback takes 18
seconds. Hold deformation allows slider inspection from 0 to 1; its safety bound
is 60 seconds. Undo cancels a held effect. Clear Hold and Slow before a subsequent
normal-speed deletion. Inspection controls are absent from Release/everyday UI.
`--verify-fx --fx-retention-probe --inspect-note` is a short local retention
diagnostic that leaves the synthetic session open for inspection.

## Verification evidence and limitations

Debug and Release builds passed with zero warnings/errors. Checks passed:
12 storage checks, the existing geometry checks (466,560 and 1,570,752 sampled
patches), 9 prepared-asset checks, all 16 Release product checks and all 25
Release legacy-renderer checks. The dedicated FX run completed 72 successful
effects, including 30 repeats on one live note and 40 delete/reopen cycles;
capture cancellation, injected initialization/asset/render failures, recovery,
and final dormancy also passed. Native UI Automation passed all five checks
for identifiable text, scrubbed rendering, cancellation/undo, normal-speed
deletion and subsequent editing.

Measured on **Windows 10 build 19045 / NVIDIA GeForce RTX 3060 Ti**:

| Observation | Result |
| --- | --- |
| Median of successful effects' median frame intervals | 15.40ms |
| Largest per-effect frame-interval p95 | 16.24ms |
| Median of per-effect median CPU draw/submit/present time | 0.24ms (not GPU timing) |
| Same live note, private memory after diagnostic GC at repeats 10/20/30 | 159.4 / 160.1 / 157.5 MiB |
| Same live note, handles at those points | 1,400 / 1,403 / 1,397 |
| Delete/reopen cycles, private memory after diagnostic GC at 10/20/30/40 | 232.9 / 238.2 / 233.3 / 231.2 MiB |
| Delete/reopen cycles, handles at those points | 2,788 / 2,720 / 2,730 / 2,737 |
| Closed-note weak references alive after each diagnostic GC | 1, without accumulation |
| Device creation during ordinary repeats / 13-note run | 1 shared device |
| Cold idle / 13-note idle CPU during 1.2-second samples | 0 / 15.625ms; zero FX frames |

The unmodified HEAD baseline was built and measured separately: one note used
0 CPUms and 13 notes used 15.625 CPUms during two-second idle samples. These
short, timer-quantized samples show no obvious new idle load; they do not prove
precise CPU equivalence. The dormant renderer retains shared graphics resources
after its first use. Diagnostic full collections are test instrumentation, not
part of product deletion. Finite repeat samples show stabilization rather than
continuous resource growth; injected failures intentionally recreate devices.

Local evidence: `artifacts/nootfx-checked/fx-verification-final.json` and
`fx-frames-final.jsonl`, `artifacts/nootfx-release/product-verification.json`
and `verification.json`, plus `artifacts/nootfx-ui/ui-verification.json` and
the native screenshots `01-note.png` through `05-pose-1.png`. JSON evidence
and screenshots are ignored; generated binaries, heap dumps and synthetic
note stores do not belong in source control.

An additional 18-second native playback was captured at approximately 14.0,
15.2 and 16.6 seconds (`artifacts/nootfx-ui/throw-*.png`). Inspected frames
show compact paper translating right, rotating and fading. The native check
then confirmed both note and overlay disappeared and restored the synthetic
note through Undo (`throw-verification.json`). This is sampled visual evidence,
not a claim that Computer Use observed continuous motion.

Computer Use skill was read and `@oai/sky` initialized. It successfully listed and
read the real Noot control window's accessibility tree. Its Windows screenshot
call failed with **`SetIsBorderRequired failed: No such interface supported
(0x80004002)`** on Windows 10 build 19045. A fresh target selection did not resolve
the missing capture capability. Click failed with **`coordinate input geometry
is unavailable`**; setting a slider from its returned accessibility tree failed
with **`element 14 is not available in cached app state`**. Tab/Space keyboard
attempts did not create a note. The sticky toolwindows were also absent from its
window inventory. These are recorded failures, not successful Computer Use steps.

WinApp CLI 0.6.1 (already supplied by the repository's SDK package) provides the
working native UI Automation and screen-capture path. Those screenshots were
actually inspected: real note text stayed correctly oriented on the flat mesh,
non-uniform folds developed, and the final silhouette was compact 3D paper.
This substantiates the native rendering implementation and satisfies the accepted
visual-validation method. The evidence files retain `computerUseVerified: false`
because Computer Use itself did not successfully capture the application.

Remaining visual/architectural limits: one borrowed rectangular bake is scaled
to each note's aspect ratio; extreme proportions are not a new physical solve.
The back is plain note-coloured paper. Lighting has no mesh self-shadow map;
the desktop shadow is an approximation. Mixed-DPI/monitor-boundary movement,
Windows 11, integrated GPUs, remote desktop and device removal have not been
physically tested. Injected faults prove lifecycle fallback, not every driver
failure. Memory observations cover finite local runs, not a universal leak proof.

Next milestone recommendation: art-direct a Noot-owned bake and refine crease
lighting with user feedback, then broaden verification to mixed-DPI displays
and integrated GPUs. Do not add new paper interactions yet.
