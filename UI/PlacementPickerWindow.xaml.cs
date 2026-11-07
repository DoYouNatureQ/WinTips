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

public partial class PlacementPickerWindow : Window
{
    private const double SnapDistance = 8;

    private const int RepeatDelayMs = 350;
    private const int RepeatIntervalMs = 40;

    public event Action<double, double>? Saved;

    private bool _dragging;
    private Point _dragOrigin;
    private double _startLeft, _startTop;

    private bool _snapX, _snapY;

    private readonly Rectangle[] _bands = new Rectangle[4];
    private readonly Line[] _zoneLines = new Line[4];
    private Line? _snapV;
    private Line? _snapH;

    private Brush _guideBrush = Brushes.Gray;
    private Brush _accentBrush = Brushes.DodgerBlue;
    private Brush _bandBrush = Brushes.Transparent;

    private readonly DispatcherTimer _repeat = new() { Interval = TimeSpan.FromMilliseconds(RepeatDelayMs) };
    private Key _repeatKey = Key.None;

    public PlacementPickerWindow()
    {
        InitializeComponent();
        ApplyTheme();

        double scale = AppSettings.NormalizeScale(AppSettings.Instance.TipScale);
        if (Math.Abs(scale - 1) > 0.001) TipCard.LayoutTransform = new ScaleTransform(scale, scale);

        Left = _area.Left;
        Top = _area.Top;
        Width = _area.Width;
        Height = _area.Height;

        BuildGuides();

        Loaded += (_, _) => LayoutInitial();
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded) return;
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

    private MonitorArea _area = MonitorArea.Primary();

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        ThemeHelper.ApplyTemplateResources(this, dark);
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        var secondary = new SolidColorBrush(ThemeHelper.TextSecondary(dark));
        HintText.Foreground = secondary;
        ZoneHint.Foreground = secondary;

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
        if (_snapV != null) _snapV.Stroke = _guideBrush;
        if (_snapH != null) _snapH.Stroke = _guideBrush;
    }

    private void LayoutInitial()
    {
        var s = AppSettings.Instance;
        _area = MonitorArea.Primary();
        (double left, double top) = s.HasCustomPosition
            ? (s.CustomLeft!.Value, s.CustomTop!.Value)
            : OsdPlacement.DefaultFor(AppSettings.NormalizePlacement(s.TipPlacement),
                AppSettings.NormalizeVertical(s.TipVertical),
                TipCard.ActualWidth, TipCard.ActualHeight, _area);

        MoveCard(left, top, allowSnap: true);

        GuideLayer.Opacity = 1;
        TipCard.Opacity = 1;
        HintBar.Opacity = 1;
    }

    private void MoveCard(double left, double top, bool allowSnap)
    {
        double w = TipCard.ActualWidth, h = TipCard.ActualHeight;
        if (w <= 0 || h <= 0) return;

        (left, top) = OsdPlacement.ClampToUsable(left, top, w, h, _area);
        if (allowSnap) (left, top, _snapX, _snapY) = SnapToCenter(left, top, w, h);
        else _snapX = _snapY = false;

        Canvas.SetLeft(TipCard, left);
        Canvas.SetTop(TipCard, top);
        MirrorPreviewBar(top, h);
        PlaceHintBar(left, top);
        UpdateGuides(left, top, w, h);
    }

    private void MirrorPreviewBar(double top, double h)
    {
        bool onTop = top + h / 2 < ScreenH / 2;
        TipContent.Margin = onTop ? new Thickness(16, 8, 12, 4) : new Thickness(16, 4, 12, 8);
        TipBar.VerticalAlignment = onTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        TipBar.Margin = onTop ? new Thickness(0, 6, 0, 0) : new Thickness(0, 0, 0, 6);
    }

    private (double Left, double Top, bool SnapX, bool SnapY) SnapToCenter(double left, double top, double w, double h)
    {
        double centerLeft = (ScreenW - w) / 2;
        double centerTop = (ScreenH - h) / 2;
        bool snapX = Math.Abs(left - centerLeft) <= SnapDistance;
        bool snapY = Math.Abs(top - centerTop) <= SnapDistance;
        return (snapX ? centerLeft : left, snapY ? centerTop : top, snapX, snapY);
    }

    private void UpdateGuides(double left, double top, double w, double h)
    {
        if (_zoneLines[0] == null) return;
        double sw = ScreenW, sh = ScreenH;
        if (sw <= 0 || sh <= 0) return;

        double zw = sw * OsdPlacement.ZoneRatio;
        double zh = sh * OsdPlacement.ZoneRatio;

        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Top], 0, zh, sw, zh);
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Bottom], 0, sh - zh, sw, sh - zh);
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Left], zw, 0, zw, sh);
        SetLine(_zoneLines[(int)OsdPlacement.PopSide.Right], sw - zw, 0, sw - zw, sh);

        SetRect(_bands[(int)OsdPlacement.PopSide.Top], 0, 0, sw, zh);
        SetRect(_bands[(int)OsdPlacement.PopSide.Bottom], 0, sh - zh, sw, zh);
        SetRect(_bands[(int)OsdPlacement.PopSide.Left], 0, 0, zw, sh);
        SetRect(_bands[(int)OsdPlacement.PopSide.Right], sw - zw, 0, zw, sh);

        var active = OsdPlacement.ActiveSide(left, top, w, h, _area);
        Show(_bands[(int)OsdPlacement.PopSide.Left], active == OsdPlacement.PopSide.Left);
        Show(_bands[(int)OsdPlacement.PopSide.Right], active == OsdPlacement.PopSide.Right);
        Show(_bands[(int)OsdPlacement.PopSide.Top], active == OsdPlacement.PopSide.Top);
        Show(_bands[(int)OsdPlacement.PopSide.Bottom], active == OsdPlacement.PopSide.Bottom);

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

    private void PlaceHintBar(double tipLeft, double tipTop)
    {
        double workBottom = _area.Usable().Bottom;
        double hintW = HintBar.ActualWidth, hintH = HintBar.ActualHeight;
        double tipH = TipCard.ActualHeight;

        double top = tipTop + tipH + 8;
        if (top + hintH > workBottom - 8)
            top = Math.Max(8, tipTop - 8 - hintH);
        double left = tipLeft + TipCard.ActualWidth / 2 - hintW / 2;
        left = Math.Clamp(left, 8, Math.Max(8, ScreenW - hintW - 8));

        Canvas.SetLeft(HintBar, left);
        Canvas.SetTop(HintBar, top);
    }

    private void Tip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        StopRepeat();
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
        MoveCard(left, top, allowSnap: false);
    }

    private void Save()
    {
        (double left, double top) = OsdPlacement.ClampToUsable(
            Canvas.GetLeft(TipCard), Canvas.GetTop(TipCard), TipCard.ActualWidth, TipCard.ActualHeight, _area);
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
    }
}
