# ScrunchFX

ScrunchFX is the internal name of the native discard effect: the animation that
plays when a note is thrown away. An ordinary note is a WinUI editor; only its
captured pixels enter the effect. Geometry comes from prepared offline bakes, so
there is no runtime solver, collision pass or borrowed geometry.

Source lives in `src/Scrunch/ScrunchFX` (renderer, overlay, shader),
`src/Scrunch.Core/ScrunchFX` (prepared-asset reader, timeline, seed contract) and
`src/Scrunch/Assets/ScrunchFX` (prepared bakes, compiled shader objects,
provenance, notices).

## Architecture and lifecycle

`ScrunchFxService.Shared` owns at most one lazy hardware D3D11 device, reusable
buffers and render targets, and one hidden, non-activating, click-through overlay
HWND. Presentation is DXGI composition swap chain -> DirectComposition -> HWND.
Ordinary editing allocates no D3D device and there is no idle FX timer: the
effect-scoped dispatcher timer is created per playback and detaches on success,
cancellation, timeout and failure. The per-note captured texture is released
after each playback; the shared resources stay reusable. A second request while
one effect is playing takes the immediate fallback.

The product persists a recoverable discard *before* any capture or animation, so
a graphics failure still closes an already-saved note. Undo cancels capture and
playback and restores editing. Keyboard discard, reduced motion and ordinary
window-close discard stay immediate. Capture has a 1.5 second timeout; playback
has an independent watchdog.

### Failure behaviour: fallback and recreate on next use

Any exception raised while initializing, preparing or rendering an effect is
caught, the note closes without the animation ("Fallback: <reason>"), and the
renderer is disposed so the **next** discard creates a fresh device. This is
fallback and recreate on next use, not device-lost recovery: the effect in
progress is abandoned rather than replayed, nothing is re-rendered after the
failure, and no `DXGI_ERROR_DEVICE_REMOVED`-specific handling, resource
re-upload or continuation exists. A removed adapter, a driver reset or a
corrupted asset all take the same path.

## Families and offline authoring

`tools/scrunchfx-assets/author.mjs` generates Corner Crush, Side Scrunch and
Centre Collapse from an initially flat sheet with position-based dynamics
(Müller et al. 2007). Every lattice edge is a length constraint, and every pair
of triangles sharing an edge is a signed dihedral-angle constraint, using the
analytic angle gradient of Bridson et al. 2003. Bending past a yield angle moves
the hinge's rest angle (plastic bending, as in Tallinen et al. 2009 and Narain
et al. 2013), so creases stay instead of springing back. Small, smooth rest-angle
flaws let buckles grow gradually rather than snapping.

The hand is a confining ellipsoid per vertex, after the shrinking-sphere
crumpling experiments of Tallinen et al. 2009. Each vertex's ellipsoid starts at
its rest radius and closes to the wad once a gather wave from the family's start
point reaches it. Low-frequency bumps keep the outline from reading as a sphere,
and contact with the hand damps sliding. After the squeeze the hand eases open
by 15% so the elastic part of each fold springs back into facets, then damping
rises so the wad settles by the last frame.

Paper has thickness (Bridson, Fedkiw and Anderson 2002). Vertex-triangle and
edge-edge pairs that are close at the start of a substep keep the side they
started on, at least the thickness apart, using a spatial hash. This replaced
the earlier rigid hinge folds, which finished as a spiky wad with flat flaps and
about 6,600 intersecting triangle pairs; the final wads now have 1-17.

Families differ in where the gather starts and how fast it spreads. Corner
Crush gathers from one corner, Side Scrunch from one side, and Centre Collapse
from the centre, which a thumb presses back and holds. Each family seeds its own
flaws and hand bumps. Families are selected discretely and never blended.

This is an artistic approximation: constraints are solved iteratively, so edges
stretch a little and a few intersections remain. Maximum edge stretch is
asserted below 2x rest length by `FxTests` rather than claimed to be exact.

## Prepared asset format (NFX2)

Little-endian, fixed layout, no source-format decoders at runtime. Sample values
are IEEE binary16; `PaperBake` expands them into the `Vector4` position/normal
pairs the vertex shader indexes, so the GPU buffers and shader are unchanged.

| Section | Count | Element | Bytes |
| --- | --- | --- | ---: |
| Magic `NFX2` (`0x3258464e`) | 1 | u32 | 4 |
| Vertex count | 1 | u32 | 4 |
| Frame count | 1 | u32 | 4 |
| Index count | 1 | u32 | 4 |
| UVs | vertices | float32 x2 | 5,000 |
| Indices | indices | u32 | 13,824 |
| Samples | frames x vertices | half x6: position xyz, normal xyz | 457,500 |
| Total | | | 476,340 |

Positions use a unit rest sheet: x right, y down, z toward the viewer. Normals
are area-weighted and unit length. Frame zero matches the captured note exactly.
All three families share 625 vertices (a 25x25 lattice), 1,152 triangles and 61
frames, so UVs and indices are uploaded once and only the selected family's
sample buffer is bound.

Half precision costs at most 2.5e-4 against the float32 values it replaces
(measured maximum: 2.415e-4 for positions, 2.441e-4 for normal components, which
leaves normal length within 4.0e-4 of 1). That is far below the 8-bit output
quantization of the composited frame, and it removed the two zero padding lanes
per sample that the previous float32 layout carried: 1,238,840 bytes per family
became 476,340.

`PaperBake` rejects an unknown magic, out-of-range dimensions, any length other
than the exact expected one, out-of-range indices and non-finite values, all
before a GPU allocation happens.

## Reflection and the seed contract

The 25x25 UV lattice is spatially irregular but symmetric under X/Y reflection,
and alternating diagonals preserve reflected triangle connectivity. A reflected
effect reads positions from the opposite lattice location, reflects the geometry
vector and normal, and samples the **original** vertex UV: `M F(M uv)` moves
where the fold begins while keeping note text readable and unmirrored. No extra
orientation bakes exist. Renderer initialization verifies lattice symmetry and
shared family UVs, indices and frame counts before allocating resources.

`FxVariation.FromSeed(uint)` uses explicit unchecked 32-bit mixing: no `Random`,
no runtime `GetHashCode`, no timing input. Family is `seed % 3`, orientation is
`(seed / 3) % 4`, so twelve consecutive seeds cover the complete cross product.
Production picks a fresh seed from a UI-thread-owned counter initialized from
system uptime; every visual property is then a pure function of that recorded
seed, independent of previously played effects.

| Property | Range |
| --- | --- |
| Playback duration | 722-798ms (760ms +/-5%), excluding capture and device setup |
| Gather completion | 70% of normalized playback |
| Compact hold | 3.5-6.5% of playback, about 25-52ms |
| Throw | starts only after gathering and the hold |
| Horizontal travel | left or right, 24-30% of geometric-mean paper size |
| In-plane release rotation | signed 0.40-0.58 rad (23-33 degrees) |
| Depth rotation | signed 0.24 rad (about 14 degrees) |
| Vertical departure | -0.44 to -0.26, followed by a small downward arc |
| Fade | begins at 45% of release, applied once to the assembled opaque scene |

Gather integrates a short acceleration (12% of gather time), a steady middle and
a short deceleration (18%), so position and speed stay continuous while less time
is spent on the almost-flat bake frames. Hold length moves the start of release,
not the total duration. Slow playback multiplies the identical normalized
timeline to about 18 seconds.

## Compiled shaders

`Paper.hlsl` is source only: it is not shipped and is never compiled at runtime.
`tools/compile-shaders.ps1` compiles each entry point with the newest installed
Windows SDK `fxc` (override with `-Fxc`) using
`/T vs_5_0|ps_5_0 /O3 /Qstrip_debug /Qstrip_reflect /Qstrip_priv /nologo`, and
writes `src/Scrunch/Assets/ScrunchFX/Shaders/<Entry>.cso`, which is tracked in
Git and shipped by the existing `Assets\ScrunchFX\**` content glob. The stripped
objects are byte-reproducible for a given compiler, so `-Check` fails when a
tracked object differs from a fresh compile.

| Entry | Profile | Role |
| --- | --- | --- |
| `VS` / `PS` | vs_5_0 / ps_5_0 | folded paper, perspective-correct ink, matte lighting |
| `VSLight` | vs_5_0 | directional fold-shadow depth pass (1024 square) |
| `VSShadow` / `PSShadow` | vs_5_0 / ps_5_0 | soft desktop shadow quad |
| `VSComposite` / `PSComposite` | vs_5_0 / ps_5_0 | resolve and single fade of the assembled scene |

`D3DRenderer` loads the objects with `File.ReadAllBytes` from
`AppContext.BaseDirectory/Assets/ScrunchFX/Shaders`. No shader compiler binding
is referenced or deployed.

## Shared constants

Values that are the same quantity in two places have one definition:

- `PaperTokens.RestLight` / `PaperTokens.DiscardLight` hold both key light
  directions side by side. They are deliberately different vectors, for the
  reason recorded at the definition, and `Paper.hlsl` receives the discard one
  through the settings buffer instead of a literal.
- `DiscardMotion.GatherOnset` / `GatherConvergence` describe how the captured
  rectangle converges toward a square while gathering. The vertex path and the
  desktop shadow quad both use it; the shadow previously repeated the fully
  gathered value as its own literal.
- `DiscardMotion.DepthRotation`, `ThrowArc` and `ThrowDepth` are the parts of the
  release motion that are not seeded per effect (`FxVariation` carries travel,
  in-plane rotation and vertical departure).
- The fold-shadow map resolution has one definition in `D3DRenderer`; the shader
  receives its texel size rather than repeating 1024.

The Composition at-rest renderer and the D3D discard intentionally keep separate
shadow mechanisms, projection focal lengths and fallback throw distances; those
divergences are documented where they are defined, not unified.

The settings constant buffer is 128 bytes: viewport, playback, paper colour,
origin (centre, deformation, shadow texel), variation, orientation, key light and
motion.

## Aspect handling and material

The rectangular capture hands off unchanged, then from deformation 0.15 to 1 its
X/Y scales converge 85% toward the geometric mean of width and height, so wide or
tall notes finish as compact paper instead of a flat oval. It is bounded
geometric preconditioning, not a per-aspect solve. Supported shapes checked in
the native pass are 220x180, 440x180, 220x440, 440x440 and 300x320 DIPs, with
empty and dense text and all six paper colours.

The shader keeps perspective-correct captured ink, an unprinted coloured back,
4x MSAA with a single-sample fallback, a 1024-square filtered fold-shadow map and
a soft desktop shadow. Baked normals blend toward the geometric facet normal by
22-44% depending on crease disagreement and deformation progress. Ambient fill is
0.66 and the directional term 0.34, with a 0.18 diffuse wrap at grazing angles;
fold visibility attenuates the directional term by at most 28% so opposing folds
inside the wad do not go black. There is no specular lobe.

## Regenerating assets and shaders

```powershell
node tools/scrunchfx-assets/author.mjs           # rewrite the three .nfx + provenance
node tools/scrunchfx-assets/author.mjs --check   # byte-for-byte reproducibility
./tools/compile-shaders.ps1                      # rewrite Assets/ScrunchFX/Shaders/*.cso
./tools/compile-shaders.ps1 -Check               # objects match Paper.hlsl
```

Both `--check` forms run in `tools/check.ps1`. The authoring script has no
package dependencies and no network input; Node 24.11.0 and Windows SDK
10.0.26100.0 `fxc` produced the tracked outputs. Regenerated assets or shaders
always need native visual review afterwards, because bounds and byte checks do
not establish material quality.

## Verification

```powershell
dotnet test tests/Scrunch.Tests --filter FullyQualifiedName~FxTests
./tools/run.ps1 -Build -VerifyFx    # writes artifacts/debug/fx-verification.json
./tools/run.ps1 -Build -FxLab       # DEBUG-only seed/timing lab
```

`FxTests` covers the prepared lattice, cross-family topology, reflection
appearance, normal length, bounded inter-frame motion and edge stretch, invalid
asset rejection, the discard timeline, seed bounds over 10,000 seeds and
byte-exact replay of the golden seeds after unrelated seeds and fresh asset
loads.

`tests/fixtures/fx-golden-seeds.json` is the shared fixture: seeds 0-11, 42, 99,
314, 1337, 9787 and 4399. It is copied next to the test binary as
`fixtures/fx-golden-seeds.json` and read by the native UI capture scripts in
`tools/native`, so both use the same list. Seeds 0-11 cover every
family/orientation pair, 9787 and 4399 sit near the duration limits, and the
fixture also records the five supported note sizes and the held deformation
stages used for capture.

The DEBUG-only lab exposes explicit seed entry, previous/next and next-QA seed,
family/orientation and timing display, replay, production and slow playback, held
progress, sample notes and screen-corner placement. None of it exists in Release.
