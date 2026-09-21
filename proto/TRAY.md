# Scrunch tray residency

Implemented and verified on 21–22 September 2026 with Windows 10, x64, Windows App
SDK **1.8.260317003**, .NET 9, and the existing unpackaged/self-contained WinUI
project. The shell remains the same `ShellView`, Acrylic backdrop, Inter font,
search/list/settings structure and integrated title bar. Notes, Drawably type,
persistence and NootFX retain their existing architecture.

## Lifecycle and actions

`TrayIcon.cs` owns a native `Shell_NotifyIcon` registration on the long-lived shell
HWND. It adds the icon, selects `NOTIFYICON_VERSION_4`, handles `NIN_SELECT` and
`NIN_KEYSELECT`, and removes the icon on real exit. Tooltip: **Scrunch**. There
are no balloons, toasts, third-party tray dependencies, or tray timers.

The notification GUID is deterministic for the executable's normalized absolute
path. It is stable across restarts and in-place upgrades. Microsoft binds unsigned
notification GUIDs to executable paths: a moved development/portable build gets
a separate GUID and may return to Explorer's overflow area. It does not reuse a
GUID already bound to another binary path. Normal single-instance routing remains
global to Scrunch, independent of the executable location.

- Manual launch shows the shell and restores active notes independently.
- Tray selection positions, shows and activates the existing shell. A second
  selection hides it. A mouse-down deactivation followed by the same click's
  selection is suppressed so the shell does not flicker back open.
- Losing activation hides a tray-opened shell. Settings stays in the same window;
  a settings dropdown stays usable. Native menu tracking temporarily suppresses
  dismissal, with a final focus check when tracking finishes.
- Escape returns from Settings, clears a nonempty search, then dismisses an empty
  tray-opened home surface. Search and shell state otherwise survive hiding.
- **X / Alt+F4 / WM_CLOSE** cancel shell destruction and hide it. Maximize,
  minimize and resizing are disabled; native close, drag region, border and shadow
  remain. The resident shell is excluded from taskbar/Alt+Tab switchers while the
  tray is available. Notes never inherit shell visibility.
- **Quit**, from either surface, captures outstanding note state, saves, marks
  shutdown explicitly, stops the save timer, closes notes without discarding them,
  deletes the tray registration, disposes native handles/hotkey/store/FX and exits.
  Save failure keeps the process and notes open and reveals the shell.
- `--startup` restores notes while leaving the shell hidden. No sign-in entry is
  registered. A later manual launch reveals that resident shell.

The native `CreatePopupMenu` / `TrackPopupMenuEx` menu supplies keyboard/mnemonic
and accessibility behaviour. Its commands are **New note**, **Show notes**,
**Undo last discard**, separator, **Settings**, **Quit**. Undo's enabled state is
read when the menu opens. Show notes restores/focuses active notes without changing
their saved or native always-on-top setting.

The existing Ctrl+Alt+N registration stays attached to the surviving HWND. The
existing compact shortcut-conflict warning remains unchanged.

If tray creation fails, the shell remains in the taskbar/switchers, cannot hide
via Close or click-away, and displays a compact inline explanation. Even a quiet
launch reveals the shell in this case. A `TaskbarCreated` registered message
re-adds the icon and selects version 4 again after Explorer restarts; a failed
re-add reveals the same usable fallback. There is no continuous retry polling.

## Positioning and activation

`Shell_NotifyIconGetRect` supplies the real notification rectangle. If it cannot
be read, the cursor supplies an anchor and its nearest monitor. Native
`MonitorFromRect`, `GetMonitorInfo` and `CalculatePopupWindowPosition` choose a
work-area-aware popup location. A visible Explorer overflow panel is excluded as
a whole, preventing it from covering the footer. Pure `TrayPlacement` code clamps
the result and provides an edge-agnostic fallback for top/bottom/left/right bars.
Coordinates remain signed physical desktop pixels throughout. WinUI continues to
measure its 400-DIP shell at the current window DPI; content growth is re-clamped.
The shell never receives persistent always-on-top status.

`Program.cs` disables generated XAML Main and uses Windows App SDK
`AppInstance.FindOrRegisterForKey` before creating Application, windows or storage.
Secondary processes grant foreground permission, redirect activation from an MTA,
wait while pumping COM on the STA, and exit. The first process queues activation
until its UI is ready. The existing exclusive storage lease remains defensive
protection. Bench/verification modes retain isolated sessions; DEBUG `--tray-check`
uses a separate instance key and fixed isolated data folder for restart tests.

## Icon

Editable source: [`scrunch.svg`](../tools/scrunch-icon/scrunch.svg). Rebuild with
[`tools/scrunch-icon/build.ps1`](../tools/scrunch-icon/build.ps1). The transparent
four-plane paper mark has ICO/PNG frames at **16, 20, 24, 32, 40, 48, 64, 128,
256 pixels**. The ICO is embedded in the executable and used by the shell and tray.
The tray chooses its frame using the icon monitor's DPI and refreshes on display
or system-setting changes. HICONs, HMENUs and subclass registrations are disposed.

Actual-size 16/20/24/32/48/64px rows on light/dark backgrounds and the 256px image
were visually inspected. The real Explorer tray's 16px mark was inspected at 100%
scale. The silhouette and folds remain readable; no separate theme variant was
needed. See [`size-proof.png`](../tools/scrunch-icon/size-proof.png).

## Launch at sign-in

Deferred. The current deployment is a movable development output folder, without
a stable installation location or relocation/repair contract. Persisting that path
in HKCU Run would leave stale entries when builds move. No registry entry, shortcut,
startup task or package conversion was added. `--startup` provides the quiet launch
behaviour for a future explicit opt-in once deployment location is settled.

## Verification

Debug and Release x64 builds pass with zero warnings/errors. Headless checks pass:
16 storage checks; 466,560 paper patches and 1,570,752 crumple patches; the existing
FX assets/motion/10,000-seed suite; and 9 new tray identity/positioning checks,
including 10,000 work-area cases, negative coordinates and all four taskbar edges.

Native checks on the real interactive desktop:

| Suite | Result |
| --- | --- |
| Existing product / shell / persistence integration | 45 passed |
| Existing paper renderer | 25 passed |
| Existing NootFX regression, NVIDIA RTX 3060 Ti | 62 passed |
| New resident tray integration | 22 passed |
| External native desktop / keyboard / menu / restart script | 20 passed |
| Tray-unavailable native fallback | 3 passed |

The native suites exercise real notification registration, reuse/show/hide,
WM_CLOSE, actual deactivation, note independence and placement, unchanged pinning,
Settings/dropdown focus, command routing, Undo, duplicate executable launch,
global key delivery, clean shutdown and resource release. `TaskbarCreated` was
sent to the actual HWND and successfully re-registered a real Explorer icon.

The external script additionally verifies actual tray mouse input, Explorer's
Scrunch name, the native menu and its keyboard mnemonic, disabled/enabled Undo,
New/Show/Undo/Settings, click-away, Ctrl+Alt+N with Explorer foreground, icon removal
after Quit, restored notes, quiet startup, manual reactivation and footer Quit.
The second-selection toggle was exercised through the actual version-4 callback;
a promoted-tray-icon double-click/timing stress test was not performed.

Both connected displays were exercised: primary work area `(0,0,2560,1400)` and
secondary `(-1920,174,1920,1040)`, both 100% scale. Native positioning moved/clamped
the shell on the negative-coordinate monitor. NootFX also captured and animated
on both displays. Actual notification invocation was from the primary taskbar's
overflow, not a relocated secondary notification area.

Tray idle reference sample: **46.875 ms CPU over 10 seconds** (about **0.47% of one
core**), **152.86 MiB private memory**, four idle notes, shell hidden, Debug build.
This measures the whole process, not a before/after estimate of tray-only cost.
The save timer was stopped; source inspection confirms no tray polling/render loop.
NootFX separately verified zero FX frames/callbacks during idle with ordinary notes.
The final launcher-driven repeat with 13 accumulated test notes measured 78.125 ms
over 10 seconds (0.78% of one core). GPU utilization and prolonged idle residency
were not measured. The four-note sample is preserved in `tray-idle-reference.json`.

The Computer Use screenshot API failed on this Windows 10 machine with
`SetIsBorderRequired / E_NOINTERFACE`; the repository's existing WinApp UIA/input
driver and passive desktop-pixel captures provided native interaction/visual
evidence. A final sandbox-desktop run without Explorer verified the compact
tray-unavailable warning, forced visibility on quiet launch, refusal to hide on
Close, and successful explicit Quit; that environment is not evidence of normal
tray failure on the user's desktop.

Evidence lives under `artifacts/scrunch-compact/`: `product-verification.json`,
`verification.json`, `fx-verification.json`, `tray-verification.json`, and
`tray-fallback-verification.json`, `tray-idle-reference.json`, and `tray-desktop/`
(UIA trees, actual icon/shell/menu screenshots, `checks.json`).
These generated artifacts stay out of Git. Existing unrelated edits in
`SHELL-POLISH.md` and `VERIFICATION.md` were preserved.

Reproduce on an unlocked Windows desktop after using **Quit** on existing builds:

```powershell
./proto/run.ps1 -Build -VerifyProduct
./proto/run.ps1 -VerifyTray
./proto/verify-tray-ui.ps1
dotnet run --project proto/Noot.TrayChecks
```

Let each native suite finish before starting the next. `-VerifyTray` must run first
to create the isolated restart fixture consumed by the external script.

Not physically verified: a full Explorer process restart, Windows 11, alternate
taskbar edges, mixed DPI, docking, prolonged residency, or sign-out/shutdown.
Explorer resilience is supported by the real re-registration message test and
the documented Windows mechanism; no full Explorer-restart claim is made.

## Microsoft references

- [Notification area guidance](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)
- [NOTIFYICONDATA, high DPI and moved unsigned binaries](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)
- [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)
- [CalculatePopupWindowPosition](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-calculatepopupwindowposition)
- [Single-instanced WinUI 3 applications](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)
