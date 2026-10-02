using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        ApplyTheme();
        LoadValues();
        WireEvents();
        TrySetIcon();
    }

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        var secondary = new SolidColorBrush(ThemeHelper.TextSecondary(dark));
        SubText.Foreground = secondary;
        DurationText.Foreground = secondary;
        AnimSpeedText.Foreground = secondary;
        FooterText.Foreground = secondary;
        LblKeys.Foreground = secondary;
        LblBehavior.Foreground = secondary;
        Resources["AccentBrush"] = new SolidColorBrush(ThemeHelper.AccentColor);

        Resources["CardBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
        Resources["CardStrokeBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
        Resources["SliderRestBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3D, 0x00, 0x00, 0x00));
        Resources["ThumbRingBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White);
        var divider = new SolidColorBrush(ThemeHelper.Divider(dark));
        foreach (var sep in new[] { SepK1, SepK2, SepK3, SepB1, SepB2 })
            sep.Background = divider;
    }

    private void LoadValues()
    {
        var s = AppSettings.Instance;
        CapsToggle.IsChecked = s.ShowCapsLock;
        NumToggle.IsChecked = s.ShowNumLock;
        ScrollToggle.IsChecked = s.ShowScrollLock;
        InsertToggle.IsChecked = s.ShowInsert;
        DurationSlider.Value = Math.Clamp(s.DisplaySeconds, 1, 5);
        UpdateDurationText(s.DisplaySeconds);
        AnimSpeedSlider.Value = Math.Clamp(s.AnimationSpeed, 0, 5);
        UpdateAnimSpeedText(s.AnimationSpeed);
        StartupToggle.IsChecked = AppSettings.IsStartupSet();
    }

    private static string AnimSpeedLabel(int speed) => speed switch
    {
        0 => "无动画",
        1 => "0.5x（150 毫秒）",
        2 => "1x（300 毫秒）",
        3 => "1.5x（450 毫秒）",
        4 => "2x（600 毫秒）",
        _ => "3x（900 毫秒）",
    };

    private void UpdateAnimSpeedText(int speed) => AnimSpeedText.Text = AnimSpeedLabel(speed);

    private void WireEvents()
    {
        CapsToggle.Checked += (_, _) => Save(v => v.ShowCapsLock = true);
        CapsToggle.Unchecked += (_, _) => Save(v => v.ShowCapsLock = false);
        NumToggle.Checked += (_, _) => Save(v => v.ShowNumLock = true);
        NumToggle.Unchecked += (_, _) => Save(v => v.ShowNumLock = false);
        ScrollToggle.Checked += (_, _) => Save(v => v.ShowScrollLock = true);
        ScrollToggle.Unchecked += (_, _) => Save(v => v.ShowScrollLock = false);
        InsertToggle.Checked += (_, _) => Save(v => v.ShowInsert = true);
        InsertToggle.Unchecked += (_, _) => Save(v => v.ShowInsert = false);

        DurationSlider.ValueChanged += (_, e) =>
        {
            UpdateDurationText(e.NewValue);
            Save(v => v.DisplaySeconds = e.NewValue);
        };

        AnimSpeedSlider.ValueChanged += (_, e) =>
        {
            int speed = (int)e.NewValue;
            UpdateAnimSpeedText(speed);
            Save(v => v.AnimationSpeed = speed);
        };

        StartupToggle.Checked += (_, _) => { AppSettings.SetStartup(true); Save(_ => { }); };
        StartupToggle.Unchecked += (_, _) => { AppSettings.SetStartup(false); Save(_ => { }); };
    }

    private void Save(Action<AppSettings> apply)
    {
        apply(AppSettings.Instance);
        AppSettings.Instance.Save();
    }

    private void UpdateDurationText(double v) =>
        DurationText.Text = v == Math.Floor(v) ? $"{v:0} 秒" : $"{v:0.#} 秒";

    private void TrySetIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico != null)
                Icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd)!;
        src.CompositionTarget!.BackgroundColor = Colors.Transparent;

        var m = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref m);

        bool dark = ThemeHelper.IsDarkTheme;
        int darkFlag = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));
        int backdrop = NativeMethods.DWMSBT_MAINWINDOW;
        int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        if (hr != 0)
            Panel.Background = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });

        e.Handled = true;
    }
}
