<div align="center">

# WinTips

[![License](https://img.shields.io/badge/license-MIT-3DA639?style=flat-square)](LICENSE) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square) ![Windows](https://img.shields.io/badge/Windows-11-0078D4?style=flat-square) ![Dependencies](https://img.shields.io/badge/dependencies-none-4B5563?style=flat-square)

**简体中文** | [English](README.en.md)

![WinTips 宣传图](promo/wintips-promo.png)

</div>

Windows 11 风格的键盘状态提示工具：按下 **Caps Lock / Num Lock / Scroll Lock**（及复制/粘贴/剪切）时，在任务栏上方弹出 Win11 质感的亚克力浮窗。

## 特性

- **锁键提示**：Caps/Num/Scroll Lock 与 Insert，挂锁图标随开关旋转回弹，强调色指示条（默认在卡片下缘，浮窗贴屏幕顶部时镜像到文字上方），朝贴边方向滑入/滑出并淡入淡出。
- **随位置转向的滑入**：浮窗落在屏幕左/右/上/下边缘的 15% 区域内就从该侧滑入、朝该侧滑出；左右判定优先于上下（四角也只从左右滑入，不会斜向或上下弹），落在屏幕中部时按上下半屏竖向滑入。
- **剪贴板提示**：Ctrl+C / V / X 弹出对勾或剪刀图标，右侧「×N」徽章记录连续操作；仅在剪贴板真正更新后提示。默认开启「任意复制方式都提示」——右键菜单、应用内“复制”按钮等非键盘复制（剪贴板更新）同样弹出；清空剪贴板不会误提示。
- **系统材质**：DWM 亚克力 + 系统圆角 + 深浅色与强调色跟随系统，高 DPI 自适应；Win10、高对比度、关闭合成、系统关掉「透明效果」或进入节电模式时自动退回设计稿里的不透明纯色，不会出现半透明叠在纯色兜底上的偏色（Mica 不受节电模式影响，设置窗口照常）。
- **不打扰**：点击穿透、不抢焦点、不进任务栏和 Alt+Tab，停留 1–5 秒（可调）自动淡出。
- **设置窗口**：Win11 设置风格，即改即存；浮窗位置可自由搭配——垂直选顶部/底部，水平选左/中/右（如顶部+居中、顶部+右侧、底部+居中）；也可点「自定义位置」拖动浮窗任意摆放后保存（可一键还原默认）。
- **多显示器**：默认跟随焦点——提示落在当前活动窗口所在的那块屏，按该屏自己的 DPI 与任务栏形态摆放（贴边滑入、指示条镜像、任务栏自动隐藏跟随都照常生效）；关掉「跟随焦点显示器」则固定回主显示器。
- **可视化摆放**：自定义位置时用虚线标出四边 15% 的贴边判定区，浮窗中心进入哪个区就高亮该区（即真实提示的滑入方向）；浮窗靠近屏幕中轴时自动吸附对齐；方向键可逐像素微调（按一下 1 像素、长按连发，Shift 每次 10 像素）。
- **多语言界面**：内置简体中文、繁體中文、English、Deutsch、日本語、Русский、Español、Português 八种语言，默认跟随系统语言；其余语言收在设置的下拉框里，切换后立即生效、无需重启。
- 托盘常驻、单实例、无第三方依赖（WPF + WinForms，.NET 10）。

## 使用

直接运行 `WinTips.exe`，托盘出现图标即在工作。

- 托盘左键 → 设置；右键 → 菜单（含测试提示）。
- 命令行：`--settings` 打开设置；`--test caps on 4` 测试提示；`--test menu` 预览菜单。
- 设置保存在 `%APPDATA%\WinTips\settings.json`。

## 构建与发布

需要 .NET 10 SDK（Windows）。

```powershell
# 调试运行
dotnet build -c Release

# 发布单文件 exe（无 .NET 依赖，约 70 MB）
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish
```

## 已知限制

- Insert 提示为事件式，不显示开/关状态（Windows 不在系统层维护该状态）。
- 多显示器下提示默认跟着当前活动窗口走（同一块屏上）；想让它固定在主显示器，可在设置里关掉「跟随焦点显示器」。
- 低级键盘钩子收不到发往提权（管理员）窗口的按键。
- 非键盘来源的剪切（如右键菜单“剪切”）无法与复制区分，会以复制样式提示。
- 界面字体优先使用 **HarmonyOS Sans SC**（中英文同一字体文件），未安装时自动回退到微软雅黑 UI / Segoe UI，观感会略有差异。字体不随程序分发，需自行安装（个人使用可直接双击 ttf 安装到当前用户，无需管理员权限）。

## 致谢

浮窗外观与动效复刻自 [FluentFlyout](https://github.com/unchihugo/FluentFlyout)（GPL-3.0）的 LockWindow 设计，代码为独立实现。

---

[↑ 回到顶部](#wintips) · [English version](README.en.md)
