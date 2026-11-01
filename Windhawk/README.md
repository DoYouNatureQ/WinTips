# WinTips for Windhawk

WinTips 的 Windhawk 移植版：单个 `.wh.cpp` 文件，作为 **tool mod** 跑在独立的
`windhawk.exe` 进程里，不注入资源管理器，也不 hook 任何系统函数。

与原版 `WinTips.exe`（WPF，仓库根目录）**完全隔离**：

| | WinTips.exe | 本 mod |
|---|---|---|
| 运行方式 | 独立 EXE + 托盘 | Windhawk tool mod 进程 |
| 配置 | `%APPDATA%\WinTips\settings.json` | Windhawk 设置页（注册表） |
| 开机启动 | `Run` 键 `WinTips` | Windhawk 自启 |
| 构建 | `dotnet publish` | Windhawk 内编译 |

两边可以同时安装：mod 每次弹窗前会探测 `Local\WinTips.SingleInstance`，
发现原版正在运行就自动静默，不会出现两个浮窗。**但建议只保留其中一个的开机启动。**

## 安装

Windhawk → **Create new mod** → 把 `wintips-osd.wh.cpp` 全文粘进去 →
**Compile mod** → **Exit editing** → 打开开关。

元数据里 `@include windhawk.exe` 是 tool mod 的标记，不要改。

## 设置

| 设置项 | 说明 |
|---|---|
| Caps / Num / Scroll Lock、Insert、Copy / Paste / Cut | 分类开关 |
| 任意方式复制都提示 | 右键菜单、应用内“复制”按钮等非键盘复制也提示 |
| 显示时长 | 500–10000 毫秒 |
| 动画速度 | 百分比，100 为正常（300 ms），0 关闭动画 |
| 显示在 | 跟随鼠标所在显示器 / 固定主显示器 |
| 垂直 / 水平位置 | 贴任务栏上方或顶部；左 / 中 / 右（贴屏幕顶部时强调色指示条会镜像到文字上方） |
| 水平 / 垂直偏移 | 像素级微调 |
| 主题 | 跟随系统 / 深色 / 浅色 |
| 卡片材质 | 亚克力（实时模糊背后画面）/ 纯色 |
| 模糊半径 | 0–40 像素，仅亚克力材质有效 |
| 强调色 | `#RRGGBB`，留空跟随系统 |
| 提权窗口下也检测 | 每 250 ms 轮询锁定状态，兜底低级钩子的 UIPI 限制 |
| 全屏应用前台时不提示 | 游戏、全屏视频、演示模式 |

## 本地调试（不需要装 Windhawk）

`wintips-osd.wh.cpp` 用 `/DWINTIPS_STANDALONE` 编译时会替换掉 Windhawk 的
`Wh_*` 接口，变成一个普通控制台程序，同一份绘图代码直接跑：

```bat
build-test.bat
```

```bat
wintips-osd-test.exe --run                     :: 真实运行，按 Caps Lock 即可看到浮窗
wintips-osd-test.exe --live-force 10           :: 同上，但忽略原版互斥量，10 秒后自动退出
wintips-osd-test.exe --selftest                :: 走完整链路自检并打印结果
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh   :: 渲染一帧存 PNG
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh screen  :: 用真实桌面背景测试亚克力
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh bg.png  :: 用指定图片当背景测试亚克力
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh "" top  :: 第 10 个参数 top 预览“指示条在文字上方”的镜像布局
wintips-osd-test.exe --grab screen.png         :: 截取整个虚拟桌面（含副屏）
wintips-osd-test.exe --crop in.png x y w h 3 out.png    :: 裁切放大
wintips-osd-test.exe --bounds a.png            :: 量出卡片实际像素尺寸
wintips-osd-test.exe --pick a.png x y 4        :: 取一小块像素的平均颜色
```

`build-test.bat` 用 `vswhere` 定位 MSVC，需要装了 C++ 生成工具的 Visual Studio。

## 与原版的已知差异

- **亚克力是"截屏 + 模糊"，不是 DWM 材质**。原版用「非分层窗口 + 系统 Acrylic」，
  系统负责实时模糊；系统材质要求窗口不能是 `WS_EX_LAYERED`，而整窗淡入淡出必须用它。
  本版的做法是：弹出瞬间截取卡片所在区域（此时窗口是隐藏的，不会拍到自己），
  两次盒式模糊后与色调一起合成进同一张 ARGB 面，再走分层窗口呈现。
  结果是**模糊和动画都保住了**，代价是那 2 秒里透出的是"弹出那一刻"的背景快照，
  背后画面变了不会跟着变（肉眼基本看不出）。材质可在设置里切回「纯色」。
- **提权窗口可能拍不到**：前面是管理员窗口时截屏会返回全黑，此时该次提示自动退回纯色卡片，
  不会出现黑框。持续需要的话可把材质设为「纯色」。
- **文字抗锯齿是灰度**，原版 WPF 走 ClearType。同样的字号下本版笔画略细。
- **没有可视化摆放器**（原版可以拖着浮窗任意摆放），改为「预设 + X/Y 偏移」。
- **摆放器换成了「跟随鼠标所在显示器」**：卡片出现在指针那块屏幕，用该屏幕自己的
  DPI 缩放和工作区（含各自的任务栏）。原版有可视化摆放器，可以拖到任意位置，
  但多屏缩放不同的时候并不会跟着变缩放。
- **没有托盘菜单**，开关直接在 Windhawk 里。
- 插入提示是事件式的；Windows 不维护 Insert 的全局状态，这点与原版一致。

## 发布前待办

- 外观设计参考自 FluentFlyout（GPL-3.0），本仓库代码为独立实现；
  提交官方仓库前请确认 `@license` 与这段表述一致。
- `@author` / `@github` / `@homepage` 已填为 `DoYouNatureQ`，与远程仓库一致。
