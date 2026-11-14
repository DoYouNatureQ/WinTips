# WinTips for Windhawk

WinTips 的 Windhawk 移植版：一个 `.wh.cpp` 文件，作为 **tool mod** 跑在独立的
`windhawk.exe` 进程里，不注入任何程序，也不 hook 系统函数。

与根目录的原版 `WinTips.exe` 互不影响，可同时安装，但**开机启动只留一个**：

| | WinTips.exe | 本 mod |
|---|---|---|
| 运行方式 | 独立 EXE + 托盘 | Windhawk tool mod 进程 |
| 配置 | `%APPDATA%\WinTips\settings.json` | Windhawk 设置页 |
| 开机启动 | `Run` 键 | Windhawk 自启 |

原版在跑时 mod 会探测 `Local\WinTips.SingleInstance` 并自动静默，不会出现两个浮窗。

## 安装

Windhawk → **Create new mod** → 全文粘贴 `wintips-osd.wh.cpp` → **Compile mod** →
**Exit editing** → 打开开关。元数据里的 `@include windhawk.exe` 是 tool mod 标记，别改。

## 设置

- **提示项** — 大写 / 数字 / 滚动锁定、Insert、复制 / 粘贴 / 剪切各自开关；
  「任意方式复制都提示」把右键菜单、应用内按钮的复制也算上。
- **语言** — 浮窗文字语言，默认跟随系统（内置八种，其余语系显示英文），也可固定。
- **显示时长 / 动画速度** — 500–10000 ms；动画 100 为正常，0 关闭。
- **位置** — 垂直、水平、显示在（跟随焦点 / 鼠标 / 主显示器）与 X/Y 像素偏移；
  任务栏形态实时跟随，自动隐藏滑出时会让位。
- **外观** — 主题、卡片材质（亚克力 / 纯色）、底色不透明度（20–90%）、模糊半径
  （0–40 px）、强调色 `#RRGGBB`（留空跟随系统）、大小（60–200%）。
- **全屏应用前台时不提示** — 游戏、全屏视频、演示模式。

提权窗口不用单独开关：前台进程完整性级别更高时，自动改用 Raw Input 与按键状态
轮询兜底，换回普通程序立刻停用。

## 文件

| 路径 | 用途 |
|---|---|
| `wintips-osd.wh.cpp` | 模组本体，唯一需要提交到 Windhawk 的文件 |
| `mod-readme.md` | 模组页正文，与 `.wh.cpp` 里 `==WindhawkModReadme==` 块逐行一致 |
| `standalone/` + `build-test.bat` | 不装 Windhawk 也能跑的调试台，需装了 C++ 生成工具的 VS |
| `preview/` | 模组页截图与 GIF，命名 `osd-<主题>-<场景>.png` |

## 本地调试

```bat
build-test.bat
wintips-osd-test.exe --live                  :: 真实运行，按 Caps Lock 看浮窗
wintips-osd-test.exe --selftest              :: 走完整链路自检
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh   :: 渲染一帧存 PNG
```

其余子命令（`--bench`、`--grab`、`--crop` 等）见 `standalone/wintips-osd-test.cpp` 末尾。

## 已知限制

- **亚克力是「截屏 + 模糊」**，不是 DWM 材质：卡片显示期间每 22 ms 重抓背景混合
  （Win10 2004+、非远程会话），拿不到该能力就退回弹出时的快照。
- **独占全屏游戏里画不出来**；无边框全屏与窗口化正常。
- **提权窗口前拍不到背景**（返回全黑），该次提示退回纯色卡片，中英文模式读不到。
- **语言提示要靠按键触发**：用鼠标点语言栏 / 输入法状态条不提示。
- **没有托盘菜单**，开关都在 Windhawk 里。

## 发布

- `mod-readme.md` 改动后必须同步进 `.wh.cpp` 的 `==WindhawkModReadme==` 块；其中
  图片 URL 指向 `main` 分支，换图要先 push 再提 PR，否则 CI 取图 404。
- 外观参考 FluentFlyout（同为 GPL-3.0），代码为独立实现，`@license` 为
  `GPL-3.0-or-later`，与根目录 `LICENSE` 一致。
