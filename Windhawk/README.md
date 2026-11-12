# WinTips for Windhawk

WinTips 的 Windhawk 移植版：一个 `.wh.cpp` 文件，作为 **tool mod** 跑在独立的
`windhawk.exe` 进程里，不注入任何程序，也不 hook 系统函数。

和仓库根目录的原版 `WinTips.exe`（WPF 应用）互不影响，两边可以同时装：

| | WinTips.exe | 本 mod |
|---|---|---|
| 运行方式 | 独立 EXE + 托盘 | Windhawk tool mod 进程 |
| 配置 | `%APPDATA%\WinTips\settings.json` | Windhawk 设置页 |
| 开机启动 | `Run` 键 | Windhawk 自启 |

原版在跑时 mod 会探测 `Local\WinTips.SingleInstance` 并自动静默，不会出现两个浮窗；
**但开机启动只留一个。**

## 安装

Windhawk → **Create new mod** → 全文粘贴 `wintips-osd.wh.cpp` → **Compile mod** →
**Exit editing** → 打开开关。元数据里的 `@include windhawk.exe` 是 tool mod 标记，别改。

## 文件

| 路径 | 用途 |
|---|---|
| `wintips-osd.wh.cpp` | 模组本体，唯一需要提交到 Windhawk 的文件 |
| `mod-readme.md` | 模组页正文，与 `.wh.cpp` 里 `==WindhawkModReadme==` 块逐行一致 |
| `standalone/` | 不装 Windhawk 也能跑的调试台（引用模组源码，`wh-shims.h` 补 `Wh_*` 桩） |
| `build-test.bat` | 编译调试台，用 `vswhere` 找 MSVC，需要装了 C++ 生成工具的 VS |
| `preview/` | 模组页引用的截图与 GIF，命名 `osd-<主题>-<场景>.png` |
| `obj/`、`*.exe` | 编译产物，已在 `.gitignore` 中排除 |

## 设置

- **提示项** — 大写 / 数字 / 滚动锁定、Insert、复制 / 粘贴 / 剪切各自开关；
  「任意方式复制都提示」把右键菜单、应用内按钮的复制也算上。
- **语言** — 浮窗文字语言，默认跟随系统（内置八种，其余语系显示英文），也可固定。
  与 Windhawk 界面语言互不影响。
- **显示时长 / 动画速度** — 500–10000 ms；动画 100 为正常（300 ms），0 关闭。
- **位置** — 垂直（任务栏上方 / 屏幕顶部）、水平（左 / 中 / 右）、显示在（跟随焦点 /
  跟随鼠标 / 主显示器）、X/Y 像素偏移。位置是预设 + 偏移，没有可视化摆放器；
  任务栏形态实时跟随，自动隐藏滑出时会让位。
- **外观** — 主题、卡片材质（亚克力 / 纯色）、底色不透明度（20–90%，默认 55%）、
  模糊半径（0–40 px）、强调色 `#RRGGBB`（留空跟随系统）、浮窗大小（60–200%，
  间距不缩放）。
- **全屏应用前台时不提示** — 游戏、全屏视频、演示模式。

提权窗口不用单独开关：每次前台切换时读一下目标进程的完整性级别，比本进程高才改用
兜底通道。兜底有两条——Raw Input 走设备栈投递，跟焦点和完整性级别都无关，是主力；
按键状态轮询跟在后面，管远控、虚拟键盘这类 Raw Input 收不到的情况。换回普通程序
立刻收手，两条一起停。

## 本地调试（不需要装 Windhawk）

```bat
build-test.bat
wintips-osd-test.exe --live                  :: 真实运行，按 Caps Lock 看浮窗
wintips-osd-test.exe --selftest              :: 走完整链路自检
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh   :: 渲染一帧存 PNG
wintips-osd-test.exe --bench 500             :: 单帧耗时、截屏耗时、空闲 CPU 与内存
wintips-osd-test.exe --grab screen.png       :: 截整个虚拟桌面，含副屏
```

`--shot` 后面依次是卡片名、开/关、缩放、输出文件、主题、语言，再往后可选
`screen`（真实桌面背景）、图片路径（指定背景）、`top` / `bottom`（位置）、`60–200`（大小）；
另有 `--livetest`、`--crop`、`--bounds`、`--pick`、`--keymap`、`--tap` 等量图与测试用子命令，
参数解析见 `standalone/wintips-osd-test.cpp` 末尾。

## 已知限制

- **亚克力是「截屏 + 模糊」**，不是 DWM 材质：整窗淡入淡出必须是分层窗口，而系统材质
  不允许。卡片显示期间每 22 ms 重抓一次背景并混合（Win10 2004+、DWM 合成、非远程会话），
  拿不到该能力就退回弹出时的快照。抓取范围比卡片大一圈，滑入和跟随任务栏不必重抓。
- **独占全屏游戏里画不出来**：画面归显示管线独占。无边框全屏与窗口化正常，置顶看守会
  把卡片重新顶回去（每 100 ms 检查一次，被盖住才动手）。
- **提权窗口前拍不到背景**（返回全黑），该次提示退回纯色卡片；语言仍能读，输入法中英文
  模式读不到。
- **语言提示要靠按键触发**：用鼠标点语言栏 / 输入法状态条不提示，系统没有对应通知。
- **没有托盘菜单**，开关都在 Windhawk 里；文字抗锯齿是灰度（原版 WPF 走 ClearType）。

## 发布前待办

- 外观参考 FluentFlyout（同为 GPL-3.0），代码为独立实现，`@license` 为
  `GPL-3.0-or-later`，与根目录 `LICENSE` 一致。
- `@author` / `@github` / `@homepage` 已填 `DoYouNatureQ`。
- `mod-readme.md` 改动后必须同步进 `.wh.cpp`；其中的图片 URL 指向 `main` 分支，
  换图要先 push 再提 PR，否则 CI 取图 404。
