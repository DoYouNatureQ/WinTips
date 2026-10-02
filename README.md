# WinTips

Windows 11 风格的键盘状态提示工具：按下 **Caps Lock / Num Lock / Scroll Lock** 时，在任务栏上方居中弹出同款 Win11 质感的亚克力浮窗（外观 1:1 复刻 [FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的锁键浮窗设计）；**Insert** 键以事件方式提示（系统层面没有开/关状态，改写模式由各应用自行维护）。

## 特性

- **浮窗复刻 FluentFlyout LockWindow**：160×50 卡片、左侧挂锁图标随开/关**锁扣旋转 + 回弹**、文案「大写锁定 开启/关闭」「Insert 键已按下」、底部**强调色指示条**（开=60px 全不透明 / 关=36px 20%）、上滑淡入 / 下滑淡出。
- **系统材质**：DWM 系统亚克力（`DWMSBT_TRANSIENTWINDOW`）+ 系统圆角 + 深浅色跟随系统 + 系统强调色，高 DPI 与自动隐藏任务栏自适应。
- **不打扰**：点击穿透、不抢焦点、不进任务栏和 Alt+Tab，停留 1–5 秒（可调）自动淡出。
- **状态自校验**：钩子回调时系统尚未应用翻转，先按"取反"显示，250 ms 后用 `GetKeyState` 复核纠偏。
- **托盘常驻**：左键打开设置；右键为自定义圆角亚克力菜单（设置 / 可展开的测试提示 / 开机自启 / 退出），失焦与 Esc 关闭。
- **设置窗口**：Win11 设置风格圆角卡片 + 定制开关与滑杆，即改即存。
- **单实例**、无第三方依赖（WPF + WinForms，.NET 10）。

## 键位与文案

| 键 | 文案 | 指示条 |
| --- | --- | --- |
| Caps Lock | 大写锁定 开启/关闭 | 开 60px / 关 36px |
| Num Lock | 数字锁定 开启/关闭 | 同上 |
| Scroll Lock | 滚动锁定 开启/关闭 | 同上 |
| Insert | Insert 键已按下（事件式） | 常亮 |

## 项目结构

```
WinTips.slnx / WinTips.csproj  .NET 10 WPF 工程（单项目）
App/
  App.xaml(.cs)                应用入口：单实例、托盘装配、命令行、异常日志
Core/
  KeyboardHook.cs              WH_KEYBOARD_LL 全局钩子（过滤自动重复）
  OsdController.cs             提示调度、设置过滤、状态 250ms 自校验
  TipInfo.cs                   键位定义（虚拟键码/显示名/解析）
  AppSettings.cs               设置持久化（%APPDATA%\WinTips）与开机自启
  ThemeHelper.cs               系统深浅色与强调色读取
Native/
  NativeMethods.cs             Win32 P/Invoke（钩子/DWM 系统材质/Shell）
  TaskbarInfo.cs               任务栏矩形与自动隐藏检测
UI/
  OsdWindow.xaml(.cs)          锁键浮窗（挂锁/指示条/动画，DWM 亚克力）
  TrayMenuWindow.xaml(.cs)     托盘右键菜单（圆角亚克力卡片）
  SettingsWindow.xaml(.cs)     设置窗口（Mica + 圆角卡片）
  TrayService.cs               托盘图标与菜单装配
Resources/
  Styles.xaml                  共享控件样式（Win11 开关/滑杆）
  app.ico                      应用图标（源文件，同时内嵌 exe）
Properties/
  app.manifest                 DPI 感知清单
docs/                          文档截图
tools/                         辅助脚本（图标生成/截图/输入模拟）
```

## 构建与发布

需要 .NET 10 SDK（Windows）。

```powershell
# 调试运行
dotnet build -c Release
./bin/Release/net10.0-windows/WinTips.exe

# 发布单文件 exe（无 .NET 依赖，约 70 MB）
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish

# 重新生成应用图标（苹果风格 squircle）
powershell -File tools/icogen.ps1
```

> **一键重建**：双击根目录的 `rebuild.bat`（自动结束旧进程 → 发布 → 重新启动；路径相对脚本位置，仓库放哪都能用）。

## 使用

- 直接运行 `WinTips.exe`，托盘出现图标即在工作。
- 托盘左键 → 设置；右键 → 圆角亚克力菜单（含测试提示）。
- 命令行：
  - `WinTips.exe --settings` 直接打开设置；
  - `WinTips.exe --test caps on 4` 测试提示（`caps|num|scroll|insert` + `on|off` + 停留秒数）；
  - `WinTips.exe --test menu` 预览托盘菜单。

设置保存在 `%APPDATA%\WinTips\settings.json`；异常日志在同目录 `error.log`。

## 已知限制

- Insert 键提示为"事件式"：只提示按键按下，不显示开/关——Windows 不在系统层维护改写状态，Word/终端/IDE 各自记录。
- 提示只出现在主显示器；任务栏竖放/顶部停靠时退化为工作区底部居中。
- 提示窗口不响应鼠标（点击穿透），与系统音量浮窗同时出现时会短暂重叠。
- 低级键盘钩子收不到发往提权（管理员）窗口的按键；如需覆盖可后续加轮询兜底。

## 致谢

- 浮窗外观与动效复刻自 [FluentFlyout](https://github.com/unchihugo/FluentFlyout)（GPL-3.0）的 LockWindow 设计；本项目代码为独立实现。
- 图标字体：Segoe Fluent Icons（系统自带）。
