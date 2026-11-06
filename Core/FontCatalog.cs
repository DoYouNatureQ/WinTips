using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Markup;
using System.Windows.Media;

namespace WinTips.Core;

/// <summary>字体下拉框里的一项：内置默认字体（<see cref="IsDefault"/>），或系统里已装的某个字族。</summary>
public sealed record FontChoice(string Name, bool IsDefault)
{
    /// <summary>「默认字体」那项的文案随界面语言变化，所以显示名在取用时才算，列表每次重建。</summary>
    public string Display => IsDefault ? Loc.T("FontChoiceDefault") : Name;

    /// <summary>下拉框直接用这个字符串，模板里不必再配 DisplayMemberPath。</summary>
    public override string ToString() => Display;
}

/// <summary>
/// 系统已装字族目录。字族枚举（<see cref="Fonts.SystemFontFamilies"/>）要几百毫秒，
/// 所以只在第一次用到时算一遍并缓存：打开设置窗口或恢复用户上次选的字体时各用一次。
/// </summary>
public static class FontCatalog
{
    private static readonly Lazy<(string Name, FontFamily Family)[]> Installed = new(Enumerate);

    /// <summary>下拉框数据源：第一项永远是内置字体，其后是系统字体（按当前语言的排序规则）。</summary>
    public static List<FontChoice> BuildChoices()
    {
        var list = new List<FontChoice>(Installed.Value.Length + 1) { new("", true) };
        foreach (var entry in Installed.Value) { list.Add(new FontChoice(entry.Name, false)); }
        return list;
    }

    /// <summary>按名字取已装字族；没装（或名字对不上）返回 null，由调用方回落内置字体。</summary>
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

            // 内置字体自己不入列：它已经由「内置」那一项代表，两处都出现只会让人分不清
            // 哪个才是随包分发的那份。GDI 的竖排变体（"@微软雅黑"）也不需要。
            if (string.IsNullOrWhiteSpace(name) || name[0] == '@') { continue; }
            if (name.Equals(AppFont.EmbeddedFamily, StringComparison.OrdinalIgnoreCase)) { continue; }
            if (!seen.Add(name)) { continue; }

            list.Add((name, family));
        }

        list.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.CurrentCulture));
        return list.ToArray();
    }

    /// <summary>
    /// 取一个跨语言稳定的族名：优先 en-US（Windows 上族名的英文写法唯一且不随界面语言变），
    /// 只有单一本地化名字的字体（如部分日文/中文字体）才退回第一个可用值。
    /// 存进设置文件的就是这个字符串。
    /// </summary>
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
