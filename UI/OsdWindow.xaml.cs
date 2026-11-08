using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private const int GlassBlurRadius = 16;

    private const int GlassBlurPadding = 24;

    private const double GlassCornerRadius = 8;

    private const double GlassStrokeThickness = 1;

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

        DwmChrome.SetFrameExtended(hwnd, true);

        src.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
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
            ClipboardUpdated?.Invoke();
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

        Root.Background = acrylic || glass ? Brushes.Transparent : _surfaceBrush;
        ApplyGlassLayers(glass, dark);

        _glassActive = glass;
    }

    private static bool GlassWanted =>
        DwmChrome.SupportsBackdrop && ThemeHelper.AcrylicAllowed && AppSettings.Instance.TipGlass;

    private void ApplyGlassLayers(bool glass, bool dark)
    {
        if (!glass)
        {
            GlassImage.Fill = null;
            GlassImage.Visibility = Visibility.Collapsed;
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

    private void UpdateBackdrop()
    {
        if (!_glassActive) return;

        var bitmap = BackdropCapture.CaptureBlurred(
            new WindowInteropHelper(this).Handle,
            GlassBlurPadding,
            GlassBlurRadius);

        if (bitmap is null)
        {
            // 抓不到桌面就退回纯色卡片，别留一块只有薄纱的半透明浮动玻璃
            Root.Background = _surfaceBrush;
            GlassImage.Fill = null;
            return;
        }

        Root.Background = Brushes.Transparent;
        _glassBrush ??= new ImageBrush { Stretch = Stretch.Fill };
        if (!ReferenceEquals(_glassBrush.ImageSource, bitmap)) _glassBrush.ImageSource = bitmap;
        if (!ReferenceEquals(GlassImage.Fill, _glassBrush)) GlassImage.Fill = _glassBrush;
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
        _visible = true;
        _shownOnce = true;
        if (!ready)
        {
            ApplyChrome();
            Reposition();
        }
        AnimateIn();
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
