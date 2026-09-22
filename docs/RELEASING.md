# Releasing Scrunch

## Build and distribution

Scrunch remains an unpackaged WinUI 3 application. Both .NET and Windows App SDK
are self-contained; no shared runtime or MSIX registration is required. Authenticode
signing is optional and described under [Signing](#signing). Release trims partially and disables ReadyToRun and single-file bundling;
Debug is untrimmed and keeps reflection for the diagnostics writers. Release uses
the WinUI/DWrite SDK component packages rather than the full umbrella runtime.
See [package size](PACKAGE-SIZE.md) for measured savings and startup tradeoffs.
The prepared graphics bakes, compiled shaders, icon, fonts, XAML and full dependency
notices are verified before packaging; see [ScrunchFX](SCRUNCHFX.md) for the asset
and shader pipeline. Development benches and verification entrypoints are compiled
out of Release.

Inno Setup was selected for its small declarative definition, per-user installation,
Start Menu integration and standard uninstall/upgrade support. MSIX would add a
package identity and certificate trust process without improving this milestone's
unsigned direct-download experience. The primary install is always
`%LOCALAPPDATA%\Programs\Scrunch\Scrunch.exe`. The ZIP is secondary and shares the
same payload and profile data; it does not implement a separate portable data mode.

`Directory.Build.props` is the version authority. It drives assembly/file metadata,
Settings, installer metadata and artifact names. The Win32 manifest's assembly
identity version is an OS compatibility identity, not the release version. The
unused package manifest is a development template, not a shipped MSIX identity.
Use semantic versions; the initial public release is `0.1.0`.

```powershell
./tools/release.ps1
```

The pinned .NET SDK plus NuGet lockfiles make dependency restore reproducible.
Inno Setup 6.7.3 is pinned by SHA-256 and its Authenticode signature is checked on
download. This is a reproducible build procedure, not a claim of byte-identical
installer/ZIP output: packaging timestamps can differ. `-SkipChecks` is for local
iteration after the same source has passed `tools/check.ps1 -Locked`.

## Data and upgrades

New data lives in `%LOCALAPPDATA%\Scrunch`. The only old product-name literal in
application code identifies the legacy `Noot` data directory for migration.
First launch copies a legacy notebook into a unique staging directory while holding
its exclusive writer lease, validates with the existing reader, then atomically
renames that directory to Scrunch. Primary files, backups, damaged snapshots and
interrupted saves are retained. The source is never deleted. Newer schemas fail
closed; corrupt primaries retain normal backup recovery. Existing destination
notebooks always win. An existing non-notebook destination fails closed and asks
for inspection. An interrupted staging directory can be inspected/recovered; it is
never silently merged. Do not keep using an old build after migration: its retained
notebook is a historical copy and changes are not synchronized.

A fatal fault is appended to `crash.log` in the same data folder, so an isolated
`SCRUNCH_DATA_DIRECTORY` run keeps its crash record with its own notebook. One entry
per fault (UTC timestamp, message, full exception), discarded once the file passes
256 KiB. It is local evidence only: nothing is uploaded and the app never reads it back.

The installer never accesses either data directory. Upgrades overwrite the stable
application location and remove obsolete root binary/resource files. Quit is
required before upgrade/uninstall; the installer checks a process-lifetime mutex
and never force-terminates the app. Saved notes/preferences and opt-in startup
survive an upgrade. The stable executable path keeps the tray GUID stable. AppInstance
routing remains one ordinary session, independent of executable location.

Uninstall removes installed files, the Start Menu shortcut, install-location value
and Scrunch's Run value. It does not delete notes, backups, or unrelated Run values.
There is no auto-update, installer analytics or crash-reporting service.

## Start at sign-in

Settings uses one HKCU `Software\Microsoft\Windows\CurrentVersion\Run` value named
`Scrunch`, containing the quoted installed executable path followed by `--startup`.
It is absent by default. Enable/disable is idempotent; Settings reads the actual
registry state when opened, including Windows Startup Apps disabling the entry.
Scrunch does not override Windows' approval state. A blocked entry can be removed
from Settings or enabled in Windows Startup Apps. Portable and development copies
cannot register movable paths. Uninstall removes the Run value; upgrade preserves it.

## Verification

`tools/check.ps1 -Locked` restores with the lockfiles, runs the xUnit suite in
`tests/Scrunch.Tests` (`dotnet test`: storage/migration, note session, geometry, FX
bakes and tray geometry/identity), reproduces the prepared ScrunchFX bakes with
`tools/scrunchfx-assets/author.mjs --check`, and compares the compiled shaders with
`tools/compile-shaders.ps1 -Check`. These work in CI. Native product, renderer,
FX, tray, actual keyboard/mouse and installation tests require an unlocked Windows
desktop. They must not run in a headless GitHub runner.

Use process-only `SCRUNCH_DATA_DIRECTORY` to run the **exact Release executable**
against a synthetic notebook. It also isolates the AppInstance key. Never set that
variable permanently. Debug verification modes have their own isolated fixtures.
Never use personal notes for screenshots. Keep raw evidence under ignored artifacts.
The release acceptance report records actual results and remaining hardware gaps.

Test the final installer, including a second install at the same path, startup
on/off, quiet launch, quit/relaunch, uninstall and preserved data. Restore the
machine's original startup values after testing. Also extract and run the exact ZIP.
Hash the distributed files after all changes. Any changed payload requires another
artifact smoke test; an intermediate publish run does not count.

With no existing personal installation and all Scrunch processes quit:

```powershell
./tools/verify-installation.ps1
./tools/verify-portable.ps1
```

The first command installs, exercises and uninstalls the final local installer, saving evidence
under `artifacts/installed-verification`. It temporarily changes only Scrunch's
startup values and restores their original state in `finally`. It refuses to
overwrite an existing installation unless explicitly identified as a prior test.
The second extracts and runs the exact ZIP with synthetic data. Both need an
interactive desktop and save evidence under the ignored artifacts directory.

For interactive tray acceptance, install the final artifact and run in an unlocked,
foregroundable Windows desktop session:

```powershell
./tools/native/verify-tray-ui.ps1 -BuildDirectory "$env:LOCALAPPDATA/Programs/Scrunch" -DataDirectory "$PWD/artifacts/manual-tray-data" -EvidenceDirectory "$PWD/artifacts/manual-tray-evidence"
```

The remaining desktop scripts live beside it in `tools/native` (shell matrix and the
four ScrunchFX capture sweeps); they share `tools/native/Verify.psm1`, which resolves
the WinApp CLI, isolates `SCRUNCH_DATA_DIRECTORY`, and writes the JSON evidence files.

For the separate programmatic native-menu check, use:

```powershell
./tools/verify-tray-callbacks.ps1 -Executable "$env:LOCALAPPDATA/Programs/Scrunch/Scrunch.exe" -DataDirectory "$PWD/artifacts/callback-tray-data" -EvidenceDirectory "$PWD/artifacts/callback-tray-evidence"
```

For manual crumple acceptance, launch that installed executable with a process-only
`SCRUNCH_DATA_DIRECTORY` pointing at a synthetic notebook. Create and edit a note,
right-click its top edge, choose Discard note, observe the crumple finish, then Undo
and confirm the original text returns. Quit and uninstall the test copy afterwards.
Never substitute personal data for a blocked desktop automation test.

## Signing

`tools/sign.ps1` signs whatever `SCRUNCH_SIGN_ARGS` describes: the `signtool sign`
arguments for the certificate source (Azure Trusted Signing dlib, a PFX, an HSM).
`tools/release.ps1` signs `Scrunch.exe` before hashing it into the ZIP and passes the
same hook to Inno Setup, which signs `Setup.exe` and the embedded uninstaller
(`SignedUninstaller`). With the variable unset the release is produced unsigned and
the script says so; `-RequireSigning` turns that into a failure.

The intended source is Azure Trusted Signing (Basic tier, monthly fee, no
certificate to store). One-time provisioning is a guided walk-through:

```bash
./tools/setup-signing.sh
```

It creates the account, identity validation, Public Trust certificate profile, an
Entra app registration with a GitHub federated credential bound to the `release`
environment, the role assignment, and writes the `AZURE_*` repository secrets plus
`TRUSTED_SIGNING_*` variables the workflow reads. Identity validation is reviewed by
Microsoft and can take days; the wizard remembers values between runs.

CI signs only `v*` tag builds, through the `release` environment and OIDC (no stored
secret). Locally, after `az login` with an account holding the *Trusted Signing
Certificate Profile Signer* role:

```powershell
. ./tools/trusted-signing.ps1 -Endpoint https://<region>.codesigning.azure.net -Account <account> -Profile <profile>
./tools/release.ps1 -RequireSigning
```

`tools/trusted-signing.ps1` pins the `Microsoft.Trusted.Signing.Client` package by
SHA-256 under the ignored `tools/.cache` and sets `SCRUNCH_SIGN_ARGS` for the session.
Signed artifacts change the SmartScreen story from "unknown publisher" to the
validated name; reputation still accrues over downloads.

## Publishing

1. Review `docs/RELEASE-VERIFICATION.md` and complete every outstanding release gate.
2. Update the single version in `Directory.Build.props`, and add release notes for it.
3. Run `./tools/release.ps1`; inspect payload and complete native acceptance.
4. Commit the complete source, lockfiles, notices and intended small demo media.
5. Push the source, then create and push the version tag:

   ```powershell
   git tag -a v0.1.0 -m "Scrunch 0.1.0"
   git push origin v0.1.0
   ```

6. Actions builds and uploads the EXE, ZIP and SHA256SUMS, then creates a **draft**
   GitHub Release titled `Scrunch <version> (native acceptance pending)`, whose notes
   open with the same warning: CI ran the headless gate only. Download those exact
   artifacts, verify their hashes, and repeat native install/portable acceptance if
   the workflow rebuilt them. Remove the pending suffix when you publish.
7. Make the repository public only when ready (it was private during preparation).
   Review and publish the draft after acceptance. No automatic public publication.

To publish the already tested local binaries, wait until the tag workflow has
created its draft, then replace its rebuilt assets with the exact files whose
hashes appear in the verification report:

```powershell
gh release upload v0.1.0 ./artifacts/release/0.1.0/Scrunch-0.1.0-Setup.exe ./artifacts/release/0.1.0/Scrunch-0.1.0-win-x64.zip ./artifacts/release/0.1.0/SHA256SUMS.txt --repo nickzonzh/scrunch --clobber
```

When deliberately ready for public OSS publication, review the draft assets and
notes, then run these owner-controlled final steps (they are not run by the build):

```powershell
gh repo edit nickzonzh/scrunch --visibility public --accept-visibility-change-consequences
gh release edit v0.1.0 --repo nickzonzh/scrunch --draft=false
```

Only x64 is built and released. The project declares no ARM64 platform: there is
no ARM64 hardware to accept a build on, and Windows on ARM cannot be emulated on an
x64 host well enough to stand in for acceptance. Reintroduce the platform together
with real hardware. See Microsoft's [deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
and Inno Setup's [non-admin mode](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
and [application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm).
