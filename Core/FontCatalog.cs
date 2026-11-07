using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Markup;
using System.Windows.Media;

namespace WinTips.Core;

public sealed record FontChoice(string Name, bool IsDefault)
{
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
