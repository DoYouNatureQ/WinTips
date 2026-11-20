using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTips.Core;

internal enum StartupChangeResult
{
    Ok,

    BlockedByUser,

    BlockedByPolicy,

    Failed,
}

internal static class StartupManager
{
    public const string TaskId = "WinTipsStartup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WinTips";

    private const int AppModelErrorNoPackage = 15700;

    private static bool? _packaged;

    public static bool IsPackaged => _packaged ??= DetectPackaged();

    public static bool IsEnabled()
    {
        if (!IsPackaged) return IsRunValuePresent();
        return PackagedStartup.IsEnabled();
    }

    /// <summary>
    /// 开关开机自启。打包版要等 WinRT 异步操作（可能秒级），必须异步调用，
    /// 别在 UI 线程上 .Result/.Wait——设置页会假死。
    /// </summary>
    public static Task<StartupChangeResult> SetEnabledAsync(bool enable)
    {
        if (!IsPackaged)
        {
            // 写注册表失败必须让调用方知道：吞掉后 UI 会显示"已开启自启"而注册表里
            // 根本没有值（被组策略限制、虚拟化重定向、ProcessPath 拿不到都可能失败），
            // 重启后不生效，用户还以为设置好了。注册表写很快，留在调用线程上即可。
            return Task.FromResult(WriteRunValue(enable) ? StartupChangeResult.Ok : StartupChangeResult.Failed);
        }

        return PackagedStartup.SetEnabledAsync(enable);
    }

    private static bool DetectPackaged()
    {
        try
        {
            uint length = 0;
            int rc = GetCurrentPackageFullName(ref length, null);
            return rc != AppModelErrorNoPackage;
        }
        catch { return false; }
    }

    private static bool IsRunValuePresent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) != null;
        }
        catch { return false; }
    }

    private static bool WriteRunValue(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(RunValue, $"\"{exe}\"");
            }
            else if (key.GetValue(RunValue) != null)
            {
                key.DeleteValue(RunValue);
            }
            return key.GetValue(RunValue) != null || !enable;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenSystemStartupSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps")
            {
                UseShellExecute = true,
            });
        }
        catch { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
}
