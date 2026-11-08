<div align="center">

# WinTips

[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)](LICENSE) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square) ![Windows](https://img.shields.io/badge/Windows-11-0078D4?style=flat-square) ![Dependencies](https://img.shields.io/badge/dependencies-none-4B5563?style=flat-square)

[简体中文](README.md) | **English**

![WinTips promo banner](promo/wintips-promo-en.png)

</div>

A Windows 11-style keyboard state indicator. Press **Caps Lock / Num Lock / Scroll Lock**, copy, paste or cut, or switch the input language or an IME's input mode — an acrylic flyout with Win11 polish slides in just above the taskbar.

## Features

- **Lock-key hints**: Caps / Num / Scroll Lock and Insert. A padlock icon spins and springs back on every toggle, an accent bar sits along the bottom — mirrored above the label when the flyout hugs the top edge — and the card fades and slides toward the nearest screen edge.
- **Edge-aware slide**: within the 15% zone along any screen edge, the flyout arrives from that edge and leaves the same way. Left and right outrank top and bottom, so corners slide horizontally; mid-screen it slides vertically instead.
- **Clipboard hints**: Ctrl+C / V / X, plus the legacy Ctrl+Insert / Shift+Insert / Shift+Delete, show a checkmark or scissors icon with an "×N" badge counting consecutive actions. Nothing appears until the clipboard really changes, which is why Shift+Delete in Explorer never claims a cut — it is a permanent delete and leaves the clipboard alone. "Notify on any copy method" is on by default: context-menu copies, an app's own Copy button and any other clipboard change trigger it too, while clearing the clipboard does not.
- **Input language & IME mode hints**, off by default and switchable in Settings. After switching the input language with Win+Space, Ctrl+Shift or Alt+Shift, the flyout names the current language the way Windows' own language settings do. Switching the input mode inside an IME — the IME's own shortcut, Shift or Ctrl depending on the IME — shows a short card naming the mode it switched to, native or English. These are two layers: the first changes the keyboard layout, or HKL, the second leaves it untouched and can only be read from the IME window's conversion mode. Windows broadcasts neither, so the app samples the state the instant Shift / Ctrl / Alt / Win / Space goes down and watches for a moment afterwards. The baseline has to be taken on the spot: once you switch with the mouse on the language bar, any "last seen state" is already stale and the next hotkey press lands one step out of sync. Nothing shows unless the state really changed, and layout differences picked up by changing windows never produce a false tip.
- **Native materials**: DWM acrylic, system rounded corners, light/dark theme and accent color follow the system, high-DPI aware. Windows 10 has no acrylic or Mica, so there the flyout and the tray menu use a self-made glass instead — a blurred snapshot of the desktop behind them — and the windows become layered windows drawing their own rounded corners, truly transparent and anti-aliased. In high contrast, with composition disabled, with "Transparency effects" off or while Battery Saver is active, the app falls back to the opaque colors from the design spec rather than overlaying translucent cards on DWM's solid fallback; Mica ignores Battery Saver, so the settings window keeps its material.
- **Unobtrusive**: click-through, never steals focus, hidden from the taskbar and Alt+Tab, fades out on its own after 1–5 seconds, adjustable.
- **Settings window**: Win11 Settings styling, applied and saved instantly. Position is two independent axes — Top/Bottom plus Left/Center/Right — or choose "Custom position", drag the flyout anywhere and save; one click restores the default.
- **Multiple displays**: follows the focused display by default, so the flyout lands on the display with the active window, positioned with that display's own DPI and taskbar state; edge-aware slide, mirrored accent bar and auto-hide taskbar following all still apply. Turn "Follow the focused display" off to pin it back to the primary display.
- **Visual placement**: in custom-position mode dashed guides mark the 15% edge zones and the zone the flyout center falls into lights up — that is the side it will slide from. The flyout snaps to the screen's center axes, and the arrow keys nudge it pixel by pixel: one press is 1 px, hold to repeat, Shift for 10 px.
- **Multilingual UI**: eight UI languages built in. Follows the system language by default; another one can be pinned in Settings, effective immediately and without a restart.
- **Font ships with the app**: the UI font **HarmonyOS Sans SC** is embedded inside `WinTips.exe` in three weights, Latin and CJK in one family, so it looks the same whether or not the font is installed and nothing is written to the system font table. The flyout is sized from measured text, so identical metrics mean identical layout, and a single-file publish still produces exactly one exe. Settings can switch the UI font to any installed family, applied immediately and re-laid out on the new metrics; if the embedded resource fails to load or the chosen font gets uninstalled, it falls back to Microsoft YaHei UI / Segoe UI.
- Lives in the tray, single instance, no third-party dependencies — WPF + WinForms, .NET 10.

## Usage

Just run `WinTips.exe` — once the tray icon appears, it is working.

- The very first launch shows a one-time "is running" flyout, since the app has no main window: it both confirms the app is running in the background and points at the tray icon. It never appears again.
- Tray left-click opens Settings; right-click opens a menu with the test notifications.
- Command line: `--settings` opens Settings; `--test caps on 4` previews a notification, with `lang` accepted as the key name; `--test menu` previews the menu.
- Settings are stored in `%APPDATA%\WinTips\settings.json`.

## Known Limitations

- The Insert hint is event-based and shows no on/off state, since Windows does not track that state at the system level. Only a bare Insert counts: with Ctrl or Shift held it is a copy or a paste instead.
- With several displays the flyout follows the active window's display by default; turn off "Follow the focused display" in the settings to keep it on the primary display.
- A low-level keyboard hook cannot see keys sent to elevated, administrator windows.
- Cuts from non-keyboard sources, such as the "Cut" context-menu item, cannot be told apart from copies and are shown with the copy style.
- The IME mode hint relies on the IME reporting its conversion mode. Most IMEs do; a few third-party ones do not respond and their mode switch shows no hint.
- Switching with the **mouse** — the language bar or the IME status bar — shows no hint: that kind of change has no keystroke to hang on and sends no system notification.

## License

WinTips is released under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for the full text.

## Credits

The flyout's look and motion are recreated from the LockWindow design in [FluentFlyout](https://github.com/unchihugo/FluentFlyout), also GPL-3.0; the code here is an independent implementation. WinTips itself is GPL-3.0 too, so the two are license-compatible.

The UI font **HarmonyOS Sans SC** is © Huawei Device Co., Ltd. and is redistributed unmodified with the app under the [HarmonyOS Sans Fonts License Agreement](Resources/Fonts/LICENSE_Fonts.txt). The font files are not modified, hence no subsetting, and are never distributed on their own. The full agreement is kept in the repository and compiled into the assembly: click the version number at the bottom of the settings window and choose "Show full agreement" to read it, and hovering the version shows the font credit. The dialog links the upstream text for cross-checking only.

---

[↑ Back to top](#wintips) · [中文版](README.md)
