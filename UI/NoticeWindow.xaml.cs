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

    private void ApplyPlatformChrome()
    {
        if (DwmChrome.SupportsFluent) return;
        WindowStyle = WindowStyle.None;
        LegacyCaption.Visibility = Visibility.Visible;
    }

    private void LegacyCaption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) {  }
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
        var src = AppIcons.Source;
        if (src == null) return;
        Icon = src;
        AppIcon.Source = src;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd)!;

        bool dark = ThemeHelper.IsDarkTheme;
        var surface = new SolidColorBrush(ThemeHelper.WindowSurface(dark));

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);

        bool mica = ThemeHelper.MicaAllowed && DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_MAINWINDOW);
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
