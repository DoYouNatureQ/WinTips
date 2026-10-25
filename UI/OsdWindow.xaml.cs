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
/// 底部强调色指示条（开=60px 全不透明，关=36px 20% 不透明）。
/// 锁扣与指示条共用同一时长与缓动，且在窗口可见后才同帧启动，保证同步起落。
/// 显示于主显示器任务栏上方，水平位置可设为左侧/居中/右侧（默认居中）；点击穿透、不抢焦点。
/// 预热分两步：EnsureHandle() 只建句柄不显示窗口（原地 Show() 会带出一帧
/// DWM 背景）；随后把窗口移到屏幕外真实渲染一帧再隐藏，暖起冷启动的
/// 渲染管线。句柄与管线就绪后首次 ShowTip 无创建迟滞，也不会看到内容
/// “初始化”的过程。
/// </summary>
public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();
    private bool _visible;
    private bool _shownOnce;
    private int _generation;
    private EventHandler? _warmRendered;
    private DispatcherTimer? _warmGuard;
    private TipKind _currentKind;
    private double _lastTop;

    /// <summary>系统剪贴板更新（WM_CLIPBOARDUPDATE），由 OsdController 订阅。</summary>
    public event Action? ClipboardUpdated;

    private int _clipCount;
    private bool _countIncreased;

    private static bool IsClip(TipKind k) => TipInfo.IsClipboard(k);

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => HideAnimated();
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

        var m = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref m);

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

        int darkFlag = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));
        int corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        //系统不支持系统亚克力时退回纯色卡片
        Root.Background = hr == 0
            ? Brushes.Transparent
            : new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xFB, 0xFB, 0xFB));

        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        LockIndicator.Fill = new SolidColorBrush(ThemeHelper.AccentColor);
        CountBadge.Background = new SolidColorBrush(ThemeHelper.AccentColor);
        CountText.Foreground = Brushes.White;
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

        LockTextBlock.Text = "大写锁定 开启";
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

        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 10));

        if (_visible)
        {
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            Root.Opacity = 1;
            Reposition();
            AnimateStatus(on);
            RestartTimer();
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
        if (!TipInfo.IsToggle(kind))
        {
            LockTextBlock.Text = kind == TipKind.Insert ? "Insert 键已按下" : TipInfo.Name(kind);
            return true;
        }

        bool on = state == true;
        LockTextBlock.Text = $"{TipInfo.Name(kind)} {(on ? "开启" : "关闭")}";
        return on;
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

        // 锁键模式：内容区上 6 / 下 10（底部给指示条留空间）、左 16 / 右 12（原设计）；
        // 剪贴板模式：右侧只留 8px，让 ×N 徽章贴近卡片右缘，避免挤占文字空间。
        ContentGrid.Margin = clip ? new Thickness(16, 8, 8, 8) : new Thickness(16, 6, 12, 10);
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

    private void Reposition()
    {
        double scale = 1.0;
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (src?.CompositionTarget != null)
        {
            double m = src.CompositionTarget.TransformToDevice.M11;
            if (m > 0) scale = m;
        }

        BeginAnimation(TopProperty, null);

        var s = AppSettings.Instance;
        (Left, Top) = s.HasCustomPosition
            ? OsdPlacement.ClampToPrimary(s.CustomLeft!.Value, s.CustomTop!.Value, Width, Height)
            : OsdPlacement.DefaultFor(AppSettings.NormalizePlacement(s.TipPlacement), Width, Height, scale);
        _lastTop = Top;
    }

    private void AnimateIn()
    {
        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        double target = Top;
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

        var slide = new DoubleAnimation(target + 20, target, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(TopProperty, slide);
    }

    private void HideAnimated()
    {
        if (!_visible) return;
        int gen = _generation;
        double target = _lastTop;
        int ms = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);

        void FinishHide()
        {
            Hide();
            _visible = false;
            _clipCount = 0; // 浮窗收起后连续计数归零
            Root.BeginAnimation(UIElement.OpacityProperty, null);
            BeginAnimation(TopProperty, null);
            Root.Opacity = 1;
            Top = target;
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

        var slide = new DoubleAnimation(target + 20, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        BeginAnimation(TopProperty, slide);
    }

    private void RestartTimer()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
