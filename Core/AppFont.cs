using System;
using System.Windows;
using System.Windows.Media;

namespace WinTips.Core;

public static class AppFont
{
    public const string ResourceKey = "AppUiFont";

    public const string EmbeddedFamily = "HarmonyOS Sans SC";

    private const string EmbeddedDir = "./Resources/Fonts/";

    private const string SystemFamily = "Microsoft YaHei UI, Segoe UI";

    private static FontFamily? _default;
    private static FontFamily _ui = Default;

    public static FontFamily Ui => _ui;

    public static string CurrentSetting { get; private set; } = "";

    public static readonly FontFamily Icon = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");

    public static event Action? Changed;

    private static FontFamily Default => _default ??= CreateDefault();

    public static void Apply(string? setting)
    {
        string wanted = setting?.Trim() ?? "";
        FontFamily? installed = wanted.Length == 0 ? null : FontCatalog.Find(wanted);
        FontFamily family = installed ?? Default;
        string effective = installed is null ? "" : wanted;

        bool changed = !ReferenceEquals(family, _ui) || effective != CurrentSetting;
        _ui = family;
        CurrentSetting = effective;

        if (Application.Current is { } app) { app.Resources[ResourceKey] = family; }

        if (changed) { Changed?.Invoke(); }
    }

    private static FontFamily CreateDefault()
    {
        try
        {
            var embedded = new FontFamily(new Uri("pack://application:,,,/"), EmbeddedDir + "#" + EmbeddedFamily);
            if (IsEmbedded(embedded)) { return embedded; }
        }
        catch (Exception)
        {
        }
        return new FontFamily(SystemFamily);
    }

    private static bool IsEmbedded(FontFamily family)
    {
        foreach (var typeface in family.GetTypefaces())
        {
            if (!typeface.TryGetGlyphTypeface(out var glyph)) { continue; }
            if (glyph.FontUri.IsAbsoluteUri && glyph.FontUri.Scheme == System.IO.Packaging.PackUriHelper.UriSchemePack) { return true; }
        }
        return false;
    }
}
