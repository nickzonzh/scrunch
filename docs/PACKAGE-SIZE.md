# Package size

Measured on 22 September 2026 from `tools/release.ps1` on Windows 10 x64 build
19045. Sizes below use decimal MB; the payload guard uses binary MiB.

| Artifact | Original 0.1.0 | Component SDK only | Trimmed (current) |
| --- | ---: | ---: | ---: |
| Installer | 73,141,554 bytes | 49,493,811 bytes | 25,383,043 bytes |
| Portable ZIP | 108,522,863 bytes | 73,254,496 bytes | 36,617,008 bytes |
| Extracted payload | 281,295,253 bytes | 180,762,305 bytes | 85,965,275 bytes |
| Payload files | 535 | 479 | 320 |

Overall: installer 65% smaller, extracted payload 69% smaller than the original
0.1.0 packaging, with every feature, font, paper bake and the native crumple intact,
and a faster start (below). Trimming alone reached 77,850,587 bytes; ReadyToRun adds
8.1 MB of native code back for what survived.

## What the payload is

| Bucket | Bytes | Notes |
| --- | ---: | --- |
| Windows App SDK native (`Microsoft.ui.xaml.dll`, controls, `WinUIEdit`, DWriteCore, DWM/DComp shims) | 49.4 MB | The floor. Native code; cannot be trimmed. |
| .NET runtime natives (`coreclr`, `clrjit`, GC, host) | 9.4 MB | Required to run IL. |
| Windows App SDK managed projections | 5.1 MB | `Microsoft.WindowsAppRuntime.dll` is not trimmable. |
| .NET base library, trimmed + ReadyToRun | 10.1 MB | Was 61.6 MB; 4.8 MB as IL only. |
| WinUI localized control resources (`*.mui`, 170 locales) | 3.6 MB | Accessibility strings; kept. |
| Fonts, paper bakes, shaders, icons | 2.4 MB | Bakes are NFX2 half precision (476,340 bytes each). |
| Scrunch, compiled XAML, resources, notices | 2.6 MB | `Scrunch.dll` 643,072 bytes with ReadyToRun (345,088 IL only). |
| CsWinRT projection, trimmed + ReadyToRun | 1.7 MB | Was 26.9 MB; 0.6 MB as IL only. |
| Vortice/SharpGen, trimmed + ReadyToRun | 0.4 MB | Was 1.8 MB. |

## Shipping choices

Scrunch bundles .NET and the Windows App SDK. Installation and portable launch do
not require the user to install a shared runtime. The project references the WinUI
and DWrite component packages from Windows App SDK 1.8.6 rather than the umbrella
package, so the AI, ML, widgets and tensor dependencies are never restored.

**Partial trimming** (`PublishTrimmed` with `TrimMode=partial`, Release only) removes
unused IL from assemblies that opt in: the .NET base library, the CsWinRT and WinUI
projections, Vortice and `Scrunch.Core`. Two things make this safe:

- Every class that crosses the WinRT ABI is `partial`, so the CsWinRT AOT optimizer
  (`CsWinRTAotOptimizerEnabled`) generates its vtables at build time instead of
  relying on runtime reflection. `CsWinRTAotWarningLevel=2` fails loudly when a new
  class forgets this.
- The notebook JSON uses a source-generated `JsonSerializerContext`
  (`NoteJsonContext` in `src/Scrunch.Core/NoteStore.cs`). Trimming turns off
  reflection-based `System.Text.Json`; the earlier trimming trial failed for exactly
  this reason (and for the non-partial classes), not because of Windows SDK or
  SharpGen interop. Debug builds keep reflection for their diagnostics writers.

**Debugger-only runtime binaries** (`mscordaccore*.dll`, `mscordbi.dll`,
`Microsoft.DiaSymReader.Native.amd64.dll`, `createdump.exe`, 6.2 MB) are removed by
the `RemoveScrunchDiagnosticsNatives` publish target. Crashes are recorded by
`App.UnhandledException` into `crash.log` next to `notes.json`.

**Shaders** are compiled offline by `tools/compile-shaders.ps1` into committed
`.cso` objects; `Vortice.D3DCompiler` and the runtime `d3dcompiler_47` dependency are
gone. **Paper bakes** store IEEE half-precision samples (NFX2; maximum position error
2.4e-4 of the paper width, far below one device pixel).

**ReadyToRun** (`PublishReadyToRun`, Release only) runs after trimming. The runtime
pack ships its assemblies precompiled; the trimmer rewrites them as IL only, which
moved that compilation to the JIT on every launch and made the trimmed build start
*slower* than the untrimmed one. Crossgen2 restores native code for the surviving
methods at a cost of 8.1 MB extracted (3.7 MB in the ZIP, 2.8 MB in the installer).

.NET satellite resource assemblies are restricted to English; the WinUI `*.mui`
control strings are kept for every locale.

`tools/verify-payload.ps1` requires the shaders, fonts and bakes, rejects the
AI/ML/widgets binaries, the debugger natives and shader source, and fails a payload
over 90 MiB (94,371,840 bytes). The current payload is 82.0 MiB. Review and measure
intentional increases rather than silently raising the budget.

## Startup

Five alternating isolated launches per build on the same machine (i7-10700KF, RTX
3060 Ti, NVMe, warm file cache), measured with the WinApp CLI: process start until
the shell's New note button answers UI Automation, then New note until the note's
editor answers. Medians; peak working set from the process.

| Build | Start to shell | New note to editor | Peak working set |
| --- | ---: | ---: | ---: |
| Untrimmed (component SDK only) | 434 ms | 309 ms | 152 MB |
| Trimmed, IL only | 563 ms | 349 ms | 134 MB |
| Trimmed + ReadyToRun (shipped) | 374 ms | 256 ms | 129 MB |

The IL-only row is why ReadyToRun is on. Run-to-run spread was under 25 ms for every
cell; a cold-cache first launch after install is slower for every build and was not
measured.

## What is left on the table

- Native AOT would fold `coreclr`, `clrjit` and the remaining IL into one native
  executable and start faster still, but it needs the Visual Studio C++ build tools
  (`link.exe`), which are not installed on the release machine, and SharpGen
  2.4.2-beta still reports trim warnings (IL2104). Not attempted; the ReadyToRun
  result above removed the startup motivation.
- Dropping the 170 WinUI locale directories saves 3.6 MB raw but only about 0.3 MB
  compressed and costs localized control accessibility names. Not worth it.
- Anything below ~50 MB extracted means not shipping the Windows App SDK
  self-contained, which is a different product decision, not a packaging one.

## Verification

The shipped build was exercised with the real artifacts: `tools/check.ps1 -Locked`
(86 xUnit tests, asset and shader checks), `tools/verify-portable.ps1` (exact ZIP:
launch, settings, note save, restart restore, quit), the physical right-click menu
discard with recorded crumple frames and undo, `tools/verify-tray-callbacks.ps1`
(12 Explorer tray checks) and `tools/verify-installation.ps1` (26 installer
lifecycle checks). [RELEASE-VERIFICATION.md](RELEASE-VERIFICATION.md) is the record.
