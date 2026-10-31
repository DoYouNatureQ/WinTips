using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
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
        ApplyPlatformChrome();
        ApplyTheme();
        LoadValues();
        WireEvents();
        TrySetIcon();

        // 语言切换后 XAML 文案由绑定自动刷新，代码里拼好的数值标签需手动重算
        Loc.Changed += RefreshValueLabels;
        ThemeHelper.Changed += OnSystemThemeChanged;
        Closed += (_, _) =>
        {
            Loc.Changed -= RefreshValueLabels;
            ThemeHelper.Changed -= OnSystemThemeChanged;
        };
    }

    /// <summary>系统深浅色 / 高对比度切换后重刷配色与窗口材质。</summary>
    private void OnSystemThemeChanged()
    {
        ApplyTheme();
        ApplyChrome();
    }

    /// <summary>
    /// Win11 保留系统标题栏（自带 Mica 与圆角，和系统设置页一致）；
    /// Win10 拿不到 Mica/圆角，系统标题栏会露出 Aero 时代的方形边框，
    /// 因此改为无边框 + 自绘标题栏，杜绝“Win7 样式窗口”。
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

    private void RefreshValueLabels()
    {
        UpdateDurationText(DurationSlider.Value);
        UpdateAnimSpeedText(AnimSpeedSlider.Value);
    }

    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        ThemeHelper.ApplyTemplateResources(this, dark);

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
        DurationSlider.Value = Math.Clamp(s.DisplaySeconds, 0.5, 5);
        UpdateDurationText(DurationSlider.Value);
        AnimSpeedSlider.Value = Math.Clamp(s.AnimationSpeed, 0, 3);
        UpdateAnimSpeedText(s.AnimationSpeed);
        LoadPlacement();
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
        PosTop.IsEnabled = presetEnabled;
        PosBottom.IsEnabled = presetEnabled;
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

    /// <summary>程序化回填单选态时置位，避免触发 Checked 里的保存与预览提示。</summary>
    private bool _syncingPlacement;

    /// <summary>回填预设位置：垂直（顶部/底部）与水平（左/中/右）两组单选各自独立。</summary>
    private void LoadPlacement()
    {
        var s = AppSettings.Instance;
        string h = AppSettings.NormalizePlacement(s.TipPlacement);
        string v = AppSettings.NormalizeVertical(s.TipVertical);

        _syncingPlacement = true;
        try
        {
            PosTop.IsChecked = v == AppSettings.PlacementTop;
            PosBottom.IsChecked = v == AppSettings.PlacementBottom;
            PosLeft.IsChecked = h == AppSettings.PlacementLeft;
            PosCenter.IsChecked = h == AppSettings.PlacementCenter;
            PosRight.IsChecked = h == AppSettings.PlacementRight;
        }
        finally { _syncingPlacement = false; }
    }

    /// <summary>
    /// 预设位置变更（顶部/底部与左/中/右可自由组合）：写入设置后弹一次真实提示，
    /// 让用户直观看到落点。只传需要改动的那个轴，另一轴保持当前值。
    /// </summary>
    private void OnPresetChecked(string? horizontal, string? vertical)
    {
        if (_syncingPlacement) return;
        Save(v =>
        {
            if (horizontal != null) v.TipPlacement = horizontal;
            if (vertical != null) v.TipVertical = vertical;
        });
        _showTest?.Invoke(TipKind.CapsLock, true);
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
        PosTop.Checked += (_, _) => OnPresetChecked(null, AppSettings.PlacementTop);
        PosBottom.Checked += (_, _) => OnPresetChecked(null, AppSettings.PlacementBottom);
        PosLeft.Checked += (_, _) => OnPresetChecked(AppSettings.PlacementLeft, null);
        PosCenter.Checked += (_, _) => OnPresetChecked(AppSettings.PlacementCenter, null);
        PosRight.Checked += (_, _) => OnPresetChecked(AppSettings.PlacementRight, null);

        CustomPosBtn.Click += (_, _) => OpenPlacementPicker();
        ResetPosBtn.Click += (_, _) =>
        {
            Save(v => { v.CustomLeft = null; v.CustomTop = null; });
            LoadPlacement();
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
        ApplyChrome();
    }

    /// <summary>
    /// 窗口材质：只有真正拿到 Mica 才让客户区透明。Win10 / 高对比度 / 合成关闭时，
    /// -1 扩展边框会让客户区变成 DWM 的实心填充色，在面板四周留一圈 Win7 Aero 式色带，
    /// 因此必须退回“整窗不透明纯色”。可重复调用，用于运行时切换主题。
    /// </summary>
    private void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var src = HwndSource.FromHwnd(hwnd);
        if (src?.CompositionTarget == null) return;

        bool dark = ThemeHelper.IsDarkTheme;
        var surface = new SolidColorBrush(ThemeHelper.WindowSurface(dark));

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);

        bool mica = ThemeHelper.GlassAllowed && DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_MAINWINDOW);
        if (mica)
        {
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            DwmChrome.SetFrameExtended(hwnd, true);
            Background = Brushes.Transparent;
            Panel.Background = Brushes.Transparent;
            return;
        }

        DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_NONE);
        DwmChrome.SetFrameExtended(hwnd, false);
        Background = surface;
        Panel.Background = surface;
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
