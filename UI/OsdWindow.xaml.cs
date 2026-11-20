using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

public partial class OsdWindow : Window
{
    private const int FollowIntervalMs = 60;

    private const int FollowMs = 140;

    private readonly DispatcherTimer _hideTimer = new();

    private readonly DispatcherTimer _followTimer = new();

    /// <summary>显示期间盯住置顶位置的看守，见 <see cref="TopmostGuard"/>。</summary>
    private readonly TopmostGuard _topmost = new();

    private bool _visible;
    private bool _shownOnce;
    private int _generation;
    private EventHandler? _warmRendered;
    private DispatcherTimer? _warmGuard;
    private TipKind _currentKind;

    private bool? _currentState;

    private string _languageText = string.Empty;

    private bool _welcome;
    private string _welcomeTitle = string.Empty;
    private string _welcomeSubtitle = string.Empty;

    private double _lastLeft;
    private double _lastTop;
    private double _followLeft;
    private double _followTop;
    private int _moveGen;

    private MonitorArea _target = MonitorArea.Primary();

    public event Action? ClipboardUpdated;

    /// <summary>
    /// 上一次剪贴板更新的序号，注册监听时取基线。MMC 宿主程序（设备管理器、
    /// services.msc 等一切 mmc.exe 控制台）启动时会向所有剪贴板监听窗口广播
    /// WM_CLIPBOARDUPDATE，但剪贴板内容根本没有变——序号不动。只在剪贴板里
    /// 已有内容时才现形，正好撞上"有旧内容就弹卡"的回退路径。序号不变即视为
    /// 幻影消息，在这里拦掉，不让它消耗按键意图或触发非键盘复制提示。
    /// </summary>
    private uint _clipboardSeq;

    private int _clipCount;
    private bool _countIncreased;

    private bool _barOnTop;

    private static bool IsClip(TipKind k) => TipInfo.IsClipboard(k);

    private FontFamily _osdFont = AppFont.Ui;
    private Typeface _mediumFace = CreateFace(AppFont.Ui, FontWeights.Medium);
    private Typeface _semiBoldFace = CreateFace(AppFont.Ui, FontWeights.SemiBold);

    private static Typeface CreateFace(FontFamily family, FontWeight weight) =>
        new(family, FontStyles.Normal, weight, FontStretches.Normal);

    private readonly Dictionary<(string Text, double Size, FontWeight Weight), double> _widthCache = new();

    private (bool Dark, bool HighContrast, Color Accent) _chromeKey;
    private bool _chromeReady;

    private bool _glassActive;

    private SolidColorBrush? _glassVeilBrush;

    private ImageBrush? _glassBrush;

    private BackdropSnapshot? _backdrop;

    private bool _backdropTracking;

    private int _backdropX;
    private int _backdropY;
    private int _backdropW;
    private int _backdropH;

    private Thread? _liveCapture;

    private ManualResetEventSlim? _liveWait;

    private IntPtr _liveHwnd;

    private int _livePadding;

    /// <summary>是否正提着系统定时器精度（要和 <see cref="NativeMethods.TimeEndPeriod"/> 配对）。</summary>
    private bool _liveTimerPrecise;

    /// <summary>抓屏循环排节拍用。Stopwatch 走 QPC，比 TickCount 的 15.6ms 分辨率可靠得多。</summary>
    private readonly Stopwatch _liveClock = Stopwatch.StartNew();

    /// <summary>抓屏线程的世代号。加一就等于叫停——比一个 bool 更能防住"旧线程还没退干净又起新的"。</summary>
    private volatile int _liveGeneration;

    // ---- 实时刷新的显示缓冲 ----

    /// <summary>显示位图两份轮换：只写当前没在显示的那份，写完再把填充切过去。</summary>
    private WriteableBitmap? _liveA;
    private WriteableBitmap? _liveB;
    private ImageBrush? _liveBrushA;
    private ImageBrush? _liveBrushB;
    private bool _liveShowA;

    /// <summary>上一帧真正显示出去的像素，用来跟新帧混一点，压掉低帧率下的跳变。</summary>
    private byte[]? _liveShown;

    /// <summary>新帧落点。跟上一帧混好之后才写进位图。</summary>
    private byte[]? _liveScratch;

    private int _liveFrameW;
    private int _liveFrameH;
    private bool _liveHasFrame;

    /// <summary>已有一帧交给 UI 线程、还没画出去。用来做背压，不让快照在消息队列里堆积。</summary>
    private int _livePending;

    /// <summary>上一帧窗口所在的位置，用来判断卡片是不是正在动。</summary>
    private int _liveRectX;
    private int _liveRectY;

    private const int GlassBlurRadius = 16;

    /// <summary>
    /// 实时刷新用的半径。两次方框模糊的 sigma 只有 radius×0.82，放大半径到 20 才跟三次、
    /// 半径 16 的观感持平，而每帧的卷积次数少了三分之一。
    /// </summary>
    private const int GlassLiveBlurRadius = 20;

    /// <summary>实时刷新的模糊次数（静态快照用 3 次）。</summary>
    private const int GlassLiveBlurPasses = 2;

    /// <summary>
    /// 抓不到实时刷新时，快照四周固定留这么多白（单位 DIP，抓屏前按缩放换成物理像素）：
    /// 模糊核半径（卡片边缘取样要用到外侧像素）+ 卡片会滑动的距离（滑入 20、跟随自动隐藏
    /// 的任务栏要一个任务栏厚度）。这段留白就是卡片能自由移动、快照又不用重抓的范围。
    /// </summary>
    private const int GlassBlurPadding = 72;

    /// <summary>
    /// 能实时刷新时留白可以小得多：每 <see cref="GlassLiveRefreshMs"/> 会重新抓一次并重新
    /// 对准卡片，留白只需盖住两次刷新之间挪动的距离。
    /// </summary>
    private const int GlassLivePadding = 40;

    /// <summary>
    /// 实时刷新的间隔。抓一次桌面要等一次桌面合成，再密也快不了，所以不追满帧；真正的
    /// 流畅感靠帧间混合补——否则 20 来帧的模糊背景一跳一跳，看着就是在闪。
    /// </summary>
    private const int GlassLiveRefreshMs = 22;

    /// <summary>
    /// 新帧在混合里的权重（256 为完全用新帧）。取 150 左右：一帧过去跟到六成、三帧基本
    /// 到位（约 100ms），既有"跟得上手"的即时感，又不会被二十几帧的内容跳变硌到眼睛。
    /// </summary>
    private const int GlassLiveBlendWeight = 150;

    private const double GlassCornerRadius = 8;

    private const double GlassStrokeThickness = 1;

    /// <summary>
    /// 系统圆角（DWMWA_WINDOW_CORNER_PREFERENCE）只有 Win11 认，Win10 上 DWM 一定给直角。
    /// 这种系统里把窗口换成 WPF 分层窗口，卡片自己画成圆角矩形：四角是真透明（透出桌面），
    /// 边缘由 WPF 抗锯齿，观感和 Win11 的 DWM 圆角一致，也不必等 DWM 来裁。
    /// </summary>
    private static readonly bool OwnCorners = !DwmChrome.SupportsFluent;

    /// <summary>缩回时卡片淡到的最低不透明度：留一层底子再随窗口消失，不淡成透明。</summary>
    private const double HideOpacityFloor = 0.9;

    private SolidColorBrush _surfaceBrush = Brushes.White;
    private SolidColorBrush _textBrush = Brushes.Black;
    private SolidColorBrush _accentBrush = Brushes.DodgerBlue;
    private SolidColorBrush _onAccentBrush = Brushes.White;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public OsdWindow()
    {
        InitializeComponent();
        // 分层窗口的样式在窗口句柄一创建就定死了，必须在 InitializeComponent 之后、
        // EnsureHandle 之前切换（WindowStyle 已经是 None，这一句才合法）。
        if (OwnCorners) AllowsTransparency = true;
        ReloadFont();
        AppFont.Changed += OnUiFontChanged;
        _hideTimer.Tick += (_, _) => HideAnimated();
        _followTimer.Interval = TimeSpan.FromMilliseconds(FollowIntervalMs);
        _followTimer.Tick += (_, _) => FollowTaskbar();
    }

    private void ReloadFont()
    {
        _osdFont = AppFont.Ui;
        _mediumFace = CreateFace(_osdFont, FontWeights.Medium);
        _semiBoldFace = CreateFace(_osdFont, FontWeights.SemiBold);
        _widthCache.Clear();
    }

    private void OnUiFontChanged()
    {
        ReloadFont();
        if (IsVisible) { UpdateSize(); }
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

        // 分层窗口的透明四角由 WPF 自己的 alpha 负责；再让 DWM 扩展边框进来只会招来
        // 一圈玻璃边框和发黑的角。
        if (!AllowsTransparency) DwmChrome.SetFrameExtended(hwnd, true);

        src.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(hwnd);
        // 幻影消息过滤的基线：注册之后送来的每条消息都以此刻的序号为准。
        _clipboardSeq = NativeMethods.GetClipboardSequenceNumber();
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmost.End();
        StopBackdropTracking();
        StopLiveBackdrop();
        AppFont.Changed -= OnUiFontChanged;
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
        {
            // 序号没动 = 内容没变，MMC 等程序启动时的幻影广播，直接吞掉。
            uint seq = NativeMethods.GetClipboardSequenceNumber();
            if (seq == _clipboardSeq) { handled = true; return IntPtr.Zero; }
            _clipboardSeq = seq;
            ClipboardUpdated?.Invoke();
        }
        else if (msg == NativeMethods.WM_DISPLAYCHANGE || msg == NativeMethods.WM_SETTINGCHANGE)
        {
            RefreshTarget();
            Reposition();
        }
        return IntPtr.Zero;
    }

    private void RefreshTarget() =>
        _target = MonitorArea.FromHandle(_target.Handle) ?? ResolveTarget();

    private static MonitorArea ResolveTarget()
    {
#if DEBUG
        if (DebugTargetPoint() is { } xy &&
            MonitorArea.FromHandle(NativeMethods.MonitorFromPoint(
                xy, NativeMethods.MONITOR_DEFAULTTONEAREST)) is { } area)
            return area;
#endif
        return AppSettings.Instance.TipFollowFocus ? MonitorArea.Focused() : MonitorArea.Primary();
    }

#if DEBUG
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

    private double CurrentScale()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return 1.0;
        double m = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return m > 0 ? m : 1.0;
    }

    private void SyncDpi(double left, double top)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || _target.Handle == IntPtr.Zero) return;

            double want = _target.Scale > 0 ? _target.Scale : 1.0;
            if (Math.Abs(CurrentScale() - want) <= 0.01) return;

            PlacePhysical(left, top);
            if (Math.Abs(CurrentScale() - want) <= 0.01) return;

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

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _widthCache.Clear();

        UpdateSize();
        PositionOnTarget();
    }

    private void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool dark = ThemeHelper.IsDarkTheme;

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);
        DwmChrome.TrySetCorner(hwnd, NativeMethods.DWMWCP_ROUND);

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
            WelcomeBadge.Fill = _accentBrush;
            WelcomeCheck.Stroke = _onAccentBrush;
        }

        // 实时模糊的卡片整块由桌面快照铺满，系统亚克力只会被压在快照底下。可那层是 DWM
        // 画的，WPF 的 Opacity 带不动它：淡入淡出时亚克力矩形始终是满不透明度，卡片淡到
        // 一半就露底、最后剩一块纯色再硬切消失。所以玻璃态下不叠亚克力，让整张卡片只走
        // WPF 的透明度。
        bool glass = GlassWanted;

        bool acrylic = !glass && ThemeHelper.AcrylicAllowed &&
                       DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_TRANSIENTWINDOW);

        if (glass) DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_NONE);

        // 有系统圆角（Win11）时底色可以直接铺满窗口，四角交给 DWM 裁；Win10 得留透明四角。
        Root.Background = OwnCorners || acrylic || glass ? Brushes.Transparent : _surfaceBrush;
        ApplyCardLayers(glass, dark);

        _glassActive = glass;
        if (!glass)
        {
            StopBackdropTracking();
            StopLiveBackdrop();
        }
    }

    /// 玻璃卡片有两套实现：Win11 上系统亚克力兜底，其余情况靠抓桌面快照自己模糊。
    /// 后者纯 GDI，不需要 DWM，只要有逐像素 alpha 就能跑——Win11 是 DWM 给的，
    /// Win10 是我们把窗口换成分层窗口换来的（见 OwnCorners），所以 Win10 也有模糊。
    private static bool GlassWanted =>
        (DwmChrome.SupportsBackdrop || OwnCorners) &&
        ThemeHelper.GlassAllowed && AppSettings.Instance.TipGlass;

    private void ApplyCardLayers(bool glass, bool dark)
    {
        if (!glass)
        {
            // 没有玻璃底时，纯色卡片要交给这张带圆角的矩形来画：它才是那个 8px 圆角，
            // Root 是方的。系统圆角可用时照旧铺满窗口，省掉一层不必要的透明合成。
            GlassImage.Fill = OwnCorners ? _surfaceBrush : null;
            GlassImage.Visibility = OwnCorners ? Visibility.Visible : Visibility.Collapsed;
            GlassPlate.Visibility = Visibility.Collapsed;
            return;
        }

        GlassPlate.Stroke = ThemeHelper.GlassStroke(dark);
        ApplyGlassVeil(dark);
        GlassImage.Visibility = Visibility.Visible;
        GlassPlate.Visibility = Visibility.Visible;
    }

    private void ApplyGlassVeil(bool dark)
    {
        var tint = ThemeHelper.GlassTintBase(dark);
        byte alpha = (byte)Math.Round(
            AppSettings.NormalizeGlassOpacity(AppSettings.Instance.TipGlassOpacity) * 255);

        _glassVeilBrush = Frozen(Color.FromArgb(alpha, tint.R, tint.G, tint.B));
        GlassPlate.Fill = _glassVeilBrush;
    }

    /// <summary>
    /// 抓一张桌面快照做玻璃底。必须在窗口弹出之前调用——窗口已经在屏幕上时抓屏，
    /// 拍到的就是卡片自己。
    /// </summary>
    private void UpdateBackdrop()
    {
        if (!_glassActive) return;

        int padding = (int)Math.Ceiling(GlassBlurPadding * Math.Max(1.0, CurrentScale()));

        var previous = _backdrop;
        _backdrop = BackdropCapture.Capture(
            new WindowInteropHelper(this).Handle,
            padding,
            GlassBlurRadius);
        previous?.Release();

        if (_backdrop is null)
        {
            // 抓不到桌面就退回纯色卡片，别留一块只有薄纱的半透明浮动玻璃
            _backdropW = 0;
            Root.Background = OwnCorners ? Brushes.Transparent : _surfaceBrush;
            GlassImage.Fill = OwnCorners ? _surfaceBrush : null;
            return;
        }

        Root.Background = Brushes.Transparent;
        _glassBrush ??= new ImageBrush { Stretch = Stretch.Fill };
        GlassImage.Fill = _glassBrush;
        _backdropW = 0;
        SyncBackdrop();
    }

    /// <summary>
    /// 按卡片当前的位置从快照里重新取一块。
    /// </summary>
    /// <remarks>
    /// 快照是死的，卡片是活的：滑入、跟随自动隐藏的任务栏时窗口每帧都在挪，只有让取样框
    /// 跟着窗口一起挪，玻璃底下透出的画面才会跟着变——真玻璃滑过桌面就是这个观感。
    /// 反过来，如果把快照当成一张贴在窗口上的图，移动时整块模糊会跟着卡片平移，一眼就
    /// 看出是张静态照片。
    /// </remarks>
    private void SyncBackdrop()
    {
        if (_backdrop is null) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var box)) return;

        int x = box.Left;
        int y = box.Top;
        int w = box.Right - box.Left;
        int h = box.Bottom - box.Top;
        if (x == _backdropX && y == _backdropY && w == _backdropW && h == _backdropH) return;

        if (BackdropCapture.Render(_backdrop, x, y, w, h) is not { } bitmap) return;

        _backdropX = x;
        _backdropY = y;
        _backdropW = w;
        _backdropH = h;

        if (_glassBrush is not null && !ReferenceEquals(_glassBrush.ImageSource, bitmap))
            _glassBrush.ImageSource = bitmap;
    }

    private void StartBackdropTracking()
    {
        if (_backdrop is null || _backdropTracking) return;
        _backdropTracking = true;
        CompositionTarget.Rendering += OnBackdropFrame;
    }

    private void StopBackdropTracking()
    {
        if (!_backdropTracking) return;
        _backdropTracking = false;
        CompositionTarget.Rendering -= OnBackdropFrame;
    }

    private void OnBackdropFrame(object? sender, EventArgs e) => SyncBackdrop();

    /// <summary>
    /// 卡片显示期间持续重抓桌面，让模糊跟着背后的画面一起动。
    /// </summary>
    /// <remarks>
    /// 关键是先把窗口排除出抓屏（WDA_EXCLUDEFROMCAPTURE），否则每张快照里都是卡片自己。
    /// 拿不到这项能力（旧系统、远程桌面、DWM 不合成）就什么都不做，退回弹出前抓一次、
    /// 之后靠取样框在留白里跟着挪的老办法。
    /// </remarks>
    private void StartLiveBackdrop()
    {
        if (_liveCapture is not null || !_glassActive) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !BackdropCapture.TryExcludeFromCapture(hwnd)) return;

        _liveHwnd = hwnd;
        _livePadding = (int)Math.Ceiling(GlassLivePadding * Math.Max(1.0, CurrentScale()));
        _liveWait = new ManualResetEventSlim(false);

        int gen = ++_liveGeneration;
        var wait = _liveWait;
        _liveCapture = new Thread(() => CaptureLoop(wait, gen))
        {
            IsBackground = true,
            Name = "WinTips backdrop",
            // 抓屏要等桌面合成，本来就不快；压低优先级，别跟界面动画抢 UI 线程。
            Priority = ThreadPriority.BelowNormal,
        };

        // 只在实时刷新这段时间里把系统定时器调细，循环里的等待才不会被 15.6ms 的粒度吃掉。
        if (!_liveTimerPrecise)
        {
            _liveTimerPrecise = true;
            NativeMethods.TimeBeginPeriod(1);
        }

        _liveCapture.Start();
    }

    private void StopLiveBackdrop()
    {
        if (_liveCapture is not null)
        {
            _liveGeneration++;
            _liveWait?.Set();
            _liveCapture.Join(400);
            _liveCapture = null;
            _liveWait = null;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) BackdropCapture.AllowCapture(hwnd);
        }

        if (_liveTimerPrecise)
        {
            _liveTimerPrecise = false;
            NativeMethods.TimeEndPeriod(1);
        }

        _backdrop?.Release();
        _backdrop = null;
        _backdropW = 0;

        _liveA = null;
        _liveB = null;
        _liveBrushA = null;
        _liveBrushB = null;
        _liveShown = null;
        _liveScratch = null;
        _liveFrameW = 0;
        _liveFrameH = 0;
        _liveHasFrame = false;

        // 排队中的那一帧可能因为窗口已经关掉而永远不会被处理，标志必须在这里清，
        // 否则下一次弹出时每一帧都会被当成「上一帧还没画」扔掉，实时刷新就再也不动了。
        Interlocked.Exchange(ref _livePending, 0);
    }

    private void CaptureLoop(ManualResetEventSlim wait, int gen)
    {
        int misses = 0;
        long next = _liveClock.ElapsedMilliseconds;

        while (gen == _liveGeneration)
        {
            long start = _liveClock.ElapsedMilliseconds;

            var snapshot = BackdropCapture.Capture(
                _liveHwnd, _livePadding, GlassLiveBlurRadius, waitForComposition: false,
                passes: GlassLiveBlurPasses);
            if (snapshot is null)
            {
                // 偶发抓不到（窗口刚换显示器、桌面正在切）不算事，连着抓不到才收摊。
                if (++misses >= 10) break;
            }
            else
            {
                misses = 0;

                TraceBackdrop(snapshot, (int)(_liveClock.ElapsedMilliseconds - start));

                // 上一帧 UI 线程还没消化掉，这一帧就直接扔了。排队只会越排越旧，
                // 到后面一串积压一起涌上来，比降帧还难看。
                if (Interlocked.Exchange(ref _livePending, 1) == 1)
                {
                    snapshot.Release();
                }
                else
                {
                    try
                    {
                        Dispatcher.BeginInvoke(() => PresentLiveFrame(snapshot));
                    }
                    catch
                    {
                        Interlocked.Exchange(ref _livePending, 0);
                        snapshot.Release();
                        break;
                    }
                }
            }

            // 节拍按「上一拍 + 间隔」排，而不是「现在 + 间隔」：偶尔一帧慢了几毫秒就不会
            // 一路累积下去。用 Stopwatch 而不是 TickCount——后者在默认时钟粒度下只有 15.6ms
            // 的分辨率，按它算间隔会让周期在 17/32/48 三档之间跳，看着就是闪。
            next += GlassLiveRefreshMs;

            long now = _liveClock.ElapsedMilliseconds;
            if (now <= next)
            {
                wait.Wait((int)Math.Min(next - now, GlassLiveRefreshMs));
            }
            else
            {
                // 落后超过一整拍了（系统卡了一下），别再补抓，从当前时刻重新排。
                next = now;
            }
        }
    }

    private static readonly string? DebugTrace =
        Environment.GetEnvironmentVariable("WINTIPS_DEBUG_BACKDROP_TRACE") is { Length: > 0 } path
            ? path
            : null;

    /// <summary>
    /// 诊断用：把每次实时抓屏拿到的中心像素和耗时记一行，判断模糊是真的在跟桌面走、
    /// 还是把卡片自己拍进去了，以及一帧要花多久。只有设了环境变量才会写文件。
    /// </summary>
    private static void TraceBackdrop(BackdropSnapshot snapshot, int cost)
    {
        if (DebugTrace is null) return;

        int cx = snapshot.Width / 2;
        int cy = snapshot.Height / 2;
        int i = (cy * snapshot.Width + cx) * 4;
        try
        {
            System.IO.File.AppendAllText(DebugTrace,
                $"{DateTime.Now:HH:mm:ss.fff} cost={cost} {snapshot.Pixels[i + 2]},{snapshot.Pixels[i + 1]},{snapshot.Pixels[i]}\n");
        }
        catch { }
    }

    private long _lastPresentTick;

    private static readonly string? DebugFrameDir =
        Environment.GetEnvironmentVariable("WINTIPS_DEBUG_LIVE_FRAME_DIR") is { Length: > 0 } dir
            ? dir
            : null;

    private int _dumpCount;

    private int _presentCount;

    /// <summary>诊断用：把送给屏幕的那一帧存成 PNG，检查实时画面本身有没有坏（撕裂、错位、黑块）。</summary>
    private void DumpLiveFrame(BitmapSource bitmap)
    {
        if (DebugFrameDir is null || _dumpCount >= 4) return;
        try
        {
            System.IO.Directory.CreateDirectory(DebugFrameDir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(DebugFrameDir, $"live{_dumpCount}.png"));
            encoder.Save(stream);
        }
        catch { }
        _dumpCount++;
    }

    /// <summary>
    /// 调试用：记下真正显示出去的那一帧（混合之后）的中心像素和帧间隔，用来看跟得平不平。
    /// </summary>
    private void TracePresented(byte[] pixels, int width, int height)
    {
        if (DebugTrace is null) return;

        long now = _liveClock.ElapsedMilliseconds;
        int gap = _lastPresentTick == 0 ? 0 : (int)(now - _lastPresentTick);
        _lastPresentTick = now;

        int i = ((height / 2) * width + width / 2) * 4;
        try
        {
            System.IO.File.AppendAllText(DebugTrace,
                $"P {DateTime.Now:HH:mm:ss.fff} gap={gap} {pixels[i + 2]},{pixels[i + 1]},{pixels[i]}\n");
        }
        catch { }
    }

    /// <summary>把后台抓到的新快照换上去。在 UI 线程上跑。</summary>
    /// <remarks>
    /// 每帧只写一次位图，而且是写到没在显示的那一份，写完再切填充。直接往正在显示的位图上
    /// 写像素，渲染线程可能取到写了一半的画面——背景看起来就一闪一闪的。
    /// </remarks>
    private void PresentLiveFrame(BackdropSnapshot snapshot)
    {
        try
        {
            if (!_visible || !_glassActive) return;

            if (_liveHwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(_liveHwnd, out var box)) return;

            int w = box.Right - box.Left;
            int h = box.Bottom - box.Top;
            if (w <= 0 || h <= 0) return;

            EnsureLiveBuffers(w, h);

            var work = _liveScratch!;
            if (!BackdropCapture.CopyInto(snapshot, box.Left, box.Top, w, h, work)) return;

            int bytes = w * h * 4;

            // 卡片自己在动（滑入、跟随任务栏）时不做混合：那会儿背景是整体平移的，
            // 混前后两帧等于把两个位置叠在一起，边缘会糊成重影。停下来再混，才是要的效果。
            bool moved = Math.Abs(box.Left - _liveRectX) > 2 || Math.Abs(box.Top - _liveRectY) > 2;
            _liveRectX = box.Left;
            _liveRectY = box.Top;

            // 第一帧没有「上一帧」可混，直接照搬，否则会从全黑淡进来。
            if (_liveHasFrame && !moved) BackdropCapture.Blend(_liveShown!, work, bytes, GlassLiveBlendWeight);

            // 混好的这份就是接下来要显示的，直接把两个缓冲的角色对调，不动数据。
            (_liveShown, _liveScratch) = (_liveScratch, _liveShown);
            _liveHasFrame = true;

            var target = _liveShowA ? _liveB! : _liveA!;
            target.WritePixels(new Int32Rect(0, 0, w, h), _liveShown!, w * 4, 0);

            TracePresented(_liveShown!, w, h);

            _liveShowA = !_liveShowA;
            GlassImage.Fill = _liveShowA ? _liveBrushA : _liveBrushB;

            if (_presentCount++ % 8 == 0) DumpLiveFrame(target);
        }
        finally
        {
            snapshot.Release();
            Interlocked.Exchange(ref _livePending, 0);
        }
    }

    /// <summary>
    /// 卡片尺寸变了（换显示器、改缩放、文案变长）就重做一对显示位图和缓冲。
    /// </summary>
    private void EnsureLiveBuffers(int width, int height)
    {
        if (_liveA is not null && _liveFrameW == width && _liveFrameH == height) return;

        // 位图按设备像素建、DPI 也按当前缩放标：这样它在 DIP 空间里正好是卡片的尺寸，
        // 铺上去是一对一映射，不会每帧被重采样一次（重采样也会让边缘发颤）。
        double dpi = 96 * Math.Max(1.0, CurrentScale());

        _liveA = new WriteableBitmap(width, height, dpi, dpi, PixelFormats.Bgra32, null);
        _liveB = new WriteableBitmap(width, height, dpi, dpi, PixelFormats.Bgra32, null);
        _liveBrushA = new ImageBrush(_liveA) { Stretch = Stretch.Fill };
        _liveBrushB = new ImageBrush(_liveB) { Stretch = Stretch.Fill };

        _liveShown = new byte[width * height * 4];
        _liveScratch = new byte[width * height * 4];

        _liveFrameW = width;
        _liveFrameH = height;
        _liveHasFrame = false;

        // false 表示 A 现在没在显示，第一帧写 A。
        _liveShowA = false;
    }

    public void PreWarm()
    {
        try
        {
            _target = ResolveTarget();
            if (new WindowInteropHelper(this).EnsureHandle() == IntPtr.Zero) return;
            ApplyChrome();
            Reposition();
            _visible = false;
            WarmRenderPipeline();
        }
        catch { }
    }

    private void WarmRenderPipeline()
    {
        if (_visible) return;

        LockTextBlock.Text = TipInfo.Describe(TipKind.CapsLock, true);
        SetPoseInstant(true);
        UpdateSize();

        Left = SystemParameters.VirtualScreenLeft - Width - 1000;
        Top = SystemParameters.VirtualScreenTop;
        Show();

        EventHandler rendered = (_, _) => FinishWarmRender();
        _warmRendered = rendered;
        CompositionTarget.Rendering += rendered;

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
        if (_visible) return;
        Hide();
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        PositionOnTarget();
    }

    public void ShowTip(TipKind kind, bool? state, double seconds) =>
        ShowTipCore(kind, state, seconds, welcome: false);

    public void ShowWelcome(string title, string subtitle, double seconds)
    {
        _welcomeTitle = title;
        _welcomeSubtitle = subtitle;
        ShowTipCore(TipKind.CapsLock, true, seconds, welcome: true);
    }

    private void ShowTipCore(TipKind kind, bool? state, double seconds, bool welcome)
    {
        var target = ResolveTarget();

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

        if (Diag.Enabled)
        {
            Diag.Write($"show {kind} state={state} target={target.Left:F0},{target.Top:F0} " +
                       $"{target.Width:F0}x{target.Height:F0} scale={target.Scale:F2}");
        }

        var prevKind = _currentKind;
        _currentKind = kind;
        _welcome = welcome;
        _currentState = state;
        _generation++;

        ApplyMode(kind);
        bool on = SetStatusText(kind, state);

        if (IsClip(kind))
        {
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
            RaiseTopmost();
            AnimateStatus(on);
            RestartTimer();
            StartFollow();
            return;
        }

        bool ready = new WindowInteropHelper(this).Handle != IntPtr.Zero;

        ApplyChrome();
        Reposition();

        UpdateBackdrop();

        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        if (ms <= 0)
        {
            AnimateStatus(on);
        }
        else
        {
            if (!_shownOnce)
                SetPoseInstant(!on);
            Root.Opacity = 0;
        }

        Show();
        PlacePhysical(_lastLeft, _lastTop);
        EnsureOnScreen();
        RaiseTopmost();
        _visible = true;
        _shownOnce = true;
        if (!ready)
        {
            ApplyChrome();
            Reposition();
        }
        AnimateIn();
        StartLiveBackdrop();
        // 能实时重抓就不必再逐帧挪取样框了：每次重抓都会按卡片当前的位置重新对准，
        // 而这六十毫秒里卡片顶多挪几像素，模糊本来就看不出来，省下一次常驻的渲染循环。
        if (_liveCapture is null) StartBackdropTracking();
        if (ms > 0) AnimateStatus(on);
        RestartTimer();
        StartFollow();
    }

    public void ShowLanguage(string text, double seconds)
    {
        _languageText = text;
        ShowTip(TipKind.Language, true, seconds);
    }

    public void UpdateState(TipKind kind, bool on)
    {
        if (!_visible || _welcome || kind != _currentKind) return;
        SetStatusText(kind, on);
        UpdateSize();
        Reposition();
        AnimateStatus(on);
        RestartTimer();
    }

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
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Opacity = 1;
        _hideTimer.Interval = TimeSpan.FromSeconds(ms);
        RestartTimer();
    }

    public void PreviewAppearance(double seconds)
    {
        bool wantGlass = GlassWanted;

        if (_visible && wantGlass == _glassActive)
        {
            if (_glassActive) ApplyGlassVeil(ThemeHelper.IsDarkTheme);
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            Root.Opacity = 1;
            _hideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10));
            RestartTimer();
            return;
        }

        if (_visible) HideInstantly();
        ShowTipCore(_currentKind, _currentState, seconds, _welcome);
    }

    private void HideInstantly()
    {
        StopFollow();
        _topmost.End();
        StopBackdropTracking();
        StopLiveBackdrop();
        _hideTimer.Stop();
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Root.Opacity = 1;
        Hide();
        _visible = false;
    }

    private bool SetStatusText(TipKind kind, bool? state)
    {
        if (_welcome)
        {
            WelcomeTitle.Text = _welcomeTitle;
            WelcomeSubtitle.Text = _welcomeSubtitle;
            return true;
        }

        LockTextBlock.Text = kind == TipKind.Language ? _languageText : TipInfo.Describe(kind, state);
        return !TipInfo.IsToggle(kind) || state == true;
    }

    private void ApplyMode(TipKind kind)
    {
        bool clip = !_welcome && IsClip(kind);
        bool lang = !_welcome && kind == TipKind.Language;
        bool lockUi = !_welcome && !clip && !lang;

        WelcomeSymbol.Visibility = _welcome ? Visibility.Visible : Visibility.Collapsed;
        WelcomeText.Visibility = _welcome ? Visibility.Visible : Visibility.Collapsed;
        LockTextBlock.Visibility = _welcome ? Visibility.Collapsed : Visibility.Visible;
        LockSymbol.Visibility = lockUi ? Visibility.Visible : Visibility.Collapsed;
        LockIndicator.Visibility = lockUi ? Visibility.Visible : Visibility.Collapsed;
        ClipSymbol.Visibility = clip ? Visibility.Visible : Visibility.Collapsed;
        CountBadge.Visibility = clip ? Visibility.Visible : Visibility.Collapsed;
        LangSymbol.Visibility = lang ? Visibility.Visible : Visibility.Collapsed;

        bool cut = kind == TipKind.Cut;
        CheckMark.Visibility = cut ? Visibility.Collapsed : Visibility.Visible;
        CutGlyph.Visibility = cut ? Visibility.Visible : Visibility.Collapsed;

        ApplyBarSide();
    }

    private void ApplyBarSide()
    {
        if (_welcome)
        {
            ContentGrid.Margin = new Thickness(16, 10, 12, 10);
            return;
        }

        if (IsClip(_currentKind))
        {
            ContentGrid.Margin = new Thickness(16, 8, 8, 8);
            return;
        }

        if (_currentKind == TipKind.Language)
        {
            ContentGrid.Margin = new Thickness(16, 8, 12, 8);
            return;
        }

        ContentGrid.Margin = _barOnTop ? new Thickness(16, 10, 12, 6) : new Thickness(16, 6, 12, 10);
        LockIndicator.VerticalAlignment = _barOnTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        LockIndicator.Margin = _barOnTop ? new Thickness(0, 7, 0, 0) : new Thickness(0, 0, 0, 7);
    }

    private void UpdateBarSide()
    {
        bool onTop = _lastTop + Height / 2 < _target.CenterY;
        if (onTop == _barOnTop) return;
        _barOnTop = onTop;
        ApplyBarSide();
    }

    private void AnimateStatus(bool on)
    {
        if (_welcome) return;

        if (IsClip(_currentKind))
        {
            AnimateClipStatus(instant: false);
            return;
        }

        if (_currentKind == TipKind.Language) return;

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

    private void SetPoseInstant(bool on)
    {
        if (_welcome) return;
        if (_currentKind == TipKind.Language) return;

        if (IsClip(_currentKind))
        {
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

    private const double BaseHeight = 50;

    private const double WelcomeBaseHeight = 60;

    private void UpdateSize()
    {
        double scale = AppSettings.NormalizeScale(AppSettings.Instance.TipScale);
        ApplyScale(scale);

        if (_welcome)
        {
            UpdateWelcomeSize(scale);
            return;
        }

        double textWidth = MeasureText(LockTextBlock.Text);
        bool clip = CountBadge.Visibility == Visibility.Visible;
        double extra = clip
            ? MeasureText(CountText.Text, 12.5, FontWeights.SemiBold) + 22
            : 0;
        double baseWidth = Math.Max(160, Math.Ceiling(16 + 22 + 18 + textWidth + 12 + extra));

        if (clip)
        {
            double pillWidth = MeasureText(CountText.Text, 12.5, FontWeights.SemiBold) + 14;
            const double gridLeft = 16;
            const double iconEnd = 22;
            double badgeStart = baseWidth - gridLeft - 8 - pillWidth;
            double mid = (iconEnd + badgeStart) / 2;
            double left = Math.Max(iconEnd + 8, mid - textWidth / 2);
            LockTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
            LockTextBlock.Margin = new Thickness(left, 0, 0, 0);
        }
        else
        {
            LockTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
            LockTextBlock.Margin = new Thickness(18, 0, 0, 2);
        }

        Width = Math.Ceiling(baseWidth * scale);
        Height = Math.Ceiling(BaseHeight * scale);
    }

    private void UpdateWelcomeSize(double scale)
    {
        double textWidth = Math.Max(
            MeasureText(WelcomeTitle.Text, 14, FontWeights.Medium),
            MeasureText(WelcomeSubtitle.Text, 11.5, FontWeights.Medium));
        double baseWidth = Math.Max(200, Math.Ceiling(16 + 22 + 18 + textWidth + 16));

        Width = Math.Ceiling(baseWidth * scale);
        Height = Math.Ceiling(WelcomeBaseHeight * scale);
    }

    private ScaleTransform? _rootScale;

    private void ApplyScale(double scale)
    {
        if (_rootScale is null)
        {
            _rootScale = new ScaleTransform(scale, scale);
            Root.LayoutTransform = _rootScale;
        }
        else if (Math.Abs(_rootScale.ScaleX - scale) >= 0.001)
        {
            _rootScale.ScaleX = _rootScale.ScaleY = scale;
        }

        ApplyGlassMetrics(scale);
    }

    private void ApplyGlassMetrics(double scale)
    {
        double inv = scale > 0 ? 1 / scale : 1;
        double radius = GlassCornerRadius * inv;
        double stroke = GlassStrokeThickness * inv;

        GlassImage.RadiusX = GlassImage.RadiusY = radius;
        GlassPlate.RadiusX = GlassPlate.RadiusY = radius;
        GlassPlate.StrokeThickness = stroke;
        GlassPlate.Margin = new Thickness(stroke / 2);
    }

    private double MeasureText(string text, double size = 14, FontWeight? weight = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var face = weight ?? FontWeights.Medium;
        var key = (text, size, face);
        if (_widthCache.TryGetValue(key, out double cached)) return cached;

        double pixelsPerDip = 1.0;
        try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            face == FontWeights.SemiBold ? _semiBoldFace : _mediumFace, size, Brushes.Black, pixelsPerDip);
        double width = ft.Width;

        if (_widthCache.Count >= 256) _widthCache.Clear();
        _widthCache[key] = width;
        return width;
    }

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
        if (!_target.IsLive) _target = ResolveTarget();

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        var (left, top) = TargetPosition();
        SyncDpi(left, top);

        UpdateSize();
        PositionOnTarget();
    }

    private void PositionOnTarget()
    {
        var (left, top) = TargetPosition();
        _lastLeft = _followLeft = left;
        _lastTop = _followTop = top;
        UpdateBarSide();
        Place();
    }

    private void Place()
    {
        PlacePhysical(_lastLeft, _lastTop);
        Left = ToWindowDip(_lastLeft);
        Top = ToWindowDip(_lastTop);
    }

    private double WindowDipRatio()
    {
        double want = _target.Scale > 0 ? _target.Scale : 1.0;
        double cur = CurrentScale();
        return cur <= 0 || Math.Abs(cur - want) < 0.0001 ? 1.0 : want / cur;
    }

    private double ToWindowDip(double targetDip) => targetDip * WindowDipRatio();

    /// <summary>
    /// 每次弹出前重新插到置顶带的最前面，并在显示期间一直盯住这个位置。
    /// </summary>
    /// <remarks>
    /// 窗口自己带 WS_EX_TOPMOST，但置顶窗口之间是按先后排的：边界全屏的游戏、录制软件、
    /// 各种覆盖层启动后也会把自家窗口设成置顶，于是压在提示上面，提示就"没出现"了。
    /// 摆位用的是 SWP_NOZORDER（不扰动焦点），这一步单独补 Z 序。
    ///
    /// 只补一次不够：游戏拿到焦点、切分辨率、重建交换链时都会再把自家窗口顶一次，
    /// 于是提示先露出来又被压回去。真正的看门人交给 <see cref="TopmostGuard"/>，
    /// 它会盯着卡片中心有没有被别人盖住，盖住了才补抢一次。
    /// </remarks>
    private void RaiseTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _topmost.Begin(hwnd);
    }

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
    /// 兜底：卡片必须落在某块显示器上，落空了就往回挪。
    /// </summary>
    /// <remarks>
    /// 位置是一路算出来的（前台窗口 → 显示器 → 工作区 → 任务栏挡掉的那条边 → DPI），
    /// 全屏应用、任务栏被覆盖、显示器热插拔时这几项都可能给到异常值。算出来的点一旦
    /// 落空，用户看到的就是"提示弹了，但屏幕上什么都没有"——跟"根本没弹"完全无法区分，
    /// 排起来最费时间。这里用卡片中心点在显示那一刻做一次判定，判不过就退回目标显示器
    /// 可用区正中。只判一次，不跟着移动，不会变成常驻的抖动源。
    /// </remarks>
    private void EnsureOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return;

        int cx = r.Left + (r.Right - r.Left) / 2;
        int cy = r.Top + (r.Bottom - r.Top) / 2;

        var center = new NativeMethods.POINT { X = cx, Y = cy };
        if (NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero)
        {
            Diag.Write($"card {r.Right - r.Left}x{r.Bottom - r.Top}@({r.Left},{r.Top}) visible");
            return;
        }

        var area = _target.IsLive ? _target : MonitorArea.Primary();
        var (l, t, rt, b) = area.Usable();

        double left = l + (rt - l - Width) / 2;
        double top = t + (b - t - Height) / 2;

        _lastLeft = _followLeft = left;
        _lastTop = _followTop = top;
        PlacePhysical(left, top);

        Diag.Write($"card offscreen at ({r.Left},{r.Top}), recentered to ({left:F0},{top:F0})");
    }

    private void FollowTaskbar()
    {
        if (!_visible) return;

        var (left, top) = TargetPosition();
        if (Math.Abs(left - _followLeft) < 0.5 && Math.Abs(top - _followTop) < 0.5) return;

        _followLeft = left;
        _followTop = top;
        _lastLeft = left;
        _lastTop = top;
        UpdateBarSide();

        bool animated = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed) > 0;
        MoveContentTo(left, top, animated ? FollowMs : 0);
    }

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
        _moveGen++;
    }

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
        StopFollow();
        int gen = _generation;
        double targetLeft = _lastLeft;
        double targetTop = _lastTop;
        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);

        void FinishHide()
        {
            _topmost.End();
            StopBackdropTracking();
            StopLiveBackdrop();
            Hide();
            _visible = false;
            _clipCount = 0;
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Root.Opacity = 1;
            Left = ToWindowDip(targetLeft);
            Top = ToWindowDip(targetTop);
            PlacePhysical(targetLeft, targetTop);
        }

        if (ms <= 0)
        {
            FinishHide();
            return;
        }

        // 缩回时不淡到全透明：卡片一路留着底子（模糊快照 + 薄纱）滑出去，
        // 淡到 0 会先看见底色「化掉」、只剩桌面，最后再硬切消失，反而不像一张卡片退场。
        var fade = new DoubleAnimation(HideOpacityFloor, TimeSpan.FromMilliseconds(ms));
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
