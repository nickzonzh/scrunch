# Offline NootFX asset preparation

The native application ships only `crumple.nfx`, the licenses and provenance.
Three.js is a **development-only parser** here; no JS, browser renderer, FBX,
EXR, or heavyweight asset decoder enters the application.

From this directory:

```powershell
./fetch.ps1
npm.cmd ci --ignore-scripts
node prepare.mjs
```

`fetch.ps1` pins upstream commit `f84648b001dd13becda7a629ab5398cecfe3a252`
and checks the downloaded mesh and EXR hashes. The converter removes the dummy
triangles, welds points by their VAT lookup ID, fixes source EXR row order and
X displacement signs, trims static frames, maps XZ to a canonical UV-aligned
paper plane, and computes area-weighted shared normals. It does no simulation.

Output: 3,500 vertices, 6,762 indexed triangles, 38 frames, 4,365,160 bytes.
The header is `NFX1`, vertex count, frame count, index count (little-endian u32),
followed by float2 UVs, u32 indices and frame-major float4 position/float4 normal
pairs. Positions are normalized by the rest sheet dimensions; depth uses their
geometric mean. The native vertex shader interpolates two adjacent frames and
scales to the actual note aspect ratio.

Source data and decoding conventions: [Paper Crumple by nagasawa / ITEM Inc.](https://github.com/item-develop/paper-crumple-demo), MIT.
Required notice is shipped beside the prepared asset as `LICENSE.txt`.
The dependency lockfile pins Three.js; npm's installed package includes its MIT
license. All downloaded development files and `node_modules` are ignored.
