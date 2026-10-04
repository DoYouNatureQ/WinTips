using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTips.Core;

public sealed class AppSettings
{
    public bool ShowCapsLock { get; set; } = true;
    public bool ShowNumLock { get; set; } = true;
    public bool ShowScrollLock { get; set; } = true;
    public bool ShowInsert { get; set; } = true;
    public double DisplaySeconds { get; set; } = 2.0;
    public bool LaunchAtStartup { get; set; }

    /// <summary>设置文件结构版本；V1.1 及以前没有该字段（AnimationSpeed 为整数档位）。</summary>
    public int SettingsVersion { get; set; } = 2;

    /// <summary>浮现动画速度倍率：0 无动画，否则动画时长 = 300ms × 倍率（0.1–3.0，步进 0.1）。</summary>
    public double AnimationSpeed { get; set; } = 1.0;

    /// <summary>倍率对应的动画时长（毫秒）。</summary>
    public static int AnimationMs(double speed) =>
        speed <= 0 ? 0 : (int)Math.Round(300 * speed);

    public int AnimationMs() => AnimationMs(AnimationSpeed);

    [JsonIgnore]
    public static AppSettings Instance { get; private set; } = new();

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTips");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static string LogPath => Path.Combine(Dir, "error.log");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(text);
                if (s != null)
                {
                    if (MigrateFromV1(s, text)) s.Save();
                    Instance = s;
                    return s;
                }
            }
        }
        catch { }
        Instance = new AppSettings();
        return Instance;
    }

    /// <summary>
    /// V1.1 及以前 AnimationSpeed 是 0–5 整数档位（档距 0.5x），改为 0–3 倍率（步进 0.1）后，
    /// 旧档位 g 等价于倍率 0.5 × g（档位 3=1.5x → 倍率 1.5，动画毫秒数不变）。
    /// </summary>
    private static bool MigrateFromV1(AppSettings s, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                doc.RootElement.TryGetProperty(nameof(SettingsVersion), out _))
                return false;

            if (doc.RootElement.TryGetProperty(nameof(AnimationSpeed), out var el) &&
                el.ValueKind == JsonValueKind.Number)
            {
                double gear = el.GetDouble();
                s.AnimationSpeed = gear <= 0 ? 0 : Math.Min(gear * 0.5, 3.0);
            }
            return true;
        }
        catch { return false; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WinTips";

    public static bool IsStartupSet()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunValue) != null;
        }
        catch { return false; }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) k.SetValue(RunValue, $"\"{exe}\"");
            }
            else if (k.GetValue(RunValue) != null)
            {
                k.DeleteValue(RunValue);
            }
        }
        catch { }
    }
}
