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
    /// <summary>测试提示回调（由 App 注入），保存自定义位置后预览真实效果。</summary>
    private readonly Action<TipKind, bool?>? _showTest;

    public SettingsWindow(Action<TipKind, bool?>? showTest = null)
    {
        _showTest = showTest;
        InitializeComponent();
        ApplyTheme();
        LoadValues();
        WireEvents();
        TrySetIcon();

        // 语言切换后 XAML 文案由绑定自动刷新，代码里拼好的数值标签需手动重算
        Loc.Changed += RefreshValueLabels;
        Closed += (_, _) => Loc.Changed -= RefreshValueLabels;
    }

    private void RefreshValueLabels()
    {
        UpdateDurationText(DurationSlider.Value);
        UpdateAnimSpeedText(AnimSpeedSlider.Value);
    }

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        Foreground = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        var secondary = new SolidColorBrush(ThemeHelper.TextSecondary(dark));
        SubText.Foreground = secondary;
        DurationText.Foreground = secondary;
        AnimSpeedText.Foreground = secondary;
        CustomPosDesc.Foreground = secondary;
        AnyCopyDesc.Foreground = secondary;
        FooterText.Foreground = secondary;
        LblKeys.Foreground = secondary;
        LblBehavior.Foreground = secondary;
        Resources["AccentBrush"] = new SolidColorBrush(ThemeHelper.AccentColor);

        Resources["CardBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
        Resources["CardStrokeBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
        Resources["SliderRestBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3D, 0x00, 0x00, 0x00));
        Resources["ThumbRingBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White);
        Resources["SegHoverBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
        var divider = new SolidColorBrush(ThemeHelper.Divider(dark));
        foreach (var sep in new[] { SepK1, SepK2, SepK3, SepK4, SepK5, SepK6, SepK7, SepB1, SepB2, SepB3, SepB4, SepB5 })
            sep.Background = divider;
    }

    private void LoadValues()
    {
        var s = AppSettings.Instance;
        CapsToggle.IsChecked = s.ShowCapsLock;
        NumToggle.IsChecked = s.ShowNumLock;
        ScrollToggle.IsChecked = s.ShowScrollLock;
        InsertToggle.IsChecked = s.ShowInsert;
        CopyToggle.IsChecked = s.ShowCopy;
        PasteToggle.IsChecked = s.ShowPaste;
        CutToggle.IsChecked = s.ShowCut;
        AnyCopyToggle.IsChecked = s.ShowAnyCopy;
        DurationSlider.Value = Math.Clamp(s.DisplaySeconds, 1, 5);
        UpdateDurationText(s.DisplaySeconds);
        AnimSpeedSlider.Value = Math.Clamp(s.AnimationSpeed, 0, 3);
        UpdateAnimSpeedText(s.AnimationSpeed);
        LoadPlacement(s.TipPlacement);
        StartupToggle.IsChecked = AppSettings.IsStartupSet();
        LangSystem.IsChecked = Loc.Language == AppLanguage.System;
        LangZh.IsChecked = Loc.Language == AppLanguage.Chinese;
        LangEn.IsChecked = Loc.Language == AppLanguage.English;
        RefreshPlacementUi();
    }

    /// <summary>位置行 UI 状态：自定义生效时禁用预设单选并启用「还原默认」。</summary>
    private void RefreshPlacementUi()
    {
        bool custom = AppSettings.Instance.HasCustomPosition;
        ResetPosBtn.IsEnabled = custom;
        bool presetEnabled = !custom;
        PosLeft.IsEnabled = presetEnabled;
        PosCenter.IsEnabled = presetEnabled;
        PosRight.IsEnabled = presetEnabled;
    }

    private void OpenPlacementPicker()
    {
        var picker = new PlacementPickerWindow { Owner = this };
        picker.Saved += (left, top) =>
        {
            Save(v => { v.CustomLeft = left; v.CustomTop = top; });
            RefreshPlacementUi();
            _showTest?.Invoke(TipKind.CapsLock, true); // 保存后弹出真实提示，直观确认位置
        };
        picker.ShowDialog();
        RefreshPlacementUi();
    }

    private void LoadPlacement(string placement)
    {
        PosLeft.IsChecked = placement == AppSettings.PlacementLeft;
        PosCenter.IsChecked = placement != AppSettings.PlacementLeft && placement != AppSettings.PlacementRight;
        PosRight.IsChecked = placement == AppSettings.PlacementRight;
    }

    private static string AnimSpeedLabel(double speed)
    {
        if (speed <= 0) return Loc.T("AnimNone");
        string x = speed == Math.Floor(speed) ? $"{speed:0}x" : $"{speed:0.#}x";
        return Loc.Format("ValueAnimSpeed", x, AppSettings.AnimationMs(speed));
    }

    private void UpdateAnimSpeedText(double speed) => AnimSpeedText.Text = AnimSpeedLabel(speed);

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
        CopyToggle.Checked += (_, _) => Save(v => v.ShowCopy = true);
        CopyToggle.Unchecked += (_, _) => Save(v => v.ShowCopy = false);
        PasteToggle.Checked += (_, _) => Save(v => v.ShowPaste = true);
        PasteToggle.Unchecked += (_, _) => Save(v => v.ShowPaste = false);
        CutToggle.Checked += (_, _) => Save(v => v.ShowCut = true);
        CutToggle.Unchecked += (_, _) => Save(v => v.ShowCut = false);
        AnyCopyToggle.Checked += (_, _) => Save(v => v.ShowAnyCopy = true);
        AnyCopyToggle.Unchecked += (_, _) => Save(v => v.ShowAnyCopy = false);

        DurationSlider.ValueChanged += (_, e) =>
        {
            UpdateDurationText(e.NewValue);
            Save(v => v.DisplaySeconds = e.NewValue);
        };

        AnimSpeedSlider.ValueChanged += (_, e) =>
        {
            double speed = Math.Round(e.NewValue, 1);
            UpdateAnimSpeedText(speed);
            Save(v => v.AnimationSpeed = speed);
        };

        StartupToggle.Checked += (_, _) => { AppSettings.SetStartup(true); Save(_ => { }); };
        StartupToggle.Unchecked += (_, _) => { AppSettings.SetStartup(false); Save(_ => { }); };

        LangSystem.Checked += (_, _) => SetLanguage(AppLanguage.System);
        LangZh.Checked += (_, _) => SetLanguage(AppLanguage.Chinese);
        LangEn.Checked += (_, _) => SetLanguage(AppLanguage.English);

        // 切换预设位置后弹一次真实提示，让用户直观看到落点
        PosLeft.Checked += (_, _) => { Save(v => v.TipPlacement = AppSettings.PlacementLeft); _showTest?.Invoke(TipKind.CapsLock, true); };
        PosCenter.Checked += (_, _) => { Save(v => v.TipPlacement = AppSettings.PlacementCenter); _showTest?.Invoke(TipKind.CapsLock, true); };
        PosRight.Checked += (_, _) => { Save(v => v.TipPlacement = AppSettings.PlacementRight); _showTest?.Invoke(TipKind.CapsLock, true); };

        CustomPosBtn.Click += (_, _) => OpenPlacementPicker();
        ResetPosBtn.Click += (_, _) =>
        {
            Save(v => { v.CustomLeft = null; v.CustomTop = null; });
            LoadPlacement(AppSettings.Instance.TipPlacement);
            RefreshPlacementUi();
            _showTest?.Invoke(TipKind.CapsLock, true);
        };
    }

    /// <summary>切换界面语言：写入设置并立即生效（XAML 文案由绑定自动刷新，无需重启）。</summary>
    private void SetLanguage(AppLanguage language)
    {
        Save(v => v.Language = Loc.ToSetting(language));
        Loc.Apply(language);
    }

    private void Save(Action<AppSettings> apply)
    {
        apply(AppSettings.Instance);
        AppSettings.Instance.Save();
    }

    private void UpdateDurationText(double v) =>
        DurationText.Text = Loc.Format("ValueSeconds", v == Math.Floor(v) ? $"{v:0}" : $"{v:0.#}");

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
