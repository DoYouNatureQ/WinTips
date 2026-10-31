using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

public partial class NoticeWindow : Window
{
    public NoticeWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        CaptionTitleText.Text = title;
        MessageText.Text = message;
        ApplyPlatformChrome();
        ApplyTheme();
        TrySetIcon();
    }

    /// <summary>
    /// Win11 沿用系统标题栏（Mica + 圆角）；Win10 上系统标题栏是 Aero 式方形边框，
    /// 改用无边框 + 自绘标题栏保持观感一致。
    /// </summary>
    private void ApplyPlatformChrome()
    {
        if (DwmChrome.SupportsFluent) return;
        WindowStyle = WindowStyle.None;
        LegacyCaption.Visibility = Visibility.Visible;
    }

    private void LegacyCaption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { /* 鼠标已抬起，忽略 */ }
    }

    private void LegacyClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        ThemeHelper.ApplyTemplateResources(this, dark);
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        MessageText.Foreground = new SolidColorBrush(ThemeHelper.TextSecondary(dark));
    }

    private void TrySetIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico != null)
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Icon = src;
                AppIcon.Source = src;
            }
        }
        catch { }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd)!;

        bool dark = ThemeHelper.IsDarkTheme;
        var surface = new SolidColorBrush(ThemeHelper.WindowSurface(dark));

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);

        // 拿不到 Mica（Win10 / 高对比度）就不做“整窗玻璃”：扩展边框只会变成实心色带
        bool mica = ThemeHelper.GlassAllowed && DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_MAINWINDOW);
        if (mica)
        {
            src.CompositionTarget!.BackgroundColor = Colors.Transparent;
            DwmChrome.SetFrameExtended(hwnd, true);
            return;
        }

        DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_NONE);
        DwmChrome.SetFrameExtended(hwnd, false);
        Background = surface;
        Panel.Background = surface;
    }

    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
