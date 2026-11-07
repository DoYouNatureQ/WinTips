using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
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
    private readonly Action<TipKind, bool?>? _showTest;

    private readonly Action<double>? _previewSize;

    private readonly Action<double>? _previewAppearance;

    private bool _draggingScale;

    private bool _draggingGlass;

    private const double DragPreviewSeconds = 8;

    private const double PreferredWidth = 800;

    private bool _glassApplied;

    public SettingsWindow(Action<TipKind, bool?>? showTest = null, Action<double>? previewSize = null,
                          Action<double>? previewAppearance = null)
    {
        _showTest = showTest;
        _previewSize = previewSize;
        _previewAppearance = previewAppearance;
        InitializeComponent();
        ApplyPlatformChrome();
        ApplyVersionText();
        ApplyTheme();
        LoadValues();
        WireEvents();
        TrySetIcon();

        Loc.Changed += RefreshValueLabels;
        ThemeHelper.Changed += OnSystemThemeChanged;
        Closed += (_, _) =>
        {
            Loc.Changed -= RefreshValueLabels;
            ThemeHelper.Changed -= OnSystemThemeChanged;
        };
    }

    private void OnSystemThemeChanged()
    {
        ApplyTheme();
        ApplyChrome();
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

    private void ApplyVersionText() => VersionRun.Text = $"WinTips {AppInfo.DisplayVersion}";

    private void RefreshValueLabels()
    {
        UpdateDurationText(DurationSlider.Value);
        UpdateAnimSpeedText(AnimSpeedSlider.Value);
        UpdateTipScaleText(TipScaleSlider.Value);
        if (StartupHint.Visibility == Visibility.Visible) ShowStartupHint(_lastStartupResult);
        RebuildFontChoices();
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
        TipScaleText.Foreground = secondary;
        TipScaleDesc.Foreground = secondary;
        GlassDesc.Foreground = secondary;
        GlassOpacityLabel.Foreground = secondary;
        GlassOpacityText.Foreground = secondary;
        CustomPosDesc.Foreground = secondary;
        AnyCopyDesc.Foreground = secondary;
        LanguageDesc.Foreground = secondary;
        FocusMonitorDesc.Foreground = secondary;
        FontDesc.Foreground = secondary;
        StartupHint.Foreground = secondary;
        FooterText.Foreground = secondary;
        VersionLink.Foreground = secondary;
        LblKeys.Foreground = secondary;
        LblBehavior.Foreground = secondary;

        var divider = new SolidColorBrush(ThemeHelper.Divider(dark));
        foreach (var sep in new[] { SepK1, SepK2, SepK3, SepK4, SepK5, SepK6, SepK7, SepK8, SepK8b, SepK9, SepB1, SepB2, SepB2b, SepB3, SepB4, SepB5, SepB6 })
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
        LanguageToggle.IsChecked = s.ShowLanguageTip;
        DurationSlider.Value = Math.Clamp(s.DisplaySeconds, 0.5, 5);
        UpdateDurationText(DurationSlider.Value);
        AnimSpeedSlider.Value = Math.Clamp(s.AnimationSpeed, 0, 3);
        UpdateAnimSpeedText(s.AnimationSpeed);
        TipScaleSlider.Value = Math.Round(AppSettings.NormalizeScale(s.TipScale), 1);
        UpdateTipScaleText(TipScaleSlider.Value);
        GlassToggle.IsChecked = s.TipGlass;
        GlassOpacitySlider.Value = Math.Round(AppSettings.NormalizeGlassOpacity(s.TipGlassOpacity) / 0.05) * 0.05;
        UpdateGlassOpacityText(GlassOpacitySlider.Value);
        RefreshGlassUi();
        LoadPlacement();
        FocusMonitorToggle.IsChecked = s.TipFollowFocus;
        StartupToggle.IsChecked = StartupManager.IsEnabled();
        LangCombo.ItemsSource = Loc.Options;
        SyncLanguageUi();
        RefreshPlacementUi();
        RebuildFontChoices();
    }

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
            _showTest?.Invoke(TipKind.CapsLock, true);
        };
        picker.ShowDialog();
        RefreshPlacementUi();
    }

    private bool _syncingPlacement;

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

    private void ApplyFollowFocus(bool enable)
    {
        Save(v => v.TipFollowFocus = enable);
        _showTest?.Invoke(TipKind.CapsLock, true);
    }

    private static string AnimSpeedLabel(double speed)
    {
        if (speed <= 0) return Loc.T("AnimNone");
        string x = speed == Math.Floor(speed) ? $"{speed:0}x" : $"{speed:0.#}x";
        return Loc.Format("ValueAnimSpeed", x, AppSettings.AnimationMs(speed));
    }

    private void UpdateAnimSpeedText(double speed) => AnimSpeedText.Text = AnimSpeedLabel(speed);

    private void UpdateTipScaleText(double scale)
    {
        TipScaleText.Text = $"{(int)Math.Round(scale * 100)}%";
        ResetScaleBtn.IsEnabled = Math.Abs(scale - 1) > 0.001;
    }

    private void UpdateGlassOpacityText(double opacity) =>
        GlassOpacityText.Text = $"{(int)Math.Round(AppSettings.NormalizeGlassOpacity(opacity) * 100)}%";

    private void RefreshGlassUi()
    {
        bool on = GlassToggle.IsChecked == true;
        GlassOpacitySlider.IsEnabled = on;
        GlassOpacityLabel.Opacity = on ? 1 : 0.45;
        GlassOpacityText.Opacity = on ? 1 : 0.45;
    }

    private void ApplyGlass(bool enable)
    {
        Save(v => v.TipGlass = enable);
        RefreshGlassUi();
        _previewAppearance?.Invoke(AppSettings.Instance.DisplaySeconds);
    }

    private void GlassOpacitySlider_DragStarted(object sender, DragStartedEventArgs e) => _draggingGlass = true;

    private void GlassOpacitySlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _draggingGlass = false;
        _previewAppearance?.Invoke(AppSettings.Instance.DisplaySeconds);
    }

    private void ApplyGlassOpacity(double value)
    {
        double opacity = AppSettings.NormalizeGlassOpacity(value);
        UpdateGlassOpacityText(opacity);
        SaveSoon(v => v.TipGlassOpacity = opacity);
        _previewAppearance?.Invoke(_draggingGlass ? DragPreviewSeconds : AppSettings.Instance.DisplaySeconds);
    }

    private void TipScaleSlider_DragStarted(object sender, DragStartedEventArgs e) => _draggingScale = true;
    private void TipScaleSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _draggingScale = false;
        _previewSize?.Invoke(AppSettings.Instance.DisplaySeconds);
    }

    private void ApplyTipScale(double value)
    {
        double scale = AppSettings.NormalizeScale(value);
        UpdateTipScaleText(scale);
        SaveSoon(v => v.TipScale = scale);
        _previewSize?.Invoke(_draggingScale ? DragPreviewSeconds : AppSettings.Instance.DisplaySeconds);
    }

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
        LanguageToggle.Checked += (_, _) => Save(v => v.ShowLanguageTip = true);
        LanguageToggle.Unchecked += (_, _) => Save(v => v.ShowLanguageTip = false);

        DurationSlider.ValueChanged += (_, e) =>
        {
            UpdateDurationText(e.NewValue);
            SaveSoon(v => v.DisplaySeconds = e.NewValue);
        };

        AnimSpeedSlider.ValueChanged += (_, e) =>
        {
            double speed = Math.Round(e.NewValue, 1);
            UpdateAnimSpeedText(speed);
            SaveSoon(v => v.AnimationSpeed = speed);
        };

        TipScaleSlider.ValueChanged += (_, e) => ApplyTipScale(e.NewValue);

        GlassToggle.Checked += (_, _) => ApplyGlass(true);
        GlassToggle.Unchecked += (_, _) => ApplyGlass(false);
        GlassOpacitySlider.ValueChanged += (_, e) => ApplyGlassOpacity(e.NewValue);

        ResetScaleBtn.Click += (_, _) =>
        {
            TipScaleSlider.Value = 1.0;
            if (Math.Abs(AppSettings.NormalizeScale(AppSettings.Instance.TipScale) - 1) > 0.001) ApplyTipScale(1.0);
        };

        StartupToggle.Checked += (_, _) => ApplyStartup(true);
        StartupToggle.Unchecked += (_, _) => ApplyStartup(false);

        LangSystemToggle.Checked += (_, _) =>
        {
            if (_syncingLanguage) return;
            SetLanguage(AppLanguage.System);
        };
        LangSystemToggle.Unchecked += (_, _) =>
        {
            if (_syncingLanguage) return;
            if (LangCombo.SelectedItem is LanguageOption option) SetLanguage(option.Language);
        };
        LangCombo.DropDownOpened += (_, _) => LeaveSystemLanguage();
        LangCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingLanguage) return;
            if (LangCombo.SelectedItem is LanguageOption option) SetLanguage(option.Language);
        };

        FontCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingFont) return;
            if (FontCombo.SelectedItem is FontChoice choice) ApplyUiFont(choice);
        };

        PosTop.Checked += (_, _) => OnPresetChecked(null, AppSettings.PlacementTop);
        PosBottom.Checked += (_, _) => OnPresetChecked(null, AppSettings.PlacementBottom);
        PosLeft.Checked += (_, _) => OnPresetChecked(AppSettings.PlacementLeft, null);
        PosCenter.Checked += (_, _) => OnPresetChecked(AppSettings.PlacementCenter, null);
        PosRight.Checked += (_, _) => OnPresetChecked(AppSettings.PlacementRight, null);

        FocusMonitorToggle.Checked += (_, _) => ApplyFollowFocus(true);
        FocusMonitorToggle.Unchecked += (_, _) => ApplyFollowFocus(false);

        CustomPosBtn.Click += (_, _) => OpenPlacementPicker();
        ResetPosBtn.Click += (_, _) =>
        {
            Save(v => { v.CustomLeft = null; v.CustomTop = null; });
            LoadPlacement();
            RefreshPlacementUi();
            _showTest?.Invoke(TipKind.CapsLock, true);
        };
    }

    private bool _syncingStartup;

    private StartupChangeResult _lastStartupResult = StartupChangeResult.Ok;

    private void ApplyStartup(bool enable)
    {
        if (_syncingStartup) return;

        var result = StartupManager.SetEnabled(enable);
        bool actual = StartupManager.IsEnabled();

        _syncingStartup = true;
        try { StartupToggle.IsChecked = actual; }
        finally { _syncingStartup = false; }

        Save(v => v.LaunchAtStartup = actual);
        ShowStartupHint(result);
    }

    private void ShowStartupHint(StartupChangeResult result)
    {
        _lastStartupResult = result;

        string? key = result switch
        {
            StartupChangeResult.BlockedByUser => "StartupHintBlockedByUser",
            StartupChangeResult.BlockedByPolicy => "StartupHintBlockedByPolicy",
            StartupChangeResult.Failed => "StartupHintFailed",
            _ => null,
        };

        if (key is null)
        {
            StartupHint.Visibility = Visibility.Collapsed;
            return;
        }

        StartupHint.Text = Loc.T(key);
        StartupHint.Visibility = Visibility.Visible;
    }

    private bool _syncingLanguage;

    private void SyncLanguageUi()
    {
        _syncingLanguage = true;
        try
        {
            LangSystemToggle.IsChecked = Loc.Language == AppLanguage.System;
            LangCombo.SelectedItem = Loc.Options.FirstOrDefault(o => o.Language == Loc.Effective)
                                     ?? Loc.Options.FirstOrDefault();
        }
        finally { _syncingLanguage = false; }
    }

    private void LeaveSystemLanguage()
    {
        if (Loc.Language != AppLanguage.System) return;
        if (LangCombo.SelectedItem is not LanguageOption option) return;

        Save(v => v.Language = Loc.ToSetting(option.Language));
        Loc.Apply(option.Language);

        _syncingLanguage = true;
        try { LangSystemToggle.IsChecked = false; }
        finally { _syncingLanguage = false; }
    }

    private void SetLanguage(AppLanguage language)
    {
        Save(v => v.Language = Loc.ToSetting(language));
        Loc.Apply(language);
        SyncLanguageUi();
    }

    private bool _syncingFont;

    private void RebuildFontChoices()
    {
        var choices = FontCatalog.BuildChoices();
        string current = AppFont.CurrentSetting;

        _syncingFont = true;
        try
        {
            FontCombo.ItemsSource = choices;
            FontCombo.SelectedItem =
                choices.FirstOrDefault(c => c.IsDefault
                    ? current.Length == 0
                    : string.Equals(c.Name, current, StringComparison.OrdinalIgnoreCase))
                ?? choices[0];
        }
        finally { _syncingFont = false; }
    }

    private void ApplyUiFont(FontChoice choice)
    {
        string setting = choice.IsDefault ? "" : choice.Name;
        AppFont.Apply(setting);
        Save(v => v.UiFont = setting);
        _showTest?.Invoke(TipKind.CapsLock, true);
    }

    private void Save(Action<AppSettings> apply)
    {
        apply(AppSettings.Instance);
        AppSettings.Instance.Save();
    }

    private void SaveSoon(Action<AppSettings> apply)
    {
        apply(AppSettings.Instance);
        AppSettings.Instance.SaveSoon();
    }

    private void UpdateDurationText(double v) =>
        DurationText.Text = Loc.Format("ValueSeconds", v == Math.Floor(v) ? $"{v:0}" : $"{v:0.#}");

    private void TrySetIcon() => Icon = AppIcons.Source;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyChrome();
        FitToScreen();
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(ChromeWndProc);
    }

    private void FitToScreen() => WindowFit.ClampToScreen(this, PreferredWidth);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        FitToScreen();
    }

    private IntPtr ChromeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_SETTINGCHANGE && ThemeHelper.MicaAllowed != _glassApplied)
        {
            ApplyTheme();
            ApplyChrome();
        }
        return IntPtr.Zero;
    }

    private void ApplyChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var src = HwndSource.FromHwnd(hwnd);
        if (src?.CompositionTarget == null) return;

        bool dark = ThemeHelper.IsDarkTheme;
        var surface = new SolidColorBrush(ThemeHelper.WindowSurface(dark));

        DwmChrome.TrySetDarkTitleBar(hwnd, dark);

        bool mica = ThemeHelper.MicaAllowed && DwmChrome.TrySetBackdrop(hwnd, NativeMethods.DWMSBT_MAINWINDOW);
        _glassApplied = mica;
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
        src.CompositionTarget.BackgroundColor = surface.Color;
        Background = surface;
        Panel.Background = surface;
    }

    private void VersionLink_Click(object sender, RoutedEventArgs e) =>
        new LicenseWindow { Owner = this }.ShowDialog();

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
