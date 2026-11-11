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

按下 Caps Lock、Num Lock、Scroll Lock、Insert，或执行复制、粘贴、剪切时，WinTips 在屏幕边缘滑出一个亚克力质感的浮窗，报告当前按键状态或操作，随后自动淡出。程序常驻托盘，无主窗口、不联网、无第三方依赖。

## 功能

| 类别 | 说明 |
| --- | --- |
| 锁定键 | Caps Lock、Num Lock、Scroll Lock 切换时显示开关状态 |
| Insert | 单独按下时显示事件提示（Windows 不维护该键的开关状态） |
| 剪贴板 | Ctrl+C / Ctrl+V / Ctrl+X 显示对应图标，连续操作以 `×N` 计数 |
| 输入法（默认关闭） | 切换输入语言或输入法内部中英文模式后显示结果 |
| 外观 | 跟随系统深浅色、强调色与 DPI；尺寸、不透明度、动画速度、显示时长可调 |
| 定位 | 两轴吸附，滑入方向随所贴边缘变化；也可拖拽至任意位置 |
| 多显示器 | 默认跟随焦点显示器，按该屏的 DPI 与任务栏状态定位 |
| 界面 | 八种界面语言；内置 HarmonyOS Sans SC，可换用系统字体 |
| 运行 | 托盘常驻、单实例、点击穿透、不抢焦点、不进任务栏与 Alt+Tab |

## 安装

支持 Windows 10 1809 及以上与 Windows 11，仅 x64。发布版本为自包含单文件，无需安装 .NET 运行时，也不需要管理员权限。

- **Microsoft Store**：[apps.microsoft.com/detail/9nj5xdw5pd79](https://apps.microsoft.com/detail/9nj5xdw5pd79)，由商店负责更新。
- **安装包**：从 [Releases](https://github.com/DoYouNatureQ/WinTips/releases/latest) 下载 `WinTipsSetup-<版本>.exe`。按当前用户安装至 `%LOCALAPPDATA%\Programs\WinTips`，不触发 UAC。
- **便携版**：发布产物即为单个 `WinTips.exe`，放到任意目录运行即可。
- **Windhawk 模组**：[WinTips OSD](https://windhawk.net/mods/wintips-osd) 运行在独立的 `windhawk.exe` 进程中，不注入其他程序。与独立版不应同时设置开机启动。

## 使用

程序启动后只显示托盘图标。左键打开设置，右键打开菜单（设置、测试提示、退出）；首次启动会弹出一次说明提示，此后不再出现。

命令行参数：

| 参数 | 说明 |
| --- | --- |
| `--settings` | 启动后直接打开设置窗口 |
| `--test <键位> [on\|off] [秒数]` | 预览提示，键位取 `caps`、`num`、`scroll`、`insert`、`copy`、`paste`、`cut`、`lang` |
| `--test menu` | 在光标位置预览托盘菜单 |

## 配置

所有选项都在设置窗口中调整，即改即存，无需重启。设置保存在 `%APPDATA%\WinTips\settings.json`（带版本号，升级自动迁移），异常日志写入同目录的 `error.log`。

- **提示内容**：各锁定键、Insert、复制 / 粘贴 / 剪切、非键盘复制、输入法语言与中英文。
- **外观**：模糊效果开关与不透明度、提示大小、动画速度、显示时长（0.5–5 秒）。
- **位置**：垂直与水平两轴吸附，或拖拽保存自定义位置；是否跟随焦点显示器。
- **其他**：开机启动、界面语言（八种，默认跟随系统）、界面字体。

## 技术要点

- **三路按键捕获**：常规由低级键盘钩子捕获；前台为提权窗口时 UIPI 会截断回调，改由 Raw Input 读取；两者均不可用时由状态轮询兜底。三路事件统一按按键发生时刻去重，避免重复翻转。
- **玻璃与背景**：Windows 11 使用 DWM 亚克力，Windows 10 抓取桌面快照自行模糊；开启模糊时浮窗以 `WDA_EXCLUDEFROMCAPTURE` 排除自身抓屏，并持续重抓以同步背后画面。
- **资源占用**：托盘图标使用原生 `Shell_NotifyIcon` 并禁用 WinForms，减少约 19.8 MB 常驻程序集；单文件不做压缩，私有工作集由 83.5 MB 降至 38.8 MB。
- **代码分布**：`App/`（启动与装配）、`Core/`（设置、本地化、锁定键状态机）、`UI/`（WPF 窗口）、`Native/`（DWM、桌面快照、Raw Input）、`Windhawk/`（模组版本）。

## 已知限制

| 限制 | 说明 |
| --- | --- |
| 提权窗口 | 前台为提权窗口时改由 Raw Input 与轮询捕获，最多延迟约 50 ms，且只提示输入语言、不提示中英文模式 |
| 输入法 | 依赖输入法上报转换模式，个别第三方输入法不上报；通过语言栏或状态条用鼠标切换时不提示 |
| 独占全屏 | 真独占全屏游戏会独占显示管线，浮窗无法绘制；无边框全屏与窗口化不受影响 |
| 截图与录屏 | 开启模糊效果时，浮窗在显示的一两秒内不会被截屏捕获；远程桌面与旧版 Windows 10 退回单次抓取，此时会正常出现 |
| 非键盘剪切 | 右键菜单中的「剪切」无法与复制区分，按复制样式提示 |
| 多显示器 | 提示默认落在活动窗口所在显示器，需固定到主显示器时关闭「跟随焦点显示器」 |

## 许可与致谢

本项目以 **GNU General Public License v3.0** 发布，全文见 [LICENSE](LICENSE)。浮窗外观与动效参考 [FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的 LockWindow 设计（同为 GPL-3.0，本项目为独立实现）。界面字体 **HarmonyOS Sans SC** 版权归华为终端有限公司所有，依[字体许可协议](Resources/Fonts/LICENSE_Fonts.txt)随程序原样分发。

问题反馈请在 [Issues](https://github.com/DoYouNatureQ/WinTips/issues) 中说明 Windows 版本与复现步骤，并附上 `%APPDATA%\WinTips\error.log`（如存在）。

---

[↑ 回到顶部](#wintips) · [English version](README.en.md)
