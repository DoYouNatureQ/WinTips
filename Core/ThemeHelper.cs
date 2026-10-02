using Microsoft.Win32;
using System.Windows.Media;

namespace WinTips.Core;

/// <summary>读取系统深浅色与强调色，提供 Fluent 配色。</summary>
internal static class ThemeHelper
{
    public static bool IsDarkTheme
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int light ? light == 0 : false;
            }
            catch { return false; }
        }
    }

    /// <summary>系统强调色（COLORREF 0x00BBGGRR → RGB）。</summary>
    public static Color AccentColor
    {
        get
        {
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

    public static Color TextPrimary(bool dark) =>
        dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x19, 0x19, 0x19);

    public static Color TextSecondary(bool dark) =>
        dark ? Color.FromArgb(0xC7, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC7, 0x00, 0x00, 0x00);

    public static Color Divider(bool dark) =>
        dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
}
