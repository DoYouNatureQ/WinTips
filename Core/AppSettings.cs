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

    /// <summary>浮现动画速度档位（FluentFlyout 同款）：0 无动画、1=0.5x、2=1x、3=1.5x、4=2x、5=3x。</summary>
    public int AnimationSpeed { get; set; } = 3;

    /// <summary>档位对应的基准动画时长（毫秒）。</summary>
    public static int AnimationMs(int speed) => speed switch
    {
        0 => 0,
        1 => 150,
        2 => 300,
        3 => 450,
        4 => 600,
        _ => 900,
    };

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
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { Instance = s; return s; }
            }
        }
        catch { }
        Instance = new AppSettings();
        return Instance;
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
