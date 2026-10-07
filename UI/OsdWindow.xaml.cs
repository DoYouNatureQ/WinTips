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
/// 显示于主显示器任务栏上方居中；点击穿透、不抢焦点。
/// 预热用 EnsureHandle() 只建句柄不显示窗口（Show() 会带出一帧
/// DWM 背景）；句柄就绪后 Show() 走快路径，无创建迟滞。
/// </summary>
public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();
    private bool _visible;
    private int _generation;
    private TipKind _currentKind;
    private double _lastTop;

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
    }

    public void PreWarm()
    {
        try
        {
            // 只创建句柄、不显示窗口。若 Show/Hide 预热，冷启动时两者之间
            // 的帧会被 DWM 合成出来：Root 全透明时系统背景（Mica）可见，
            // 表现为开机首次启动闪现一个空窗口。EnsureHandle 同样会触发
            // OnSourceInitialized，句柄就绪后首次 ShowTip 无创建迟滞。
            if (new WindowInteropHelper(this).EnsureHandle() == IntPtr.Zero) return;
            ApplyChrome();
            Reposition();
            _visible = false;
        }
        catch { }
    }

    public void ShowTip(TipKind kind, bool? state, double seconds)
    {
        _currentKind = kind;
        _generation++;

        bool on = SetStatusText(kind, state);
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
            AnimateStatus(on); // 无动画：显示前直接落到目标状态，避免首帧闪旧值
        else
            Root.Opacity = 0;

        Show();
        _visible = true;
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
            LockTextBlock.Text = "Insert 键已按下";
            return true;
        }

        bool on = state == true;
        LockTextBlock.Text = $"{TipInfo.Name(kind)} {(on ? "开启" : "关闭")}";
        return on;
    }

    /// <summary>
    /// 锁扣旋转/回弹与指示条宽度/透明度用同一时长、同一缓动，一齐起落。
    /// 需在窗口可见后调用（窗口隐藏时动画时钟照常走表，会提前消耗）。
    /// </summary>
    private void AnimateStatus(bool on)
    {
        double indicatorOpacity = on ? 1.0 : 0.2;
        double indicatorWidth = on ? 60.0 : 36.0;
        double shackleAngle = on ? 0.0 : 25.0;

        int baseMs = AppSettings.AnimationMs(AppSettings.Instance.AnimationSpeed);
        if (baseMs <= 0)
        {
            LockIndicator.BeginAnimation(OpacityProperty, null);
            LockIndicator.Opacity = indicatorOpacity;
            LockIndicator.BeginAnimation(WidthProperty, null);
            LockIndicator.Width = indicatorWidth;
            ShackleRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            ShackleRotation.Angle = shackleAngle;
            ShackleBounce.BeginAnimation(TranslateTransform.YProperty, null);
            ShackleBounce.Y = 0;
            return;
        }

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

    private void UpdateSize()
    {
        double textWidth = MeasureText(LockTextBlock.Text);
        Width = Math.Max(160, Math.Ceiling(16 + 22 + 18 + textWidth + 12));
        Height = 50;
    }

    private double MeasureText(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var typeface = new Typeface(
            new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
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

        const double gap = 16;
        double bottomEdge;
        var tb = TaskbarInfo.GetPrimaryTaskbar();
        if (tb.HasValue && tb.Value.Edge == NativeMethods.ABE_BOTTOM && !tb.Value.AutoHide)
            bottomEdge = tb.Value.Rect.Top / scale - gap;
        else
            bottomEdge = SystemParameters.WorkArea.Bottom - gap;

        double centerX = SystemParameters.PrimaryScreenWidth / 2;
        BeginAnimation(TopProperty, null);
        Left = centerX - Width / 2;
        Top = bottomEdge - Height;
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
