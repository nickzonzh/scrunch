# Scrunch 0.1.0 release verification

Tested on 22 September 2026, Windows 10 x64 build 19045, NVIDIA RTX 3060 Ti.
This report separates the final distributed artifacts from Debug regression and
media fixtures. The repository remains private; no release has been published.

## Acceptance status

**Release engineering and local x64 acceptance are complete.** The exact local
installer and ZIP identified below pass installation, persistence, startup, tray,
crumple/Undo and cleanup checks. No known release blocker remains for these
artifacts on the tested Windows 10 system. Hardware coverage limits are listed
below; public publication remains a deliberate owner action.

Earlier attempts were blocked by Windows refusing foreground focus. Once that
desktop condition cleared, the final installer passed all 21 physical tray checks.
A physical right-click and physical Discard note menu selection triggered the
real animation; bounded recorded frames show deformation, crumpled paper and the
empty backdrop. The saved note was marked discarded, and Undo restored the same
note and original editor text. The tested installed assembly hash matches the
final packaged payload. Temporary installations and the media backdrop were
removed afterwards.

## Final local artifacts

Built with `tools/release.ps1`, pinned .NET SDK 9.0.317, locked NuGet dependencies,
Release x64, self-contained .NET and Windows App SDK, ReadyToRun on, trimming off.

| Artifact | Bytes | MiB |
| --- | ---: | ---: |
| `Scrunch-0.1.0-Setup.exe` | 73,122,532 | 69.74 |
| `Scrunch-0.1.0-win-x64.zip` | 108,509,670 | 103.48 |
| Published payload, 535 files | 281,256,758 | 268.23 |
| Installed files, including uninstaller | 285,766,245 | 272.53 |

SHA-256:

```text
e41c5f5aecf1d392b74f6c5ad2947e6cb351b809fdf2e11e1b8806bf4e1284a2  Scrunch-0.1.0-Setup.exe
941c366c44e505f96a9cb4c183798967683d10a9a6f13efe765fe9bb796e55f1  Scrunch-0.1.0-win-x64.zip
```

Executable properties: ProductName `Scrunch`, FileVersion `0.1.0.0`,
ProductVersion `0.1.0`, no invented company. `Directory.Build.props` supplies
the version. Only x64 artifacts were produced. The payload includes the SDK's
transitive redistributables; it has not been aggressively pruned for size.

## Exact installed and portable checks

`tools/verify-installation.ps1` installs the final EXE at
`%LOCALAPPDATA%\Programs\Scrunch`, hashes installed executable/assembly against
the packaged payload, and uses a process-only isolated notebook directory.
Its successful 26-check run verifies:

- Start Menu launch, visible shell, native note creation, editing and autosave.
- Closing the shell keeps the resident app and note alive; OS-delivered
  Ctrl+Alt+N creates another note with the shell hidden.
- Startup defaults off; enabling writes one quoted installed path plus
  `--startup`; disabling removes it.
- In-place reinstall preserves notebook bytes and startup opt-in. Quiet startup
  restores notes without the shell; manual launch redirects to that resident app.
- A Windows-disabled startup entry stays visibly disabled. Re-registration does
  not overwrite Windows approval; the blocked entry can be removed in Settings.
- Quit exits cleanly. Uninstall removes binaries, shortcut and an enabled Run
  value, while preserving notebook bytes. Personal notebook hashes are unchanged.

`tools/verify-tray-callbacks.ps1` passes 12 checks against the same final installation:
Explorer exposes the icon; its real registration and stable GUID are present;
New note, Undo, Show notes, Settings and Quit work through the real native menu;
Quit removes the icon; quiet startup restores notes and the same icon identity;
manual activation redirects; footer Quit exits. These use the version-4 callback
and posted native menu mnemonics, **not physical mouse input**.

`proto/verify-tray-ui.ps1` separately passes **21 physical desktop checks** on the
same final installer: physical tray left-click, click-away dismissal, right-click
menu, OS keyboard menu actions, note creation and Undo, Settings, hidden-shell
global shortcut, tray Quit, icon removal, quiet startup, manual reactivation and
footer Quit. Its fresh notebook is seeded through the real editor and restored
after a clean restart. Evidence: `artifacts/manual-tray-final-evidence/checks.json`.

Final installed crumple acceptance used physical mouse input to open the note's
context menu and select Discard note. A four-second 460×480 recording contains
120 samples at approximately 30 fps; inspected frames show the actual paper
deforming, gathering and disappearing. The persisted discard and subsequent Undo
were checked against the original note ID and editor text. Evidence under
`artifacts/installed-verification`: `crumple-final-result.json`,
`crumple-final.mp4`, and the corresponding frame manifest/images. This evidence
comes from the final installed executable, independently of the public Debug
fixture demonstration clip.

`tools/verify-portable.ps1` extracted the exact ZIP separately, checked its payload
and launched it. Its shell
and editor work, synthetic text persists, Quit exits, and startup is unavailable
and off because it is not installed at the registered stable path.

All temporary test installations were uninstalled. Original startup registry
values were restored. Synthetic notebooks and raw logs remain under ignored
`artifacts/installed-verification`; no personal note contents were captured.

## Regression results

| Suite | Result |
| --- | --- |
| Storage and migration | 23 checks passed |
| Paper geometry | 466,560 projected and 1,570,752 crumple patches checked |
| ScrunchFX assets/motion | 10,000 seed checks passed |
| Tray identity/placement | 9 checks, including 10,000 placements, passed |
| Prepared asset generator | All three bakes reproduce byte-for-byte |
| Native product integration, Debug | 45 checks passed |
| Native paper renderer, Debug | 25 checks passed |
| Native ScrunchFX, Debug | 62 checks passed |
| Native tray integration, Debug | 22 checks passed |

Native Debug checks used isolated fixtures in the renamed source tree. The FX
launcher initially reached its wait timeout; the app continued and wrote a fresh
successful 62-check report. These checks cover capture, crumple playback,
cancellation/Undo, reduced motion and renderer rest. They are distinct from final
Release installer acceptance.

The physical tray suite is preserved in `proto/verify-tray-ui.ps1`; it was not
weakened to call the programmatic callback suite a mouse test. Windows 11,
ARM64, mixed DPI and alternate taskbar edges remain unverified. A real sign-out
and sign-in cycle was not performed; registry state and the actual `--startup`
invocation path were exercised.

## Size and performance observations

Final installed Release, visible shell and three idle synthetic notes:

- Private memory: 147,824,640 bytes (140.98 MiB).
- Working set: 182,661,120 bytes (174.20 MiB).
- CPU: 15.625 ms across 10.015 seconds, about 0.16% of one logical core.
- Warm process launch to an observed shell: 394 ms.
- First New note invocation to an observed note: 206 ms.

The launch/note timings include UI automation polling and command overhead.
This is one short sample on one machine, not a cold-start or cross-hardware
benchmark. Raw observations are in `final-performance.json` in the ignored
evidence directory, including the measured managed-assembly hash.

## Cleanliness, licences and public media

MIT covers Scrunch's code and owned generated assets. Full Inter/Drawably terms,
Vortice/SharpGen notices, .NET notices, and Microsoft redistribution terms are
included. Build-time notice generation fails on an unreviewed dependency.
Unused source fonts retain their OFL notices but do not ship in the app.

Payload verification checks fonts, XAML, icon, prepared bakes, shader, runtime
and notices. It rejects local logs, QA data, source tooling, debug symbols,
private keys, unused fonts and development pages. Owned Release assembly strings
contain neither developer home paths nor verification entrypoints. Git ignores
builds, caches, notes, signing material and raw verification evidence.

Source and tools now use Scrunch/ScrunchFX. The only old product-name references
are the legacy data-directory literal, the preservation test and its explanatory
documentation. They are required to find existing users' notes. The active local
checkout directory and historical Git revisions were not renamed or rewritten.

Public media in `docs/media` contains only synthetic notes: an installed-app
desktop screenshot, a native dark-shell fixture, and a 115,111-byte six-second
460×480 MP4. The clip uses the existing Debug FX fixture to invoke the production
discard renderer at normal speed; it is demo media, not final-installer evidence.
Frames were inspected for the note, deformation, crumpled paper and empty backdrop.

## Automation and remaining release steps

The GitHub workflow builds on Windows, runs headless checks, packages and uploads
the installer/ZIP/checksums. Version tags create a draft release in a separate job
with narrowly scoped write permission. The [clean Windows runner build passed](https://github.com/nickzonzh/scrunch/actions/runs/35671612204)
for commit `4e30173983be9e94d3270825cc2e70a2d4d7a079`, including all headless checks,
Release publish, payload validation, installer/ZIP creation and artifact upload.
The first run exposed a missing ReadyToRun compiler restore on clean machines;
explicit Release restore fixed it. The local packages above were rebuilt from
that source and their 26 installation, 12 tray and portable checks rerun.
The tag-only draft-release job was correctly skipped on manual dispatch and has
not been exercised with a tag. Hosted Actions emitted a runtime deprecation notice
for the pinned v4 actions; GitHub ran them successfully with its Node 24 override.

Before publication:

1. Review the known platform coverage limits and verify the hashes above.
2. If distributing GitHub's rebuilt artifacts,
   install and smoke-test those exact downloads; local hashes do not identify them.
3. Follow [the publishing commands](RELEASING.md#publishing), then deliberately
   make the repository public and publish the draft when ready.

No tag, public release, signing certificate, auto-update or new product feature
was introduced by this milestone.
