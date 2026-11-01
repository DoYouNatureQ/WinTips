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
/// 显示于主显示器：垂直可贴屏幕顶部或任务栏上方，水平可设为左侧/居中/右侧（默认居中、任务栏上方）；点击穿透、不抢焦点。
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

    /// <summary>系统剪贴板更新（WM_CLIPBOARDUPDATE），由 OsdController 订阅。</summary>
    public event Action? ClipboardUpdated;

    private int _clipCount;
    private bool _countIncreased;

    /// <summary>指示条贴在文字上方（浮窗挂靠屏幕顶边）而非下方。</summary>
    private bool _barOnTop;

    private static bool IsClip(TipKind k) => TipInfo.IsClipboard(k);

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
            // 切换显示器/主屏或改缩放比例：隐藏窗口收不到系统 WM_DPICHANGED，
            // 主动查询主屏 DPI 并补发，否则下次弹出按旧 DPI 渲染导致变形
            SyncDpi();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 让 WPF 的 DPI 与弹窗目标显示器（主屏）保持一致。
    /// 浮窗绝大多数时间处于隐藏状态，而 Windows 只对可见窗口投递 WM_DPICHANGED；
    /// 显示拓扑或缩放变化时浮窗正隐藏，WPF 的 TransformToDevice 仍是旧值，
    /// 弹出时就会按旧缩放渲染（变形/错位）。这里取主屏有效 DPI，与窗口当前
    /// 缩放不一致时手工补发一条 WM_DPICHANGED（可见时系统本来就会发，值相等则跳过），
    /// 代价只是查询一次显示器信息。
    /// </summary>
    private void SyncDpi()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget == null) return;
            double current = src.CompositionTarget.TransformToDevice.M11;

            // 弹窗固定出现在主显示器（原点恒为 0,0），据此取主屏有效 DPI
            var mon = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            if (mon == IntPtr.Zero) return;
            if (NativeMethods.GetDpiForMonitor(mon, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) != 0 || dpiX == 0)
                return;

            if (Math.Abs(dpiX / 96.0 - current) <= 0.01) return;

            // 建议矩形用当前物理矩形：WPF 收到后会更新 DPI 并按矩形重设窗口，
            // 随后 OnDpiChanged 里再按新 DPI 重算 DIP 尺寸与位置
            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                NativeMethods.SendMessageW(hwnd, NativeMethods.WM_DPICHANGED,
                    (IntPtr)((uint)dpiX | ((uint)dpiY << 16)), ref rect);
        }
        catch { }
    }

    /// <summary>
    /// 窗口可见期间 DPI 发生变化（WPF 已按“物理尺寸守恒”缩放了 Left/Top/Width/Height）。
    /// 浮窗内容是固定 DIP 设计（160×50 基准），这里按新 DPI 重算尺寸并贴回
    /// 任务栏上方，避免沿用缩放后的尺寸而显得忽大忽小。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (!_visible) return;
        UpdateSize();
        Reposition();
    }

    private void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool dark = ThemeHelper.IsDarkTheme;
        var solid = new SolidColorBrush(ThemeHelper.OsdSurface(dark));

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);
        DwmChrome.TrySetCorner(hwnd, NativeMethods.DWMWCP_ROUND);

        // 系统不支持亚克力时退回纯色卡片（Win10、高对比度、系统关掉透明效果、
        // 节电模式都必须走这条分支，否则 -1 扩展边框会让浮窗变成 Win7 Aero 式的实心色块）
        Root.Background = ThemeHelper.AcrylicAllowed &&
                          DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_TRANSIENTWINDOW)
            ? Brushes.Transparent
            : solid;

        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        LockIndicator.Fill = new SolidColorBrush(ThemeHelper.AccentColor);
        CountBadge.Background = new SolidColorBrush(ThemeHelper.AccentColor);
        CountText.Foreground = new SolidColorBrush(ThemeHelper.OnAccent(dark));
    }

    public void PreWarm()
    {
        try
        {
            SyncDpi(); // 兜底：启动后若显示配置已变，先把 DPI 对齐再预热
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

        double left = Left, top = Top;
        Left = SystemParameters.VirtualScreenLeft - Width - 1000;
        Top = SystemParameters.VirtualScreenTop;
        Show();

        EventHandler rendered = (_, _) => FinishWarmRender(left, top);
        _warmRendered = rendered;
        CompositionTarget.Rendering += rendered;

        // 兜底：万一 Rendering 事件未按预期触发，也要把窗口收回并复位
        _warmGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _warmGuard.Tick += (_, _) => FinishWarmRender(left, top);
        _warmGuard.Start();
    }

    private void FinishWarmRender(double left, double top)
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
        Left = left;
        Top = top;
    }

    public void ShowTip(TipKind kind, bool? state, double seconds)
    {
        SyncDpi(); // 兜底：隐藏期间错过的 DPI 变化在此对齐（内部有阈值比较，通常零开销）

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
        if (ready)
        {
            ApplyChrome();
            Reposition();
        }

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
        _visible = true;
        _shownOnce = true;
        if (!ready)
        {
            ApplyChrome();
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
        bool onTop = _lastTop + Height / 2 < SystemParameters.PrimaryScreenHeight / 2;
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

    private void UpdateSize()
    {
        double textWidth = MeasureText(LockTextBlock.Text);
        bool clip = CountBadge.Visibility == Visibility.Visible;
        double extra = clip
            ? MeasureText(CountText.Text, 12.5, FontWeights.SemiBold) + 22 // 徽章宽 + 右缘留白
            : 0;
        Width = Math.Max(160, Math.Ceiling(16 + 22 + 18 + textWidth + 12 + extra));
        Height = 50;

        if (clip)
        {
            // 剪贴板模式：文字在“对勾图标 ↔ ×N 徽章”之间均匀居中（两侧间距相等），
            // 视觉上不再偏向徽章；该中点与卡片中线几乎重合。
            // 注意：TextBlock 的 Margin 是相对内容区（窗口左边距 16）的坐标，
            // 而 iconEnd/badgeStart 是窗口坐标，需先减去 16 换算，否则文字整体右偏 16px。
            // 另外必须用刚算出的新 Width（×10 时徽章变宽，中点随之后移）。
            double pillWidth = MeasureText(CountText.Text, 12.5, FontWeights.SemiBold) + 14; // 徽章内边距 7+7
            const double gridLeft = 16;                // 内容区原点（窗口坐标）
            const double iconEnd = 22;                 // 图标右缘（内容区坐标）
            double badgeStart = Width - gridLeft - 8 - pillWidth; // 徽章左缘（内容区坐标，距窗口右 8px）
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
    }

    private double MeasureText(string text, double size = 14, FontWeight? weight = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var typeface = new Typeface(
            new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontStyles.Normal, weight ?? FontWeights.Medium, FontStretches.Normal);
        double pixelsPerDip = 1.0;
        try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, 14, Brushes.Black, pixelsPerDip);
        return ft.Width;
    }

    /// <summary>当前 DPI 缩放（物理像素 → DIP），取浮窗自身的渲染变换。</summary>
    private double CurrentScale()
    {
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (src?.CompositionTarget != null)
        {
            double m = src.CompositionTarget.TransformToDevice.M11;
            if (m > 0) return m;
        }
        return 1.0;
    }

    /// <summary>浮窗该落在哪（DIP）：有自定义位置用自定义值，否则按预设（跟随任务栏当前形态）。</summary>
    private (double Left, double Top) TargetPosition(double scale)
    {
        var s = AppSettings.Instance;
        return s.HasCustomPosition
            ? OsdPlacement.ClampToPrimary(s.CustomLeft!.Value, s.CustomTop!.Value, Width, Height)
            : OsdPlacement.DefaultFor(AppSettings.NormalizePlacement(s.TipPlacement),
                AppSettings.NormalizeVertical(s.TipVertical), Width, Height, scale);
    }

    private void Reposition()
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        (Left, Top) = TargetPosition(CurrentScale());
        _lastLeft = _followLeft = Left;
        _lastTop = _followTop = Top;
        UpdateBarSide();
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

        var (left, top) = TargetPosition(CurrentScale());
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

    /// <summary>把浮窗平滑挪到目标位置（跟随任务栏时用）。</summary>
    private void MoveContentTo(double left, double top, int ms)
    {
        if (ms <= 0)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = left;
            Top = top;
            return;
        }

        double fromLeft = Left, fromTop = Top;
        int gen = ++_moveGen;
        var span = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animTop = new DoubleAnimation(fromTop, top, span) { EasingFunction = ease };
        // 动画结束后把基值落到目标：否则之后任何一次 BeginAnimation(Left/Top, null)
        // 都会把窗口弹回动画前的旧基值
        animTop.Completed += (_, _) =>
        {
            if (gen != _moveGen || !_visible) return;
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = left;
            Top = top;
        };

        BeginAnimation(LeftProperty, new DoubleAnimation(fromLeft, left, span) { EasingFunction = ease });
        BeginAnimation(TopProperty, animTop);
    }

    /// <summary>
    /// 入场/退场滑动偏移：贴边方向决定滑动轴（左右 15% 区横向滑入、上下 15% 区竖向滑入、
    /// 四角对角滑入），落在屏幕中部时回落到竖向。始终朝最近的屏幕边缘收起。
    /// </summary>
    private (double Dx, double Dy) SlideOffset(double left, double top) =>
        OsdPlacement.SlideOffset(left, top, Width, Height);

    private void AnimateIn()
    {
        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        double targetLeft = Left, targetTop = Top;
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

        var (dx, dy) = SlideOffset(targetLeft, targetTop);
        var span = TimeSpan.FromMilliseconds(ms);

        if (dx != 0)
        {
            BeginAnimation(LeftProperty, new DoubleAnimation(targetLeft + dx, targetLeft, span)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        if (dy != 0)
        {
            BeginAnimation(TopProperty, new DoubleAnimation(targetTop + dy, targetTop, span)
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
            Left = targetLeft;
            Top = targetTop;
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
            BeginAnimation(LeftProperty, new DoubleAnimation(targetLeft + dx, span)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
        }
        if (dy != 0)
        {
            BeginAnimation(TopProperty, new DoubleAnimation(targetTop + dy, span)
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
