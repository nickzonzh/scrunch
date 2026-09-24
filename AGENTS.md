# Scrunch

A native Windows sticky-notes app with tactile paper notes and a physical "crumple" animation when you discard one. It's local-only: no account, sync, telemetry or analytics. Version 0.1.0 ships from GitHub Releases as an installer or a portable ZIP, currently unsigned while Azure Trusted Signing is set up.

## Stack

C# on .NET 9 (SDK pinned in `global.json`), WinUI 3 with Windows App SDK 1.8, and custom Direct3D rendering ("ScrunchFX") through Vortice with HLSL shaders compiled to `.cso`. Tests use xUnit. Node 22 is used only for the asset tooling in `tools/scrunchfx-assets`. x64 only, by decision.

## Commands (PowerShell)

```powershell
./tools/check.ps1 -Locked      # the check: restore, tests, asset and shader drift checks
./tools/run.ps1 -Build         # debug build and run
dotnet test tests/Scrunch.Tests
./tools/release.ps1            # release build, only when Nick asks
```

Run `./tools/check.ps1 -Locked` before saying you're done.

## Structure

- `src/Scrunch.Core` holds storage, geometry and the ScrunchFX bake logic. It has no WinUI dependency, so it's unit-testable. Keep it that way.
- `src/Scrunch` is the WinUI app.
- Debug-only verification surfaces (`ProductWindow.*Checks.cs`) are excluded from Release with `<Compile Remove>` in the csproj. That's the only mechanism. Don't switch to `#if DEBUG`.

## Rules

- Compiled shaders (`.cso`) and prepared ScrunchFX assets are generated. Change `Paper.hlsl` or the generator, rebuild, and let `check.ps1` confirm there's no drift. Never hand-edit the outputs.
- Tests belong in `Scrunch.Core`, where logic runs headless. The UI acceptance scripts in `tools/native/verify-*-ui.ps1` are the end-to-end layer. They need an unlocked interactive Windows session and aren't run in CI, so tell Nick when a change needs them.
- The app was once called Noot. The `Noot` references in `src/Scrunch/AppPaths.cs`, `docs/RELEASING.md` and `tools/verify-installation.ps1` are the legacy data migration and the guard that protects old notes. Never rename or remove them.
- `%LOCALAPPDATA%\Scrunch\notes.json` (and `.bak`), plus the legacy `%LOCALAPPDATA%\Noot` folder, are Nick's real data. Never use it as a test fixture, and never delete or rewrite it.
- Changing the notes file format breaks existing notes. Ask Nick first, and include a migration.
- Signing material (`*.pfx`, `*.p12`, `*.key`) and the `AZURE_*` / `TRUSTED_SIGNING_*` secrets never go in the repo or the conversation.
- Tagging a release or publishing to GitHub Releases is outward-facing. Only on Nick's go, after native acceptance has passed.
- Package size and ReadyToRun or trimming choices are measured and documented (`docs/PACKAGE-SIZE.md`). Re-measure before changing them.
