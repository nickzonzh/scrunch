# Scrunch paper mark

`scrunch.svg` is the editable source: an irregular eight-corner silhouette with
four broad planes. No text, square background, emoji, or detailed crease mesh.
Warm white fills and a charcoal outline work on light and dark notification areas.

Run `./tools/scrunch-icon/build.ps1` from the repository root on Windows to render
the SVG's polygon/polyline subset with System.Drawing and generate transparent
PNGs at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels, plus the multi-frame
`proto/Scrunch/Assets/Scrunch.ico`. The script needs no external packages or
network. Edit the SVG, not a generated PNG. The ICO supplies the executable resource,
WinUI shell icon and native tray HICON. ICO frames are selected for the tray monitor's
current DPI, refreshed on display/settings changes and Explorer re-registration.

`size-proof.png` shows actual-size light/dark rows and an enlarged 16px pixel view.
16, 20, 24, 32, 48, 64 and 256px were visually inspected, as was the actual 16px Explorer
tray rendering at 100% scale. The source is deliberately simple enough to share
geometry at these sizes. No separate theme variant was needed. Mixed-DPI hardware
has not been visually verified.
