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
            var task = GetTask();
            return task is not null && task.State == StartupTaskState.Enabled;
        }
        catch { return false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static StartupChangeResult SetEnabled(bool enable)
    {
        try
        {
            var task = GetTask();
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupTask? GetTask()
    {
        try { return Await(StartupTask.GetAsync(StartupManager.TaskId)); }
        catch { return null; }
    }

    private static T Await<T>(Windows.Foundation.IAsyncOperation<T> operation) =>
        operation.AsTask().GetAwaiter().GetResult();
}
