<div align="center">

# WinTips

[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)](LICENSE) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square) ![Windows](https://img.shields.io/badge/Windows-11-0078D4?style=flat-square) ![Dependencies](https://img.shields.io/badge/dependencies-none-4B5563?style=flat-square)

[简体中文](README.md) | **English**

![WinTips promo banner](promo/wintips-promo-en.png)

</div>

A Windows 11-style keyboard state indicator. Press **Caps Lock / Num Lock / Scroll Lock**, copy, paste or cut, or switch the input language or an IME's input mode — an acrylic flyout with Win11 polish slides in just above the taskbar.

## Features

### What it shows

- **Lock keys & clipboard**: Caps / Num / Scroll Lock and Insert show on each press; copy, paste and cut get a checkmark or scissors icon, with an "×N" badge for repeats. The padlock spins and springs back, and an accent bar runs along the bottom edge.
- **Input hints** (off by default): names the input language you switched to, or the IME mode you landed in.

### Look & behavior

- **Native Windows 11 look**: acrylic, system corners, theme and accent color all follow the system; Windows 10 gets self-blurred glass, and high contrast or transparency off falls back to solid colors. With blur on, the flyout keeps re-capturing the desktop while it is visible, so the glass tracks whatever moves behind it.
- **Edge-aware slide**: the flyout enters and leaves from the screen edge it hugs, left/right outranking top/bottom; mid-screen it slides vertically.
- **Unobtrusive**: click-through, never steals focus, absent from the taskbar and Alt+Tab, fades after 1–5 seconds.

### Settings & fit

- **Settings window**: Win11 styling, saved as you change it. Place the flyout on two axes or drag it anywhere and save, with one click back to the default; dashed guides mark the edge zones and the arrow keys nudge it pixel by pixel.
- **Multiple displays**: follows the focused display, positioned with that display's own DPI and taskbar state.
- **Language & font**: eight UI languages follow the system or pin to one; **HarmonyOS Sans SC** is embedded in the exe, and any installed family can be swapped in.
- Tray-resident, single instance, no third-party dependencies — WPF + WinForms, .NET 10.

## Usage

Just run `WinTips.exe` — once the tray icon appears, it is working.

- The very first launch shows a one-time "is running" flyout, since the app has no main window: it both confirms the app is running in the background and points at the tray icon. It never appears again.
- Tray left-click opens Settings; right-click opens a menu with the test notifications.
- Command line: `--settings` opens Settings; `--test caps on 4` previews a notification, with `lang` accepted as the key name; `--test menu` previews the menu.
- Settings are stored in `%APPDATA%\WinTips\settings.json`.

## Known Limitations

- The Insert hint is event-based and shows no on/off state, since Windows does not track that state at the system level. Only a bare Insert counts: with Ctrl or Shift held it is a copy or a paste instead.
- With several displays the flyout follows the active window's display by default; turn off "Follow the focused display" in the settings to keep it on the primary display.
- While an elevated (administrator) window has focus, keys fall back to polling: Windows does not deliver keys destined for a higher-integrity window to a normal process's keyboard hook, so Caps/Num/Scroll/Insert are picked up by polling key state instead. They still show, at the cost of up to 50 ms extra latency. The IME's Chinese/English mode cannot be read in that state (that needs messaging an elevated window), so only the language is shown.
- The flyout cannot be drawn over games running in true exclusive fullscreen: that mode owns the picture and Windows will not let any other window cover it. Borderless fullscreen and windowed mode are fine.
- Cuts from non-keyboard sources, such as the "Cut" context-menu item, cannot be told apart from copies and are shown with the copy style.
- The IME mode hint relies on the IME reporting its conversion mode. Most IMEs do; a few third-party ones do not respond and their mode switch shows no hint.
- Switching with the **mouse** — the language bar or the IME status bar — shows no hint: that kind of change has no keystroke to hang on and sends no system notification.
- With blur on, the flyout is invisible to screenshots and screen recordings for the second or two it is on screen: it opts out of capture via `WDA_EXCLUDEFROMCAPTURE`, without which every re-capture would photograph the flyout itself. Remote Desktop sessions and older Windows 10 builds cannot do the live refresh and fall back to a single capture taken before the flyout appears, in which case it does show up in screenshots.

## License

WinTips is released under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for the full text.

## Credits

The flyout's look and motion are recreated from the LockWindow design in [FluentFlyout](https://github.com/unchihugo/FluentFlyout), also GPL-3.0; the code here is an independent implementation. WinTips itself is GPL-3.0 too, so the two are license-compatible.

The UI font **HarmonyOS Sans SC** is © Huawei Device Co., Ltd. and is redistributed unmodified with the app under the [HarmonyOS Sans Fonts License Agreement](Resources/Fonts/LICENSE_Fonts.txt). The font files are not modified, hence no subsetting, and are never distributed on their own. The full agreement is kept in the repository and compiled into the assembly: click the version number at the bottom of the settings window and choose "Show full agreement" to read it, and hovering the version shows the font credit. The dialog links the upstream text for cross-checking only.

---

[↑ Back to top](#wintips) · [中文版](README.md)
