using System.Reflection;

namespace WinTips.Core;

/// <summary>
/// 版本号的唯一出口：实际数值由 csproj 的 &lt;Version&gt; 决定，
/// 设置页底部与托盘悬停提示都从这里取，发版时只改一处。
/// </summary>
internal static class AppInfo
{
    /// <summary>形如 "0.4"：已去掉 SourceLink 追加的 +hash 等元数据。</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>形如 "V0.4"，界面直接展示用。</summary>
    public static string DisplayVersion => "V" + Version;

    private static string ResolveVersion()
    {
        var asm = typeof(AppInfo).Assembly;

        // InformationalVersion 由 <Version> 派生，优先用它（保留 0.4 这种两位写法）
        string? raw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // 兜底：AssemblyVersion 一定是四段，取前三段（0.4.0）
        if (string.IsNullOrWhiteSpace(raw))
            raw = asm.GetName().Version?.ToString(3);

        if (string.IsNullOrWhiteSpace(raw))
            return "0.0";

        raw = raw.Trim();

        // 构建工具可能追加 "+<commit>" / "-beta" 之类的后缀，版本号只要数字与点
        int cut = raw.IndexOfAny(['+', '-', ' ']);
        if (cut > 0) raw = raw[..cut];

        // 尾部多余的 .0（Version 写成 0.4.0.0 时）收成 0.4，避免显示 "V0.4.0.0"
        while (raw.EndsWith(".0", StringComparison.Ordinal) && raw.Count(c => c == '.') > 1)
            raw = raw[..^2];

        return raw;
    }
}
