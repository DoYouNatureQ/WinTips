using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel;

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
///   MSIX 商店版（进程带包标识）：调用 WinRT <see cref="StartupTask"/>，
///   对应 Build/msix/AppxManifest.xml 里的 windows.startupTask 声明。商店版必须走这条——
///   直接写 HKCU\...\Run 会被 MSIX 的注册表虚拟化静默吞掉，开关看着打开了，
///   实际重启后不会自启，甚至读回来的还是虚拟副本里的"已开启"。
///
///   免安装版（绿色 exe / Inno 安装版，无包标识）：沿用 HKCU\...\Run，与旧版行为一致。
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

    public static bool IsEnabled()
    {
        if (!IsPackaged) return IsRunValuePresent();

        try
        {
            var task = GetStartupTask();
            return task is not null && task.State == StartupTaskState.Enabled;
        }
        catch { return false; }
    }

    public static StartupChangeResult SetEnabled(bool enable)
    {
        if (!IsPackaged)
        {
            WriteRunValue(enable);
            return StartupChangeResult.Ok;
        }

        try
        {
            var task = GetStartupTask();
            if (task is null) return StartupChangeResult.Failed;

            if (!enable)
            {
                task.Disable();
                return StartupChangeResult.Ok;
            }

            return Await(task.RequestEnableAsync()) switch
            {
                StartupTaskState.Enabled => StartupChangeResult.Ok,
                StartupTaskState.DisabledByUser => StartupChangeResult.BlockedByUser,
                StartupTaskState.DisabledByPolicy => StartupChangeResult.BlockedByPolicy,
                _ => StartupChangeResult.Failed,
            };
        }
        catch { return StartupChangeResult.Failed; }
    }

    /// <summary>
    /// 取清单里声明的启动任务。清单没声明 startupTask 时 WinRT 抛 ELEMENT_NOT_FOUND，
    /// 这里统一收敛成 null，由调用方按"失败"处理。
    /// </summary>
    private static StartupTask? GetStartupTask()
    {
        try { return Await(StartupTask.GetAsync(TaskId)); }
        catch { return null; }
    }

    /// <summary>
    /// 同步等待 WinRT 异步操作。StartupTask 的完成回调由 WinRT 在自己的线程上触发，
    /// 不依赖调用线程的消息泵，所以在 UI 线程上阻塞不会死锁；单次调用在毫秒级。
    /// </summary>
    private static T Await<T>(Windows.Foundation.IAsyncOperation<T> operation) =>
        operation.AsTask().GetAwaiter().GetResult();

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
