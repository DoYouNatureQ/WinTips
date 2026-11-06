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

    /// <summary>个性化（深浅色、透明效果）注册表位置。</summary>
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// 系统“透明效果”开关（设置 → 个性化 → 颜色 / 辅助功能 → 视觉效果）。
    ///
    /// 关掉它并不等于“材质消失”：DWM 会把 Mica·亚克力整体替换成**不透明纯色兜底**
    /// （见 Materials overview：Transparency effects disabled → all SystemBackdrop
    /// materials fall back to their solid-color alternatives）。而本程序的卡片是按
    /// “壁纸着色的 Mica 底”调出来的半透明叠加（白 0xA8 / 白 0x24 / 黑 0x1F…），
    /// 一旦底从“壁纸取样色”变成平铺的 #F3F3F3 / #202020，叠出来的色相与层次就跟
    /// 开着透明效果时明显对不上——这就是“颜色很奇怪”的根因。
    /// 读不到时按“开启”处理，保持既有行为。
    /// </summary>
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

    /// <summary>
    /// 是否处于节电模式（设置 → 系统 → 电源和电池 → 节电模式，低电量时也会自动进入）。
    ///
    /// 它和“透明效果”开关的杀伤范围不一样：节电模式**只关亚克力**，Mica 不受影响
    /// （Materials overview：Battery Saver active → Windows disables acrylic，
    /// Mica is not affected）。所以浮窗与托盘菜单会退回纯色，而设置/提示窗口的 Mica 照常。
    /// </summary>
    public static bool BatterySaver
    {
        get
        {
            try { return NativeMethods.GetSystemPowerStatus(out var s) && s.SystemStatusFlag == 1; }
            catch { return false; }
        }
    }

    /// <summary>
    /// 是否允许使用系统材质（Mica / 亚克力）：Win11 + 合成正常 + 非高对比度 + 系统开着透明效果。
    ///
    /// 透明效果关闭时必须和 Win10、高对比度走同一条“不透明纯色”路径：只有在玻璃真的
    /// 存在时才让客户区透明，否则半透明卡片会露出 DWM 的纯色兜底，颜色整体偏掉。
    /// </summary>
    public static bool MicaAllowed => !HighContrast && TransparencyEnabled && DwmChrome.CanUseGlass;

    /// <summary>
    /// 亚克力是否可用：在 Mica 的基础上再排除节电模式。浮窗与托盘菜单（DWMSBT_TRANSIENTWINDOW）
    /// 走这条判定——节电模式会把它们换成纯色兜底，却不会通知任何窗口，只能每次铺材质时现查。
    /// </summary>
    public static bool AcrylicAllowed => MicaAllowed && !BatterySaver;

    public static bool IsDarkTheme
    {
        get
        {
            if (HighContrast) return false; // HC 主题自带配色，深浅色无意义
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
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

    /// <summary>
    /// 玻璃片的底色：叠在亚克力之上的一层极淡 tint。
    ///
    /// 亚克力在浅色主题下亮度层吃掉约 85%，真正透上来的只剩色温；窗口若再完全透明，
    /// 就没有任何"介质"可言——模糊无处承载，观感和一块白纸没有区别。这里透明度刻意
    /// 压得很低，只为让那点透光有个可见的落点，不至于把玻璃又压回实心。
    ///
    /// 深色下用一层极淡的<b>白</b>而不是黑：深色亚克力本身就是 #2C2C2C 高不透明度，
    /// 再往暗里压只会更像实心板；加一点白雾才有"雾面玻璃"的观感，也才不会把
    /// 材质本来就有限的透光彻底吃掉。
    /// </summary>
    public static Color GlassTint(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)
                   : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// 玻璃片描边：顶部受光、向下渐隐的 1px 线。
    ///
    /// 人眼判定"这是一片玻璃"主要靠边界而不是模糊强度——四边等亮会像贴纸，
    /// 只有顶部那道略亮的高光才有被光打到的立体感，也才像 Win11 的原生浮层。
    /// </summary>
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

    /// <summary>
    /// 自绘模糊用的底色基（不透明 RGB）：对齐 Win11 任务栏的取值——浅色 #F3F3F3、深色 #202020。
    /// 这里只定义"混什么颜色"，实际不透明度由调用方按窗口大小与观感决定。
    /// </summary>
    public static Color GlassTintBase(bool dark) =>
        HighContrast ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

    /// <summary>取某个颜色的低透明度版本（高亮带用）。</summary>
    public static Color Tint(Color c, byte alpha) => WithAlpha(alpha, c);

    private static Color WithAlpha(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>
    /// 把控件模板里用到的画刷塞进窗口级资源，模板一律走 DynamicResource，
    /// 动态资源搜索会命中窗口级覆盖值。高对比度下这些值全部来自 SystemColors。
    ///
    /// 同一份配色也会写进应用级资源：ComboBox 的下拉层、右键菜单等弹出内容是
    /// 独立的窗口树，往上搜资源搜不到窗口，只能命中应用级；只写窗口级的话，
    /// 弹出层会拿到样式表里的浅色兜底值（深色模式下就是浅底黑字）。
    /// </summary>
    public static void ApplyTemplateResources(Window w, bool dark)
    {
        Color text = TextPrimary(dark);
        bool hc = HighContrast;

        var map = new Dictionary<string, Color>();

        map["AccentBrush"] = AccentColor;
        map["OnAccentBrush"] = OnAccent(dark);

        // 卡片 / 描边
        map["CardBrush"] = hc ? SystemColors.WindowColor
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF);
        map["CardStrokeBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

        // 悬停：HC 用系统的“高亮底 + 高亮前景”，与原生菜单一致
        map["HoverBrush"] = hc ? SystemColors.HighlightColor
            : dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
        map["HoverTextBrush"] = hc ? SystemColors.HighlightTextColor : text;
        map["SegHoverBrush"] = hc ? SystemColors.HighlightColor
            : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00);

        // 文字与分隔线
        map["TextPrimaryBrush"] = text;
        map["SecondaryTextBrush"] = TextSecondary(dark);
        map["DividerBrush"] = Divider(dark);
        map["WindowSurfaceBrush"] = WindowSurface(dark);

        // 滑杆
        map["SliderRestBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3D, 0x00, 0x00, 0x00);
        map["ThumbRingBrush"] = hc ? SystemColors.WindowColor
            : dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White;
        map["ThumbStrokeBrush"] = hc ? SystemColors.WindowTextColor
            : Color.FromArgb(0x14, 0x00, 0x00, 0x00);

        // 开关：关闭态的描边与滑块在深色下原本接近纯黑（几乎看不见），改为跟随明暗
        map["SwitchBorderBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x6E, 0x00, 0x00, 0x00);
        map["SwitchThumbBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A);
        map["SwitchThumbOnBrush"] = hc ? SystemColors.HighlightTextColor : Colors.White;
        map["SwitchBorderHoverBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00);
        map["SwitchThumbHoverBrush"] = hc ? SystemColors.WindowTextColor
            : dark ? Colors.White : Color.FromRgb(0x33, 0x33, 0x33);

        // 自绘标题栏的关闭键
        map["CaptionCloseHoverBrush"] = hc ? SystemColors.HighlightColor : Color.FromRgb(0xC4, 0x2B, 0x1C);
        map["CaptionClosePressedBrush"] = hc ? SystemColors.HighlightColor : Color.FromRgb(0xB0, 0x27, 0x1A);
        map["CaptionCloseGlyphBrush"] = hc ? SystemColors.HighlightTextColor : Colors.White;

        // 输入类控件（下拉框）：卡片上再叠一层的浅色底，深色下用白色低透明度而不是黑色
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
