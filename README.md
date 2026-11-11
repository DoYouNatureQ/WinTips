<div align="center">

<sub>A Windows 11-style Caps / Num / Scroll Lock indicator with an acrylic flyout. <a href="README.en.md">Read in English</a></sub>

# WinTips

[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)](LICENSE) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square) ![Windows](https://img.shields.io/badge/Windows-11-0078D4?style=flat-square) ![Dependencies](https://img.shields.io/badge/dependencies-none-4B5563?style=flat-square)

**简体中文** | [English](README.en.md)

![WinTips 宣传图](promo/wintips-promo.png)

</div>

Windows 11 风格的键盘状态提示工具。按下 **Caps Lock / Num Lock / Scroll Lock**，或复制、粘贴、剪切，或切换输入语言与输入法内部模式——任务栏上方就会滑出 Win11 质感的亚克力浮窗。

## 特性

### 提示内容

- **锁键与剪贴板**：Caps / Num / Scroll Lock 与 Insert 一按即显；复制、粘贴、剪切弹出对勾或剪刀图标，「×N」徽章记录连续操作。挂锁随开关旋转回弹，下缘一条强调色指示条。
- **输入法**（默认关闭）：换输入语言、或切换输入法内部模式后，浮窗显示切换后的语言或模式。

### 外观与行为

- **Win11 原生质感**：DWM 亚克力、系统圆角、深浅色与强调色全部跟随系统，高 DPI 自适应；Win10 改用桌面快照自模糊的玻璃配自绘圆角，高对比度或关掉透明效果时退回不透明纯色。「模糊效果」开着时浮窗会边显示边重抓桌面，玻璃里的画面跟着背后的内容一起动。
- **滑入方向跟着位置走**：贴哪条边就从哪条边滑入、也从那条边滑出，左右优先于上下；落在屏幕中部则改为竖向滑入。
- **不打扰**：点击穿透、不抢焦点、不进任务栏和 Alt+Tab，1–5 秒后自动淡出。

### 设置与适配

- **设置窗口**：Win11 设置风格，即改即存。位置分垂直、水平两个轴，也可以把浮窗拖到任意处保存，一键还原默认；自定义时用虚线标出贴边判定区，方向键可逐像素微调。
- **多显示器**：默认跟随焦点显示器，按该屏自己的 DPI 与任务栏形态摆放。
- **语言与字体**：内置八种界面语言，默认跟随系统，也可固定任意一种；**HarmonyOS Sans SC** 内嵌在 exe 里，装没装字体都长一个样，也能换成系统已装的任意字体。
- 托盘常驻、单实例、无第三方依赖，基于 WPF + WinForms 与 .NET 10。

## 使用

直接运行 `WinTips.exe`，托盘出现图标即在工作。

- 首次启动会弹一次「已启用」浮窗。程序没有主窗口，这条提示既说明它已经在后台工作，也指出托盘图标的位置，之后永不再弹。
- 托盘左键打开设置，右键打开菜单，菜单里有测试提示。
- 命令行：`--settings` 打开设置；`--test caps on 4` 测试提示，键位名也可以用 `lang`；`--test menu` 预览菜单。
- 设置保存在 `%APPDATA%\WinTips\settings.json`。

## 已知限制

- Insert 提示为事件式，不显示开/关状态，因为 Windows 不在系统层维护该状态。只有单独按 Insert 才算：按住 Ctrl 或 Shift 时它其实是复制或粘贴。
- 多显示器下提示默认跟着当前活动窗口走，也就是落在同一块屏上；想固定在主显示器，关掉设置里的「跟随焦点显示器」。
- 提权（管理员）窗口获得焦点时，按键改由轮询兜底：系统不会把发往高完整性级别窗口的按键回调给普通进程的键盘钩子，所以那种窗口里 Caps/Num/Scroll/Insert 由按键状态轮询驱动，照常提示，代价是最多晚 50 ms 出现。输入法的中英文模式此时读不到（那要往提权窗口发消息），只提示语言、不提示模式。
- 独占全屏（真全屏，不是无边框全屏）的游戏里浮窗画不出来：那种模式下画面被显示管线独占，系统不允许任何其它窗口覆盖上去。无边框全屏或窗口化都正常。
- 非键盘来源的剪切，比如右键菜单里的“剪切”，无法与复制区分，会以复制样式提示。
- 输入法模式提示依赖输入法报告转换模式。多数输入法可用，个别第三方输入法不响应，此时模式切换不提示。
- 用**鼠标**点语言栏或输入法状态条切换时不提示：那种切换没有任何按键可依附，也收不到系统通知。
- 开着「模糊效果」时，浮窗在显示的那一两秒里对截屏/录屏是隐形的（靠 Windows 的 `WDA_EXCLUDEFROMCAPTURE` 把自己排除出抓屏，否则每次重抓到的都是浮窗自己）。截图、录屏、投屏里看不到这一闪而过的提示。远程桌面会话或旧版 Windows 10 上做不到实时刷新，退回弹出前抓一次，此时会正常出现在截图里。

## 许可

本项目以 **GNU GPL-3.0** 发布，全文见 [LICENSE](LICENSE)。

## 致谢

浮窗外观与动效复刻自 [FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的 LockWindow 设计，同样是 GPL-3.0；本项目代码为独立实现，许可与上游兼容。

界面字体 **HarmonyOS Sans SC** 版权归华为终端有限公司，按 [HarmonyOS Sans 字体许可协议](Resources/Fonts/LICENSE_Fonts.txt) 随程序原样分发。字体文件不做任何修改，因此没有做子集裁剪，也不单独分发；协议全文随仓库保留并打进程序集，设置页点底部版本号，在弹窗里点「查看协议全文」即可读到，悬停版本号还能看到字体出处。弹窗里另附官方原文链接，仅作核对。

---

[↑ 回到顶部](#wintips) · [English version](README.en.md)
