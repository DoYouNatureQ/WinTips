<!--
Windhawk 模组页面用的 readme 正文（英文，模组目录面向国际用户）。

用法：从「# WinTips OSD」这行开始整段复制进 wintips-osd.wh.cpp 的
      ==WindhawkModReadme== 两个标记之间的注释体；本段注释可一起复制，渲染时不可见。

硬性约束（官方 pr_validation 会检查）：
  · 图片只能是 Markdown 语法，host 仅限 raw.githubusercontent.com 或 i.imgur.com
  · 不能用 HTML 的 img 标签
  · 单张普通图片 < 2 MB；图片会被 CI 抓取归档，之后换图只能换新 URL/文件名
  · 下方 raw 链接指向本仓库 Windhawk/preview/ 下的文件，提交 PR 前必须先 push 到 main，
    否则 CI 取图返回 404 会直接报错
-->

# WinTips OSD

A Windows 11 style floating card for the lock keys, Insert and clipboard actions.
It runs as a Windhawk tool mod in its own process: nothing is injected into other
programs and no system functions are hooked.

![Caps Lock tip sliding in over the desktop](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-demo-dark-en.gif)

**Caps Lock**, **Num Lock**, **Scroll Lock** and **Insert** pop a small card out
of the nearest screen edge — a padlock that rotates and bounces, the state in
words, an accent bar. **Ctrl+C / V / X**, plus the classic **Ctrl+Insert**,
**Shift+Insert** and **Shift+Delete**, show a check mark or scissors with a `×N`
badge counting consecutive actions. **Win+Space**, **Ctrl+Shift**, **Alt+Shift**
and **Shift** inside an IME show a globe with the input language or the
Chinese/English mode.

![Light theme, frosted card over a wallpaper](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-acrylic-light-en.png)

## Features

- **Blurred backdrop, live.** The card blurs what is behind it, the way the
  Windows 11 OSD does, and keeps it refreshing (Windows 10 2004 and later) so
  video and scrolling pages move through as a soft wash instead of a frozen
  frame. *Card material → Solid* for an opaque card.
- **Windows 11 look, Windows 10 included.** The mod paints the card itself, so
  it needs neither system acrylic nor system rounded corners. Per-monitor DPI,
  light/dark theme and the system accent color are followed.
- **Stays in front.** A fullscreen game, recorder or launcher overlay that covers
  the card is noticed within 100 ms and the card is re-raised; when nothing
  covers it, no other window is touched.
- **Hugs the taskbar.** Anchored to the taskbar's live edge, so an auto-hiding
  bar never covers the card, and the card slides in from the nearest edge.
- **Clipboard aware.** Copy and cut tips wait for the clipboard to actually
  change, so a `Ctrl+C` that interrupts a console app and a permanent
  `Shift+Delete` stay quiet. Context-menu copies can be reported too.
- **Works under elevated windows.** Windows stops calling the keyboard hook while
  an administrator window has focus; the keys then come from Raw Input, which is
  delivered per device and ignores both focus and integrity level. A key-state
  poll stays behind it for anything Raw Input cannot see, such as a remote or a
  virtual keyboard. See below for what does not work there.
- **Light on resources.** Nothing runs while the card is hidden. A frame takes
  about 0.5 ms at 100% size; the live backdrop and the topmost guard only tick
  while a card is up.

![Paste tip with an ×3 badge](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-paste-en.png)

## Settings

- **Tips** — Caps Lock, Num Lock, Scroll Lock, Insert, Copy, Paste and Cut, each
  on or off. *Also notify non-keyboard copies* adds context-menu and in-app
  copies.
- **Input language & Chinese/English tip** — off by default; shows the globe card
  after a language switch. Set it back to off if it fires more often than it
  helps.
- **Behaviour** — *Display duration* 500–10000 ms; *Animation speed* 100 is
  normal (300 ms), 0 disables it; *Skip while a fullscreen app is in front*.
- **Placement** — *Vertical* above the taskbar or below the top edge,
  *Horizontal* left, center or right, *Show on* the active window's display
  (default), the mouse's display or the primary one, plus pixel offsets. The
  taskbar edge is tracked live.
- **Look** — *Theme* follow Windows, dark or light; *Card material* acrylic or
  solid; *Tint opacity* 20–90% (55% by default, acrylic only); *Blur radius*
  0–40 px; *Accent color* `#RRGGBB`, empty for the system accent; *Card size*
  60–200%, gap to the screen edge unchanged.
- **Language** — language of the text on the card: Follow Windows (eight UI
  languages built in, English for anything else) or pin one.

## Things to know

- Runs as a **tool mod**: it keeps working when Explorer restarts and never loads
  into other processes. The standalone **WinTips.exe** does the same job and the
  mod stays quiet while it runs, so both can be installed — but keep only one of
  the two startup entries.
- Insert is event-based: Windows keeps no global Insert state, and only a bare
  Insert counts (with Ctrl or Shift held it is a copy or a paste).
- An **exclusive-fullscreen** game covers the card, since that mode owns the
  display pipeline. Borderless and windowed games are fine.
- Behind an **elevated window** the backdrop grab comes back black, so that tip
  falls back to a solid card. The language is still reported there, the IME mode
  is not.
- The language card needs a keystroke to hang on: switching with the mouse from
  the language bar or an IME status window shows nothing.
- Without the live-refresh capability the backdrop is a snapshot taken when the
  card appeared; sliding in and following the taskbar still work, because the
  grab includes a margin around the card.

## Compared to Lock Keys Notifier

[Lock Keys Notifier](https://windhawk.net/mods/lock-keys-notifier) reports the
same lock keys but aims elsewhere: it draws a configurable toast on a solid
background, while this mod reproduces the native Windows 11 OSD and adds
clipboard tips. Want to tune every pixel of the card — layouts, fonts, shadows,
sounds? Lock Keys Notifier fits better. Want the native look or clipboard tips?
This one.

## Links

- Standalone version, source and issue tracker:
  [WinTips](https://github.com/DoYouNatureQ/WinTips)

## License

GPL-3.0-or-later, as the standalone WinTips app. The look and motion take
reference from the LockWindow design in FluentFlyout (also GPL-3.0); the code
here is an independent implementation, and redistributed versions must ship the
complete corresponding source under the GPL.
