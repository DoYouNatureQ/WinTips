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

    public static StartupChangeResult SetEnabled(bool enable)
    {
        if (!IsPackaged)
        {
            WriteRunValue(enable);
            return StartupChangeResult.Ok;
        }

        return PackagedStartup.SetEnabled(enable);
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

    private static void WriteRunValue(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(RunValue, $"\"{exe}\"");
            }
            else if (key.GetValue(RunValue) != null)
            {
                key.DeleteValue(RunValue);
            }
        }
        catch { }
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
