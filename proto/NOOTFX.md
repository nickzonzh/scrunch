# NootFX native discard

This milestone adds three Noot-owned paper trajectories and reproducible variation
without changing the native D3D11 / DirectComposition architecture. The ordinary
note remains a WinUI editor. Only its captured pixels enter the effect.

## Families and offline authoring

`tools/nootfx-assets/author.mjs` generates Corner Crush, Side Scrunch and Centre
Collapse from an initially flat sheet. No Houdini installation or callable
Houdini tool was found on this host. The replacement is an offline, art-directed
kinematic bake, not a claim of a Houdini/Vellum simulation.

Each family has its own seven oblique hinge directions, offsets, signed angles
and overlapping onset/completion times. Corner Crush starts diagonally at a
corner; Side Scrunch gathers an edge at unequal angles; Centre Collapse begins
with an off-centre depression before unequal corner folds. Subsequent hinge
axes follow the preceding deformation. A gradual, unequal closing pressure
compacts the folded sheet. The families are selected discretely; unrelated
positions are never blended together.

Direct hinge warps produced stretched spikes and serrations in the first native
candidate and were rejected. The accepted authoring path uses an irregular,
reflection-symmetric panel lattice plus 90 offline edge-length projection sweeps
per frame, guided weakly toward the authored fold trajectory. This resists long
stretched triangles while allowing directional creases and irregular remnants.
There is no runtime simulation, collision solver, random vertex motion, or
regional runtime phase offset. Generation takes a few seconds on this host.

This is an artistic approximation. Projection is not a perfectly inextensible
solve, and self-intersection is possible. The checked maximum edge stretch is
reported by `Noot.FxChecks`; it must remain below 2x rest length. It is deliberately
reported rather than described as physically exact paper. Very close held views
reveal the triangular panel construction; broad reverse faces can appear briefly.

The script has no package dependencies or network input. Run with Node 24.11.0:

```powershell
node tools/nootfx-assets/author.mjs
node tools/nootfx-assets/author.mjs --check
```

`--check` regenerates in memory and compares every output byte. The provenance
records the generator SHA-256, fold controls, solver settings, output hashes,
counts and final bounds. The same pinned Node/runtime should be used for byte
comparisons; regenerated assets should always receive native visual review.

## Prepared data and reflection

The existing little-endian **NFX1 format is unchanged**: four u32 header words
(magic, vertex count, frame count, index count), float2 UVs, u32 indices, then
frame-major float4 position / float4 normal pairs. Positions use a unit rest
sheet, x right, y down, z toward the viewer. Normals are area-weighted and unit
length. Frame zero matches the original note texture exactly.

All three files share **625 vertices, 1,152 triangles and 61 frames**. The 25x25
UV lattice is spatially irregular but symmetric under X/Y reflections; alternating
diagonals preserve reflected triangle connectivity. A reflected effect reads
positions from the opposite lattice location, reflects the geometry vector and
normal, and samples the **original vertex UV**. Thus `M F(M uv)` changes where the
fold begins while preserving readable, unmirrored note text. No extra orientation
bakes are needed. Runtime initialization checks lattice symmetry and shared
family UVs, indices and frame counts before allocating all family resources.

Prepared file sizes:

| Asset | Bytes |
| --- | ---: |
| corner-crush.nfx | 1,238,840 |
| side-scrunch.nfx | 1,238,840 |
| centre-collapse.nfx | 1,238,840 |
| Total | 3,716,520 |
| Previous single borrowed bake | 4,365,160 |

The family total is **648,640 bytes smaller (14.9%)**. GPU sample buffers total
3,660,000 bytes; UV and index buffers are shared across all three families.
The family library is immutable and loaded once with the shared lazy device.
Only the selected family's buffer is bound for an effect.

The original Codrops bake and its FBX/EXR conversion path are no longer used or
shipped. The old `.nfx`, source-specific license/provenance, fetch/conversion
scripts and development-only Three.js dependency were removed. Earlier evidence
and attribution remain in Git history (e796113 / 4787c10); no borrowed source
geometry or motion enters these new bakes. Vortice's MIT notice remains shipped.

## Deterministic seed contract (version 1)

`FxVariation.FromSeed(uint)` uses explicit unchecked 32-bit mixing, with no
`Random`, runtime `GetHashCode`, or timing input inside the mapping. Family is
`seed % 3`; orientation is `(seed / 3) % 4`. Twelve consecutive seeds cover the
complete family/orientation cross product. Remaining values come from a fixed
integer mixer and its high 24 bits.

Production uses a UI-thread-owned incrementing event counter initialized from
system uptime. That chooses a fresh event seed; **all visual properties thereafter
are a pure function of the recorded seed**. Replaying the same seed is independent
of any prior effects. DEBUG lab overrides and diagnostics expose that exact seed.
The native verification suite explicitly supplies fixed seeds.

| Property | Range |
| --- | --- |
| Playback duration | 722â€“798ms (760ms +/-5%), excluding capture/device setup |
| Gather completion | 70% of normalized playback |
| Compact hold | 3.5â€“6.5% of playback, about 25â€“52ms |
| Throw | Starts only after gathering and hold |
| Horizontal travel | Left or right, 24â€“30% of geometric-mean paper size |
| In-plane release rotation | Signed 0.40â€“0.58 radians, about 23â€“33 degrees |
| Depth rotation | Signed 0.24 radians, about 14 degrees |
| Vertical departure coefficient | -0.44 to -0.26, followed by a small downward arc |
| Fade | Begins at 45% of release; applied once to the assembled opaque scene |

There is no overshoot, cartoon spin or long travel. Hold length changes the start
of release, not the total duration. Slow playback uses the identical normalized
timeline multiplied to approximately 18 seconds.

## Aspect handling and material

The exact rectangular capture hands off unchanged. From deformation 0.15 to 1,
its X/Y scales converge 85% toward the geometric mean of width and height. This
preserves the source note initially and avoids a flat sausage at wide/tall
proportions. It is bounded geometric preconditioning, not a separate physical
solve for each aspect. The checked supported shapes are 220x180, 440x180, 220x440,
440x440, and 300x320 DIPs, including empty/dense text and all six paper colours.

The shader retains perspective-correct captured ink, an unprinted coloured back,
4x MSAA (single-sample fallback), a 1024-square filtered fold-shadow map and a soft
desktop shadow. Baked normals are blended toward the actual geometric facet
normal by 26â€“56%, depending on crease disagreement and deformation progress.
This reinforces fold direction without treating the entire sheet as hard facets
at handoff. Opaque fold rendering and a single composite fade preserve occlusion.

## Lifecycle and native architecture

`NootFxService.Shared` still owns at most one lazy hardware D3D11 device, reusable
buffers/render targets and one hidden, non-activating, click-through overlay HWND.
Rendering remains DXGI composition swap chain -> DirectComposition -> HWND.
Ordinary editing does not allocate D3D devices. There is **no idle FX timer**.
Effect-scoped timer callbacks stop and detach on success, cancellation, timeout
and failure. The per-note captured texture is released after each playback;
shared resources remain reusable. A busy request takes the immediate fallback.

The product persists a recoverable discard before any capture/animation. Graphics
failure still closes that already-saved note. Undo cancels capture/playback and
restores editing. Keyboard discard, reduced motion and ordinary window-close
discard remain immediate. The capture timeout remains 1.5 seconds; playback has
an independent watchdog. The original Composition pickup/peel renderer, native
transparency and window cleanup paths are unchanged.

## Lab and visual QA

```powershell
.\proto\run.ps1 -Build -FxLab
.\proto\verify-fx-seeds-ui.ps1 -AppPid <pid> -Record -Aspects
.\proto\run.ps1 -VerifyFx
.\proto\run.ps1 -VerifyProduct
.\proto\run.ps1 -Verify
dotnet run --project proto/Noot.FxChecks/Noot.FxChecks.csproj
dotnet run --project proto/Noot.StorageChecks/Noot.StorageChecks.csproj
dotnet run --project proto/Noot.GeometryChecks/Noot.GeometryChecks.csproj
```

The DEBUG-only isolated lab has explicit uint seed input, previous/next seed,
family/orientation/timing display, replay, production speed, slow playback, held
progress, sample notes and screen-corner placement. Replay restores the existing
note after rendering; Delete latest test note uses the ordinary saved-discard
path. Hold has a 60-second safety timeout. These controls do not appear in Release.

`proto/fx-golden-seeds.json` is the capture fixture: **0â€“11, 42, 99, 314, 1337,
9787, 4399**. Seeds 9787 and 4399 sit near the duration limits. The fixture includes
all families and reflections, both throw directions, varied holds and the five
supported size classes. Capture six held stages per seed and three additional
stages for every primary family across all six size/colour/content samples.
Record actual product deletion at production speed, then verify close and Undo.

Computer Use was initialized and listed Noot, but target capture returned
`Computer Use app approval timed out`. No Computer Use capture verification is
claimed. The existing WinApp UI Automation / screen-capture path was used as
explicitly authorized. Its frame-artifact mode supports at most 30 fps; an initial
60 fps recording request failed and is retained separately. Actual recording
cadence is reported in each `manifest.json` and is not a GPU frame-rate measure.

## Current verification evidence

Final Debug and Release builds pass with zero warnings/errors. The clean output
directories contain exactly the three owned bakes, provenance and Vortice notice;
their assets and shader match the current source bytes. DEBUG lab strings are
absent from the Release assembly.

Verified on Windows 10 / NVIDIA GeForce RTX 3060 Ti:

| Check | Result |
| --- | --- |
| Storage | 12 passed |
| Geometry | 466,560 projected patches and 1,570,752 crumple patches passed |
| Prepared assets / seed contract | 30 passed, including 10,000 seeds, fixed reference vector, uint boundary, fixture consistency, rest-panel orientation, all reflection handoffs, normalized normals, compact bounds, bounded frame steps and malformed-data rejection |
| Release product integration | 16 passed |
| Release original paper renderer | 25 passed |
| Native FX lifecycle/resources | 60 passed; 80 completed effects |
| Golden seed UI/capture | 42 passed; 18 seeds, six aspect/content samples, 18 real discard/Undo recordings |
| Existing extended native UI suite | 13 passed, including four screen corners, normal/slow recording and subsequent editing |
| Original native FX UI suite | 5 passed, including held-playback Undo and subsequent editing |
| Seed control boundary checks | Previous 0 -> uint max; Next -> 0; invalid seed cannot replay |
| Regeneration | All three bakes and provenance reproduce byte-for-byte |

| Observation | Current run |
| --- | --- |
| Median of per-effect median frame intervals | 15.52ms |
| Largest per-effect frame-interval p95 | 16.45ms |
| Median CPU draw/submit/present time | 0.23ms; GPU time not measured |
| Same-note private memory after GC, repeats 10/20/30 | 184.5 / 182.5 / 183.1 MiB |
| Same-note handles | 1,387 / 1,382 / 1,384 |
| Delete/reopen private memory after GC, cycles 10/20/30/40 | 250.1 / 251.7 / 257.7 / 253.6 MiB |
| Delete/reopen handles | 2,774 / 2,695 / 2,715 / 2,735 |
| Closed windows alive after each diagnostic GC | 1, without accumulation |
| Cold / 13-note idle CPU over 1.2 seconds | 15.625ms each; zero FX frames |
| Normal-use device count | One shared device; fault injection intentionally recreates it |

These finite local samples show no meaningful frame-time regression or accumulating
retention. They are not a universal leak guarantee or GPU benchmark. Diagnostic
collections belong only to the test suite. Both attached displays use 100% scale;
negative desktop coordinates were exercised again.

Visual review covered **162 held captures** (108 seed stages plus 54 aspect/family
stages), source notes, all four screen corners and the timestamped temporal
sequences from all **18 production discard recordings**. Their 378 distinct
recording images were assembled into labelled contact sheets and inspected,
including capture -> fold -> compression -> compact hold -> departure -> fade.
The recorder achieved 22.55â€“23.81 fps against its 30 fps request; this is separate
from the renderer's measured frame intervals. The existing 18-second slowed wide
note recording was also inspected at twelve points. This is native capture and
temporal-sequence inspection, not a claim of Computer Use or continuous human
playback observation. Other desktop windows sometimes appear in transparent
padding; black areas at screen-corner captures are outside the physical desktop.

**Visual decision:** retain this as Noot's stylised paper discard. The prominent
parallel accordion collapse is gone; folds start in different places, collect
unevenly and finish in compact, asymmetric paper shapes. Reflections preserve
readable ink. Wide/tall notes gather into volume instead of retaining a sausage
silhouette. The release stays restrained and follows the crumple.

It is finished for this stylised interaction milestone, not photoreal paper.
The remaining visible limitation is polygonal panel construction in enlarged
held poses. Centre Collapse briefly exposes a broad reverse face before its
last folds close; some individual folds remain straight by design. Neither is
a recurring corrugated tube or a soft, flowing-cloth trajectory in the reviewed
set. The lack of self-collision and residual local strain should remain explicit
if future work targets physically accurate, close-up paper.

Evidence (local ignored artifacts):

- `artifacts/nootfx-signature-checked/fx-verification.json`, `nootfx.jsonl`,
  `performance-summary.json`, `asset-checks.txt`.
- `artifacts/nootfx-signature-release/product-verification.json`, `verification.json`.
- `artifacts/nootfx-signature-final-ui/seed-ui-verification.json`, `golden-1.png`
  through `golden-6.png`, `aspect-review-0.png` through `aspect-review-5.png`,
  `seed-<seed>-production.mp4`, their `.frames` directories and `motion-review-<seed>.png`.
- `artifacts/nootfx-signature-legacy-ui/polish-ui-verification.json`, `seed-controls.json`,
  `normal.mp4`, `slow.mp4`, `corner-review.png`, `slow-review.png`.
- `artifacts/nootfx-signature-basic-ui/ui-verification.json` and its native screenshots.

`tools/nootfx-assets/review-captures.py` creates labelled evidence sheets from raw
WinApp screenshots and recordings; it does not synthesize renderer output. Its
production crop currently assumes the recorded 2560px desktop / 1600px capture
fixture and should be adjusted for a different monitor layout. Pillow is only a
development image-review dependency, not an asset-generation or runtime dependency.

Historical baseline: on Windows 10 / RTX 3060 Ti, 4787c10 measured a 15.43ms median
frame interval and 0.24ms median CPU draw/submit/present time. Private memory after
GC was 188.1/186.0/187.1 MiB for repeated playback and 256.7/250.2/254.7/262.4 MiB
for delete/reopen batches. These are previous-run comparisons, not fresh measures.
Physical mixed-DPI, Windows 11, integrated GPUs, device removal and hardware
without 4x MSAA remain outside the current host's verified coverage.
