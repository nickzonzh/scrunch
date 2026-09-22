# Offline ScrunchFX paper authoring

Run `node tools/scrunchfx-assets/author.mjs` from the repository root. No downloads,
Houdini installation, npm install, Three.js, FBX or EXR decoder are required.
`node tools/scrunchfx-assets/author.mjs --check` regenerates all outputs in memory and
requires byte-for-byte equality, including provenance. Node 24.11.0 was used.
Git attributes preserve LF for the generator/provenance, and source hashing
normalizes CRLF so Windows checkout conventions do not invalidate the manifest.

Three hand-authored oblique hinge sequences guide an irregular 25x25 panel
lattice. Offline edge-length projection resists stretched triangles; unequal
closing pressure forms a compact wad. Centre Collapse also begins with an
off-centre depression. No collision solver or runtime simulation is included.
This is an artistic baked approximation, with residual strain and possible
self-intersection; it is not advertised as a physically exact solve.

All families share 625 vertices, 1,152 triangles, 61 frames, UVs and indices.
Reflection symmetry permits exact geometry-field mirroring without mirrored ink.
Each file is 1,238,840 bytes; all three together are smaller than the old bake.
The NFX1 binary layout is unchanged. `scrunch-provenance.json` records generator and
output hashes, fold definitions, settings and final bounds.

After any authoring change, run the asset checks, then the actual native lab at
held stages **and production speed**, including mirrored seeds and wide/tall
notes. Bounds and edge checks do not establish material quality. See
[`proto/SCRUNCHFX.md`](../../proto/SCRUNCHFX.md) for the full seed and verification contract.

The borrowed Codrops conversion tooling and its Three.js dependency have been
retired. Older source and attribution remain in Git history; these assets start
from a new flat Scrunch sheet and do not reuse that geometry or motion.
