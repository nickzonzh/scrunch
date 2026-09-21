# Third-party notices

`crumple.nfx` is a prepared derivative of the mesh and baked Houdini Vellum motion
from [item-develop/paper-crumple-demo](https://github.com/item-develop/paper-crumple-demo),
commit `f84648b001dd13becda7a629ab5398cecfe3a252`.
Copyright (c) 2026 nagasawa (ITEM Inc.), MIT license, reproduced in `LICENSE.txt`.
The source hashes and prepared-data checksum are in `provenance.json`.
No demo paper designs, browser application code or physics engine are shipped.

Direct3D11, DXGI, DirectComposition and shader-compiler bindings use
[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) 3.8.3, MIT,
copyright Amer Koleci and Contributors. See `Vortice-LICENSE.txt`.
Vortice.Direct2D1 is a transitive binding dependency of DirectComposition;
NootFX does not instantiate a Direct2D renderer.
