# Scrunch paper mark

`scrunch-master.png` is the transparent source artwork for the Tight scrunch
direction selected by Nick on 5 October 2026. The compact white paper wad has broad
overlapping folds, a dark central crease and an escaping upper-right flap. The
master was isolated from the approved concept with the built-in image generator.
It has no text, background tile or cast shadow.

Run `./tools/scrunch-icon/build.ps1` from the repository root on Windows to render
the master with System.Drawing and generate transparent
PNGs at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels, plus the multi-frame
`src/Scrunch/Assets/Scrunch.ico`. The script needs no external packages or
network. Edit the master, then regenerate the sizes and ICO. The ICO supplies the executable resource,
WinUI shell icon and native tray HICON. ICO frames are selected for the tray monitor's
current DPI, refreshed on display/settings changes and Explorer re-registration.

`size-proof.png` shows actual-size light/dark rows and an enlarged 16px pixel view.
Inspect this sheet after regeneration, especially the central crease and upper-right
flap at 16 and 20px. The selected icon still requires visual acceptance in Explorer's
actual tray and taskbar. Mixed-DPI hardware has not been visually verified.
