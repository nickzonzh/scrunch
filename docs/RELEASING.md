# Releasing Scrunch

## Build and distribution

Scrunch remains an unpackaged WinUI 3 application. Both .NET and Windows App SDK
are self-contained; no shared runtime, MSIX registration or signing certificate is
required. Release uses ReadyToRun, with trimming and single-file bundling disabled.
The prepared graphics bakes, runtime shader, icon, fonts, XAML and full dependency
notices are verified before packaging. Development benches and verification
entrypoints are compiled out of Release.

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

`tools/check.ps1 -Locked` runs storage/migration, geometry, FX and tray geometry/identity
checks and reproduces prepared bakes. These work in CI. Native product, renderer,
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
   GitHub Release. Download those exact artifacts, verify their hashes, and repeat
   native install/portable acceptance if the workflow rebuilt them.
7. Make the repository public only when ready (it was private during preparation).
   Review and publish the draft after acceptance. No automatic public publication.

Manual alternative after pushing the tag:

```powershell
gh release create v0.1.0 ./artifacts/release/0.1.0/Scrunch-0.1.0-Setup.exe ./artifacts/release/0.1.0/Scrunch-0.1.0-win-x64.zip ./artifacts/release/0.1.0/SHA256SUMS.txt --verify-tag --draft --title "Scrunch 0.1.0" --notes-file docs/releases/0.1.0.md
```

Only x64 is released. ARM64 remains an unverified source target; no ARM64 or x86
artifact is advertised. See Microsoft's [deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
and Inno Setup's [non-admin mode](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
and [application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm).
