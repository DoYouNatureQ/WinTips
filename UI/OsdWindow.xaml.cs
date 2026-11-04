using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 左侧挂锁图标（锁扣随开/关旋转+回弹）、中间“大写锁定 开启/关闭”、
/// 强调色指示条（开=60px 全不透明，关=36px 20% 不透明）；指示条默认在卡片下缘，
/// 浮窗挂在屏幕顶部区域时镜像到文字上方，始终贴近它所挂靠的那条屏幕边。
/// 锁扣与指示条共用同一时长与缓动，且在窗口可见后才同帧启动，保证同步起落。
/// 显示位置：默认主显示器；开启「跟随焦点显示器」后落在焦点窗口所在的那块屏，
/// 并按该屏自己的 DPI 缩放与任务栏形态摆放。垂直可贴屏幕顶部或任务栏上方，
/// 水平可设为左侧/居中/右侧（默认居中、任务栏上方）；点击穿透、不抢焦点。
/// 预热分两步：EnsureHandle() 只建句柄不显示窗口（原地 Show() 会带出一帧
/// DWM 背景）；随后把窗口移到屏幕外真实渲染一帧再隐藏，暖起冷启动的
/// 渲染管线。句柄与管线就绪后首次 ShowTip 无创建迟滞，也不会看到内容
/// “初始化”的过程。
/// </summary>
public partial class OsdWindow : Window
{
    /// <summary>跟随轮询间隔（ms）。任务栏滑出动画约 200ms，60ms 一次足够跟手。</summary>
    private const int FollowIntervalMs = 60;

    /// <summary>跟随挪动的缓动时长（ms）：比弹出动画短，避免慢吞吞地追。</summary>
    private const int FollowMs = 140;

    private readonly DispatcherTimer _hideTimer = new();

    /// <summary>可见期间跟随任务栏滑出/收起；只在需要时运行（见 StartFollow）。</summary>
    private readonly DispatcherTimer _followTimer = new();

    private bool _visible;
    private bool _shownOnce;
    private int _generation;
    private EventHandler? _warmRendered;
    private DispatcherTimer? _warmGuard;
    private TipKind _currentKind;
    private double _lastLeft;
    private double _lastTop;
    private double _followLeft;
    private double _followTop;
    private int _moveGen;

    /// <summary>
    /// 本次显示的目标显示器（DIP 矩形 + 该屏缩放），每次 <see cref="ShowTip"/> 时按当时的焦点屏重定；
    /// 一轮显示内的重排（改状态、跟任务栏、DPI 变化）都沿用同一个目标，不会自己乱跑。
    /// </summary>
    private MonitorArea _target = MonitorArea.Primary();

    /// <summary>系统剪贴板更新（WM_CLIPBOARDUPDATE），由 OsdController 订阅。</summary>
    public event Action? ClipboardUpdated;

    private int _clipCount;
    private bool _countIncreased;

    /// <summary>指示条贴在文字上方（浮窗挂靠屏幕顶边）而非下方。</summary>
    private bool _barOnTop;

    private static bool IsClip(TipKind k) => TipInfo.IsClipboard(k);

    /// <summary>字体族只解析一次；FormattedText 每次测量都要用到它。</summary>
    private static readonly FontFamily OsdFont = new("Segoe UI Variable Display, Segoe UI");

    private static readonly Typeface MediumFace =
        new(OsdFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private static readonly Typeface SemiBoldFace =
        new(OsdFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>
    /// 文案宽度缓存。提示文案是有限集合（7 种提示 × 开/关 × ×N），命中率极高；
    /// 原先每次弹出都要重新走一遍 FormattedText 的字体匹配与字形布局。
    /// </summary>
    private static readonly Dictionary<(string Text, double Size, FontWeight Weight), double> WidthCache = new();

    /// <summary>当前外观键（深浅色 / 高对比度 / 强调色）：变化时才重建画刷。</summary>
    private (bool Dark, bool HighContrast, Color Accent) _chromeKey;
    private bool _chromeReady;

    private SolidColorBrush _surfaceBrush = Brushes.White;
    private SolidColorBrush _textBrush = Brushes.Black;
    private SolidColorBrush _accentBrush = Brushes.DodgerBlue;
    private SolidColorBrush _onAccentBrush = Brushes.White;

    /// <summary>画刷冻结后可跨线程共享，也免掉每次赋值时的变更通知链。</summary>
    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => HideAnimated();
        _followTimer.Interval = TimeSpan.FromMilliseconds(FollowIntervalMs);
        _followTimer.Tick += (_, _) => FollowTaskbar();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd)!;
        src.CompositionTarget!.BackgroundColor = Colors.Transparent;

        int ex = NativeMethods.GetWindowLong32(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong32(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE |
            NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOPMOST);

        // 扩展边框在两条路径上都要保留：这是 AllowsTransparency=false 时拿到逐像素透明的
        // 唯一办法。Win10 上没有 Mica，下面这层“玻璃”会被 ApplyChrome 铺的不透明卡片完全盖住
        DwmChrome.SetFrameExtended(hwnd, true);

        src.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(hwnd);
        }
        catch { }
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            ClipboardUpdated?.Invoke();
        else if (msg == NativeMethods.WM_DISPLAYCHANGE || msg == NativeMethods.WM_SETTINGCHANGE)
        {
            // 切换显示器/主屏、改缩放、改分辨率、改排列：目标屏本身可能已经搬走或消失，
            // 先把它的几何与缩放按系统最新值重取（见 RefreshTarget），再按新值重排一次。
            // 隐藏窗口收不到系统 WM_DPICHANGED，不主动对齐的话下次弹出会按旧缩放渲染。
            RefreshTarget();
            Reposition();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 显示配置变了：优先沿用当前这块屏（用户正看着它），但矩形与缩放一律按系统最新值重取；
    /// 只有当这块屏已经不存在（拔线、关闭内置屏）时才重新按焦点/光标挑选。
    /// <c>_target</c> 是弹出那一刻的快照，不重取就会继续按"已经不存在的屏幕"和旧缩放摆位——
    /// 这是插拔显示器、改完缩放后浮窗跑到屏外或只露半截的直接原因。
    /// </summary>
    private void RefreshTarget() =>
        _target = MonitorArea.FromHandle(_target.Handle) ?? ResolveTarget();

    /// <summary>
    /// 本次提示要落在哪台显示器：开启「跟随焦点显示器」时取焦点窗口所在屏（见
    /// <see cref="MonitorArea.Focused"/>，取不到时逐级退回鼠标所在屏、主屏），否则固定主屏。
    /// </summary>
    private static MonitorArea ResolveTarget()
    {
#if DEBUG
        // 调试用：给一个物理坐标点，把提示强制投到该点所在显示器。
        // 跨屏验证时不必真去抢焦点，设环境变量 WINTIPS_DEBUG_MONITOR_POINT="-2000,500" 即可；
        // 再加 WINTIPS_DEBUG_MONITOR_FILE=<文件> 可在进程运行期间改文件内容动态切换目标屏。
        if (DebugTargetPoint() is { } xy &&
            MonitorArea.FromHandle(NativeMethods.MonitorFromPoint(
                xy, NativeMethods.MONITOR_DEFAULTTONEAREST)) is { } area)
            return area;
#endif
        return AppSettings.Instance.TipFollowFocus ? MonitorArea.Focused() : MonitorArea.Primary();
    }

#if DEBUG
    /// <summary>
    /// 调试钩子：解析「强制目标显示器」的物理坐标点。文件优先于环境变量，
    /// 这样运行中的实例也能通过改写文件切换目标屏，做跨屏/跨缩放的自动化验证。
    /// </summary>
    private static NativeMethods.POINT? DebugTargetPoint()
    {
        string? spec = Environment.GetEnvironmentVariable("WINTIPS_DEBUG_MONITOR_POINT");
        if (Environment.GetEnvironmentVariable("WINTIPS_DEBUG_MONITOR_FILE") is { Length: > 0 } file)
        {
            try { if (System.IO.File.Exists(file)) spec = System.IO.File.ReadAllText(file).Trim(); }
            catch { }
        }
        if (string.IsNullOrEmpty(spec)) return null;

        var parts = spec.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
            return null;
        return new NativeMethods.POINT { X = x, Y = y };
    }
#endif

    /// <summary>WPF 当前认为的窗口缩放（物理像素 → DIP），取自渲染变换。</summary>
    private double CurrentScale()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return 1.0;
        double m = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return m > 0 ? m : 1.0;
    }

    /// <summary>
    /// 让 WPF 的缩放对齐目标显示器，并顺手把窗口摆到 (left, top)（DIP，目标屏坐标系）。
    /// 做法不是自己造一条 WM_DPICHANGED，而是按目标屏的物理矩形把窗口真搬过去——
    /// 这和用户拖动窗口走同一条路径，系统会主动投递真实的 DPI 变化消息，WPF 的
    /// TransformToDevice 随之更新（隐藏窗口同样有效，实测如此）。
    /// 系统万一没跟上，再补发一条合成消息兜底。
    /// </summary>
    private void SyncDpi(double left, double top)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || _target.Handle == IntPtr.Zero) return;

            double want = _target.Scale > 0 ? _target.Scale : 1.0;
            if (Math.Abs(CurrentScale() - want) <= 0.01) return;

            PlacePhysical(left, top); // 真实移动：让系统自己发现窗口换了屏
            if (Math.Abs(CurrentScale() - want) <= 0.01) return;

            // 查不到这块屏的 DPI 就用 _target 上已经记下的缩放反推（它本身也有系统 DPI 兜底），
            // 之前这里直接 return，窗口的缩放会一直停在上一条屏，落位就差一个缩放比。
            uint dpiX = 0, dpiY = 0;
            if (NativeMethods.GetDpiForMonitor(_target.Handle, NativeMethods.MDT_EFFECTIVE_DPI,
                    out dpiX, out dpiY) != 0 || dpiX == 0)
            {
                dpiX = dpiY = (uint)Math.Round(want * 96);
            }

            int x = (int)Math.Round(left * want), y = (int)Math.Round(top * want);
            int w = (int)Math.Round(Width * want), h = (int)Math.Round(Height * want);
            var rect = new NativeMethods.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
            NativeMethods.SendMessageW(hwnd, NativeMethods.WM_DPICHANGED,
                (IntPtr)(dpiX | (dpiY << 16)), ref rect);
        }
        catch { }
    }

    /// <summary>
    /// 窗口的 DPI 变了（被搬到另一块缩放不同的屏上，或用户改了缩放）。
    /// WPF 会按"物理尺寸守恒"把 Left/Top/Width/Height 一并按比例改写（实测 Width 会被乘上
    /// 新/旧缩放比，160 → 240 或 240 → 160），沿用下去浮窗就会忽大忽小、位置也偏。
    /// 浮窗内容是固定 DIP 设计（160×50 基准，再乘用户设定的"浮窗大小"倍率），
    /// 所以这里按目标屏重算尺寸与位置，把窗口摆回该在的地方。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        WidthCache.Clear(); // 字形宽度随 DPI 的舍入略有差异，换 DPI 后缓存作废

        // WPF 处理 WM_DPICHANGED 时会先套用系统给的建议矩形（按物理尺寸守恒缩放），
        // 那个矩形往往不是浮窗该在的位置/尺寸；这里紧跟着按目标屏重排一次，把窗口摆正。
        // 隐藏时也照做：下次弹出若沿用这个错尺寸，会出现一整轮大小不对的提示。
        UpdateSize();
        PositionOnTarget();
    }

    private void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool dark = ThemeHelper.IsDarkTheme;

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);
        DwmChrome.TrySetCorner(hwnd, NativeMethods.DWMWCP_ROUND);

        // 只有外观真的变了才重建画刷并重新赋予控件：Foreground 每次赋值都会
        // 触发整棵可视树的继承值重算，而 ApplyChrome 在每次弹窗时都会被调用。
        var key = (dark, ThemeHelper.HighContrast, ThemeHelper.AccentColor);
        if (!_chromeReady || !key.Equals(_chromeKey))
        {
            _chromeKey = key;
            _chromeReady = true;

            _surfaceBrush = Frozen(ThemeHelper.OsdSurface(dark));
            _textBrush = Frozen(ThemeHelper.TextPrimary(dark));
            _accentBrush = Frozen(ThemeHelper.AccentColor);
            _onAccentBrush = Frozen(ThemeHelper.OnAccent(dark));

            Foreground = _textBrush;
            LockIndicator.Fill = _accentBrush;
            CountBadge.Background = _accentBrush;
            CountText.Foreground = _onAccentBrush;
        }

        // 系统不支持亚克力时退回纯色卡片（Win10、高对比度、系统关掉透明效果、
        // 节电模式都必须走这条分支，否则 -1 扩展边框会让浮窗变成 Win7 Aero 式的实心色块）。
        // 节电模式/透明开关不会通知窗口，所以这一项每次弹出都得现查。
        Root.Background = ThemeHelper.AcrylicAllowed &&
                          DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_TRANSIENTWINDOW)
            ? Brushes.Transparent
            : _surfaceBrush;
    }

    public void PreWarm()
    {
        try
        {
            _target = ResolveTarget();
            // 先只创建句柄、不显示窗口。原地 Show/Hide 预热会让冷启动时两者
            // 之间的帧被 DWM 合成出来：Root 全透明时系统背景（Mica）可见，
            // 表现为开机首次启动闪现一个空窗口。EnsureHandle 同样会触发
            // OnSourceInitialized，句柄就绪后首次 ShowTip 无创建迟滞。
            if (new WindowInteropHelper(this).EnsureHandle() == IntPtr.Zero) return;
            ApplyChrome();
            Reposition();
            _visible = false;
            WarmRenderPipeline();
        }
        catch { }
    }

    /// <summary>
    /// 把窗口移到虚拟屏幕外，真实渲染一帧后再隐藏并复位。只建句柄不渲染时，
    /// WPF 渲染线程、D3D 设备与字体光栅都要等到首次 ShowTip 才初始化：开机
    /// 后第一次弹出，窗口已显示而内容帧迟到，表现为先见“空提示”、内容随后
    /// 才出现。屏幕外的窗口不会被 DWM 合成，因此不会像原地 Show/Hide 预热
    /// 那样闪现空窗口。
    /// </summary>
    private void WarmRenderPipeline()
    {
        if (_visible) return; // 真实提示已在显示（首弹早于预热），不能再挪动窗口

        LockTextBlock.Text = TipInfo.Describe(TipKind.CapsLock, true);
        SetPoseInstant(true);
        UpdateSize();

        Left = SystemParameters.VirtualScreenLeft - Width - 1000;
        Top = SystemParameters.VirtualScreenTop;
        Show();

        EventHandler rendered = (_, _) => FinishWarmRender();
        _warmRendered = rendered;
        CompositionTarget.Rendering += rendered;

        // 兜底：万一 Rendering 事件未按预期触发，也要把窗口收回并复位
        _warmGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _warmGuard.Tick += (_, _) => FinishWarmRender();
        _warmGuard.Start();
    }

    private void FinishWarmRender()
    {
        if (_warmRendered == null) return;
        CompositionTarget.Rendering -= _warmRendered;
        _warmRendered = null;
        _warmGuard?.Stop();
        _warmGuard = null;
        if (_visible) return; // 真实提示已弹出，位置交给 ShowTip 管理
        Hide();
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        // 不按"移出去之前的 Left/Top"手动复原：那段值此刻可能已经过期（DPI 或目标屏变了），
        // 直接按目标屏重算一次落点，DIP 与物理矩形一起摆正。
        PositionOnTarget();
    }

    public void ShowTip(TipKind kind, bool? state, double seconds)
    {
        // 目标显示器在弹出这一刻定下：跟随焦点时就是用户此刻正在操作的那块屏。
        // DPI 对齐与落位统一在 Reposition 里做（它知道最终落点，能借这次移动顺带换屏）。
        var target = ResolveTarget();

        // 换屏弹出：上一轮提示还没收起就换到另一块屏（尤其两块屏缩放不同）时，
        // WPF 改 DPI 会把 Width/Height 按比例改写再被我们改回来，可见状态下会闪一帧
        // 错尺寸的卡片。这里直接按“重新弹出”处理——内容先透明，重排完再淡入。
        if (_visible && target.Handle != _target.Handle)
        {
            StopFollow();
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            Root.Opacity = 0;
            _visible = false;
        }

        _target = target;

        var prevKind = _currentKind;
        _currentKind = kind;
        _generation++;

        // 锁键提示 / 剪贴板提示共用同一浮窗，按类型切换内容与徽章可见性
        ApplyMode(kind);
        bool on = SetStatusText(kind, state);

        if (IsClip(kind))
        {
            // 浮窗显示期间重复同一动作 → ×N 累加；切换动作或重新弹出 → 从 ×1 开始
            bool sameVisible = _visible && prevKind == kind &&
                               CountBadge.Visibility == Visibility.Visible;
            _countIncreased = sameVisible;
            _clipCount = sameVisible ? _clipCount + 1 : 1;
            CountText.Text = "\u00D7" + _clipCount.ToString(CultureInfo.InvariantCulture);
        }

        UpdateSize();

        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10));

        if (_visible)
        {
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            Root.Opacity = 1;
            Reposition();
            AnimateStatus(on);
            RestartTimer();
            StartFollow();
            return;
        }

        bool ready = new WindowInteropHelper(this).Handle != IntPtr.Zero;

        // 无论句柄是否就绪都先算一次落点：句柄未就绪时 Reposition 只写 Left/Top（物理钉位会跳过），
        // 这样下面的 PlacePhysical 不会拿到尚未初始化的 NaN 坐标——NaN 转 int 会得到
        // int.MinValue，窗口先被扔到虚拟屏外再由 Reposition 拉回，多屏下这一帧格外显眼。
        ApplyChrome();
        Reposition();

        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        if (ms <= 0)
        {
            AnimateStatus(on); // 无动画：显示前直接落到目标状态，避免首帧闪旧值
        }
        else
        {
            if (!_shownOnce)
                SetPoseInstant(!on); // 首次弹出：先把锁具摆到按键前的真实状态（新状态取反），
                                     // 过渡从真实旧状态起落，与后续弹窗一致，而非 XAML 默认值
            Root.Opacity = 0;
        }

        Show();
        // Show() 之后 WPF 可能按自己缓存的实际尺寸重设一次 HWND，再钉一次物理矩形
        PlacePhysical(_lastLeft, _lastTop);
        _visible = true;
        _shownOnce = true;
        if (!ready)
        {
            ApplyChrome(); // 句柄刚建好：亚克力、深色标题栏都要求先有 HWND
            Reposition();
        }
        AnimateIn();
        // 动画时钟在窗口隐藏时也照常走表，提前启动会在显示前被消耗一段，
        // 表现为锁扣先动完、指示条才“开始动”；显示后再启动才同步。
        if (ms > 0) AnimateStatus(on);
        RestartTimer();
        StartFollow();
    }

    public void UpdateState(TipKind kind, bool on)
    {
        if (!_visible || kind != _currentKind) return;
        SetStatusText(kind, on);
        UpdateSize();
        Reposition();
        AnimateStatus(on);
        RestartTimer();
    }

    /// <summary>
    /// 设置里拖动“浮窗大小”滑杆时的实时预览：没有提示在显示就按当前外观弹一次（含入/出场动画），
    /// 已经在显示就只按新尺寸重排并延长停留——刻意不重播开/关动效，否则滑杆每跨一格刻度
    /// 锁扣都会回弹一次，看着像在抖。
    /// </summary>
    public void PreviewSize(double seconds)
    {
        int ms = (int)Math.Clamp(seconds, 0.5, 10);
        if (!_visible)
        {
            ShowTip(TipKind.CapsLock, true, seconds);
            return;
        }

        UpdateSize();
        Reposition();
        // 正好在退场淡出途中时接管回来，否则尺寸变了却仍在淡出，看不见预览
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 1;
        _hideTimer.Interval = TimeSpan.FromSeconds(ms);
        RestartTimer();
    }

    private bool SetStatusText(TipKind kind, bool? state)
    {
        LockTextBlock.Text = TipInfo.Describe(kind, state);
        return !TipInfo.IsToggle(kind) || state == true;
    }

    /// <summary>按提示类型切换浮窗内容：锁具+指示条（锁键）或 对勾/剪刀+计数徽章（剪贴板）。</summary>
    private void ApplyMode(TipKind kind)
    {
        bool clip = IsClip(kind);
        LockSymbol.Visibility = clip ? Visibility.Collapsed : Visibility.Visible;
        LockIndicator.Visibility = clip ? Visibility.Collapsed : Visibility.Visible;
        ClipSymbol.Visibility = clip ? Visibility.Visible : Visibility.Collapsed;
        CountBadge.Visibility = clip ? Visibility.Visible : Visibility.Collapsed;

        bool cut = kind == TipKind.Cut;
        CheckMark.Visibility = cut ? Visibility.Collapsed : Visibility.Visible;
        CutGlyph.Visibility = cut ? Visibility.Visible : Visibility.Collapsed;

        ApplyBarSide();
    }

    /// <summary>
    /// 按指示条的贴边侧摆放内容区与指示条本体：指示条在卡片下缘时内容区上 6 / 下 10
    /// （底部给指示条留空间），镜像到上缘时上下对调成上 10 / 下 6；剪贴板模式没有指示条，
    /// 上下对称留 8。
    /// </summary>
    private void ApplyBarSide()
    {
        // 剪贴板模式：右侧只留 8px，让 ×N 徽章贴近卡片右缘，避免挤占文字空间。
        if (IsClip(_currentKind))
        {
            ContentGrid.Margin = new Thickness(16, 8, 8, 8);
            return;
        }

        // 锁键模式：左 16 / 右 12（原设计）
        ContentGrid.Margin = _barOnTop ? new Thickness(16, 10, 12, 6) : new Thickness(16, 6, 12, 10);
        LockIndicator.VerticalAlignment = _barOnTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        LockIndicator.Margin = _barOnTop ? new Thickness(0, 7, 0, 0) : new Thickness(0, 0, 0, 7);
    }

    /// <summary>
    /// 浮窗挂在屏幕顶部区域时，把指示条从底部镜像到文字上方，让它始终贴近浮窗所挂靠的
    /// 那条屏幕边（与滑入方向同侧），而不是夹在文字与屏幕内容之间。判据与滑入方向的
    /// 中部回落一致：浮窗中心在上半屏即视为“顶部区域”，自定义位置同样适用。
    /// 用落点 _lastTop 而非 Top：滑入/滑出动画期间 Top 是中间值，靠中轴时会被判反。
    /// </summary>
    private void UpdateBarSide()
    {
        bool onTop = _lastTop + Height / 2 < _target.CenterY;
        if (onTop == _barOnTop) return;
        _barOnTop = onTop;
        ApplyBarSide();
    }

    /// <summary>
    /// 锁扣旋转/回弹与指示条宽度/透明度用同一时长、同一缓动，一齐起落。
    /// 需在窗口可见后调用（窗口隐藏时动画时钟照常走表，会提前消耗）。
    /// </summary>
    private void AnimateStatus(bool on)
    {
        if (IsClip(_currentKind))
        {
            AnimateClipStatus(instant: false);
            return;
        }

        int baseMs = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        if (baseMs <= 0)
        {
            SetPoseInstant(on);
            return;
        }

        double indicatorOpacity = on ? 1.0 : 0.2;
        double indicatorWidth = on ? 60.0 : 36.0;
        double shackleAngle = on ? 0.0 : 25.0;

        var duration = new Duration(TimeSpan.FromMilliseconds((int)(baseMs / 1.5)));
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation(indicatorOpacity, duration) { EasingFunction = ease };
        LockIndicator.BeginAnimation(OpacityProperty, opacityAnim);

        var widthAnim = new DoubleAnimation(indicatorWidth, duration) { EasingFunction = ease };
        LockIndicator.BeginAnimation(WidthProperty, widthAnim);

        var rotationAnim = new DoubleAnimation(shackleAngle, duration) { EasingFunction = ease };
        ShackleRotation.BeginAnimation(RotateTransform.AngleProperty, rotationAnim);

        var bounceAnim = new DoubleAnimationUsingKeyFrames { Duration = duration };
        bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.1),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        ShackleBounce.BeginAnimation(TranslateTransform.YProperty, bounceAnim);
    }

    /// <summary>
    /// 剪贴板提示动效：对勾一笔描出（StrokeDashOffset），剪切图标淡入；
    /// 计数徽章首次淡入、累加时缩放弹跳。
    /// </summary>
    private void AnimateClipStatus(bool instant)
    {
        int baseMs = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        if (instant || baseMs <= 0)
        {
            CheckMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
            CheckMark.StrokeDashOffset = 0;
            CountBadge.BeginAnimation(OpacityProperty, null);
            CountBadge.Opacity = 1;
            CutGlyph.BeginAnimation(OpacityProperty, null);
            CutGlyph.Opacity = 1;
            BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            BadgeScale.ScaleX = BadgeScale.ScaleY = 1;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds((int)(baseMs / 1.5)));
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        if (CheckMark.Visibility == Visibility.Visible)
        {
            var draw = new DoubleAnimation(8.75, 0, duration) { EasingFunction = ease };
            CheckMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, draw);
        }
        else
        {
            CutGlyph.Opacity = 0;
            CutGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        }

        if (_countIncreased)
        {
            // 连续操作：徽章缩放弹跳 1.0 → 1.3 → 1.0（两个属性各需独立的动画实例）
            DoubleAnimationUsingKeyFrames MakePop() =>
                new()
                {
                    Duration = new Duration(TimeSpan.FromMilliseconds(baseMs)),
                    KeyFrames =
                    {
                        new EasingDoubleKeyFrame(1.3, KeyTime.FromPercent(0.3),
                            new CubicEase { EasingMode = EasingMode.EaseOut }),
                        new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0),
                            new CubicEase { EasingMode = EasingMode.EaseInOut }),
                    },
                };
            BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, MakePop());
            BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, MakePop());
        }
        else
        {
            CountBadge.Opacity = 0;
            CountBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        }
    }

    /// <summary>不做过渡，直接把锁扣与指示条摆到指定开/关姿态。</summary>
    private void SetPoseInstant(bool on)
    {
        if (IsClip(_currentKind))
        {
            // 剪贴板提示的“起始姿态”：对勾未描出（等显示后再动画画出）
            CheckMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
            CheckMark.StrokeDashOffset = 8.75;
            CountBadge.BeginAnimation(OpacityProperty, null);
            CountBadge.Opacity = 1;
            CutGlyph.BeginAnimation(OpacityProperty, null);
            CutGlyph.Opacity = 1;
            return;
        }

        LockIndicator.BeginAnimation(OpacityProperty, null);
        LockIndicator.Opacity = on ? 1.0 : 0.2;
        LockIndicator.BeginAnimation(WidthProperty, null);
        LockIndicator.Width = on ? 60.0 : 36.0;
        ShackleRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        ShackleRotation.Angle = on ? 0.0 : 25.0;
        ShackleBounce.BeginAnimation(TranslateTransform.YProperty, null);
        ShackleBounce.Y = 0;
    }

    /// <summary>
    /// 浮窗基准高度（DIP）：内容按这套 1.0 倍基准布局，用户改大小只缩放这套基准，
    /// 所以 UpdateSize / 徽章居中这类像素计算一律留在基准坐标系里，不掺缩放倍率。
    /// </summary>
    private const double BaseHeight = 50;

    private void UpdateSize()
    {
        double scale = AppSettings.NormalizeScale(AppSettings.Instance.TipScale);
        ApplyScale(scale);

        double textWidth = MeasureText(LockTextBlock.Text);
        bool clip = CountBadge.Visibility == Visibility.Visible;
        double extra = clip
            ? MeasureText(CountText.Text, 12.5, FontWeights.SemiBold) + 22 // 徽章宽 + 右缘留白
            : 0;
        // 基准宽度：下面所有坐标与 MeasureText 的结果都在这套基准里，最后统一乘缩放倍率
        double baseWidth = Math.Max(160, Math.Ceiling(16 + 22 + 18 + textWidth + 12 + extra));

        if (clip)
        {
            // 剪贴板模式：文字在“对勾图标 ↔ ×N 徽章”之间均匀居中（两侧间距相等），
            // 视觉上不再偏向徽章；该中点与卡片中线几乎重合。
            // 注意：TextBlock 的 Margin 是相对内容区（窗口左边距 16）的坐标，
            // 而 iconEnd/badgeStart 是窗口坐标，需先减去 16 换算，否则文字整体右偏 16px。
            // 这里必须用基准宽度而非窗口 Width：内容整体挂在缩放变换下，Margin 也是基准单位。
            double pillWidth = MeasureText(CountText.Text, 12.5, FontWeights.SemiBold) + 14; // 徽章内边距 7+7
            const double gridLeft = 16;                // 内容区原点（窗口坐标）
            const double iconEnd = 22;                 // 图标右缘（内容区坐标）
            double badgeStart = baseWidth - gridLeft - 8 - pillWidth; // 徽章左缘（内容区坐标，距窗口右 8px）
            double mid = (iconEnd + badgeStart) / 2;
            double left = Math.Max(iconEnd + 8, mid - textWidth / 2);
            LockTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
            LockTextBlock.Margin = new Thickness(left, 0, 0, 0);
        }
        else
        {
            // 锁键模式保持原版：居中 + 18px 左边距补偿图标
            LockTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
            LockTextBlock.Margin = new Thickness(18, 0, 0, 2);
        }

        // 窗口尺寸 = 基准尺寸 × 倍率：变换只改渲染，窗口本身必须同步放大，
        // 否则内容会被裁在窗口外、贴边与居中计算也全按未缩放的尺寸走。
        Width = Math.Ceiling(baseWidth * scale);
        Height = Math.Ceiling(BaseHeight * scale);
    }

    /// <summary>内容根节点的等比缩放变换：字形、矢量图标、指示条、圆角一起跟随，无需逐控件调字号。</summary>
    private ScaleTransform? _rootScale;

    private void ApplyScale(double scale)
    {
        if (_rootScale is null)
        {
            _rootScale = new ScaleTransform(scale, scale);
            Root.LayoutTransform = _rootScale;
            return;
        }
        if (Math.Abs(_rootScale.ScaleX - scale) < 0.001) return;
        _rootScale.ScaleX = _rootScale.ScaleY = scale;
    }

    private double MeasureText(string text, double size = 14, FontWeight? weight = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var face = weight ?? FontWeights.Medium;
        var key = (text, size, face);
        if (WidthCache.TryGetValue(key, out double cached)) return cached;

        double pixelsPerDip = 1.0;
        try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

        // 字号必须用调用方给的 size：之前这里写死 14，导致 ×N 徽章（12.5pt）按 14pt 量宽
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            face == FontWeights.SemiBold ? SemiBoldFace : MediumFace, size, Brushes.Black, pixelsPerDip);
        double width = ft.Width;

        // 文案集合有限，攒到上限直接清空即可（不清会随语言/DPI 切换缓慢增长）
        if (WidthCache.Count >= 256) WidthCache.Clear();
        WidthCache[key] = width;
        return width;
    }

    /// <summary>浮窗该落在哪（DIP，目标显示器坐标系）：有自定义位置用自定义偏移，否则按预设（跟随任务栏当前形态）。</summary>
    private (double Left, double Top) TargetPosition()
    {
        var s = AppSettings.Instance;
        return s.HasCustomPosition
            ? OsdPlacement.CustomFor(s.CustomLeft!.Value, s.CustomTop!.Value, Width, Height, _target)
            : OsdPlacement.DefaultFor(AppSettings.NormalizePlacement(s.TipPlacement),
                AppSettings.NormalizeVertical(s.TipVertical), Width, Height, _target);
    }

    private void Reposition()
    {
        // _target 是弹出那一刻的快照：句柄已失效（拔线、改排列）说明快照过期了，
        // 继续用它算就会把浮窗摆到一块不存在的屏幕上。
        if (!_target.IsLive) _target = ResolveTarget();

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        // 位置是 DIP，WPF 按窗口当前缩放换算物理像素：先算好落点，再借这次移动把
        // 窗口搬到目标屏上让系统对齐缩放（跨屏不同缩放时，顺序反了就会整体偏出去）
        var (left, top) = TargetPosition();
        SyncDpi(left, top);

        // 换屏时 WPF 会顺手改写 Width/Height（实测 160 → 160/1.5），必须按目标屏重算回来，
        // 否则浮窗会带着放错比例的尺寸和落点一直显示（这就是跨屏后"大小不正常"的来源）
        UpdateSize();
        PositionOnTarget();
    }

    /// <summary>
    /// 按 <see cref="_target"/> 摆位（不碰 DPI，避免在 DPI 变化回调里重入）。
    /// 落点先算成"目标屏坐标系里的 DIP"，再统一交给 <see cref="Place"/>。
    /// </summary>
    private void PositionOnTarget()
    {
        var (left, top) = TargetPosition();
        _lastLeft = _followLeft = left;
        _lastTop = _followTop = top;
        UpdateBarSide();
        Place();
    }

    /// <summary>
    /// 唯一落位入口：按目标屏缩放钉一次物理矩形，再把落点换算成 WPF 的 DIP 写回 Left/Top。
    /// 为什么不能只写 Left/Top：WPF 用"窗口此刻的缩放"把它们换成物理像素，而落点是按
    /// "目标屏缩放"算出来的。窗口缩放没跟上目标屏时（系统没投递 WM_DPICHANGED、隐藏窗口、
    /// 句柄刚建），两条换算比例不同，之后任意一次布局/Show/改尺寸引发的 SetWindowPos
    /// 都会把窗口搬偏一整段 —— 表现就是"弹出来是对的，随后自己跳走"。
    /// 也不能只钉物理矩形：WPF 不知道被挪过，下一次布局仍按旧 DIP 重算。
    /// 两边都写、且写的是同一个物理点，才不会有谁覆盖谁的问题。
    /// </summary>
    private void Place()
    {
        // 顺序很关键：先钉物理矩形（这一步可能顺带触发系统投递 WM_DPICHANGED，
        // 让窗口缩放跟上目标屏），再按"此刻"的缩放把 DIP 基值回写成同一个物理点。
        // 反过来的话，回写用的还是旧缩放，而回写过程中触发的 DPI 变化会把基值再改一次，
        // 之后任意一次布局回写就会把窗口搬偏。
        PlacePhysical(_lastLeft, _lastTop);
        Left = ToWindowDip(_lastLeft);
        Top = ToWindowDip(_lastTop);
    }

    /// <summary>
    /// 目标屏 DIP → WPF 窗口 DIP 的比例。窗口缩放与目标屏一致时恒为 1（绝大多数情况，
    /// 此时所有落位/动画代码等价于直接使用目标屏 DIP）。
    /// </summary>
    private double WindowDipRatio()
    {
        double want = _target.Scale > 0 ? _target.Scale : 1.0;
        double cur = CurrentScale();
        return cur <= 0 || Math.Abs(cur - want) < 0.0001 ? 1.0 : want / cur;
    }

    /// <summary>目标屏坐标系的 DIP → WPF 窗口坐标系的 DIP（见 <see cref="WindowDipRatio"/>）。</summary>
    private double ToWindowDip(double targetDip) => targetDip * WindowDipRatio();

    /// <summary>
    /// 按目标屏的物理像素矩形把窗口钉死（DIP × 该屏缩放）。入参是**目标屏坐标系**的 DIP，
    /// 不是 WPF 的 Left/Top（窗口缩放没跟上目标屏时两者不同，见 <see cref="Place"/>）。
    /// 换屏换缩放时 WPF 会按"物理尺寸守恒"套用系统给的建议矩形，而 Width/Height 的 DIP 值
    /// 前后一样，它就不会再把窗口改回 DIP×新缩放——从 150% 的屏切回 100% 的屏之后，
    /// 浮窗会一直保持 1.5 倍的物理尺寸（实测 240×75 而非 160×50）。位置与尺寸因此不再
    /// 依赖 WPF 的 DPI 时序，一律由这里按目标屏缩放直接换算。
    /// </summary>
    private void PlacePhysical(double left, double top)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        double scale = _target.Scale > 0 ? _target.Scale : 1.0;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            (int)Math.Round(left * scale), (int)Math.Round(top * scale),
            (int)Math.Round(Width * scale), (int)Math.Round(Height * scale),
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 实时跟随任务栏：浮窗可见期间按 60ms 轮询任务栏的真实窗口矩形——单次约 7µs
    /// （只走 MonitorFromPoint / FindWindow / GetWindowRect，刻意不碰约 365µs 的 ABM 查询），
    /// 位置没变立刻返回；变了才用 140ms 缓动挪过去。
    /// 任务栏收起时浮窗跟到屏幕边缘，滑出时自动让到它上方，不会再有“被滑出的任务栏压住”的窗口。
    /// </summary>
    private void FollowTaskbar()
    {
        if (!_visible) return;

        var (left, top) = TargetPosition();
        if (Math.Abs(left - _followLeft) < 0.5 && Math.Abs(top - _followTop) < 0.5) return;

        _followLeft = left;
        _followTop = top;
        _lastLeft = left;
        _lastTop = top;
        UpdateBarSide(); // 跟随可能跨过半屏，指示条该不该镜像要跟着变

        bool animated = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed) > 0;
        MoveContentTo(left, top, animated ? FollowMs : 0);
    }

    /// <summary>
    /// 启动跟随轮询：只在“开了自动隐藏 + 用的是预设位置”时才需要——任务栏不会滑出/收起，
    /// 或者位置被用户钉死了，都没有可跟的东西，那就一次也不跑。
    /// 副屏任务栏（Shell_SecondaryTrayWnd）同样会被跟随：可用区域每次现查，见 <see cref="TaskbarInfo.UsableRect"/>。
    /// </summary>
    private void StartFollow()
    {
        _followTimer.Stop();
        if (AppSettings.Instance.HasCustomPosition) return;
        if (!TaskbarInfo.IsAutoHide()) return;
        _followLeft = _lastLeft;
        _followTop = _lastTop;
        _followTimer.Start();
    }

    private void StopFollow()
    {
        _followTimer.Stop();
        _moveGen++; // 让在途跟随动画的收尾回调失效，别去抢收起动画对 Left/Top 的控制
    }

    /// <summary>把浮窗平滑挪到目标位置（跟随任务栏时用）。入参是目标屏坐标系的 DIP。</summary>
    private void MoveContentTo(double left, double top, int ms)
    {
        double toLeft = ToWindowDip(left), toTop = ToWindowDip(top);

        if (ms <= 0)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = toLeft;
            Top = toTop;
            PlacePhysical(left, top);
            return;
        }

        double fromLeft = Left, fromTop = Top;
        int gen = ++_moveGen;
        var span = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animTop = new DoubleAnimation(fromTop, toTop, span) { EasingFunction = ease };
        // 动画结束后把基值落到目标：否则之后任何一次 BeginAnimation(Left/Top, null)
        // 都会把窗口弹回动画前的旧基值
        animTop.Completed += (_, _) =>
        {
            if (gen != _moveGen || !_visible) return;
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = toLeft;
            Top = toTop;
            PlacePhysical(left, top);
        };

        BeginAnimation(LeftProperty, new DoubleAnimation(fromLeft, toLeft, span) { EasingFunction = ease });
        BeginAnimation(TopProperty, animTop);
    }

    /// <summary>
    /// 入场/退场滑动偏移：贴边方向决定滑动轴（左右 15% 区横向滑入、上下 15% 区竖向滑入、
    /// 四角对角滑入），落在屏幕中部时回落到竖向。始终朝最近的屏幕边缘收起。
    /// </summary>
    private (double Dx, double Dy) SlideOffset(double left, double top) =>
        OsdPlacement.SlideOffset(left, top, Width, Height, _target);

    private void AnimateIn()
    {
        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        if (ms <= 0)
        {
            Root.Opacity = 1;
            return;
        }
        Root.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Root.BeginAnimation(UIElement.OpacityProperty, fade);

        // 滑动偏移按落点（目标屏坐标系）算，写回 Left/Top 时再换算成窗口 DIP
        var (dx, dy) = SlideOffset(_lastLeft, _lastTop);
        var span = TimeSpan.FromMilliseconds(ms);

        if (dx != 0)
        {
            BeginAnimation(LeftProperty, new DoubleAnimation(
                ToWindowDip(_lastLeft + dx), ToWindowDip(_lastLeft), span)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        if (dy != 0)
        {
            BeginAnimation(TopProperty, new DoubleAnimation(
                ToWindowDip(_lastTop + dy), ToWindowDip(_lastTop), span)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    private void HideAnimated()
    {
        if (!_visible) return;
        StopFollow(); // 收起动画期间不再跟随，免得跟随动画和滑出动画抢 Left/Top
        int gen = _generation;
        double targetLeft = _lastLeft;
        double targetTop = _lastTop;
        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);

        void FinishHide()
        {
            Hide();
            _visible = false;
            _clipCount = 0; // 浮窗收起后连续计数归零
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Root.Opacity = 1;
            Left = ToWindowDip(targetLeft);
            Top = ToWindowDip(targetTop);
            PlacePhysical(targetLeft, targetTop); // 收起后把物理矩形也留在落点上，下次弹出直接从这儿起步
        }

        if (ms <= 0)
        {
            FinishHide();
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms));
        fade.Completed += (_, _) =>
        {
            if (gen != _generation) return;
            FinishHide();
        };
        Root.BeginAnimation(UIElement.OpacityProperty, fade);

        var (dx, dy) = SlideOffset(targetLeft, targetTop);
        var span = TimeSpan.FromMilliseconds(ms);

        if (dx != 0)
        {
            BeginAnimation(LeftProperty, new DoubleAnimation(ToWindowDip(targetLeft + dx), span)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
        }
        if (dy != 0)
        {
            BeginAnimation(TopProperty, new DoubleAnimation(ToWindowDip(targetTop + dy), span)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
        }
    }

    private void RestartTimer()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
