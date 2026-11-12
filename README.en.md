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

| Area | Description |
| --- | --- |
| Lock keys | Reports the on/off state of Caps Lock, Num Lock and Scroll Lock |
| Insert | Reports a single press as an event (Windows does not track an on/off state for this key) |
| Clipboard | Shows an icon for Ctrl+C, Ctrl+V and Ctrl+X, with an `×N` badge counting consecutive actions |
| Input method (off by default) | Reports the input language or the Chinese/English mode after a switch |
| Appearance | Follows the system theme, accent color and DPI; size, opacity, animation speed and display duration are adjustable |
| Placement | Snaps on two axes and slides in from the edge it sits against; can also be dragged anywhere |
| Multiple displays | Follows the focused display by default, using that display's DPI and taskbar state |
| UI | Eight languages; HarmonyOS Sans SC embedded, or any installed font |
| Runtime | Tray-resident, single instance, click-through, never steals focus, absent from the taskbar and Alt+Tab |

## Installation

Supports Windows 10 1809 and later, and Windows 11; x64 only. Release builds are self-contained single files, so no .NET runtime is required, and no administrator rights are needed.

- **Microsoft Store**: [apps.microsoft.com/detail/9nj5xdw5pd79](https://apps.microsoft.com/detail/9nj5xdw5pd79); updates are delivered by the Store.
- **Installer**: download `WinTipsSetup-<version>.exe` from [Releases](https://github.com/DoYouNatureQ/WinTips/releases/latest). It installs per user into `%LOCALAPPDATA%\Programs\WinTips` without a UAC prompt.
- **Portable**: the release artifact is a single `WinTips.exe`; run it from any directory.
- **Windhawk mod**: [WinTips OSD](https://windhawk.net/mods/wintips-osd) runs in its own `windhawk.exe` process and injects nothing into other programs. Do not set it and the standalone build to launch at startup together.

## Usage

The app shows only a tray icon. Left-click opens Settings; right-click opens the menu (Settings, Test tips, Exit); the first launch shows a one-time notice that never appears again.

Command line:

| Argument | Description |
| --- | --- |
| `--settings` | Opens Settings after startup |
| `--test <key> [on\|off] [seconds]` | Previews a tip; `<key>` is `caps`, `num`, `scroll`, `insert`, `copy`, `paste`, `cut` or `lang` |
| `--test menu` | Previews the tray menu at the cursor position |

## Configuration

Every option is set in the Settings window and saved as you change it; no restart is required. Settings are stored in `%APPDATA%\WinTips\settings.json` (versioned, migrated automatically on upgrade), and exceptions are logged to `error.log` in the same directory.

- **Content**: each lock key, Insert, copy / paste / cut, non-keyboard copies, input language and Chinese/English mode.
- **Appearance**: blur on/off and its opacity, flyout size, animation speed, display duration (0.5–5 s).
- **Placement**: snapping on the vertical and horizontal axes, or a saved custom position; whether to follow the focused display.
- **Other**: launch at startup, UI language (eight, following the system by default), UI font.

## Implementation notes

- **Three capture paths**: a low-level keyboard hook handles the normal case; while an elevated window has focus, UIPI truncates those callbacks and Raw Input reads the keys instead; key-state polling covers the rest. All three events are de-duplicated by the time the key was pressed, so a press is never toggled twice.
- **Glass and background**: Windows 11 uses DWM acrylic, Windows 10 captures and blurs the desktop itself. With blur on, the flyout excludes itself from capture via `WDA_EXCLUDEFROMCAPTURE` and keeps re-capturing so the glass tracks what is behind it.
- **Footprint**: the tray icon uses native `Shell_NotifyIcon` and WinForms is disabled, removing about 19.8 MB of resident assemblies; single-file compression is off, taking the private working set from 83.5 MB to 38.8 MB.
- **Layout**: `App/` (startup and wiring), `Core/` (settings, localization, lock-key state machine), `UI/` (WPF windows), `Native/` (DWM, desktop capture, Raw Input), `Windhawk/` (mod variant).

## Known limitations

| Limitation | Explanation |
| --- | --- |
| Elevated windows | The low-level hook receives nothing while an elevated window is in the foreground; keys fall back to Raw Input and polling, with up to about 50 ms extra latency. Copy / paste / cut are covered by the polling fallback recognizing the key combos; the input-method tip depends on the hook and does not appear |
| Input methods | Depends on the IME reporting its conversion mode; a few third-party IMEs do not. Switching from the language bar with the mouse produces no tip |
| Exclusive fullscreen | True exclusive fullscreen owns the display pipeline and the flyout cannot be drawn; borderless and windowed modes are fine |
| Screenshots | With blur on, the flyout is excluded from capture for the second or two it is visible; Remote Desktop and older Windows 10 builds fall back to a single capture, in which case it does show up |
| Non-keyboard cut | The "Cut" context-menu item cannot be told apart from a copy and uses the copy style |
| Multiple displays | Tips land on the active window's display by default; disable "Follow the focused display" to keep them on the primary display |

## License & credits

WinTips is released under the **GNU General Public License v3.0**; see [LICENSE](LICENSE) for the full text. The flyout's look and motion are based on the LockWindow design in [FluentFlyout](https://github.com/unchihugo/FluentFlyout) (also GPL-3.0; this is an independent implementation). The UI font **HarmonyOS Sans SC** is © Huawei Device Co., Ltd. and is redistributed unmodified under the [font license agreement](Resources/Fonts/LICENSE_Fonts.txt).

For bug reports, open an [issue](https://github.com/DoYouNatureQ/WinTips/issues) with your Windows version, the steps to reproduce, and `%APPDATA%\WinTips\error.log` if it exists.

---

[↑ Back to top](#wintips) · [中文版](README.md)
