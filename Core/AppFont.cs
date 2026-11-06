using System.Windows.Media;

namespace WinTips.Core;

/// <summary>
/// 全程序字体族唯一来源：XAML 走 {@code {x:Static core:AppFont.Ui}}，代码里直接用 <see cref="Ui"/>，
/// 换字体只改这里的字符串。
///
/// 为什么 SC 排在第一位：Windows 上单独分发的 "HarmonyOS Sans" 是**拉丁版**
/// （只含字母数字符号，874 个字形，没有汉字），直接用它会让中文落到下一档字体，
/// 于是同一行里中英文分属两个文件——字重、基线和行高都对不齐。
/// "HarmonyOS Sans SC" 一个文件就含拉丁 + 汉字（29063 个字形），让整段文本
/// 都出自同一字族，中英文风格和度量天然一致。后面的候选只是没装 SC 时的兜底。
/// </summary>
public static class AppFont
{
    /// <summary>界面正文字体（SC → 拉丁版 → 系统中文 → 系统西文）。</summary>
    public const string UiFamily =
        "HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Segoe UI";

    /// <summary>界面正文字体族，解析结果缓存一次。</summary>
    public static readonly FontFamily Ui = new(UiFamily);

    /// <summary>
    /// 图标字形（Fluent 图标字体）：只用于私用区码位，绝不能换成正文族，
    /// 否则图标会变成方框。OsdWindow 的剪刀、托盘菜单的勾/箭头都靠它。
    /// </summary>
    public static readonly FontFamily Icon = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
}
