# WinTips for Windhawk

WinTips 的 Windhawk 移植版：单个 `.wh.cpp` 文件，作为 **tool mod** 跑在独立的
`windhawk.exe` 进程里，不注入资源管理器，也不 hook 任何系统函数。

与仓库根目录的原版 `WinTips.exe` **完全隔离**，那是个 WPF 应用：

| | WinTips.exe | 本 mod |
|---|---|---|
| 运行方式 | 独立 EXE + 托盘 | Windhawk tool mod 进程 |
| 配置 | `%APPDATA%\WinTips\settings.json` | Windhawk 设置页，存注册表 |
| 开机启动 | `Run` 键 `WinTips` | Windhawk 自启 |
| 构建 | `dotnet publish` | Windhawk 内编译 |

两边可以同时安装：mod 每次弹窗前会探测 `Local\WinTips.SingleInstance`，
发现原版正在运行就自动静默，不会出现两个浮窗。**但建议只保留其中一个的开机启动。**

## 目录里都有什么

| 路径 | 用途 | 是否入库 |
|---|---|---|
| `wintips-osd.wh.cpp` | 模组本体，唯一需要提交到 Windhawk 的文件 | 是 |
| `mod-readme.md` | Windhawk 模组页正文；与 `.wh.cpp` 里的 `// ==WindhawkModReadme==` 逐行一致 | 是 |
| `README.md` | 这份说明：安装、设置、调试、与原版差异 | 是 |
| `standalone/` | 不装 Windhawk 也能跑的本机调试台：`wintips-osd-test.cpp` 引用模组源码，`wh-shims.h` 提供 `Wh_*` 桩 | 是 |
| `build-test.bat` | 用 `vswhere` 找 MSVC 编译上面那套调试台 | 是 |
| `preview/` | 模组页引用的 3 张截图 + 1 个 GIF | 是 |
| `obj/`、`wintips-osd-test.exe` | 编译中间产物与调试台可执行文件 | 否，`.gitignore` 已排除 |

`preview/` 只保留 mod-readme 里实际引用的文件，调试用的抽帧、对比图不进仓库；
新增截图请沿用 `osd-<主题>-<场景>.png` 命名，否则会被忽略规则挡掉。

## 安装

Windhawk → **Create new mod** → 把 `wintips-osd.wh.cpp` 全文粘进去 →
**Compile mod** → **Exit editing** → 打开开关。

元数据里 `@include windhawk.exe` 是 tool mod 的标记，不要改。

## 设置

| 设置项 | 说明 |
|---|---|
| 语言 | 浮窗文字语言：默认跟随系统，按 Windows 显示语言自动匹配内置的八种之一；也可固定为其中一种 |
| Caps / Num / Scroll Lock、Insert、Copy / Paste / Cut | 分类开关 |
| 任意方式复制都提示 | 右键菜单、应用内“复制”按钮等非键盘复制也提示 |
| 显示时长 | 500–10000 毫秒 |
| 动画速度 | 百分比，100 为正常，即 300 ms；0 关闭动画 |
| 浮窗大小 | 60%–200%，文字、图标、指示条与 `×N` 徽章一起等比缩放；100% 为 Win11 原生大小，与屏幕边缘的间距不随之缩放 |
| 显示在 | 跟随焦点，即当前活动窗口所在的显示器，默认；或跟随鼠标所在显示器、固定主显示器 |
| 垂直 / 水平位置 | 贴任务栏上方，跟随任务栏此刻的形态：自动隐藏滑出时自动让位，收起时贴回屏幕边缘。也可贴屏幕顶部；贴顶部时强调色指示条会镜像到文字上方 |
| 水平 / 垂直偏移 | 像素级微调 |
| 主题 | 跟随系统 / 深色 / 浅色 |
| 卡片材质 | 亚克力，实时模糊背后画面；或纯色 |
| 底色不透明度 | 20%–90%，默认 55%：亚克力下模糊背景之上那层底色盖住多少，越低越透；与原版「材质透明度」是同一个数值 |
| 模糊半径 | 0–40 像素，仅亚克力材质有效 |
| 强调色 | `#RRGGBB`，留空跟随系统 |
| 提权窗口下也检测 | 每 250 ms 轮询锁定状态，兜底低级钩子的 UIPI 限制 |
| 全屏应用前台时不提示 | 游戏、全屏视频、演示模式 |

语言分两层，互不影响。Windhawk 里的 **mod 名称、描述和设置项文案**跟随 Windhawk 的应用语言，
也就是元数据与设置块里的 `@name:zh-CN`、`$name:zh-CN`；**浮窗上的文字**由「语言」设置决定，
默认为「跟随系统」，按 Windows 显示语言自动匹配内置的八种之一，其余语系显示英文，
也可以在下拉框里固定其中一种。把 Windhawk 界面切成别的语言不会改浮窗文字，反之亦然。

## 性能占用

下面的数字来自 `wintips-osd-test.exe --bench`，同一份代码、`/O2`、单屏 2560×1440 @100%：

| 项目 | 占用 |
|---|---|
| 隐藏时，只剩可选的 250 ms 轮询 | 10 秒 **0 ms** CPU，前后两次采样差值为 0 |
| 一帧卡片：清缓冲、GDI+ 重绘、预乘 | **0.52 ms**，200% 尺寸时 0.92 ms |
| 每次弹出截取并模糊背景 | 约 **7 ms**，仅一次 |
| 一次完整提示，300 ms 入场约 18 帧 | 约 **16 ms** CPU |
| 进程常驻 | 工作集 10.8 MB / 私有 2.2 MB，裸进程，没有 Windhawk 宿主 |

要点：

- **没有空的定时器**。卡片隐藏时只有「提权窗口下也检测」那个 250 ms 轮询在跑，
  可在设置里关掉，其余全是事件驱动；入场动画结束后动画定时器立刻销毁。
- **60 ms 跟随循环只做一次 `GetWindowRect`**。副屏任务栏窗口 `Shell_SecondaryTrayWnd`
  首次按屏查找后缓存 HWND，不再每个 tick 都用 `EnumWindows` 遍历全部顶层窗口加
  `GetClassNameW`；该屏没有任务栏时也会记住「没有」，不会反复重查。
- **DPI 函数指针只解析一次**，`LoadLibrary("shcore.dll")` 不再每次弹出都走一遍。

## 本地调试，不需要装 Windhawk

`wintips-osd.wh.cpp` 用 `/DWINTIPS_STANDALONE` 编译时会替换掉 Windhawk 的
`Wh_*` 接口，变成一个普通控制台程序，同一份绘图代码直接跑：

```bat
build-test.bat
```

```bat
wintips-osd-test.exe --live                    :: 真实运行，按 Caps Lock 即可看到浮窗
wintips-osd-test.exe --live 10                 :: 同上，10 秒后自动退出
wintips-osd-test.exe --selftest                :: 走完整链路自检并打印结果
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh   :: 渲染一帧存 PNG
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh screen  :: 用真实桌面背景测试亚克力
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh bg.png  :: 用指定图片当背景测试亚克力
wintips-osd-test.exe --shot caps on 2.0 a.png dark zh "" top  :: 第 10 个参数 top 预览“指示条在文字上方”的镜像布局
wintips-osd-test.exe --shot caps on 1.0 a.png dark zh "" bottom 200  :: 第 11 个参数是“浮窗大小”百分比，60–200
wintips-osd-test.exe --bench 500               :: 实测单帧耗时、截屏耗时、空闲 CPU 与内存
wintips-osd-test.exe --grab screen.png         :: 截取整个虚拟桌面，含副屏
wintips-osd-test.exe --crop in.png x y w h 3 out.png    :: 裁切放大
wintips-osd-test.exe --bounds a.png            :: 量出卡片实际像素尺寸
wintips-osd-test.exe --pick a.png x y 4        :: 取一小块像素的平均颜色
```

`build-test.bat` 用 `vswhere` 定位 MSVC，需要装了 C++ 生成工具的 Visual Studio。

## 与原版的已知差异

- **亚克力是"截屏 + 模糊"，不是 DWM 材质**。原版用「非分层窗口 + 系统 Acrylic」，
  系统负责实时模糊；系统材质要求窗口不能是 `WS_EX_LAYERED`，而整窗淡入淡出必须用它。
  本版的做法是：弹出瞬间截取卡片所在区域，此时窗口是隐藏的，不会拍到自己，
  两次盒式模糊后与色调一起合成进同一张 ARGB 面，再走分层窗口呈现。
  结果是**模糊和动画都保住了**，代价是那 2 秒里透出的是"弹出那一刻"的背景快照，
  背后画面变了不会跟着变，肉眼基本看不出。跟随任务栏挪位时带的也是这张快照，
  落位后重拍一次。材质可在设置里切回「纯色」。上一张卡片刚淡出时截屏会把它自己拍进去，
  拍出来就是一块卡片大小的纯色，所以隐藏窗口后先 `DwmFlush()` 等一帧合成再截屏，
  与原版 0.52 的处理一致。
- **Win10 不需要额外适配**：本版本来就是自绘的分层窗口 + 自己抓屏模糊，
  不依赖 DWM 亚克力，也不依赖系统圆角；也就是说原版 0.52 为 Win10 补的
  「自绘圆角 + 快照玻璃」在这里本来就成立。这一点没有在 Win10 实机验证过。
- **没有输入法语言 / 输入模式提示**：原版 0.51 起有，默认关闭。Win+Space、
  Ctrl+Shift 换语言，或在输入法内部切换模式时原版会弹卡片显示当前语言或模式。本版
  没有这个提示类型，也没有对应设置项。
- **提权窗口可能拍不到**：前面是管理员窗口时截屏会返回全黑，此时该次提示自动退回纯色卡片，
  不会出现黑框。持续需要的话可把材质设为「纯色」。
- **底色不透明度默认值变了**：1.4.x 把底色写死在 84%，几乎实心，模糊几乎看不见；
  现在默认 55%，与原版「模糊效果」的默认值一致，背后的模糊才真正透出来。想回到旧观感，
  把「底色不透明度」拉到 85 左右即可。
- **文字抗锯齿是灰度**，原版 WPF 走 ClearType。同样的字号下本版笔画略细。
- **没有可视化摆放器**：原版可以拖着浮窗任意摆放，这里改为「预设 + X/Y 偏移」。
- **多显示器与大小与原版对齐**：默认同样「跟随焦点显示器」，卡片落在当前活动窗口
  所在的那块屏，用该屏自己的 DPI 缩放与任务栏此刻的位置，自动隐藏滑出/收起时实时跟随，
  与 WinTips.exe 同一套逻辑；也能切回「跟随鼠标」或固定主屏。浮窗大小同样可调
  60%–200%，间距按 DIP 计算、不随尺寸放大。原版的可视化摆放器这里仍然没有。
- **没有托盘菜单**，开关直接在 Windhawk 里。
- 插入提示是事件式的；Windows 不维护 Insert 的全局状态，这点与原版一致。
  只有单独按下 Insert 才算插入：按住 Ctrl / Shift 时它分别是复制与粘贴，
  Ctrl+Insert / Shift+Insert / Shift+Delete 这组旧式快捷键一并支持。

## 发布前待办

- 外观设计参考自 FluentFlyout，同为 GPL-3.0；本仓库代码为独立实现，
  `@license` 已按 SPDX 标为 `GPL-3.0-or-later`，与根目录 `LICENSE` 的 GPL-3.0 或更新版本一致。
- `@author` / `@github` / `@homepage` 已填为 `DoYouNatureQ`，与远程仓库一致。
