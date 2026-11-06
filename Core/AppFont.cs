using System;
using System.Windows;
using System.Windows.Media;

namespace WinTips.Core;

/// <summary>
/// 全程序字体族唯一来源：XAML 走动态资源 <c>{DynamicResource AppUiFont}</c>（键值见
/// <see cref="ResourceKey"/>，由本类写入），代码里直接用 <see cref="Ui"/>，换字体只改这一个类。
///
/// 默认字体跟着包体走（<c>Resources\Fonts\*.ttf</c> 以 WPF Resource 编进程序集），运行时用
/// pack:// 目录 URI 解析，不依赖系统字体表：用户装没装 HarmonyOS Sans 都长一个样，
/// 单文件发布也不会多出外部文件。用户也可以在设置里改成系统里已装的任意字族
/// （见 <see cref="Apply"/> 与 <see cref="FontCatalog"/>）。
///
/// 为什么默认是 SC：Windows 上单独分发的 "HarmonyOS Sans" 是**拉丁版**（只含字母数字符号，
/// 874 个字形，没有汉字），直接用它会让中文落到下一档字体，于是同一行里中英文分属两个
/// 文件——字重、基线和行高都对不齐。"HarmonyOS Sans SC" 一个文件就含拉丁 + 汉字
/// （29063 个字形），让整段文本都出自同一字族，中英文风格和度量天然一致。
///
/// 为什么三个字重都带上：界面用到 Normal / Medium / SemiBold（见 Styles.xaml 与
/// OsdWindow 的测量逻辑），WPF 只能在同族的多份字体文件里挑字重，缺一个就会退化成
/// 合成加粗，粗细和字宽都会和设计稿不一样。SemiBold 在本族里没有独立文件，按 WPF 的
/// 匹配规则落到 Bold —— 与装好系统字体时的观感完全一致。
///
/// 许可：HarmonyOS Sans 允许原样嵌入、随软件分发，但禁止修改字体文件（所以不能做子集
/// 裁剪），并要求在软件中注明使用、随副本保留协议。协议全文见
/// <c>Resources\Fonts\LICENSE_Fonts.txt</c>（同时打进程序集），软件内的出处标注在
/// 设置页版本号点开的许可弹窗里（见 <c>UI\LicenseWindow</c>）。
/// </summary>
public static class AppFont
{
    /// <summary>动态资源键：样式与各窗口的 FontFamily 都指向它，换字体时改这里一处即可全量刷新。</summary>
    public const string ResourceKey = "AppUiFont";

    /// <summary>内置字体族的名字，必须和 ttf 里的 family name（name 表 ID 1/16）逐字一致。</summary>
    public const string EmbeddedFamily = "HarmonyOS Sans SC";

    /// <summary>内嵌字体在程序集里的 pack 目录（相对 pack://application:,,,/，末尾保留斜杠）。</summary>
    private const string EmbeddedDir = "./Resources/Fonts/";

    /// <summary>既没有内嵌资源、用户也没选字体时的兜底：系统中文 → 系统西文。</summary>
    private const string SystemFamily = "Microsoft YaHei UI, Segoe UI";

    private static FontFamily? _default;
    private static FontFamily _ui = Default;

    /// <summary>界面正文字体族（当前生效的那一个）。</summary>
    public static FontFamily Ui => _ui;

    /// <summary>当前设置值：空串 = 内置字体，否则是系统已装字族的英文族名。</summary>
    public static string CurrentSetting { get; private set; } = "";

    /// <summary>
    /// 图标字形（Fluent 图标字体）：只用于私用区码位，绝不能换成正文族，
    /// 否则图标会变成方框。OsdWindow 的剪刀、托盘菜单的勾/箭头都靠它。
    /// 也不随用户选择变化——这些码位只有 Fluent 图标字体才有。
    /// </summary>
    public static readonly FontFamily Icon = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");

    /// <summary>字体族换过之后触发：OSD 的尺寸测量缓存必须跟着作废（见 OsdWindow）。</summary>
    public static event Action? Changed;

    /// <summary>内置字体：程序集里那份 HarmonyOS Sans SC，取不到时退系统字体。</summary>
    private static FontFamily Default => _default ??= CreateDefault();

    /// <summary>
    /// 切换界面字体。<paramref name="setting"/> 为空 = 用内置的 HarmonyOS Sans SC；
    /// 否则按系统已装字族的名字解析，解析不到（字体被卸载、名字变了）自动回落到内置字体，
    /// 并把设置值归一成空串——宁可回到默认，也不要挂着一个渲染不出来的字族名。
    /// </summary>
    public static void Apply(string? setting)
    {
        string wanted = setting?.Trim() ?? "";
        FontFamily? installed = wanted.Length == 0 ? null : FontCatalog.Find(wanted);
        FontFamily family = installed ?? Default;
        string effective = installed is null ? "" : wanted;

        bool changed = !ReferenceEquals(family, _ui) || effective != CurrentSetting;
        _ui = family;
        CurrentSetting = effective;

        // 只改资源字典：样式与各窗口上的 DynamicResource 会在下一次渲染时自动取新值
        if (Application.Current is { } app) { app.Resources[ResourceKey] = family; }

        if (changed) { Changed?.Invoke(); }
    }

    /// <summary>
    /// 以 pack 目录 + 族名解析内嵌字体：WPF 会把该目录下所有 ttf 里同族的字重都收进来
    /// （Regular / Medium / Bold 三份），给 <c>FontWeight</c> 留出真实的选择空间。
    /// </summary>
    private static FontFamily CreateDefault()
    {
        try
        {
            // 注意：族名解析失败时 WPF **不抛异常**，而是静默返回全局兜底族（全是系统字体），
            // 所以必须回查字形的来源 URI，确认真的拿到了程序集里的那份。
            var embedded = new FontFamily(new Uri("pack://application:,,,/"), EmbeddedDir + "#" + EmbeddedFamily);
            if (IsEmbedded(embedded)) { return embedded; }
        }
        catch (Exception)
        {
            // pack 资源缺失（被裁剪、改名或不在 WPF 宿主里）时退回系统字体，宁可观感有差异也不能崩。
        }
        return new FontFamily(SystemFamily);
    }

    private static bool IsEmbedded(FontFamily family)
    {
        foreach (var typeface in family.GetTypefaces())
        {
            if (!typeface.TryGetGlyphTypeface(out var glyph)) { continue; }
            // FontUri 为绝对 pack URI 才算命中内嵌资源（系统字体是 file:///C:/Windows/Fonts/...）。
            if (glyph.FontUri.IsAbsoluteUri && glyph.FontUri.Scheme == System.IO.Packaging.PackUriHelper.UriSchemePack) { return true; }
        }
        return false;
    }
}
