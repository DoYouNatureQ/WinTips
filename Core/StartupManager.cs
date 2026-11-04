using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WinTips.Core;

/// <summary>开机自启操作结果。开启可能被系统拦下，调用方必须据此回滚 UI，不能给假开关。</summary>
internal enum StartupChangeResult
{
    /// <summary>已达成目标状态。</summary>
    Ok,

    /// <summary>用户曾在任务管理器里关掉过它，Windows 要求本人到「设置 &gt; 应用 &gt; 启动」里重新打开。</summary>
    BlockedByUser,

    /// <summary>组策略禁止应用自行开启自启。</summary>
    BlockedByPolicy,

    /// <summary>调用失败（清单缺少 startupTask 声明、包状态损坏等）。</summary>
    Failed,
}

/// <summary>
/// 开机自启的唯一入口，按运行形态走两条通道：
///
///   MSIX 商店版（进程带包标识）：交给 <see cref="PackagedStartup"/> 调 WinRT StartupTask，
///   对应 Build/msix/AppxManifest.xml 里的 windows.startupTask 声明。商店版必须走这条——
///   直接写 HKCU\...\Run 会被 MSIX 的注册表虚拟化静默吞掉，开关看着打开了，
///   实际重启后不会自启，甚至读回来的还是虚拟副本里的"已开启"。
///
///   免安装版（绿色 exe / Inno 安装版，无包标识）：沿用 HKCU\...\Run，与旧版行为一致。
///
/// 本类刻意不出现任何 <c>Windows.*</c> 类型：WinRT 投影（Microsoft.Windows.SDK.NET.dll，
/// 本机实测 23.7MB）一旦被 JIT 触碰就会整块加载进内存，而免安装版根本用不到它。
/// 因此 WinRT 代码全部隔离在 <see cref="PackagedStartup"/>，本类只在带包标识时转调它。
///
/// 对外是同一套同步 API，调用方不需要知道自己在哪种形态下运行。
/// </summary>
internal static class StartupManager
{
    /// <summary>必须与 Build/msix/AppxManifest.xml 里 StartupTask 的 TaskId 完全一致。</summary>
    public const string TaskId = "WinTipsStartup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WinTips";

    /// <summary>GetCurrentPackageFullName 在无包标识进程中的返回值。</summary>
    private const int AppModelErrorNoPackage = 15700;

    private static bool? _packaged;

    /// <summary>当前进程是否带 MSIX 包标识。结果缓存：一个进程生命周期内不会变。</summary>
    public static bool IsPackaged => _packaged ??= DetectPackaged();

    /// <summary>
    /// 查询自启状态。<see cref="PackagedStartup.IsEnabled"/> 标了 NoInlining，
    /// 保证本方法的方法体里不会出现 WinRT 类型令牌，免安装版也就不加载那 23.7MB 投影。
    /// </summary>
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

    /// <summary>
    /// 判定是否带包标识：无标识时返回 AppModelErrorNoPackage；有标识时因为缓冲为空
    /// 返回 ERROR_INSUFFICIENT_BUFFER（122），两者足以区分。API 不存在（Win7）时按无标识处理。
    /// </summary>
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

    /// <summary>
    /// 打开系统「启动应用」设置页。用户手动关掉自启后，Windows 只认用户自己去那里重新打开，
    /// 应用再怎么调 RequestEnableAsync 都会被拒——把用户送到正确的位置比留在原地解释更有用。
    /// </summary>
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
