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
badge counting consecutive actions. **Win+Space**, **Ctrl+Shift**,
**Alt+Shift** and **Shift** inside an IME show a globe with the input language
or the Chinese/English mode; IMEs that never report their mode to the system
(a good number of third-party builds) are tracked by the solo-Shift gesture
instead.

![Light theme, frosted card over a wallpaper](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-acrylic-light-en.png)

## Features

- **Blurred backdrop, live.** The card blurs what is behind it and keeps it
  refreshing (Windows 10 2004 and later), so video and scrolling pages move
  through as a soft wash, with the same saturation lift the system acrylic
  applies. *Card material → Solid* for an opaque card.
- **Windows 11 look, Windows 10 included.** The mod paints the card itself, so
  it needs neither system acrylic nor rounded corners. Per-monitor DPI,
  light/dark theme and the accent color are followed; labels are drawn by
  DirectWrite with a GDI+ fallback.
- **Stays in front.** Anything covering the card is noticed within 100 ms and
  the card is re-raised; when nothing covers it, no other window is touched.
- **Hugs the taskbar.** Anchored to the taskbar's live edge, so an auto-hiding
  bar never covers the card.
- **Clipboard aware.** Copy and cut tips wait for the clipboard to actually
  change, so a stray `Ctrl+C` in a console app stays quiet. Context-menu copies
  can be reported too.
- **Works under elevated windows.** While an administrator window has focus,
  the lock keys and Insert come from Raw Input, with a key-state poll behind it
  for anything Raw Input cannot see.
- **Light on resources.** Nothing runs while the card is hidden.

![Paste tip with an ×3 badge](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-paste-en.png)

## Settings

- **Tips** — Caps Lock, Num Lock, Scroll Lock, Insert, Copy, Paste and Cut,
  each on or off. *Also notify non-keyboard copies*; *Show the ×N count badge*.
- **Input language & Chinese/English tip** — off by default. *Tip when
  switching windows* covers Windows' "Let me use a different input method for
  each app window" option.
- **Behaviour** — display duration 500–10000 ms; animation speed (100 is
  normal, 0 disables it); skip while a fullscreen app is in front.
- **Placement** — vertical and horizontal anchors, which display to show on,
  and pixel offsets; the taskbar edge is tracked live.
- **Look** — theme, card material, tint opacity 20–90%, blur radius 0–40 px,
  accent color `#RRGGBB`, card size 60–200%.
- **Language** — the text on the card: follow Windows (eight UI languages
  built in) or pin one.

## Things to know

- Runs as a **tool mod**: never loads into other processes. The standalone
  **WinTips.exe** does the same job and the mod stays quiet while it runs —
  but keep only one of the two startup entries.
- Insert is event-based; only a bare Insert counts (with Ctrl or Shift held it
  is a copy or a paste).
- Copy, paste and cut are keyed to Ctrl (+ Shift), never to combos that also
  hold **Win**.
- An **exclusive-fullscreen** game covers the card; borderless and windowed
  games are fine.
- Behind an **elevated window** the backdrop grab comes back black (solid card
  fallback) and copy / paste / cut are not reported either.
- The language card needs a keystroke or a window switch to hang on; switching
  with the mouse from the language bar shows nothing.
- The card is **left out of screen capture** while the live backdrop runs
  (`WDA_EXCLUDEFROMCAPTURE`); pick *Card material → Solid* if you want it in a
  recording.
- Without the live-refresh capability the backdrop is a snapshot taken when the
  card appeared.

## Compared to Lock Keys Notifier

[Lock Keys Notifier](https://windhawk.net/mods/lock-keys-notifier) draws a
configurable toast on a solid background — tune every pixel of it if you like.
This mod reproduces the native Windows 11 OSD and adds clipboard tips.

## Links

- Standalone version, source and issue tracker:
  [WinTips](https://github.com/DoYouNatureQ/WinTips)

## License

GPL-3.0-or-later, as the standalone WinTips app. The look and motion take
reference from the LockWindow design in FluentFlyout (also GPL-3.0); the code
here is an independent implementation.
