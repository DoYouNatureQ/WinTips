using System.Net.Http;
using System.Text.Json;

namespace WinTips.Core;

/// <summary>
/// 独立版（安装包 / 便携版）的新版本检查。
/// 只访问 GitHub Releases API 拿 tag_name，不带任何遥测；失败完全静默，
/// 24 小时节流。Store 打包版由商店负责更新，直接跳过。
/// </summary>
internal static class UpdateChecker
{
    private const string ApiUrl = "https://api.github.com/repos/DoYouNatureQ/WinTips/releases/latest";

    private static readonly TimeSpan Throttle = TimeSpan.FromHours(24);

    private static int _busy;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>最近一次联网查询到的远程版本号（已剥掉 v 前缀），查询成功且比本地新时才有值。</summary>
    public static string? LatestVersion { get; private set; }

    public static bool HasUpdate => LatestVersion != null && Compare(LatestVersion, AppInfo.Version) > 0;

    /// <summary>发现新版本时触发；可能来自后台线程，订阅方需自行调度回 UI 线程。</summary>
    public static event Action? Changed;

    public static bool IsPackaged
    {
        get
        {
            try { _ = Windows.ApplicationModel.Package.Current.Id; return true; }
            catch { return false; }
        }
    }

    /// <summary>
    /// 启动后台检查。先用上次联网结果做纯本地比对（节流窗口内重启也能立即亮小红点），
    /// 再决定是否真的发起网络请求。settings 打开时也会以 delayMs=0 调它，命中节流就只是空跑。
    /// </summary>
    public static void StartBackgroundCheck(int delayMs = 30_000)
    {
        var settings = AppSettings.Instance;

        if (settings.LatestReleaseVersion is { } cached && Compare(cached, AppInfo.Version) > 0)
        {
            LatestVersion = cached;
            Changed?.Invoke();
        }

        if (!settings.CheckForUpdates || IsPackaged) return;
        if (settings.LastUpdateCheckAt is { } at && DateTime.UtcNow - at < Throttle) return;

        _ = Task.Run(async () =>
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            await CheckAsync().ConfigureAwait(false);
        });
    }

    public static async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;

        try
        {
            using var resp = await Http.GetAsync(ApiUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tag) ||
                tag.ValueKind != JsonValueKind.String)
                return;

            string? version = NormalizeVersion(tag.GetString());
            if (string.IsNullOrEmpty(version)) return;

            var settings = AppSettings.Instance;
            settings.LastUpdateCheckAt = DateTime.UtcNow;
            // 远程不再比本地新（比如用户手动装了新版），清掉缓存让小红点熄灭。
            settings.LatestReleaseVersion = Compare(version, AppInfo.Version) > 0 ? version : null;
            settings.Save();

            if (Compare(version, AppInfo.Version) <= 0) return;

            LatestVersion = version;
            Changed?.Invoke();
        }
        catch
        {
            // 静默：断网、限流、代理问题都当没有，下次启动或用户再打开设置时重试。
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub API 强制要求 User-Agent；只带产品名和版本，不含任何用户信息。
        http.DefaultRequestHeaders.UserAgent.TryParseAdd($"WinTips/{AppInfo.Version}");
        http.DefaultRequestHeaders.Accept.TryParseAdd("application/vnd.github+json");
        return http;
    }

    private static string? NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().TrimStart('v', 'V');
    }

    /// <summary>按 '.' 分段转 int 逐段比较；缺的段补 0，非数字段按 0 处理。</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = ParseParts(a);
        var pb = ParseParts(b);
        for (int i = 0; i < pa.Length; i++)
        {
            int c = pa[i].CompareTo(pb[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    private static int[] ParseParts(string? value)
    {
        var parts = new int[4];
        if (string.IsNullOrWhiteSpace(value)) return parts;

        var segments = value.Trim().TrimStart('v', 'V').Split('.');
        for (int i = 0; i < segments.Length && i < parts.Length; i++)
            int.TryParse(segments[i].AsSpan().Trim(), out parts[i]);

        return parts;
    }
}
