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

A Windows 11 style floating overlay for the lock keys, Insert and clipboard
actions. Nothing is injected into other programs and no system functions are
hooked: it runs as a Windhawk tool mod in its own process.

![Caps Lock tip sliding in over the desktop](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-demo-dark-en.gif)

Toggle **Caps Lock**, **Num Lock**, **Scroll Lock** or press **Insert** and a small
card slides in from the screen edge it sits closest to: a padlock icon that
rotates and bounces, the state in words, and an accent bar along the bottom.
**Ctrl+C / Ctrl+V / Ctrl+X** show a check mark or scissors with a `×N` badge that
counts consecutive actions.

![Light theme, frosted card over a wallpaper](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-acrylic-light-en.png)

## Features

- **Real blurred backdrop.** The card blurs whatever is behind it, the way the
  Windows 11 OSD does, while keeping a per-pixel-alpha window so the whole card
  can still fade and slide. Switch *Card material* to Solid for an opaque card.
- **Slides in from the nearest edge.** A card within 15% of the left or right
  edge comes in horizontally (this wins over top/bottom), otherwise vertically
  from whichever half of the screen it sits in.
- **Follows the taskbar out of the way.** The card is anchored to the taskbar's
  live edge instead of the work area: with auto-hide on it lifts as the bar
  slides in and settles back to the screen edge as the bar retreats, so a
  taskbar that pops up mid-tip never covers it.
- **Clipboard aware.** Copy tips only appear after the clipboard actually
  changes, so `Ctrl+C` used to interrupt a console app doesn't pop a false
  "Copied". Non-keyboard copies (context menu, in-app buttons) can be reported
  too.
- **Elevated apps.** A 250 ms poll catches lock-key toggles made while an
  administrator window has focus, which a low-level keyboard hook can't see.
- **Follows Windows.** Per-monitor DPI, light/dark theme and the system accent
  color.
- **Doesn't get in the way.** Click-through, no focus stealing, not in the
  taskbar or Alt+Tab, fades out on its own.

![Paste tip with an ×3 badge](https://raw.githubusercontent.com/DoYouNatureQ/WinTips/main/Windhawk/preview/osd-paste-en.png)

## Settings

- **Caps Lock / Num Lock / Scroll Lock / Insert / Copy / Paste / Cut** — turn
  each tip on or off.
- **Also notify non-keyboard copies** — context menu copies, in-app "Copy"
  buttons and any other clipboard change that wasn't made with `Ctrl+C`.
- **Display duration** — how long the card stays, 500 to 10000 ms.
- **Animation speed** — 100 is normal (300 ms), 50 is twice as fast, 0 disables
  the animation.
- **Vertical / horizontal position** — above the taskbar (tracked live, so a bar
  that auto-hides never covers the card) or below the top edge; left, center or
  right.
- **Show on** — the monitor that holds the mouse cursor, or always the primary
  display.
- **Horizontal / vertical offset** — pixel-level fine tuning.
- **Theme** — follow Windows, dark or light.
- **Card material** — acrylic (blurred backdrop) or solid.
- **Blur radius** — 0 to 40 pixels, acrylic material only.
- **Accent color** — `#RRGGBB` for the status bar and the `×N` badge; leave
  empty to follow the system accent color.
- **Detect toggles under elevated apps** — the 250 ms poll described above
  (lock keys only).
- **Skip while a fullscreen app is in front** — games, fullscreen video and
  presentation mode.

## Things to know

- Runs as a **tool mod**: it keeps working when Explorer restarts, and it never
  loads into other processes.
- The standalone **WinTips.exe** does the same job. If it is already running,
  this mod stays quiet automatically, so both can be installed — but disable one
  of the two startup entries.
- Insert is event-based: Windows doesn't keep a global Insert state.
- The acrylic backdrop is a screen copy taken the moment the card appears. Behind
  an elevated window that copy can come back black, and the card falls back to
  solid for that tip instead of showing a black box.
- Placement is preset based; fine positioning uses the offset settings.

## Compared to Lock Keys Notifier

[Lock Keys Notifier](https://windhawk.net/mods/lock-keys-notifier) reports the
same lock keys, so the overlap is worth spelling out. The two aim at different
things:

- **Look.** This mod reproduces the Windows 11 OSD: the card blurs the desktop
  behind it, slides in from the nearest edge and animates a padlock. Lock Keys
  Notifier draws a configurable toast on a solid background.
- **Scope.** Clipboard tips have no equivalent there.
- **Configuration.** Here the size, theme and material follow Windows on
  purpose. Lock Keys Notifier is built around being configurable — layouts,
  fonts, shadows, sounds, per-key accents — so it isn't trying to match the
  native OSD.

If you want lock keys only, and want to tune every pixel of the card,
Lock Keys Notifier is the better fit. If you want the native Windows 11 look, or
clipboard tips, this mod is.

## Links

- Standalone version, source and issue tracker:
  [WinTips](https://github.com/DoYouNatureQ/WinTips)
