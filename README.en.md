<div align="center">

# WinTips

[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)](LICENSE) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square) ![Windows](https://img.shields.io/badge/Windows-11-0078D4?style=flat-square) ![Dependencies](https://img.shields.io/badge/dependencies-none-4B5563?style=flat-square)

[简体中文](README.md) | **English**

![WinTips promo banner](promo/wintips-promo-en.png)

</div>

A Windows 11-style keyboard state indicator: press **Caps Lock / Num Lock / Scroll Lock** (or copy / paste / cut) and an acrylic flyout with Win11 polish appears just above the taskbar.

## Features

- **Lock-key hints**: Caps / Num / Scroll Lock and Insert — a padlock icon spins and springs back on each toggle, with an accent bar (mirrored above the label when the flyout hugs the top edge) and a fade plus slide toward the nearest screen edge.
- **Edge-aware slide**: inside the 15% zone along any screen edge the flyout slides in from (and back toward) that edge — Left/Right outrank Top/Bottom, so corners slide horizontally too — and falls back to a vertical slide when it sits mid-screen.
- **Clipboard hints**: Ctrl+C / V / X shows a checkmark or scissors icon, with an "×N" badge counting consecutive actions; the flyout only appears when the clipboard actually changes. "Notify on any copy method" is on by default — non-keyboard copies (context menu, an app's own Copy button, etc.) trigger it too, while clearing the clipboard does not.
- **Native materials**: DWM acrylic, system rounded corners, light/dark theme and accent color follow the system, high-DPI aware. On Windows 10, in high contrast, with composition disabled, when system "Transparency effects" is off, or while Battery Saver is active, the app falls back to the opaque colors from the design spec instead of overlaying translucent cards on DWM's solid fallback (Mica ignores Battery Saver, so the settings window keeps its material).
- **Unobtrusive**: click-through, never steals focus, hidden from the taskbar and Alt+Tab, auto-dismisses after 1–5 seconds (adjustable).
- **Settings window**: Win11 Settings styling, applied and saved instantly. Flyout position is two independent axes — Top/Bottom plus Left/Center/Right (Top + Center, Top + Right, Bottom + Center, …) — or choose "Custom position" and drag the flyout anywhere, then save (one click restores the default).
- **Multiple displays**: follows the focused display by default — the flyout lands on the display with the active window, positioned with that display's own DPI and taskbar state (edge-aware slide, mirrored accent bar and auto-hide taskbar following all still apply); turn "Follow the focused display" off to pin it back to the primary display.
- **Visual placement**: in custom-position mode dashed guides mark the 15% edge zones and the zone the flyout center falls into is highlighted (that is the side it will slide from); the flyout snaps to the screen's center axes, and the arrow keys nudge it pixel by pixel (tap = 1 px, hold to repeat, Shift = 10 px).
- **Multilingual UI**: Simplified Chinese, Traditional Chinese, English, German, Japanese, Russian, Spanish and Portuguese built in. Follows the system language by default; other languages live in a drop-down in Settings — effective immediately, no restart.
- **Font ships with the app**: the UI font **HarmonyOS Sans SC** (Regular / Medium / Bold, Latin and CJK in one family) is embedded inside `WinTips.exe`, so it looks the same whether or not the font is installed (the flyout is sized from measured text, so identical metrics mean identical layout), and nothing is written to the system font table. A single-file publish still produces exactly one exe. Settings can switch the UI font to any installed family — applied immediately, with the flyout re-laid out on the new metrics; if the embedded resource fails to load or the chosen font gets uninstalled, it falls back to Microsoft YaHei UI / Segoe UI.
- Lives in the tray, single instance, no third-party dependencies (WPF + WinForms, .NET 10).

## Usage

Just run `WinTips.exe` — once the tray icon appears, it is working.

- Tray left-click → Settings; right-click → menu (including test notifications).
- Command line: `--settings` opens Settings; `--test caps on 4` previews a notification; `--test menu` previews the menu.
- Settings are stored in `%APPDATA%\WinTips\settings.json`.

## Known Limitations

- The Insert hint is event-based and shows no on/off state (Windows does not track that state at the system level).
- With several displays the flyout follows the active window's display by default; turn off "Follow the focused display" in the settings to keep it on the primary display.
- A low-level keyboard hook cannot see keys sent to elevated (administrator) windows.
- Cuts from non-keyboard sources (e.g. the "Cut" context-menu item) cannot be told apart from copies and are shown with the copy style.

## License

WinTips is released under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for the full text.

## Credits

The flyout's look and motion are recreated from the LockWindow design in [FluentFlyout](https://github.com/unchihugo/FluentFlyout) (GPL-3.0); the code here is an independent implementation. WinTips itself is GPL-3.0 too, so the two are license-compatible.

The UI font **HarmonyOS Sans SC** is © Huawei Device Co., Ltd. and is redistributed unmodified with the app under the [HarmonyOS Sans Fonts License Agreement](Resources/Fonts/LICENSE_Fonts.txt): the font files are not modified (hence no subsetting), are never distributed on their own, and the full agreement is kept in the repository and compiled into the assembly — click the version number at the bottom of the settings window and choose "Show full agreement" to read it (hovering the version also shows the font credit; the dialog links the upstream text for cross-checking only).

---

[↑ Back to top](#wintips) · [中文版](README.md)
