using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinTips.Native;

namespace WinTips.Core;

internal static class ThemeHelper
{
    public static event Action? Changed;

    public static void RaiseChanged() => Changed?.Invoke();

    public static bool HighContrast => SystemParameters.HighContrast;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool TransparencyEnabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return k?.GetValue("EnableTransparency") is not int v || v != 0;
            }
            catch { return true; }
        }
    }

    public static bool BatterySaver
    {
        get
        {
            try { return NativeMethods.GetSystemPowerStatus(out var s) && s.SystemStatusFlag == 1; }
            catch { return false; }
        }
    }

    public static bool MicaAllowed => !HighContrast && TransparencyEnabled && DwmChrome.CanUseGlass;

    public static bool AcrylicAllowed => MicaAllowed && !BatterySaver;

    public static bool IsDarkTheme
    {
        get
        {
            if (HighContrast) return false;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return k?.GetValue("AppsUseLightTheme") is int light ? light == 0 : false;
            }
            catch { return false; }
        }
    }

    public static Color AccentColor
    {
        get
        {
            if (HighContrast) return SystemColors.HighlightColor;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (k?.GetValue("AccentColor") is int c)
                    return Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
            }
            catch { }
            return Color.FromRgb(0x00, 0x67, 0xC0);
        }
    }

    public static Color OnAccent(bool dark) =>
        HighContrast ? SystemColors.HighlightTextColor : Colors.White;

    public static Color TextPrimary(bool dark) =>
        HighContrast ? SystemColors.WindowTextColor
            : dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x19, 0x19, 0x19);

    public static Color TextSecondary(bool dark) =>
        HighContrast ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0xC7, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC7, 0x00, 0x00, 0x00);

    public static Color Divider(bool dark) =>
        HighContrast ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);

    public static Color WindowSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

    public static Color MenuSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF9, 0xF9, 0xF9);

    public static Color OsdSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xFB, 0xFB, 0xFB);

    public static Color PickerSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0xD9, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xD9, 0xFB, 0xFB, 0xFB);

    public static Color PickerStroke(bool dark) =>
        HighContrast ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

    public static Color PickerScrim() =>
        HighContrast ? WithAlpha(0xD9, SystemColors.WindowColor) : Color.FromArgb(0x40, 0x00, 0x00, 0x00);

    public static Color GlassTint(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)
                   : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);

    public static Brush GlassStroke(bool dark)
    {
        if (HighContrast) return new SolidColorBrush(SystemColors.WindowTextColor);

        var brush = new LinearGradientBrush(
            dark ? Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x4A, 0xFF, 0xFF, 0xFF),
            dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            new Point(0.5, 0), new Point(0.5, 1));
        brush.Freeze();
        return brush;
    }

    public static Color GlassTintBase(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

    public static Color Tint(Color c, byte alpha) => WithAlpha(alpha, c);

    private static Color WithAlpha(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

    public static void ApplyTemplateResources(Window w, bool dark)
    {
        Color text = TextPrimary(dark);
        bool hc = HighContrast;

        var map = new Dictionary<string, Color>();

        map["AccentBrush"] = AccentColor;
        map["OnAccentBrush"] = OnAccent(dark);

        map["CardBrush"] = hc ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF);
        map["CardStrokeBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

        map["HoverBrush"] = hc ? SystemColors.HighlightColor
            : dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
        map["HoverTextBrush"] = hc ? SystemColors.HighlightTextColor : text;
        map["SegHoverBrush"] = hc ? SystemColors.HighlightColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00);

        map["TextPrimaryBrush"] = text;
        map["SecondaryTextBrush"] = TextSecondary(dark);
        map["DividerBrush"] = Divider(dark);
        map["WindowSurfaceBrush"] = WindowSurface(dark);

        map["ScrollThumbBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x59, 0x00, 0x00, 0x00);

        map["SliderRestBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3D, 0x00, 0x00, 0x00);
        map["ThumbRingBrush"] = hc ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White;
        map["ThumbStrokeBrush"] = hc ? SystemColors.WindowTextColor
            : Color.FromArgb(0x14, 0x00, 0x00, 0x00);

        map["SwitchBorderBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x6E, 0x00, 0x00, 0x00);
        map["SwitchThumbBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A);
        map["SwitchThumbOnBrush"] = hc ? SystemColors.HighlightTextColor : Colors.White;
        map["SwitchBorderHoverBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00);
        map["SwitchThumbHoverBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Colors.White : Color.FromRgb(0x33, 0x33, 0x33);

        map["CaptionCloseHoverBrush"] = hc ? SystemColors.HighlightColor : Color.FromRgb(0xC4, 0x2B, 0x1C);
        map["CaptionClosePressedBrush"] = hc ? SystemColors.HighlightColor : Color.FromRgb(0xB0, 0x27, 0x1A);
        map["CaptionCloseGlyphBrush"] = hc ? SystemColors.HighlightTextColor : Colors.White;

        map["InputBrush"] = hc ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
        map["InputHoverBrush"] = hc ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        map["InputStrokeBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
        map["ChevronBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x66, 0x00, 0x00, 0x00);

        var app = Application.Current?.Resources;
        foreach (var (key, color) in map)
        {
            w.Resources[key] = new SolidColorBrush(color);
            if (app != null) app[key] = new SolidColorBrush(color);
        }
    }
}
