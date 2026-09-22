# ScrunchFX native discard

ScrunchFX is the retained internal graphics name for **Scrunch**. The current app
is `artifacts/scrunch-shell/Scrunch.exe`, launched with `run.ps1`. Historical
reports below retain their original executable names and paths. The shell rename
does not change the prepared assets, shader, renderer or motion timings.

This candidate refines matte shading and collapse timing on the three Scrunch-owned
paper trajectories without changing the native D3D11 / DirectComposition architecture. The ordinary
note remains a WinUI editor. Only its captured pixels enter the effect.

## Families and offline authoring

`tools/scrunchfx-assets/author.mjs` generates Corner Crush, Side Scrunch and Centre
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
reported by `Scrunch.FxChecks`; it must remain below 2x rest length. It is deliberately
reported rather than described as physically exact paper. Very close held views
reveal the triangular panel construction; broad reverse faces can appear briefly.

The script has no package dependencies or network input. Run with Node 24.11.0:

```powershell
node tools/scrunchfx-assets/author.mjs
node tools/scrunchfx-assets/author.mjs --check
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

Gather now integrates a short acceleration (12% of gather time), a steady middle
and a short deceleration (18%). Position and speed are continuous. At nominal
760ms, deformation 0.2 arrives at 122.4ms instead of 152.7ms; deformation 0.35-0.85
takes 226.1ms instead of 189.9ms (+19.1%). Gather still finishes at 532ms, with the
same seeded hold and release. The existing baked trajectories already ease their
individual folds, so the former whole-gather smoothstep unnecessarily rushed the
middle while lingering on the almost-flat start.

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
normal by 22-44% (previously 26-56%), depending on crease disagreement and deformation progress.
This reinforces fold direction without treating the entire sheet as hard facets
at handoff. Opaque fold rendering and a single composite fade preserve occlusion.

Ambient fill is 0.66 instead of 0.52; directional contribution is 0.34 instead of
0.48, with a restrained 0.18 diffuse wrap at grazing angles. Fold visibility now
attenuates the directional term by at most 28%, instead of extinguishing it.
There is still no specular lobe. Front and unprinted back use the same matte
response; existing captured grain/ink stays intact. No new texture, noise, mesh,
asset, sample buffer or render pass was added. Geometry and shadow-map resolution
are unchanged. This lifts opposing planes without rounding the silhouette.

## Lifecycle and native architecture

`ScrunchFxService.Shared` still owns at most one lazy hardware D3D11 device, reusable
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
dotnet run --project proto/Scrunch.FxChecks/Scrunch.FxChecks.csproj
dotnet run --project proto/Scrunch.StorageChecks/Scrunch.StorageChecks.csproj
dotnet run --project proto/Scrunch.GeometryChecks/Scrunch.GeometryChecks.csproj
```

The DEBUG-only isolated lab has explicit uint seed input, previous/next seed,
Next QA seed, family/orientation and gather/hold/exit timing display, replay,
production speed, slow playback, held
progress, sample notes and screen-corner placement. Replay restores the existing
note after rendering; Delete latest test note uses the ordinary saved-discard
path. Hold has a 60-second safety timeout. These controls do not appear in Release.

`proto/fx-golden-seeds.json` is the capture fixture: **0â€“11, 42, 99, 314, 1337,
9787, 4399**. Seeds 9787 and 4399 sit near the duration limits. The fixture includes
all families and reflections, both throw directions, varied holds and the five
supported size classes. Capture six held stages per seed and three additional
stages for every primary family across all six size/colour/content samples.
Record actual product deletion at production speed, then verify close and Undo.

Computer Use was initialized and listed Scrunch, but target capture returned
`Computer Use app approval timed out`. No Computer Use capture verification is
claimed. The existing WinApp UI Automation / screen-capture path was used as
explicitly authorized. Its frame-artifact mode supports at most 30 fps; an initial
60 fps recording request failed and is retained separately. Actual recording
cadence is reported in each `manifest.json` and is not a GPU frame-rate measure.

## Material candidate review (21 September 2026)

The bakes, family mapping, reflection mapping, throw bounds and seed contract are
unchanged. Regeneration still matches every asset and provenance byte. The native
held captures confirm the same silhouettes and directional crease layout.

Reviewed all 18 golden seeds at six held stages, all three families on the six
size/colour/content samples, and every distinct timestamped frame from the 18
production-speed discard recordings. The latter are temporal-sequence reviews
of native recordings, not continuous human playback observation. All recorded
deletions closed the note, went dormant and restored it through Undo. The new
Next QA seed control was checked against the entire fixture including wraparound.

| Seed | Family | Reflection | Throw | Total / gather / hold / exit (ms, rounded) | Visual assessment |
| ---: | --- | --- | --- | --- | --- |
| 0 | Corner | None | Right | 752 / 526 / 46 / 180 | Lower-right crush; compact irregular wad |
| 1 | Side | None | Right | 750 / 525 / 28 / 197 | Right edge gathers; open asymmetric bundle closes |
| 2 | Centre | None | Right | 724 / 507 / 40 / 177 | Interior cup; broad reverse flap closes into hooked wad |
| 3 | Corner | X | Left | 764 / 535 / 49 / 180 | Lower-left origin; coherent reflected folds |
| 4 | Side | X | Right | 759 / 531 / 41 / 187 | Left edge gathers; coherent opposing throw |
| 5 | Centre | X | Left | 785 / 549 / 48 / 187 | Reflected cup and flap; compact angular finish |
| 6 | Corner | Y | Left | 752 / 526 / 38 / 188 | Upper-right crush; preserved ink and volume |
| 7 | Side | Y | Left | 790 / 553 / 51 / 186 | Right edge, reversed vertical gathering order |
| 8 | Centre | Y | Right | 774 / 542 / 37 / 195 | Reflected interior cup; upright irregular finish |
| 9 | Corner | XY | Left | 765 / 536 / 30 / 200 | Upper-left crush; coherent compact finish |
| 10 | Side | XY | Right | 761 / 533 / 27 / 202 | Left edge, reversed vertical order; short hold still visible |
| 11 | Centre | XY | Left | 789 / 552 / 47 / 189 | Reversed cup/flap; asymmetric upright finish |
| 42 | Corner | Y | Left | 732 / 512 / 26 / 193 | Same geometry as 6; brisk timing remains legible |
| 99 | Corner | X | Right | 728 / 510 / 38 / 181 | Same geometry as 3; strongest reach stays restrained |
| 314 | Centre | None | Left | 791 / 554 / 50 / 187 | Same geometry as 2; longer timing and opposite exit |
| 1337 | Centre | X | Right | 744 / 521 / 36 / 188 | Same geometry as 5; compact finish, shorter reach |
| 9787 | Side | Y | Right | 722 / 505 / 40 / 176 | Minimum-duration case remains readable |
| 4399 | Side | Y | Left | 798 / 559 / 49 / 190 | Maximum-duration case remains brisk |

No weak/outlier seed justified changing the mapping. Matte fill removes the
large dark patches that made the old final wad resemble foil or rock; crease
edges remain angular and visible. Existing grain is most legible on yellow and
peach paper. No silky undulation or new soft geometry was introduced.

Variation is deliberately finite: **three trajectories, twelve reflected
family/orientation combinations**, then bounded timing and release variation.
Reflections move the collapse origin and preserve ink, but are mathematically
reflections, not additional independently authored trajectories. Seeds with the
same family/orientation have identical held geometry. The three primary families
do differ in origin, intermediate motion and final shape; this is more than one
excellent animation with different throws. Repeated side-by-side QA reveals the
finite library; no claim of a unique wad for every uint seed is made.

Initial screen captures from the sandbox were black and rejected. The fresh
interactive-desktop capture suite is the visual evidence. The review-sheet tool
now rejects solid/blank held captures. Computer Use did not list the lab window;
WinApp UI Automation and native screen recordings supplied the evidence.

Candidate evidence is under `artifacts/scrunchfx-material-final-ui/`:
`material-before-after.png`, `golden-1.png` through `golden-6.png`,
`aspect-review-0.png` through `aspect-review-5.png`, all 18
`seed-<seed>-production.mp4` recordings and `motion-review-<seed>.png` sheets,
`seed-ui-verification.json` and `qa-control-verification.json`.
`artifacts/scrunchfx-material-checked/seed-summary.json` records exact timing,
direction, reach, rotation and vertical departure per seed.

### Native close investigation

The material pass exposed an intermittent failure in the existing closed-window
collection check. An unchanged `41ae31b` executable reproduced it: 2, 11, 20 and
30 closed windows remained at the four checkpoints. Candidate runs also alternated
between passing and accumulating closed windows. Extra dispatcher waits and
collections did not reliably resolve it and were removed.

With the user's explicit scope extension, close now performs terminal teardown:
detach pending activation/focus and view Loaded/Unloaded handlers, clear managed
callbacks from the view to its owner and from the window to the product, release
the shadow image/visual references and menu, and explicitly disconnect the system
backdrop. A closed view cannot reinitialize graphics or attach its asynchronously
loaded shadow image. Temporary Unloaded still uses the original reloadable path.
No collection or retention polling was added to production.

Heap inspection after a failed test found closed objects without ordinary managed
roots or positive sampled COM-wrapper counts after the test returned. That did
not identify one definitive native root. The fix removes the remaining lifetime
links explicitly; repeated native collection/resource checks are the evidence for
its effectiveness. The DEBUG check now tracks both windows and their NoteViews,
using the original collection timing and threshold (at most two); the current
test-local note normally accounts for one. Independent fallback checks run even
if the retained-object result will fail at the end.

Failed runs and successful reruns remain separately named in
`artifacts/scrunchfx-material-checked/`; diagnostic dumps are local ignored artifacts
under `artifacts/diagnostics/`. No ordinary user notes were used.

The final physical-key check also reproduced an existing `Ctrl+Shift+Delete`
failure in the unchanged executable: the focused TextBox consumed Delete before
the parent accelerator. A narrow preview handler now routes that exact existing
shortcut to immediate saved discard; other Delete combinations retain text-editing
behavior. It is detached during close. This follows Microsoft's
[preview-key routing guidance](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-accelerators#override-default-keyboard-behavior).
The native UI script now sends the real shortcut from the focused editor and
checks immediate close, absence of an overlay, and exact-text Undo.

### Candidate verification configuration

Debug builds use `dotnet build proto/Scrunch/Scrunch.csproj -p:Platform=x64
-o proto/artifacts/scrunchfx-material-checked`. Local Release validation uses
`-c Release -p:Platform=x64 -p:PublishTrimmed=false
-o proto/artifacts/scrunchfx-material-release`, matching the previous checked
Release runtime configuration. Both final builds have zero warnings/errors.
The shipped source assets/shader match both output directories, exactly three
bakes remain, and DEBUG lab strings are absent from the Release assembly.

An initial Release rebuild omitted `PublishTrimmed=false`; the existing project
default disables reflection-based JSON serialization and its verification report
could not be written. That attempt is preserved in
`artifacts/scrunchfx-material-release/trimmed-config-check-failure.txt`. Fresh reports
from the untrimmed validation build replace the old reports; trimmed publishing
is not validated by this milestone. Packaging/release configuration remains out
of scope.

### Final candidate results

Verified on Windows 10 / NVIDIA GeForce RTX 3060 Ti, on both attached displays
(100% scaling, including negative desktop coordinates):

| Check | Result |
| --- | --- |
| Final native FX lifecycle | 62 passed; 80 completed effects, 30 same-note replays, 40 saved delete/reopen cycles, injected initialization/asset/render failures, recovery, native reduced-motion fast path and final idle |
| Retention repetitions | Four fresh runs after explicit teardown: 320 completed effects and 160 delete/reopen cycles; closed-window counts stayed at 1 in every batch; all three runs that also tracked closed NoteViews stayed at 1 for those too |
| Release product / original paper | 16 / 25 passed, including persistence, cancellation, Undo, editing, reduced motion and idle shutdown |
| Prepared assets / timing / seeds | 32 passed, including 10,000 deterministic seeds and the longer middle-collapse interval |
| Storage / geometry | 12 passed; 466,560 projected and 1,570,752 crumple patches passed |
| Golden UI suite | 42 passed; all 18 seeds, 162 held captures, six aspect/content samples and 18 real discard/Undo recordings |
| QA seed control | Entire fixed fixture and wraparound passed |
| Extended native UI suite | 13 passed; normal/slow recordings, four corners, six samples, Undo and subsequent editing |
| Final focused-editor UI suite | 7 passed; held-playback Undo, completed-discard recovery, actual Ctrl+Shift+Delete and exact-text Undo |
| Assets | Byte-exact regeneration; same three bakes, 3,716,520 bytes total; no additional rendering resources or passes |

| Final performance observation | Measured value |
| --- | --- |
| Median of per-effect median frame intervals | 15.26ms |
| Largest per-effect frame-interval p95 | 29.91ms |
| Median CPU draw/submit/present duration | 0.241ms; largest per-effect p95 1.42ms; GPU time not measured |
| Same-note private memory after GC, repeats 10/20/30 | 180.5 / 177.9 / 180.4 MiB |
| Same-note handles | 1,357 / 1,352 / 1,357 |
| Delete/reopen private memory after GC, cycles 10/20/30/40 | 257.7 / 244.2 / 251.2 / 248.7 MiB |
| Delete/reopen handles | 2,741 / 2,659 / 2,679 / 2,699 |
| Closed windows / NoteViews after each GC | 1 / 1; no accumulating batch |
| Cold / 13-note idle CPU over 1.2 seconds | 0 / 78.125ms of total process CPU; zero FX frames |
| Normal-use devices | One shared lazy device; fault injection deliberately recreates it |

The fresh unchanged-build probe measured 15.31ms median frame intervals, 32.10ms
largest per-effect p95 and 0.24ms median draw time. Its shorter 20-effect workload
is a host-cadence cross-check, not a statistically controlled benchmark. The three
earlier teardown runs ranged from 12.19 to 17.98ms median frame intervals and
0.23 to 0.25ms median draw time. This supports no material increase in rendering
cost, but does not establish a frame-time improvement or guaranteed 60fps.

Total 13-note process idle CPU varied from 46.875 to 250ms in those runs, versus
15.625ms in the baseline probe. That includes WinUI and collection work and is
not an isolated FX CPU measure; it should not be described as zero application
CPU or a proven baseline-equivalent idle CPU result. The directly verified
dormancy invariant is zero FX frames with the timer stopped. Memory stayed in
the previous milestone's approximate range without closed-object accumulation.
These finite checks do not prove an unbounded leak-free run.

Final native reports/logs are `artifacts/scrunchfx-material-checked/fx-verification.json`,
`scrunchfx.jsonl` and `final-performance-summary.json`; the three earlier teardown
runs are `fx-verification-close-fix-1.json` through `-3.json`, with matching logs
and `close-fix-performance-summary.json`. Fresh baseline comparison is preserved
under `artifacts/scrunchfx-signature-checked/*matched-idle*`. Release reports are in
`artifacts/scrunchfx-material-release/`.

Additional native captures are in `artifacts/scrunchfx-material-polish-ui/`:
`normal.mp4`, `slow.mp4`, their timestamped frames, `normal-review.png`,
`slow-review.png`, `corners-review.png` and `polish-ui-verification.json`.
Reviewed all 24 distinct normal recording images, 36 timestamp-spaced slow
samples and all four source/fold corner pairs. Those videos recorded at about
30fps; the earlier 18-seed recordings achieved 22.72-23.91fps (360 distinct images).
Neither recorder rate is the renderer's GPU frame rate. Corner images include
black pixels outside desktop bounds, not missing paper inside the screen.
The final seven-check UI report and held captures are in
`artifacts/scrunchfx-material-native-ui/`; the pre-fix keyboard reproduction is
`artifacts/scrunchfx-material-polish-ui/keyboard-baseline.json`.

**Recommendation: visually finished enough for this candidate.** The lighter
matte planes preserve the stiff crease structure and the unchanged silhouettes;
the longer middle collapse reads clearly without extending deletion. No reviewed
seed needs remapping. Centre Collapse still exposes a broad reverse flap before
its hooked final wad, close held views reveal the panel lattice, and repeated
side-by-side playback reveals the finite three-trajectory library. Those are
accepted stylisation limits, not a newly weak seed. Mixed-DPI / Windows 11 and
long-duration resource testing remain outside this host's evidence. Stop visual
tuning here and retain the captured suite for future regressions.

## Structured variation sweep (21 September 2026)

This follow-up inspected the live family/seed/reflection mapping, shader, prepared
assets, throw and timeline, lab controls, golden fixture and verification scripts
before changing implementation. The initial fresh Debug assembly matched the
material-pass assembly byte for byte (SHA-256
`86536FDEBE963A1E835796DAF5B7884DA4EEDC96CF84FA7327E0693C22B8AE17`).
The existing square and aspect held captures were therefore reviewed as applicable
evidence, alongside fresh production, slow and held captures.

### Coverage and judgement

All 18 golden seeds were reviewed at production speed through their timestamped
image sequences, slow playback through 36 timestamp-spaced observations plus the
source frame, and held deformation. This is native temporal-sequence inspection,
not continuous human playback observation. The current sweep held 0, .4, .7 and 1;
the earlier square sweep supplied 0, .2, .4, .6, .8 and 1 for every seed.

The mapping and exact durations are unchanged. The additional rectangle matrix
below runs each row at both speeds, records saved deletion/close, checks dormant
overlay state, and restores exact text through Undo. Every seed also has the
earlier common 440x440 held comparison, so size/colour differences do not stand
in for family differences.

| Seed | Family | Reflection | Throw | Duration ms | Fresh paper DIPs | Visual assessment |
| ---: | --- | --- | --- | ---: | --- | --- |
| 0 | Corner | None | Right | 752 | 440x440 | Corner-led crush; compact irregular reference |
| 1 | Side | None | Right | 750 | 440x440 | Edge-led roll and unequal closing flaps |
| 2 | Centre | None | Right | 724 | 440x440 | Interior cup; reverse flap closes to hooked wad |
| 3 | Corner | X | Left | 764 | 440x180 | Opposite corner gathers wide paper into volume |
| 4 | Side | X | Right | 759 | 220x440 | Narrow upright intermediate closes compactly |
| 5 | Centre | X | Left | 785 | 220x180 | Small cup remains legible; no needle-like finish |
| 6 | Corner | Y | Left | 752 | 220x440 | Upper-corner origin; tall axis compacts coherently |
| 7 | Side | Y | Left | 790 | 220x180 | Reversed gathering order; compact asymmetric bundle |
| 8 | Centre | Y | Right | 774 | 440x180 | Broad middle pose closes; no final flat sausage |
| 9 | Corner | XY | Left | 765 | 220x180 | Opposite upper-corner crush; small wad retains irregularity |
| 10 | Side | XY | Right | 761 | 440x180 | Edge origin distinct from 3 and 8; short hold remains visible |
| 11 | Centre | XY | Left | 789 | 220x440 | Deep cup with upright hooked finish; not a spike |
| 42 | Corner | Y | Left | 732 | 300x320 dense | Dense ink survives until occluded; brisk collapse coherent |
| 99 | Corner | X | Right | 728 | 300x320 empty | Placeholder stays readable; longest reach remains restrained |
| 314 | Centre | None | Left | 791 | 300x320 dense | Reverse flap and hooked finish consistent with seed 2 |
| 1337 | Centre | X | Right | 744 | 440x440 | Reflected cup; same matte material as reference |
| 9787 | Side | Y | Right | 722 | 300x320 empty | Near-minimum duration remains readable |
| 4399 | Side | Y | Left | 798 | 300x320 dense | Near-maximum duration does not drag; ink remains attached |

Supplementary **360x240 and 260x360** notes cover moderate wide/tall shapes with
seeds **0, 4 and 11** (all three families, None/X/XY). They use the ordinary resize
grip, with UIA bounds and restored dimensions checked. The full supported extrema
remain **220x180, 440x180, 220x440 and 440x440**; **300x320** covers near-square,
empty and dense notes. The previous six-colour held matrix was also inspected.

No seed/family/orientation needs remapping, disabling, new timing, different throw
bounds, aspect compensation or material tuning. All three deformation origins
are distinct independently of throw, speed and reflection. Text remains attached
and unmirrored; world-space lighting changes appropriately with reflected folds.
No reviewed final wad becomes a puck, flower, sphere, needle, sausage or crushed
featureless blob. Enlarged images reveal angular panels and occasional straight
flap edges, especially Centre's reverse flap and hooked silhouette. Those are
consistent stylisation limits rather than a materially worse seed. The finite
three-trajectory library is recognisable during concentrated repeated review;
ordinary consecutive seeds change family and do not repeat one identical motion.

### Targeted correction and QA tooling

One actual visual defect was found **after** the deformation had finished:
production recordings for **3, 6 and 4399** caught a white rectangle for one
recorded frame during native window close. It occupied the old HWND bounds,
including transparent padding. Terminal teardown disconnected transparency and
content while DWM could still present that window. `NoteWindow` now hides its
AppWindow at the start of the terminal Closed handler, before releasing those
resources. It adds no timer, delay, frame callback or rendering resource.
This is an exit correction, not a family-specific geometry defect.

Fresh production reruns across all 18 seeds found **zero white-flash candidates**.
The controlled fixture check scans every changed production image for the large
white rectangle, in addition to visual review. The original three failing frames
remain in `artifacts/scrunchfx-variation-ui/white-flash-findings.json` with exact paths
and timestamps. Post-fix findings are in the final UI directory. This finite
recording sample does not establish that every possible desktop frame was seen.

The lab already had sufficient controls; no lab UI was added. Two bounded native
capture scripts were added: `verify-fx-variation-ui.ps1` and
`verify-fx-moderate-ui.ps1`. A moderate-shape capture initially read UIA bounds
before layout had settled; the script now waits for the requested bounds and
supports explicit resume. The partial report is retained as
`resize-read-before-layout.json`. This was a verification race; the actual saved
paper dimensions were correct.

`Scrunch.FxChecks` now fingerprints each golden seed plus uint max, including exact
timing/throw fields, 801 elapsed-time poses, original UVs and every reflected
prepared position/normal. It replays in reverse order after unrelated seeds and
fresh asset loads. Two separate process runs produced identical manifests.
These establish reproducible mapping and prepared paths, not pixel-identical
wall-clock scheduling or cross-GPU floating-point output. Existing 10,000-seed,
UV/reflection, compactness, continuity and malformed-asset checks remain in place.

`review-variation.py` adds contact sheets, labelled temporal sequences, enlarged
silhouette details, white-flash detection and a local video index. WinApp's raw
MP4 uses constant requested-rate timestamps even when capture runs more slowly;
playing it directly can accelerate the effect. The `*-walltime.mp4` evidence
rebuilds actual elapsed timestamps using VFR, without interpolating frames.
B-frames are disabled so sparse-frame MP4 duration metadata stays correct too.
Raw recordings, frame indexes and timestamps are retained unchanged.

### Evidence and verification

The compact pack is `artifacts/scrunchfx-variation-final-ui/index.html`:
`production-outcomes.png`, `production-details.png`, `held-intermediates.png`,
18 production videos and 18 slow videos with corrected timestamps. Slow videos
come from the first sweep, before the terminal hide, with unchanged deformation,
material and timing. Every production video and held image in the final pack is
from the post-fix build. The index explicitly identifies this distinction.
Moderate aspect evidence is `artifacts/scrunchfx-variation-moderate-ui/index.html`.
The material-pass square/colour references remain in their original directory.

Both evidence indexes include one production and one slow video per case; the
moderate pack adds six cases (12 videos). `verify-video-timing.py` verified all
48 videos against every captured image-change timestamp and full duration.

| Final validation | Result |
| --- | --- |
| Debug and Release builds | Passed, zero warnings/errors; Release built with trimming disabled for these native checks |
| Asset/motion checks | 33 passed, including the new exact replay fingerprints; two process manifests byte-identical |
| Prepared asset regeneration | All three byte-exact with `author.mjs --check` |
| Geometry checks | 466,560 projected and 1,570,752 crumple patches passed |
| Storage checks | 12 passed |
| Native FX lifecycle | 62 passed; 80 completed effects, 30 same-note replays and 40 saved delete/Undo cycles |
| Release product / original paper | 16 / 25 passed |
| Physical keyboard and Undo UI regression | 7 passed |
| Captured final production cases | 18 golden + six moderate cases passed; zero terminal white-flash candidates |

The final unrecorded native FX run used this development machine's **RTX 3060 Ti**,
two displays at **100% scaling**. It recorded 46–52 rendered frames per completed
effect. Statistics below are the median of per-effect medians and largest
per-effect p95, not pooled percentiles or a controlled before/after benchmark.

| Measurement | Final result |
| --- | --- |
| Frame interval | Median 15.597ms; largest per-effect p95 16.270ms |
| CPU draw/submit/present | Median 0.236ms; largest per-effect p95 0.916ms; GPU execution time not measured |
| Cold / 13-note idle process CPU, 1.2s | 0 / 15.625ms; zero FX callbacks/frames |
| Same-note private memory after GC, cycles 10/20/30 | 183.7 / 182.6 / 183.7 MiB |
| Same-note handles | 1429 / 1417 / 1419 |
| Delete/Undo private memory after diagnostic GC, cycles 10/20/30/40 | 267.2 / 254.4 / 249.0 / 257.0 MiB |
| Delete/Undo handles after GC | 2805 / 2722 / 2747 / 2767 |
| Closed windows / visual trees retained after GC | 1 / 1 at every checkpoint; no growth across cycles |
| Prepared asset footprint | Unchanged: 3,716,520 bytes total; 625 vertices, 1,152 triangles and 61 frames per family |

The earlier material run measured 15.259ms median frame interval and 0.241ms CPU
draw/submit/present. The current result supports acceptable pacing and stable
repetition on this machine; the small difference does not establish a speedup or
regression. There is no growing retention trend in this bounded sample. Idle FX
remains dormant; total application CPU is a separate measurement.

The first original-paper Release check reported an anomalous 37.31% of one core
during its three-second idle sample while evidence video encoding was also
running. Its complete report is retained as
`scrunchfx-variation-release/verification-first-with-video-encoding.json`. A fresh
repeat with no recording or encoding passed all 25 checks and measured 0.00%.
The cause of the first process-CPU sample is not established; neither sample is
a broad claim about application idle cost.

Native lifecycle results and the bounded event slice are in
`artifacts/scrunchfx-variation-final/{fx-verification.json,fx-check-events.jsonl,performance-summary.json}`.
Build/check logs and exact replay manifests are in
`artifacts/scrunchfx-variation-checked`; Release reports are in
`artifacts/scrunchfx-variation-release`, and the seven UI checks in
`artifacts/scrunchfx-variation-native-ui/ui-verification.json`.
`proto/run.ps1` now launches the final checked candidate; use
`./proto/run.ps1 -FxLab` to inspect it or `-Build -VerifyFx` to rebuild and verify.

**Final judgement: yes, ScrunchFX delete crumple is visually finished enough to stop
touching it.** All primary families are distinct, all four reflections remain
believable with unmirrored text, and the deliberate aspect matrix has no broken
outlier. Keep the current mappings and accept the visible stylised panel/hook
character under enlarged inspection. Move on to the **app shell/UI milestone**;
no shell/UI work was started in this sweep.

## Previous geometry milestone evidence (41ae31b)

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

**Visual decision:** retain this as Scrunch's stylised paper discard. The prominent
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

- `artifacts/scrunchfx-signature-checked/fx-verification.json`, `scrunchfx.jsonl`,
  `performance-summary.json`, `asset-checks.txt`.
- `artifacts/scrunchfx-signature-release/product-verification.json`, `verification.json`.
- `artifacts/scrunchfx-signature-final-ui/seed-ui-verification.json`, `golden-1.png`
  through `golden-6.png`, `aspect-review-0.png` through `aspect-review-5.png`,
  `seed-<seed>-production.mp4`, their `.frames` directories and `motion-review-<seed>.png`.
- `artifacts/scrunchfx-signature-legacy-ui/polish-ui-verification.json`, `seed-controls.json`,
  `normal.mp4`, `slow.mp4`, `corner-review.png`, `slow-review.png`.
- `artifacts/scrunchfx-signature-basic-ui/ui-verification.json` and its native screenshots.

`tools/scrunchfx-assets/review-captures.py` creates labelled evidence sheets from raw
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
