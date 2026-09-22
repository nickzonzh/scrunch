# Scrunch 0.1.0 release verification

Tested on 22 September 2026, Windows 10 x64 build 19045, NVIDIA RTX 3060 Ti.
The repository remains private; no release has been published.

> **Superseded packaging.** This record covers the 180 MB component-SDK build
> (`ee70395`). The current trimmed packaging (see [PACKAGE-SIZE.md](PACKAGE-SIZE.md))
> has passed the headless gate, the portable ZIP check, the physical menu discard and
> the tray callback suite, but the installer lifecycle, sign-in startup and uninstall
> acceptance below must be repeated against the new artifacts before publishing.

## Acceptance status

The smaller self-contained installer and ZIP pass local x64 acceptance. The
installer lifecycle, portable restart/font persistence, native product suite and
actual Release animated discard were checked after the package changes.
No known release blocker remains on the tested system. Platform limits below
still apply; public publication remains a deliberate owner action.

## Final local artifacts

Built with `tools/release.ps1`, pinned .NET SDK 9.0.317, locked NuGet dependencies,
Release x64, self-contained .NET and Windows App SDK WinUI/DWrite components.
ReadyToRun, trimming and single-file bundling are disabled. See
[package size](PACKAGE-SIZE.md) for the comparison and rejected trimming trial.

| Artifact | Bytes | MiB |
| --- | ---: | ---: |
| `Scrunch-0.1.0-Setup.exe` | 49,493,811 | 47.20 |
| `Scrunch-0.1.0-win-x64.zip` | 73,254,496 | 69.86 |
| Published payload, 479 files | 180,762,305 | 172.39 |

SHA-256:

```text
c8846da7747dbe7b0255c344a1332e0bd27e731de35bc5efcb52c1a2509023c9  Scrunch-0.1.0-Setup.exe
702523a5d47426709a55db090587d0aaa22bee9fd7d3ce11e964a9f7fdeb1456  Scrunch-0.1.0-win-x64.zip
```

Managed application assembly SHA-256:
`16741e9b95eee21272e75117f031384fbf0bdb48bc7be3265892354285b15093`.
Executable metadata: ProductName `Scrunch`, FileVersion `0.1.0.0`, ProductVersion
`0.1.0`, no invented company. Only x64 artifacts were produced.

## Exact installed and portable checks

`tools/verify-installation.ps1` passed all 26 checks against the smaller installer:

- Per-user installation and Start Menu launch; installed executable and assembly
  hashes match the packaged payload.
- Native note creation, editing, autosave, hidden-shell residency and OS-delivered
  Ctrl+Alt+N.
- Startup defaults off; enabling writes one quoted installed path plus `--startup`.
- Reinstall preserves notebook bytes and startup opt-in. Quiet startup restores
  notes without the shell; manual activation redirects to the resident process.
- Windows-disabled startup remains disabled and its approval state is preserved;
  the blocked registration can be removed through Settings.
- Quit exits; uninstall removes binaries, shortcut and enabled startup entry while
  preserving saved notes. Personal notebooks remain untouched.

`tools/verify-portable.ps1` extracted the exact ZIP and passed payload verification,
launch, editing, saving, font selection and full process restart. Inter at size 28
and the synthetic note text were restored. Startup registration stays unavailable
and off outside the stable installed path. Quit exits cleanly.

Temporary test installations were uninstalled and original startup registry state
restored. Evidence is under ignored `artifacts/installed-verification`; prior
font-build reports are preserved in `artifacts/size-baseline/verification`.

## Native rendering and regression results

The exact Release publish payload was launched with an isolated notebook over a
neutral backdrop. Physical right-click and Discard note menu click triggered the
real animation. A four-second 460 x 480 recording contains 120 samples; inspected
frames show readable paper, deformation, crumpled paper and an empty backdrop.
Discard persisted, and Undo restored the same note ID and editor text. Its managed
assembly matches the packaged and installation-verified assembly. Evidence:
`artifacts/size-discard/result.json`, `discard.mp4`, and `discard.frames`.

| Suite | Result | Evidence scope |
| --- | --- | --- |
| Storage and migration | 27 checks passed | Current source |
| Paper geometry | 466,560 projected + 1,570,752 crumple patches | Current source |
| ScrunchFX assets/motion | 10,000 seeds passed | Current source |
| Tray identity/placement | 9 checks, including 10,000 placements | Current source |
| Prepared asset generator | All three bakes byte-exact | Current source |
| Native product, Debug | 52 checks passed | Current component build |
| Native ScrunchFX, Debug | 62 checks passed | Current component build |
| Native paper renderer, Debug | 25 checks passed | Prior build |
| Native tray integration, Debug | 22 checks passed | Prior build |
| Native tray callback smoke | 12 checks passed | Exact Release payload |
| Physical desktop tray suite | 21 checks passed | Prior build |

The current native product suite covers shell/search/list/settings, Unicode
editing, placement, autosave, discard/Undo races, font preview and persistence,
fresh glyph capture, animated menu discard and clean shutdown. Debug evidence:
`proto/artifacts/size-check/product-verification.json`. Build: zero warnings/errors.
The 12 callback checks use the real Explorer registration and native menu commands,
not physical mouse input; evidence is in `artifacts/size-tray/checks.json`.
Prior physical tray evidence remains `artifacts/manual-tray-final-evidence/checks.json`;
it is not represented as a fresh run of this smaller build.

## Performance and platform limits

Five alternating runs measured median launch to shell control availability of
391 ms before and 438 ms after; first-note availability measured 266 ms and 308 ms.
The first observed launch was 504 ms before and 1,389 ms after. These include UI
automation overhead and are not controlled cold-cache measurements. Full context
and raw evidence references are in [package size](PACKAGE-SIZE.md).

Earlier three-note Release sampling measured 140.98 MiB private memory, 174.20 MiB
working set and 15.625 ms CPU over 10.015 seconds. Those measurements predate this
package change and do not establish the current build's steady-state budget.

Windows 11, ARM64, mixed DPI and alternate taskbar edges remain unverified. A real
sign-out/sign-in cycle was not performed; registry state and actual `--startup`
invocation were exercised. Binaries remain unsigned.

## Cleanliness, licences and public media

MIT covers Scrunch code and owned generated assets. Font, Vortice/SharpGen, .NET
and Microsoft redistribution notices are retained. Dependency notice generation
uses the narrower restored graph and fails on unreviewed dependencies.

Payload validation requires fonts, XAML, icons, prepared bakes, shader, runtime and
notices. It rejects unused AI/ML/widgets components, payloads above 200 MiB, local
logs, notes, test media, development pages, source tooling, debug symbols and keys.
Owned Release strings contain neither developer home paths nor test entrypoints.

Source uses Scrunch/ScrunchFX. Old-name references only support preserved legacy
notebook migration and its tests/documentation. The active checkout directory and
Git history were not rewritten.

Public media in `docs/media` contains synthetic notes only. The public discard
clip uses the Debug fixture at normal speed and remains demonstration media;
the newer Release recording above is separate acceptance evidence.

## Automation and publication

The Windows workflow restores locked dependencies, runs headless checks, publishes,
validates the payload and uploads installer/ZIP/checksums. Version tags create a
draft release in a separate job with narrowly scoped write permission. The
[clean Windows runner build](https://github.com/nickzonzh/scrunch/actions/runs/35683399518)
passed for `b8449abc7cbfffa821c67c33abf722267b93721d` in three minutes, including
locked restore, all headless checks, publish, size/payload validation, packaging
and artifact upload. The tag-only draft-release job was correctly skipped; it has
not been exercised with a tag. Hosted actions emitted the existing Node 20
deprecation annotation and successfully ran with GitHub's Node 24 override.

Before publication:

1. Review platform coverage limits and verify the hashes above.
2. If distributing GitHub's rebuilt artifacts, smoke-test those exact downloads;
   local artifact hashes do not identify hosted builds.
3. Follow [publishing](RELEASING.md#publishing), then deliberately make the
   repository public and publish the draft when ready.
