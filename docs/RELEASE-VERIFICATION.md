# Scrunch 0.1.0 release verification

Tested on 22 September 2026, Windows 10 x64 build 19045, NVIDIA RTX 3060 Ti.
The repository remains private; no release has been published.

## Native acceptance, 5 October 2026

### Selected release icon

Nick selected concept C, Tight scrunch, on 5 October 2026. Its transparent paper
artwork now supplies the executable, window and tray icon at 16 through 256px.
The generated light/dark size sheet was visually inspected. This changes the
packaged icon and executable resource; application logic and crumple animation
data are unchanged. Earlier installer hashes do not identify this refreshed
package. Visual acceptance in the actual Windows tray/taskbar and smoke checks
of the refreshed installer and ZIP remain pending before publication.

### Hand-tested application behaviour

Commit `41ff38ceba6ec5c05550f9541b9f7584511bcf88` was **hand-tested by Nick**
on 5 October 2026. Nick confirmed the current native build is good to go as an
unsigned early preview. This acceptance covers the 2 October 2026 change,
"Crumple into a rounded wad with an original confined-sheet solver", at that
commit, together with the preceding note ink, caret and dragging seam fixes.

`tools/check.ps1 -Locked` passed on that commit on 5 October 2026: all 86 tests
passed, all three prepared bakes matched their generator, and all seven compiled
shaders matched their source. Code signing remains deferred by Nick's direction.

The results and hashes below are the historical 22 September acceptance record.
They do not identify newly built October release artifacts. Final installer and
ZIP smoke checks must refer to the exact artifacts prepared for the draft release.

### October CI reproducibility fix

The first hosted run failed because the provenance file recorded unsaved solver
precision that differed between Node 22 and Node 24. Commit
`a00d33d92db8d794fa6708c4fe433aee9d3c8298` measures final bounds from the encoded
binary16 positions instead. All three animation files remain byte-for-byte
unchanged. The exact asset comparisons remain in place; no tests were weakened,
added or deleted. Independent decoding confirmed the recorded bounds.

The full local locked check and the [hosted build and packaging run](https://github.com/nickzonzh/scrunch/actions/runs/37270145873)
passed after the fix, including all 86 tests. Nick's hand-tested app code and
animation data are unchanged by this metadata fix.

Fresh final installer lifecycle and portable smoke checks remain pending. The
automated lifecycle script must not replace Nick's existing personal installation.
The October draft must record its own source commit and artifact hashes, and the
exact final packages must be checked before publication.

## Acceptance status, 22 September 2026

The trimmed, ReadyToRun, self-contained installer and ZIP pass local x64
acceptance. The installer lifecycle, portable restart/font persistence, native
product/FX/tray suites and the actual Release animated discard were all run against
the exact artifacts hashed below, after the last source change. No known release
blocker remains on the tested system. Platform limits below still apply; public
publication remains a deliberate owner action.

## Final local artifacts

Built with `tools/release.ps1`, pinned .NET SDK 9.0.317, locked NuGet dependencies,
Release x64, self-contained .NET and Windows App SDK WinUI/DWrite components,
partial trimming and ReadyToRun; single-file bundling is off. See
[package size](PACKAGE-SIZE.md) for the comparison and startup measurements.

| Artifact | Bytes | MiB |
| --- | ---: | ---: |
| `Scrunch-0.1.0-Setup.exe` | 25,383,043 | 24.21 |
| `Scrunch-0.1.0-win-x64.zip` | 36,617,008 | 34.92 |
| Published payload, 320 files | 85,965,275 | 81.98 |

SHA-256:

```text
9604386a8dd43cc78e4a52c78251613a922d84fde23ece4cd1b350410346c690  Scrunch-0.1.0-Setup.exe
61d1a8fb62ed5cea29a58fbc9e9c45a236cdaa877ce39c59387b240a4fee6fd6  Scrunch-0.1.0-win-x64.zip
```

Managed application assembly SHA-256:
`2a9686f6a271b1e92aa5433874b745da67f25f7c9e01c6d286dcb49adb387bd4`.
Executable metadata: ProductName `Scrunch`, FileVersion `0.1.0.0`, ProductVersion
`0.1.0`, no invented company. The project builds x64 only.

## Exact installed and portable checks

`tools/verify-installation.ps1` passed all 26 checks against the final installer:

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
restored. Evidence is under ignored `artifacts/installed-verification`.

## Native rendering and regression results

The exact Release publish payload was launched with an isolated notebook over a
neutral backdrop. Physical right-click and Discard note menu click triggered the
real animation. A four-second recording was sampled into 31 frames; inspected
frames show readable paper, a crumpling sheet with visible folds, and an empty
backdrop. Discard persisted, and Undo restored the same note ID and editor text.
The managed assembly hash recorded by the run equals the packaged and
installation-verified assembly. Evidence: `artifacts/size-discard/result.json`,
`discard.mp4`, and `discard.frames`.

| Suite | Result | Evidence scope |
| --- | --- | --- |
| Storage and migration | 27 checks passed | Current source |
| Paper geometry | 466,560 projected + 1,570,752 crumple patches | Current source |
| ScrunchFX assets/motion | 10,000 seeds passed | Current source |
| Tray identity/placement | 9 checks, including 10,000 placements | Current source |
| Prepared asset generator | All three bakes byte-exact | Current source |
| Native product, Debug | 52 checks passed | Current source |
| Native ScrunchFX, Debug | 62 checks passed | Current source |
| Native tray integration, Debug | 22 checks passed | Current source |
| Native tray callback smoke | 12 checks passed | Exact Release payload |
| Physical desktop tray suite | 21 checks passed | Prior build |

The current native product suite covers shell/search/list/settings, Unicode
editing, placement, autosave, discard/Undo races, font preview and persistence,
fresh glyph capture, animated menu discard and clean shutdown. Debug evidence:
`artifacts/debug/{product,fx,tray}-verification.json`. The former standalone paper
renderer bench (`MainPage`) was removed; its checks are covered by the ScrunchFX and
product suites. Build: zero warnings/errors.
The 12 callback checks use the real Explorer registration and native menu commands,
not physical mouse input; evidence is in `artifacts/size-tray/checks.json`.
Prior physical tray evidence remains `artifacts/manual-tray-final-evidence/checks.json`;
it is not represented as a fresh run of this smaller build.

## Performance and platform limits

Five alternating isolated launches measured a median 374 ms from process start to
the shell's New note control and 256 ms from New note to an editable note, against
434 ms and 309 ms for the untrimmed component-SDK build; peak working set 129 MB
against 152 MB. These include UI automation overhead and use a warm file cache. The
table and the IL-only comparison are in [package size](PACKAGE-SIZE.md#startup).

Windows 11, mixed DPI and alternate taskbar edges remain unverified. ARM64 is not
built. A real sign-out/sign-in cycle was not performed; registry state and actual
`--startup` invocation were exercised. Binaries remain unsigned until Trusted
Signing is provisioned ([signing](RELEASING.md#signing)).

## Cleanliness, licences and public media

MIT covers Scrunch code and owned generated assets. Font, Vortice/SharpGen, .NET
and Microsoft redistribution notices are retained. Dependency notice generation
uses the narrower restored graph and fails on unreviewed dependencies.

Payload validation requires fonts, XAML, icons, prepared bakes, compiled shaders,
runtime and notices. It rejects unused AI/ML/widgets components, debugger natives,
shader source, payloads above 90 MiB, local logs, notes, test media, source tooling,
debug symbols and keys.
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
