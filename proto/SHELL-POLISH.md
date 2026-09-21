# Compact Scrunch shell

## Implementation

The information architecture and native controls are unchanged: Scrunch / New
note, search, note list, Undo, Settings / Quit. The paper renderer, NootFX code,
prepared bakes, note colours and storage schema are unchanged.

The XAML root width is **400 DIP**. The root is measured at that width with unbounded
height, then constrained to **220–560 DIP including the integrated caption**, also
bounded by the current display work area minus 64 DIP. The three-note body is
**329 DIP high** (previously 335),
plus a **32-DIP integrated caption**. A recovery/shortcut notice adds its measured
height within the same cap. Native frame thickness is measured from the XAML
host viewport. This avoids counting the old caption twice on Windows 10, and
avoids using an element's stale arrange size while a theme/list change settles.

The list is capped at 350 DIP and scrolls independently; header, search and footer
stay in place; the full window remains bounded even with a notice. Settings is capped
at 460 DIP plus the shell padding and keeps its existing scroll viewer. Sizing
runs on content/view, notice, theme and window-position changes, with a queued
measurement and a no-op check when dimensions are already correct. No continuous
render timer was added.

Spacing: 16-DIP horizontal padding, 8/10 top/bottom padding, 10 between sections,
50-DIP note rows, 10-DIP colour dots with 0.75-DIP theme-aware outlines. Search
retains its native 32-DIP minimum height, with a search glyph and “Search notes…”.
The standalone note-count row is removed; count remains in the list's accessible
name. New note, Undo, Settings and Quit use 30-DIP subtle native actions. Native
focus, hover, pressed, disabled and list selection styles remain intact.

## Fonts

- Shell, settings and menus: **Inter Variable 4.1**, bundled unmodified as
  `Assets/Fonts/InterVariable.ttf`, resolved using its actual internal family
  name `Inter Variable`. Body/search/secondary/footer text use 400; rows, New note
  and the quieter 18-DIP wordmark use 500. Settings stays at 19 DIP / 600.
  No axis manipulation or font registration is needed.
- Paper content only: **Drawably Pen**, `Assets/Fonts/DrawablyPen.ttf`, 23 DIP,
  regular. Native TextBox line layout and scrolling are retained. This was
  inspected at 240 × 240, 300 × 320 and 440 × 420 DIP paper sizes.
- Explicit WinUI font-family fallback: requested bundled font → Segoe UI Variable
  Text → Segoe UI → Segoe UI Emoji. DirectWrite's script-aware system fallback
  supplements the chain. This does not rewrite text, split runs, replace Unicode
  or change the native editor. Windows 10 can skip the absent Variable family.
- Source versions, hashes and licenses ship in `Assets/Fonts/NOTICE.txt`,
  `Inter-LICENSE.txt` and `Drawably-LICENSE.txt`. Existing legacy assets remain.

Inter Variable was selected after inspecting its font name table and the rendered
WinUI weight hierarchy. The handwriting captures include Latin punctuation,
numerals, accented text, CJK, Greek, Cyrillic, Arabic, and colour/ZWJ emoji. Their
storage comparison normalizes only WinUI's CR paragraph delimiters for assertions.

## Native material and compatibility

`ShellBackdrop` derives from XAML `SystemBackdrop` and wraps the Windows App SDK
**DesktopAcrylicController**, selecting **Thin**. SDK 1.8's convenience
`DesktopAcrylicBackdrop` does not expose that selection. The implementation uses
XAML's default configuration object for activation/theme/accessibility, and the
native controller for policy, transparency and power fallback. It creates the
system dispatcher queue even when taking the unsupported solid path; otherwise
Windows.UI.Composition activation can fail when no controller initialized it.

The final palette is deliberately frostier than the stock Thin preset:

| Theme | TintColor | TintOpacity | LuminosityOpacity | FallbackColor |
|---|---|---:|---:|---|
| Light | #FFF3F3F3 | 0.12 | 0.72 | #FFF3F3F3 |
| Dark | #FF202020 | 0.40 | 0.86 | #FF202020 |

The old luminosity values were 0.44 / 0.64 with zero tint opacity. Custom values
must be reapplied on theme changes. The shared XAML configuration still controls
activation, high contrast and transparency/power fallback. The notification
refreshes our palette without calling the base notification implementation,
which rejected its target during a live root-theme switch on this SDK/host.
Unsupported/forced fallback uses a solid composition brush; high contrast uses
the system background. There is no extra fake-glass layer or custom blur.

The dark-only follow-up raises tint from 0.18 to 0.40 and luminosity from 0.78
to 0.86 using the same neutral #202020 charcoal. It reduces the warm wallpaper
cast without changing light mode, fallback colours, or the native blur pipeline.

The title area extends the same backdrop using `ExtendsContentIntoTitleBar` and
`SetTitleBar` on an empty 32-DIP drag strip. It adds no duplicate logo or title.
Windows retains its caption controls, system menu, minimising and save-on-close.
The existing non-resizable/non-maximizable presenter stays in place. Caption
backgrounds are transparent, with theme-aware glyph and interaction colours;
high contrast resets the colours to system defaults. Unsupported customization
keeps the ordinary native caption. DWM retains the frame/shadow and small Windows
11 rounding. The FX lab keeps its original native title bar.

Search overrides only three native TextBox fill resources, scoped to that field.
Light fills use white at 20% / 29% / 45% for rest/hover/focus; dark fills use black
at 9% / 13% / 25%. These are approximately 10% lower opacity than the first tidy-up.
High contrast uses the opaque system window colour. Native
text, selection, border, accent focus underline, disabled state and search icon
remain. Footer actions use regular weight and the divider's bottom margin drops
from 4 to 2 DIP. The footer divider now uses half the theme brush's opacity,
returning to full opacity in high contrast. Note rows retain 14-DIP medium primary text, 12-DIP theme-secondary
text, 50-DIP spacing and coloured dots. Drawably and note interactions are unchanged.

Shortcut conflicts now appear below the footer as a neutral two-line notice with
a 12-DIP information icon and a 24-DIP dismiss button. It adds 38 DIP (previously
68 DIP for the full-width yellow InfoBar); dismissing it removes that space and
returns keyboard focus to Settings. The shortcut explanation remains in Settings.
Save failures, recovery warnings and their retry action retain the original InfoBar.

API references: [title-bar customization](https://learn.microsoft.com/windows/apps/develop/title-bar?tabs=winui3),
[SystemBackdrop customization](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop),
[configuration notification](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop.ondefaultsystembackdropconfigurationchanged?view=windows-app-sdk-1.8),
and [native system backdrop configuration](https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops).

## Verification boundary

The current implementation was rebuilt and rechecked against the full shell brief
at 23:16 local time on 21 September 2026. No further UI or renderer changes were
needed. Fresh evidence is in `artifacts/shell-review/`: 45 product checks, 62
NootFX checks, 25 paper-renderer checks, and six inspected native desktop captures
under `desktop-verification/`. All four translucent cases reported Acrylic
`Active`, including the latest dark **0.40 / 0.86** palette; the two explicit
solid cases reported `Forced solid fallback`. The controlled bright/dark
backgrounds visibly show through Acrylic but not through the solid fallbacks.
This closes the active-Acrylic capture gap recorded for the dark-tint follow-up.
The user's OS theme and wallpaper were not changed; light/dark were per-window
WinUI themes. The earlier Windows 11 and OS-policy verification limits below remain.

The final tidy-up is visually verified on Windows 10 19045: light and dark app
themes over bright beach and dark floral Windows wallpaper images, displayed in
a temporary native review window. Both solid fallbacks were also inspected.
The user's actual wallpaper and system theme were not changed. Captures show
real desktop Acrylic, the integrated caption, readable rows/footer and translucent
search; they are not XAML-only simulations. Evidence is in
`artifacts/scrunch-compact/tidy-verification/`; details are in `VERIFICATION.md`.

The earlier blank-client capture problem was resolved by capturing from the
interactive desktop outside the command sandbox. Computer Use's WGC capture
still returns `SetIsBorderRequired ... 0x80004002` on this host; its accessibility
inspection works. The screenshot skill's native desktop capture works in the
interactive session. Earlier failed screenshots remain historical evidence.

Final Debug build: zero warnings/errors; 45 native integration checks pass after
the feedback refinements. The preceding pass also ran all 16 storage checks.
The integration run switches the entire window theme and
checks bounded lists, caption separation, creation, editing, search, activation,
Undo, settings, saving and animated discard. Native system-menu inspection
confirms Move/Minimize/Close and disabled Size/Maximize. Another running older
Noot prototype coincided with a real global-shortcut conflict; the fallback notice
was kept visible in captures rather than suppressed for presentation.

Windows 11, mixed-DPI dragging, physical caption-button/drag gestures, real OS
high-contrast and transparency/power toggles remain unverified. Forced solid
fallback and per-window themes do not substitute for those checks. No packaging,
tray, paper renderer, NootFX or note-lifecycle changes are part of this pass.
