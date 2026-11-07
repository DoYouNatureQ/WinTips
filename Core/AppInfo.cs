using System.Reflection;

namespace WinTips.Core;

internal static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string DisplayVersion => "V" + Version;

    private static string ResolveVersion()
    {
        var asm = typeof(AppInfo).Assembly;

        string? raw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(raw))
            raw = asm.GetName().Version?.ToString(3);

        if (string.IsNullOrWhiteSpace(raw))
            return "0.0";

        raw = raw.Trim();

        int cut = raw.IndexOfAny(['+', '-', ' ']);
        if (cut > 0) raw = raw[..cut];

        while (raw.EndsWith(".0", StringComparison.Ordinal) && raw.Count(c => c == '.') > 1)
            raw = raw[..^2];

        return raw;
    }
}
