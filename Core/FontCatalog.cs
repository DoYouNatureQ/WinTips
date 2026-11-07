using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Markup;
using System.Windows.Media;

namespace WinTips.Core;

/// <summary>
/// 字体下拉框里的一项：内置默认字体（<see cref="IsDefault"/>），或系统里已装的某个字族。
///
/// 这里必须是**引用相等**的 class，不能用 record：切换界面语言时会重建整个列表，
/// 而 WPF 的 Selector 在重新设 ItemsSource 时是按 Equals 去找「原选中项」的。record 是
/// 值相等，新旧两个「默认字体」项必然相等，于是选中项被还原成**旧的那个实例**，
/// ComboBox.SelectionBoxItem 的引用一直不变，选中的 ContentPresenter 也就不会重新
/// 计算 ToString —— 下拉框收起时显示的文字会冻结在第一次渲染时的语言（切英文后仍显示
/// 「默认字体」）。改成 class 后每次重建都是新实例，选中项会真正变化，文字才会跟着语言走。
/// </summary>
public sealed class FontChoice
{
    public FontChoice(string name, bool isDefault)
    {
        Name = name;
        IsDefault = isDefault;
    }

    public string Name { get; }

    public bool IsDefault { get; }

    public string Display => IsDefault ? Loc.T("FontChoiceDefault") : Name;

    public override string ToString() => Display;
}

public static class FontCatalog
{
    private static readonly Lazy<(string Name, FontFamily Family)[]> Installed = new(Enumerate);

    public static List<FontChoice> BuildChoices()
    {
        var list = new List<FontChoice>(Installed.Value.Length + 1) { new("", true) };
        foreach (var entry in Installed.Value) { list.Add(new FontChoice(entry.Name, false)); }
        return list;
    }

    public static FontFamily? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        foreach (var entry in Installed.Value)
        {
            if (string.Equals(entry.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) { return entry.Family; }
        }
        return null;
    }

    private static (string Name, FontFamily Family)[] Enumerate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string, FontFamily)>();

        foreach (var family in Fonts.SystemFontFamilies)
        {
            string name = StableName(family);

            if (string.IsNullOrWhiteSpace(name) || name[0] == '@') { continue; }
            if (name.Equals(AppFont.EmbeddedFamily, StringComparison.OrdinalIgnoreCase)) { continue; }
            if (!seen.Add(name)) { continue; }

            list.Add((name, family));
        }

        list.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.CurrentCulture));
        return list.ToArray();
    }

    private static string StableName(FontFamily family)
    {
        var names = family.FamilyNames;
        if (names.TryGetValue(XmlLanguage.GetLanguage("en-US"), out var en) && !string.IsNullOrWhiteSpace(en)) { return en; }

        foreach (var value in names.Values)
        {
            if (!string.IsNullOrWhiteSpace(value)) { return value; }
        }
        return family.Source;
    }
}
