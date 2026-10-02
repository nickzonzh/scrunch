# Offline ScrunchFX paper authoring

Run `node tools/scrunchfx-assets/author.mjs` from the repository root. No downloads,
Houdini installation, npm install, Three.js, FBX or EXR decoder are required.
`node tools/scrunchfx-assets/author.mjs --check` regenerates all outputs in memory and
requires byte-for-byte equality, including provenance. Node 24.11.0 was used.
Git attributes preserve LF for the generator/provenance, and source hashing
normalizes CRLF so Windows checkout conventions do not invalidate the manifest.

An irregular 25x25 panel lattice is simulated as paper with position-based
dynamics: inextensible edges, plastic dihedral hinges and paper thickness,
gathered by a closing hand into a rounded, creased wad. Corner Crush starts from
a corner, Side Scrunch from one side and Centre Collapse from a pressed centre.
All of it runs offline: there is no runtime simulation.
This is an artistic baked approximation, with residual strain and possible
self-intersection; it is not advertised as a physically exact solve.

All families share 625 vertices, 1,152 triangles, 61 frames, UVs and indices.
Reflection symmetry permits exact geometry-field mirroring without mirrored ink.
Each file is 476,340 bytes. The NFX2 binary layout stores samples as IEEE
binary16 (position xyz, normal xyz): 61% smaller than the NFX1 float32 layout
and within 2.5e-4 of it. `scrunch-provenance.json` records generator and
output hashes, family definitions, settings and final bounds.

After any authoring change, run the asset checks, then the actual native lab at
held stages **and production speed**, including mirrored seeds and wide/tall
notes. Bounds and edge checks do not establish material quality. See
[`docs/SCRUNCHFX.md`](../../docs/SCRUNCHFX.md) for the full seed and verification contract.

The borrowed Codrops conversion tooling and its Three.js dependency have been
retired. Older source and attribution remain in Git history; these assets start
from a new flat Scrunch sheet and do not reuse that geometry or motion.
