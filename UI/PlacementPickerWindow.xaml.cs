using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 自定义位置模式：全屏半透明遮罩（置于浮窗与拖动提示之下）压暗桌面并挡住桌面点击；
/// 提示浮窗与真实 OSD 同尺寸同外观（所见即所得），按住拖动；操作条（提示文字 + 取消/保存）
/// 跟随浮窗，下方优先、贴近工作区底边时翻到浮窗上方。遮罩窗口铺满主显示器（与 OSD
/// 显示范围一致），保存时以浮窗左上角作为提示位置（DIP，主屏坐标系）。
/// </summary>
public partial class PlacementPickerWindow : Window
{
    /// <summary>点击“保存位置”时触发，参数为钳制后的提示左上角坐标（DIP）。</summary>
    public event Action<double, double>? Saved;

    private bool _dragging;
    private Point _dragOrigin;
    private double _startLeft, _startTop;

    public PlacementPickerWindow()
    {
        InitializeComponent();
        ApplyTheme();

        // 遮罩只盖主显示器（与 OSD 的显示范围一致）；坐标即屏幕 DIP，保存时无需换算
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        Loaded += (_, _) => LayoutInitial();
        CancelBtn.Click += (_, _) => Close();
        SaveBtn.Click += (_, _) => Save();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        TipCard.MouseLeftButtonDown += Tip_MouseLeftButtonDown;
        TipCard.MouseMove += Tip_MouseMove;
        TipCard.MouseLeftButtonUp += Tip_MouseLeftButtonUp;
        TipCard.LostMouseCapture += (_, _) => _dragging = false;
    }

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        HintText.Foreground = new SolidColorBrush(ThemeHelper.TextSecondary(dark));

        // 遮罩上的卡片无法用 DWM 亚克力，改用高不透明度纯色模拟同款质感
        var card = dark ? Color.FromArgb(0xD9, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xD9, 0xFB, 0xFB, 0xFB);
        var stroke = dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
        TipCard.Background = new SolidColorBrush(card);
        TipCard.BorderBrush = new SolidColorBrush(stroke);
        HintBar.Background = new SolidColorBrush(card);
        HintBar.BorderBrush = new SolidColorBrush(stroke);
    }

    /// <summary>布局浮窗与操作条的初始位置：已有自定义用自定义值，否则模拟当前预设位置。</summary>
    private void LayoutInitial()
    {
        var s = AppSettings.Instance;
        (double left, double top) = s.HasCustomPosition
            ? (s.CustomLeft!.Value, s.CustomTop!.Value)
            : OsdPlacement.DefaultFor(AppSettings.NormalizePlacement(s.TipPlacement),
                TipCard.ActualWidth, TipCard.ActualHeight, OsdPlacement.PrimaryScale());
        (left, top) = OsdPlacement.ClampToPrimary(left, top, TipCard.ActualWidth, TipCard.ActualHeight);
        Canvas.SetLeft(TipCard, left);
        Canvas.SetTop(TipCard, top);

        PlaceHintBar(left, top);
        TipCard.Opacity = 1;
        HintBar.Opacity = 1;
    }

    /// <summary>操作条跟随浮窗：下方留 8px；贴近工作区底边放不下时翻到浮窗上方；水平居中并钳制在屏内。</summary>
    private void PlaceHintBar(double tipLeft, double tipTop)
    {
        double workBottom = SystemParameters.WorkArea.Bottom;
        double hintW = HintBar.ActualWidth, hintH = HintBar.ActualHeight;
        double tipH = TipCard.ActualHeight;

        double top = tipTop + tipH + 8; // 优先放浮窗下方
        if (top + hintH > workBottom - 8)
            top = Math.Max(8, tipTop - 8 - hintH);   // 底边放不下 → 翻到上方
        double left = tipLeft + TipCard.ActualWidth / 2 - hintW / 2;
        left = Math.Clamp(left, 8, Math.Max(8, Width - hintW - 8));

        Canvas.SetLeft(HintBar, left);
        Canvas.SetTop(HintBar, top);
    }

    private void Tip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOrigin = e.GetPosition(this);
        _startLeft = Canvas.GetLeft(TipCard);
        _startTop = Canvas.GetTop(TipCard);
        if (double.IsNaN(_startLeft) || double.IsNaN(_startTop)) { _dragging = false; return; }
        TipCard.CaptureMouse();
        e.Handled = true;
    }

    private void Tip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        (double left, double top) = OsdPlacement.ClampToPrimary(
            _startLeft + p.X - _dragOrigin.X,
            _startTop + p.Y - _dragOrigin.Y,
            TipCard.ActualWidth, TipCard.ActualHeight);
        Canvas.SetLeft(TipCard, left);
        Canvas.SetTop(TipCard, top);
        PlaceHintBar(left, top);
    }

    private void Tip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TipCard.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Save()
    {
        (double left, double top) = OsdPlacement.ClampToPrimary(
            Canvas.GetLeft(TipCard), Canvas.GetTop(TipCard), TipCard.ActualWidth, TipCard.ActualHeight);
        Saved?.Invoke(left, top);
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd)!;
        src.CompositionTarget!.BackgroundColor = Colors.Transparent;

        var m = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref m);
        // 不用系统 backdrop：整屏窗口不适用亚克力，遮罩与卡片质感全部由 WPF 元素绘制
    }
}
