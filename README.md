# WinTips

![WinTips 宣传图](promo/wintips-promo.png)

Windows 11 风格的键盘状态提示工具：按下 **Caps Lock / Num Lock / Scroll Lock**（及复制/粘贴/剪切）时，在任务栏上方弹出 Win11 质感的亚克力浮窗。

## 特性

- **锁键提示**：Caps/Num/Scroll Lock 与 Insert，挂锁图标随开关旋转回弹，底部强调色指示条，上滑淡入/下滑淡出。
- **剪贴板提示**：Ctrl+C / V / X 弹出对勾或剪刀图标，右侧「×N」徽章记录连续操作；仅在剪贴板真正更新后提示。默认开启「任意复制方式都提示」——右键菜单、应用内“复制”按钮等非键盘复制（剪贴板更新）同样弹出；清空剪贴板不会误提示。
- **系统材质**：DWM 亚克力 + 系统圆角 + 深浅色与强调色跟随系统，高 DPI 自适应。
- **不打扰**：点击穿透、不抢焦点、不进任务栏和 Alt+Tab，停留 1–5 秒（可调）自动淡出。
- **设置窗口**：Win11 设置风格，即改即存；浮窗位置可选左/中/右，或点「自定义位置」拖动浮窗任意摆放后保存（可一键还原默认）。
- **中英双语**：界面文案内置简体中文与 English，默认跟随系统语言，也可在设置的「界面语言」里随时切换，切换后立即生效、无需重启。
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

> 一键重建：双击根目录的 `rebuild.bat`（自动结束旧进程 → 清理打包产物 bin/obj/publish → 重新发布 → 重新启动）。

## 已知限制

- Insert 提示为事件式，不显示开/关状态（Windows 不在系统层维护该状态）。
- 提示只出现在主显示器。
- 低级键盘钩子收不到发往提权（管理员）窗口的按键。
- 非键盘来源的剪切（如右键菜单“剪切”）无法与复制区分，会以复制样式提示。

## 致谢

浮窗外观与动效复刻自 [FluentFlyout](https://github.com/unchihugo/FluentFlyout)（GPL-3.0）的 LockWindow 设计，代码为独立实现。
