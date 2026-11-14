<div align="center">

<img src="Resources/app-icon-512.png" width="104" alt="WinTips">

# WinTips

Windows 键盘状态提示工具

[![Release](https://img.shields.io/github/v/release/DoYouNatureQ/WinTips?style=flat-square&color=0078D4&label=release)](https://github.com/DoYouNatureQ/WinTips/releases)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?style=flat-square)](#安装)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/download)

简体中文 | [English](README.en.md)

</div>

<img src="promo/wintips-promo.png" width="100%" alt="WinTips 浮窗与大写锁定提示">

按下 Caps Lock、Num Lock、Scroll Lock、Insert，或复制、粘贴、剪切时，WinTips 从屏幕边缘滑出一个亚克力浮窗报告状态，随后自动淡出。程序常驻托盘，无主窗口、不联网、无第三方依赖。

## 功能

- **锁定键** — Caps Lock、Num Lock、Scroll Lock 切换时显示开关状态
- **Insert** — 单独按下时提示（Windows 不维护该键的开关状态）
- **剪贴板** — Ctrl+C / V / X 显示图标，连续操作以 `×N` 计数
- **输入法**（默认关闭）— 切换输入语言或中英文模式后显示结果
- **外观** — 跟随系统深浅色、强调色与 DPI；大小、不透明度、动画速度、显示时长可调
- **定位** — 两轴吸附，也可拖拽至任意位置；默认跟随焦点显示器
- **界面** — 八种界面语言；内置 HarmonyOS Sans SC，可换用系统字体

## 安装

支持 Windows 10 1809 及以上与 Windows 11，仅 x64。发布版为自包含单文件，无需 .NET 运行时，也不需要管理员权限。

- **Microsoft Store**：[apps.microsoft.com/detail/9nj5xdw5pd79](https://apps.microsoft.com/detail/9nj5xdw5pd79)，由商店负责更新。
- **安装包**：从 [Releases](https://github.com/DoYouNatureQ/WinTips/releases/latest) 下载 `WinTipsSetup-<版本>.exe`，按当前用户安装，不触发 UAC。
- **便携版**：单个 `WinTips.exe`，放到任意目录运行。
- **Windhawk 模组**：[WinTips OSD](https://windhawk.net/mods/wintips-osd)，详见 [Windhawk/README.md](Windhawk/README.md)。与独立版不要同时设置开机启动。

## 使用

启动后只显示托盘图标：左键打开设置，右键打开菜单。设置窗口底部可检查更新，有新版本时红点提示，检查过后显示「已是最新版本」；后台每 24 小时静默检查一次。

命令行参数：

| 参数 | 说明 |
| --- | --- |
| `--settings` | 启动后直接打开设置窗口 |
| `--test <键位> [on\|off] [秒数]` | 预览提示，键位取 `caps`、`num`、`scroll`、`insert`、`copy`、`paste`、`cut`、`lang` |

所有选项在设置窗口中即改即存，无需重启。设置保存在 `%APPDATA%\WinTips\settings.json`（升级自动迁移），异常日志写入同目录的 `error.log`。

## 已知限制

| 限制 | 说明 |
| --- | --- |
| 提权窗口 | 钩子收不到按键，改由 Raw Input 与轮询兜底，最多延迟约 50 ms；此时输入法提示不弹，复制 / 粘贴 / 剪切由轮询识别组合键 |
| 输入法 | 优先跟随系统上报的转换模式；不规范的第三方输入法改按「Shift 单独按下并抬起」推断，连按两次不提示；切换键不是 Shift 或用鼠标点语言栏时不提示 |
| 独占全屏 | 真独占全屏游戏浮窗画不出来；无边框全屏与窗口化不受影响 |
| 截图与录屏 | 开启模糊时浮窗显示期间不被截屏捕获；远程桌面等退回单次抓取的场景会正常出现 |
| 非键盘剪切 | 右键菜单中的「剪切」无法与复制区分，按复制样式提示 |
| 多显示器 | 默认落在活动窗口所在显示器，需固定到主显示器时关闭「跟随焦点显示器」 |

## 许可与致谢

本项目以 **GNU General Public License v3.0** 发布，全文见 [LICENSE](LICENSE)。浮窗外观与动效参考 [FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的 LockWindow 设计（同为 GPL-3.0，本项目为独立实现）。界面字体 **HarmonyOS Sans SC** 版权归华为终端有限公司所有，依[字体许可协议](Resources/Fonts/LICENSE_Fonts.txt)随程序原样分发。

问题反馈请在 [Issues](https://github.com/DoYouNatureQ/WinTips/issues) 中说明 Windows 版本与复现步骤，并附上 `%APPDATA%\WinTips\error.log`（如存在）。

---

[↑ 回到顶部](#wintips) · [English version](README.en.md)
