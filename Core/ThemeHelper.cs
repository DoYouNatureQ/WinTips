using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 系统外观状态的唯一裁决点：深浅色与高对比度都从这里取值，窗口不再自己写死颜色。
///
/// 高对比度（HC）下所有颜色改由 SystemColors 提供（窗口底 / 文字 / 分隔线 / 强调色 / 强调前景），
/// 并禁用 Mica·亚克力与深色标题栏——半透明材质会让系统为对比度所做的配色失效，
/// 这正是之前“硬编码颜色在高对比度下不跟随”的根因。
/// </summary>
internal static class ThemeHelper
{
    /// <summary>系统主题或对比度变化。由 App 的 SystemEvents 转发，始终在 UI 线程触发。</summary>
    public static event Action? Changed;

    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>系统是否处于高对比度模式。</summary>
    public static bool HighContrast => SystemParameters.HighContrast;

    /// <summary>是否允许使用 Mica/Acrylic：仅 Win11 且非高对比度时成立。</summary>
    public static bool GlassAllowed => !HighContrast && DwmChrome.CanUseGlass;

    public static bool IsDarkTheme
    {
        get
        {
            if (HighContrast) return false; // HC 主题自带配色，深浅色无意义
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int light ? light == 0 : false;
            }
            catch { return false; }
        }
    }

    /// <summary>系统强调色（COLORREF 0x00BBGGRR → RGB）；高对比度下用系统高亮色。</summary>
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

    /// <summary>强调色上的文字色（主按钮、徽章）。</summary>
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

    /// <summary>设置窗口底色（无 Mica 时铺满整窗，杜绝窗口边框色带）。</summary>
    public static Color WindowSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

    /// <summary>托盘菜单卡片底色。</summary>
    public static Color MenuSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF9, 0xF9, 0xF9);

    /// <summary>浮窗（OSD）卡片底色。</summary>
    public static Color OsdSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xFB, 0xFB, 0xFB);

    /// <summary>选择器里的提示卡片：高对比度下必须不透明，否则压不住遮罩。</summary>
    public static Color PickerSurface(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0xD9, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xD9, 0xFB, 0xFB, 0xFB);

    public static Color PickerStroke(bool dark) =>
        HighContrast ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

    /// <summary>选择器的全屏遮罩：高对比度下用系统窗口色压暗，避免半透明混出低对比画面。</summary>
    public static Color PickerScrim() =>
        HighContrast ? WithAlpha(0xD9, SystemColors.WindowColor) : Color.FromArgb(0x40, 0x00, 0x00, 0x00);

    /// <summary>取某个颜色的低透明度版本（高亮带用）。</summary>
    public static Color Tint(Color c, byte alpha) => WithAlpha(alpha, c);

    private static Color WithAlpha(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>
    /// 把控件模板里用到的画刷塞进窗口级资源，模板一律走 DynamicResource，
    /// 动态资源搜索会命中窗口级覆盖值。高对比度下这些值全部来自 SystemColors。
    /// </summary>
    public static void ApplyTemplateResources(Window w, bool dark)
    {
        var r = w.Resources;
        Color text = TextPrimary(dark);
        bool hc = HighContrast;

        SolidColorBrush Solid(Color c) => new(c);

        r["AccentBrush"] = Solid(AccentColor);
        r["OnAccentBrush"] = Solid(OnAccent(dark));

        // 卡片 / 描边
        r["CardBrush"] = Solid(hc ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
        r["CardStrokeBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));

        // 悬停：HC 用系统的“高亮底 + 高亮前景”，与原生菜单一致
        r["HoverBrush"] = Solid(hc ? SystemColors.HighlightColor
            : dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
        r["HoverTextBrush"] = Solid(hc ? SystemColors.HighlightTextColor : text);
        r["SegHoverBrush"] = Solid(hc ? SystemColors.HighlightColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));

        // 文字与分隔线
        r["SecondaryTextBrush"] = Solid(TextSecondary(dark));
        r["DividerBrush"] = Solid(Divider(dark));
        r["WindowSurfaceBrush"] = Solid(WindowSurface(dark));

        // 滑杆
        r["SliderRestBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3D, 0x00, 0x00, 0x00));
        r["ThumbRingBrush"] = Solid(hc ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White);
        r["ThumbStrokeBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : Color.FromArgb(0x14, 0x00, 0x00, 0x00));

        // 开关：关闭态的描边与滑块在深色下原本接近纯黑（几乎看不见），改为跟随明暗
        r["SwitchBorderBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x6E, 0x00, 0x00, 0x00));
        r["SwitchThumbBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A));
        r["SwitchThumbOnBrush"] = Solid(hc ? SystemColors.HighlightTextColor : Colors.White);
        r["SwitchBorderHoverBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        r["SwitchThumbHoverBrush"] = Solid(hc ? SystemColors.WindowTextColor
            : dark ? Colors.White : Color.FromRgb(0x33, 0x33, 0x33));

        // 自绘标题栏的关闭键
        r["CaptionCloseHoverBrush"] = Solid(hc ? SystemColors.HighlightColor : Color.FromRgb(0xC4, 0x2B, 0x1C));
        r["CaptionClosePressedBrush"] = Solid(hc ? SystemColors.HighlightColor : Color.FromRgb(0xB0, 0x27, 0x1A));
        r["CaptionCloseGlyphBrush"] = Solid(hc ? SystemColors.HighlightTextColor : Colors.White);
    }
}
