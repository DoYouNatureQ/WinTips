using System.Runtime.CompilerServices;
using Windows.ApplicationModel;

namespace WinTips.Core;

internal static class PackagedStartup
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsEnabled()
    {
        try
        {
            var task = GetTaskAsync().GetAwaiter().GetResult();
            return task is not null && task.State == StartupTaskState.Enabled;
        }
        catch { return false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<StartupChangeResult> SetEnabledAsync(bool enable)
    {
        try
        {
            var task = await GetTaskAsync();
            if (task is null) return StartupChangeResult.Failed;

            if (!enable)
            {
                task.Disable();
                return StartupChangeResult.Ok;
            }

            // RequestEnableAsync 可能要拉取注册信息甚至等系统确认，耗时以秒计。
            // 绝不能在 UI 线程上同步等它（设置页会假死），必须真异步。
            return (await task.RequestEnableAsync()) switch
            {
                StartupTaskState.Enabled => StartupChangeResult.Ok,
                StartupTaskState.DisabledByUser => StartupChangeResult.BlockedByUser,
                StartupTaskState.DisabledByPolicy => StartupChangeResult.BlockedByPolicy,
                _ => StartupChangeResult.Failed,
            };
        }
        catch { return StartupChangeResult.Failed; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<StartupTask?> GetTaskAsync()
    {
        try { return await StartupTask.GetAsync(StartupManager.TaskId); }
        catch { return null; }
    }
}
