<div align="center">

<img src="Resources/app-icon-512.png" width="104" alt="WinTips">

# WinTips

A keyboard state indicator for Windows

[![Release](https://img.shields.io/github/v/release/DoYouNatureQ/WinTips?style=flat-square&color=0078D4&label=release)](https://github.com/DoYouNatureQ/WinTips/releases)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?style=flat-square)](#installation)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/download)

[简体中文](README.md) | English

</div>

<img src="promo/wintips-promo-en.png" width="100%" alt="WinTips flyout showing Caps Lock On">

When you press Caps Lock, Num Lock, Scroll Lock or Insert, or copy, paste or cut, WinTips slides an acrylic flyout in from the screen edge and reports the key state or the action, then fades out on its own. The app lives in the tray, has no main window, makes no network requests, and has no third-party dependencies.

## Features

- **Lock keys** — reports the on/off state of Caps Lock, Num Lock and Scroll Lock
- **Insert** — reports a single press as an event (Windows does not track an on/off state for this key)
- **Clipboard** — shows an icon for Ctrl+C / V / X, with an `×N` badge counting consecutive actions
- **Input method** (off by default) — reports the input language or the Chinese/English mode after a switch
- **Appearance** — follows the system theme, accent color and DPI; size, opacity, animation speed and duration are adjustable
- **Placement** — snaps on two axes, or can be dragged anywhere; follows the focused display by default
- **UI** — eight languages; HarmonyOS Sans SC embedded, or any installed font

## Installation

Supports Windows 10 1809 and later, and Windows 11; x64 only. Release builds are self-contained single files, so no .NET runtime is required and no administrator rights are needed.

- **Microsoft Store**: [apps.microsoft.com/detail/9nj5xdw5pd79](https://apps.microsoft.com/detail/9nj5xdw5pd79); updates are delivered by the Store.
- **Installer**: download `WinTipsSetup-<version>.exe` from [Releases](https://github.com/DoYouNatureQ/WinTips/releases/latest); installs per user, no UAC prompt.
- **Portable**: a single `WinTips.exe`; run it from any directory.
- **Windhawk mod**: [WinTips OSD](https://windhawk.net/mods/wintips-osd), see [Windhawk/README.md](Windhawk/README.md). Do not set it and the standalone build to launch at startup together.

## Usage

The app shows only a tray icon: left-click opens Settings, right-click opens the menu. The bottom of the Settings window checks for updates — a red dot marks a new version, and once a check has run it reads "Up to date"; a background check runs every 24 hours.

Command line:

| Argument | Description |
| --- | --- |
| `--settings` | Opens Settings after startup |
| `--test <key> [on\|off] [seconds]` | Previews a tip; `<key>` is `caps`, `num`, `scroll`, `insert`, `copy`, `paste`, `cut` or `lang` |

Every option is set in the Settings window and saved as you change it; no restart is required. Settings are stored in `%APPDATA%\WinTips\settings.json` (migrated automatically on upgrade), and exceptions are logged to `error.log` in the same directory.

## Known limitations

| Limitation | Explanation |
| --- | --- |
| Elevated windows | The hook receives nothing; keys fall back to Raw Input and polling with up to about 50 ms extra latency. The input-method tip does not appear there; copy / paste / cut are covered by the polling fallback |
| Input methods | Chinese/English toggles follow the conversion mode the IME reports. Third-party IMEs that never report it are inferred from a solo Shift press-and-release; pressing Shift twice in a row produces no tip. No tip when the toggle key is not Shift, or when switching from the language bar with the mouse |
| Exclusive fullscreen | True exclusive fullscreen owns the display pipeline and the flyout cannot be drawn; borderless and windowed modes are fine |
| Screenshots | With blur on, the flyout is excluded from capture while visible; Remote Desktop and similar one-shot-capture cases show it normally |
| Non-keyboard cut | The "Cut" context-menu item cannot be told apart from a copy and uses the copy style |
| Multiple displays | Tips land on the active window's display by default; disable "Follow the focused display" to keep them on the primary display |

## License & credits

WinTips is released under the **GNU General Public License v3.0**; see [LICENSE](LICENSE) for the full text. The flyout's look and motion are based on the LockWindow design in [FluentFlyout](https://github.com/unchihugo/FluentFlyout) (also GPL-3.0; this is an independent implementation). The UI font **HarmonyOS Sans SC** is © Huawei Device Co., Ltd. and is redistributed unmodified under the [font license agreement](Resources/Fonts/LICENSE_Fonts.txt).

For bug reports, open an [issue](https://github.com/DoYouNatureQ/WinTips/issues) with your Windows version, the steps to reproduce, and `%APPDATA%\WinTips\error.log` if it exists.

---

[↑ Back to top](#wintips) · [中文版](README.md)
