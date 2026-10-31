using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 自定义位置模式：全屏半透明遮罩（置于浮窗与拖动提示之下）压暗桌面并挡住桌面点击；
/// 提示浮窗与真实 OSD 同尺寸同外观（所见即所得），按住拖动；操作条（提示文字 + 取消/保存）
/// 跟随浮窗，下方优先、贴近工作区底边时翻到浮窗上方。遮罩窗口铺满主显示器（与 OSD
/// 显示范围一致），保存时以浮窗左上角作为提示位置（DIP，主屏坐标系）。
/// 辅助线：屏幕四边各 15% 的贴边判定区用虚线框出，按生效方向高亮对应的一条边缘带
/// （左右优先于上下，与真实提示的滑入方向一致）；浮窗中心靠近屏幕中轴时自动吸附对齐。
/// 定位方式：按住浮窗拖动，或用方向键逐像素微调（长按连发）。
/// </summary>
public partial class PlacementPickerWindow : Window
{
    /// <summary>中轴吸附距离（DIP）：浮窗中心距屏幕中轴在此范围内即对齐到正中心。</summary>
    private const double SnapDistance = 8;

    /// <summary>方向键连发：按住多久后开始连发、连发间隔（毫秒）。</summary>
    private const int RepeatDelayMs = 350;
    private const int RepeatIntervalMs = 40;

    /// <summary>点击“保存位置”时触发，参数为钳制后的提示左上角坐标（DIP）。</summary>
    public event Action<double, double>? Saved;

    private bool _dragging;
    private Point _dragOrigin;
    private double _startLeft, _startTop;

    private bool _snapX, _snapY;

    // 辅助线元素，下标与 OsdPlacement.PopSide 一一对应（Top=0 / Bottom=1 / Left=2 / Right=3）
    private readonly Rectangle[] _bands = new Rectangle[4];
    private readonly Line[] _zoneLines = new Line[4];
    private Line? _snapV;
    private Line? _snapH;

    private Brush _guideBrush = Brushes.Gray;
    private Brush _accentBrush = Brushes.DodgerBlue;
    private Brush _bandBrush = Brushes.Transparent;

    /// <summary>方向键连发计时器：首次按下立即走 1 像素，按住 RepeatDelayMs 后按 RepeatIntervalMs 持续移动。</summary>
    private readonly DispatcherTimer _repeat = new() { Interval = TimeSpan.FromMilliseconds(RepeatDelayMs) };
    private Key _repeatKey = Key.None;

    public PlacementPickerWindow()
    {
        InitializeComponent();
        ApplyTheme();

        // 遮罩只盖主显示器（与 OSD 的显示范围一致）；坐标即屏幕 DIP，保存时无需换算
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        BuildGuides();

        Loaded += (_, _) => LayoutInitial();
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded) return; // 尺寸随显示配置变化时重算辅助线几何
            MoveCard(Canvas.GetLeft(TipCard), Canvas.GetTop(TipCard), allowSnap: true);
        };
        CancelBtn.Click += (_, _) => Close();
        SaveBtn.Click += (_, _) => Save();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => StopRepeat();
        Closed += (_, _) => StopRepeat();

        _repeat.Tick += (_, _) =>
        {
            _repeat.Interval = TimeSpan.FromMilliseconds(RepeatIntervalMs);
            Nudge(_repeatKey, StepSize());
        };

        TipCard.MouseLeftButtonDown += Tip_MouseLeftButtonDown;
        TipCard.MouseMove += Tip_MouseMove;
        TipCard.MouseLeftButtonUp += Tip_MouseLeftButtonUp;
        TipCard.LostMouseCapture += (_, _) => _dragging = false;
    }

    private double ScreenW => ActualWidth > 0 ? ActualWidth : Width;
    private double ScreenH => ActualHeight > 0 ? ActualHeight : Height;

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        ThemeHelper.ApplyTemplateResources(this, dark);
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        var secondary = new SolidColorBrush(ThemeHelper.TextSecondary(dark));
        HintText.Foreground = secondary;
        ZoneHint.Foreground = secondary;

        // 遮罩上的卡片无法用 DWM 亚克力，改用高不透明度纯色模拟同款质感；
        // 高对比度下改为完全不透明的系统窗口色，避免半透明把对比度拉低
        var card = new SolidColorBrush(ThemeHelper.PickerSurface(dark));
        var stroke = new SolidColorBrush(ThemeHelper.PickerStroke(dark));
        TipCard.Background = card;
        TipCard.BorderBrush = stroke;
        HintBar.Background = card;
        HintBar.BorderBrush = stroke;

        Resources["ScrimBrush"] = new SolidColorBrush(ThemeHelper.PickerScrim());

        var accent = ThemeHelper.AccentColor;
        _guideBrush = secondary;
        _accentBrush = new SolidColorBrush(accent);
        _bandBrush = new SolidColorBrush(
            ThemeHelper.Tint(accent, ThemeHelper.HighContrast ? (byte)0x40 : (byte)0x1F));
        ApplyGuideBrushes();
    }

    /// <summary>
    /// 创建辅助线：4 条贴边判定区边界虚线、4 条边缘高亮带（同一时刻只亮一条）、2 条中轴吸附辅助线。
    /// 高亮带铺满整条边缘，卡片摆在四角时相邻的那条边也能被高亮覆盖。
    /// </summary>
    private void BuildGuides()
    {
        if (_zoneLines[0] != null) return;

        for (int i = 0; i < _bands.Length; i++)
        {
            var band = new Rectangle { Visibility = Visibility.Collapsed };
            GuideLayer.Children.Add(band);
            _bands[i] = band;
        }
        for (int i = 0; i < _zoneLines.Length; i++)
        {
            var line = new Line
            {
                StrokeThickness = 1,
                Opacity = 0.55,
                StrokeDashArray = new DoubleCollection { 4, 4 },
            };
            GuideLayer.Children.Add(line);
            _zoneLines[i] = line;
        }

        _snapV = MakeSnapLine();
        _snapH = MakeSnapLine();
        ApplyGuideBrushes();
    }

    private Line MakeSnapLine()
    {
        var line = new Line
        {
            StrokeThickness = 1,
            Opacity = 0.45,
            StrokeDashArray = new DoubleCollection { 1, 3 },
        };
        GuideLayer.Children.Add(line);
        return line;
    }

    private void ApplyGuideBrushes()
    {
        foreach (var band in _bands)
            if (band != null) band.Fill = _bandBrush;
        foreach (var line in _zoneLines)
            if (line != null) line.Stroke = _guideBrush;
        // 中轴线的描边在 UpdateGuides 里按是否吸附逐条设置，这里只兜底初始色
        if (_snapV != null) _snapV.Stroke = _guideBrush;
        if (_snapH != null) _snapH.Stroke = _guideBrush;
    }

    /// <summary>布局浮窗与操作条的初始位置：已有自定义用自定义值，否则模拟当前预设位置。</summary>
    private void LayoutInitial()
    {
        var s = AppSettings.Instance;
        (double left, double top) = s.HasCustomPosition
            ? (s.CustomLeft!.Value, s.CustomTop!.Value)
            : OsdPlacement.DefaultFor(AppSettings.NormalizePlacement(s.TipPlacement),
                AppSettings.NormalizeVertical(s.TipVertical),
                TipCard.ActualWidth, TipCard.ActualHeight, OsdPlacement.PrimaryScale());

        MoveCard(left, top, allowSnap: true);

        GuideLayer.Opacity = 1;
        TipCard.Opacity = 1;
        HintBar.Opacity = 1;
    }

    /// <summary>落位：钳制进屏内 → 中轴吸附 → 移动浮窗与操作条 → 刷新高亮带与辅助线。</summary>
    private void MoveCard(double left, double top, bool allowSnap)
    {
        double w = TipCard.ActualWidth, h = TipCard.ActualHeight;
        if (w <= 0 || h <= 0) return;

        (left, top) = OsdPlacement.ClampToPrimary(left, top, w, h);
        if (allowSnap) (left, top, _snapX, _snapY) = SnapToCenter(left, top, w, h);
        else _snapX = _snapY = false;

        Canvas.SetLeft(TipCard, left);
        Canvas.SetTop(TipCard, top);
        PlaceHintBar(left, top);
        UpdateGuides(left, top, w, h);
    }

    /// <summary>中轴吸附：浮窗中心与屏幕水平/垂直中轴的距离在 SnapDistance 内时对齐到正中心。</summary>
    private (double Left, double Top, bool SnapX, bool SnapY) SnapToCenter(double left, double top, double w, double h)
    {
        double centerLeft = (ScreenW - w) / 2;
        double centerTop = (ScreenH - h) / 2;
        bool snapX = Math.Abs(left - centerLeft) <= SnapDistance;
        bool snapY = Math.Abs(top - centerTop) <= SnapDistance;
        return (snapX ? centerLeft : left, snapY ? centerTop : top, snapX, snapY);
    }

    /// <summary>重算辅助线几何，并按浮窗当前位置高亮生效的贴边方向。</summary>
    private void UpdateGuides(double left, double top, double w, double h)
    {
        if (_zoneLines[0] == null) return;
        double sw = ScreenW, sh = ScreenH;
        if (sw <= 0 || sh <= 0) return;

        double zw = sw * OsdPlacement.ZoneRatio;
        double zh = sh * OsdPlacement.ZoneRatio;

        // 四边判定区的内边界虚线（外侧边界与屏幕边框重合，不画）
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Top], 0, zh, sw, zh);
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Bottom], 0, sh - zh, sw, sh - zh);
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Left], zw, 0, zw, sh);
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Right], sw - zw, 0, sw - zw, sh);

        // 高亮带铺满整条边缘（含四角）：卡片摆在角落时高亮仍与它相邻；同一时刻只亮一条，不会叠色
        SetRect(_bands[(int)OsdPlacement.PopSide.Top], 0, 0, sw, zh);
        SetRect(_bands[(int)OsdPlacement.PopSide.Bottom], 0, sh - zh, sw, zh);
        SetRect(_bands[(int)OsdPlacement.PopSide.Left], 0, 0, zw, sh);
        SetRect(_bands[(int)OsdPlacement.PopSide.Right], sw - zw, 0, zw, sh);

        // 只高亮真正生效的那一条边（左右优先于上下，与真实提示的滑动方向一致）
        var active = OsdPlacement.ActiveSide(left, top, w, h);
        Show(_bands[(int)OsdPlacement.PopSide.Left], active == OsdPlacement.PopSide.Left);
        Show(_bands[(int)OsdPlacement.PopSide.Right], active == OsdPlacement.PopSide.Right);
        Show(_bands[(int)OsdPlacement.PopSide.Top], active == OsdPlacement.PopSide.Top);
        Show(_bands[(int)OsdPlacement.PopSide.Bottom], active == OsdPlacement.PopSide.Bottom);

        // 中轴辅助线：平时淡显，吸附成功时转为强调色
        if (_snapV != null) SetLine(_snapV, sw / 2, 0, sw / 2, sh);
        if (_snapH != null) SetLine(_snapH, 0, sh / 2, sw, sh / 2);
        StyleSnapLine(_snapV, _snapX);
        StyleSnapLine(_snapH, _snapY);
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void StyleSnapLine(Line? line, bool snapped)
    {
        if (line == null) return;
        line.Stroke = snapped ? _accentBrush : _guideBrush;
        line.Opacity = snapped ? 1 : 0.45;
    }

    private static void SetLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
    }

    private static void SetRect(Rectangle rect, double left, double top, double w, double h)
    {
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        rect.Width = w;
        rect.Height = h;
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
        left = Math.Clamp(left, 8, Math.Max(8, ScreenW - hintW - 8));

        Canvas.SetLeft(HintBar, left);
        Canvas.SetTop(HintBar, top);
    }

    private void Tip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        StopRepeat(); // 拖动期间停掉方向键连发，避免两个来源同时改坐标
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
        MoveCard(_startLeft + p.X - _dragOrigin.X, _startTop + p.Y - _dragOrigin.Y, allowSnap: true);
    }

    private void Tip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TipCard.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>
    /// 方向键像素级微调：按一下移动 1 像素（按住 Shift 为 10 像素），按住不放先等 RepeatDelayMs
    /// 再按 RepeatIntervalMs 连发。系统自身的按键重复（e.IsRepeat）忽略掉，节奏统一由计时器控制。
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        if (!IsArrow(e.Key) || e.IsRepeat) return;

        Nudge(e.Key, StepSize());
        _repeatKey = e.Key;
        _repeat.Interval = TimeSpan.FromMilliseconds(RepeatDelayMs);
        _repeat.Stop();
        _repeat.Start();
        e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == _repeatKey) StopRepeat();
    }

    private void StopRepeat()
    {
        _repeat.Stop();
        _repeatKey = Key.None;
    }

    private static bool IsArrow(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down;

    /// <summary>单次步长：默认 1 像素，按住 Shift 为 10 像素。</summary>
    private static double StepSize() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;

    private void Nudge(Key key, double step)
    {
        double left = Canvas.GetLeft(TipCard), top = Canvas.GetTop(TipCard);
        if (double.IsNaN(left) || double.IsNaN(top)) return;

        switch (key)
        {
            case Key.Left: left -= step; break;
            case Key.Right: left += step; break;
            case Key.Up: top -= step; break;
            case Key.Down: top += step; break;
            default: return;
        }
        // 键盘微调不参与中轴吸附：否则贴着中轴时怎么按都还在吸附范围内，挪不开 1 像素
        MoveCard(left, top, allowSnap: false);
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
