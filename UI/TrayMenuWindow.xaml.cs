using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

public partial class TrayMenuWindow : Window
{
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly Action<bool> _toggleStartup;
    private readonly Action<TipKind, bool?> _showTest;
    private bool _startup;
    private StackPanel? _testSection;
    private TextBlock? _chevron;
    private IntPtr _mouseHook;
    private IntPtr _hwnd;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private long _openedAt;
    private bool _seenDown;
    private double _taskbarTopPx = double.MaxValue;
    private static TrayMenuWindow? _active;

    internal TrayMenuWindow(Action openSettings, Action exit, Action<bool> toggleStartup,
        bool startup, Action<TipKind, bool?> showTest)
    {
        _openSettings = openSettings;
        _exit = exit;
        _toggleStartup = toggleStartup;
        _startup = startup;
        _showTest = showTest;

        _mouseProc = OutsideClickHookProc;

        InitializeComponent();
        ApplyTheme();
        BuildItems();

        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => _seenDown = true), true);
    }

    private bool InputAllowed() =>
        Environment.TickCount64 - _openedAt >= 300 && _seenDown;

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        Resources["HoverBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00));

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var src = HwndSource.FromHwnd(hwnd)!;
        src.CompositionTarget!.BackgroundColor = Colors.Transparent;

        int ex = NativeMethods.GetWindowLong32(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong32(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST);

        var m = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref m);

        int darkFlag = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));
        int corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        if (hr != 0)
            Root.Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF9, 0xF9, 0xF9));
    }

    private Border AddItem(string label, string? glyph, Action onClick, bool? isChecked = null,
        bool isChevron = false, bool indent = false, bool closeOnClick = true)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (indent) text.Margin = new Thickness(6, 0, 0, 0);

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };

        if (!string.IsNullOrEmpty(glyph))
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.9,
            };
            grid.Children.Add(icon);
            Grid.SetColumn(icon, 0);
            text.Margin = new Thickness(12, 0, 0, 0);
        }
        else if (indent)
        {
            text.Margin = new Thickness(6, 0, 0, 0);
        }

        Grid.SetColumn(text, 1);

        if (isChevron)
        {
            _chevron = new TextBlock
            {
                Text = "\uE70D",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
            };
            grid.Children.Add(_chevron);
            Grid.SetColumn(_chevron, 2);
        }
        else if (isChecked.HasValue)
        {
            var check = new TextBlock
            {
                Text = isChecked.Value ? "\uE73E" : "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeHelper.AccentColor),
            };
            grid.Children.Add(check);
            Grid.SetColumn(check, 2);
        }

        grid.Children.Add(text);

        var border = new Border { Style = (Style)Resources["MenuItemBorder"], Child = grid };
        border.MouseLeftButtonUp += (_, _) =>
        {
            if (!InputAllowed()) return;
            if (closeOnClick) Close();
            onClick();
        };
        ItemsHost.Children.Add(border);
        return border;
    }

    private void AddSeparator()
    {
        ItemsHost.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(6, 5, 6, 5),
            Background = new SolidColorBrush(ThemeHelper.IsDarkTheme
                ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x1F, 0x00, 0x00, 0x00)),
        });
    }

    private void BuildItems()
    {
        AddItem("设置", "\uE713", _openSettings);

        var testBorder = AddItem("测试提示", "\uE765", ToggleTestSection, isChevron: true, closeOnClick: false);
        _testSection = new StackPanel { Visibility = Visibility.Collapsed };
        _testSection.Children.Add(MakeTestItem("大写锁定 开启", TipKind.CapsLock, true));
        _testSection.Children.Add(MakeTestItem("大写锁定 关闭", TipKind.CapsLock, false));
        _testSection.Children.Add(MakeTestItem("数字锁定 开启", TipKind.NumLock, true));
        _testSection.Children.Add(MakeTestItem("数字锁定 关闭", TipKind.NumLock, false));
        _testSection.Children.Add(MakeTestItem("滚动锁定 开启", TipKind.ScrollLock, true));
        _testSection.Children.Add(MakeTestItem("滚动锁定 关闭", TipKind.ScrollLock, false));
        _testSection.Children.Add(MakeTestItem("Insert 键已按下", TipKind.Insert, null));
        ItemsHost.Children.Add(_testSection);
        _ = testBorder;

        AddSeparator();

        AddItem("开机自动运行", null, () =>
        {
            _startup = !_startup;
            _toggleStartup(_startup);
        }, isChecked: _startup);

        AddItem("退出", "\uE7E8", _exit);
    }

    private Border MakeTestItem(string label, TipKind kind, bool? state)
    {
        var border = new Border
        {
            Style = (Style)Resources["MenuItemBorder"],
            Padding = new Thickness(10, 7, 10, 7),
            Child = new TextBlock { Text = label, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center },
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            if (!InputAllowed()) return;
            Close();
            _showTest(kind, state);
        };
        return border;
    }

    private void ToggleTestSection()
    {
        if (_testSection == null || _chevron == null) return;
        bool expand = _testSection.Visibility != Visibility.Visible;
        _testSection.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        _chevron.Text = expand ? "\uE70E" : "\uE70D";

        if (expand)
        {
            UpdateLayout();
            double scale = 1.0;
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source?.CompositionTarget != null)
            {
                double m = source.CompositionTarget.TransformToDevice.M11;
                if (m > 0) scale = m;
            }
            var tb = TaskbarInfo.GetPrimaryTaskbar();
            double bottomLimit = tb.HasValue && tb.Value.Edge == NativeMethods.ABE_BOTTOM && !tb.Value.AutoHide
                ? tb.Value.Rect.Top / scale
                : SystemParameters.WorkArea.Bottom;
            Top = Math.Max(8, Math.Min(Top, bottomLimit - ActualHeight - 8));
        }
    }

    private IntPtr OutsideClickHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam.ToInt64();
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN
                or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_XBUTTONDOWN
                or NativeMethods.WM_NCLBUTTONDOWN or NativeMethods.WM_NCRBUTTONDOWN)
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                if (_taskbarTopPx < double.MaxValue && info.pt.Y >= _taskbarTopPx)
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

                if (NativeMethods.GetWindowRect(_hwnd, out var r))
                {
                    bool inside = info.pt.X >= r.Left && info.pt.X < r.Right
                               && info.pt.Y >= r.Top && info.pt.Y < r.Bottom;
                    if (!inside)
                    {
                        const uint LLKHF_INJECTED = 0x10;
                        bool injected = (info.flags & LLKHF_INJECTED) != 0;
                        if (injected)
                            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                        if (Environment.TickCount64 - _openedAt < 300)
                            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                        Dispatcher.BeginInvoke(Close);
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void InstallOutsideClickHook()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var tb = TaskbarInfo.GetPrimaryTaskbar();
        if (tb.HasValue && tb.Value.Edge == NativeMethods.ABE_BOTTOM && !tb.Value.AutoHide)
            _taskbarTopPx = tb.Value.Rect.Top;
        else
            _taskbarTopPx = SystemParameters.WorkArea.Bottom *
                (HwndSource.FromHwnd(_hwnd)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);

        if (_active != null && !ReferenceEquals(_active, this))
        {
            var old = _active;
            old.Dispatcher.BeginInvoke(() => old.Close());
        }
        _active = this;

        if (_mouseHook == IntPtr.Zero)
            _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, NativeMethods.GetModuleHandleW(null), 0);
    }

    public void ShowMenuAtCursor()
    {
        Left = -200000;
        Top = -200000;
        Opacity = 0;
        Show();
        _openedAt = Environment.TickCount64;
        _seenDown = false;
        UpdateLayout();
        Activate();
        InstallOutsideClickHook();

        double scale = 1.0;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (source?.CompositionTarget != null)
        {
            double m = source.CompositionTarget.TransformToDevice.M11;
            if (m > 0) scale = m;
        }
        var cursor = System.Windows.Forms.Cursor.Position;
        double cx = cursor.X / scale;

        double menuW = ActualWidth, menuH = ActualHeight;

        var tb = TaskbarInfo.GetPrimaryTaskbar();
        double bottomLimit = tb.HasValue && tb.Value.Edge == NativeMethods.ABE_BOTTOM && !tb.Value.AutoHide
            ? tb.Value.Rect.Top / scale
            : SystemParameters.WorkArea.Bottom;

        Left = Math.Clamp(cx - menuW, 8, SystemParameters.PrimaryScreenWidth - menuW - 8);
        Top = Math.Max(8, bottomLimit - menuH - 8);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(_active, this)) _active = null;
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
