using System.Runtime.CompilerServices;
using Windows.ApplicationModel;

namespace WinTips.Core;

/// <summary>
/// MSIX 商店版专属的开机自启实现，走 WinRT <see cref="StartupTask"/>。
///
/// 单独成类是为了「惰性加载」：<c>Microsoft.Windows.SDK.NET.dll</c>（本机实测 23.7MB）
/// 是整个进程里最大的单个程序集，但免安装版一次都用不到。只要 StartupTask 相关类型
/// 出现在某个方法的方法体里，JIT 编译该方法时就会解析这些类型并加载该程序集——
/// 哪怕实际执行的是方法开头的另一条分支。所以这些类型必须只出现在本类中，
/// 而入口 <see cref="StartupManager"/> 只在真的带包标识时才调用到这里。
///
/// <see cref="MethodImplOptions.NoInlining"/> 是必须的：一旦被内联进调用方，
/// WinRT 类型令牌就会回到调用方方法体里，惰性加载前功尽弃。
/// </summary>
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

    /// <summary>
    /// 取清单里声明的启动任务。清单没声明 startupTask 时 WinRT 抛 ELEMENT_NOT_FOUND，
    /// 这里统一收敛成 null，由调用方按「失败」处理。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupTask? GetTask()
    {
        // StartupManager.TaskId 是 const，编译期即内联为字面量，不会引入额外的类型解析
        try { return Await(StartupTask.GetAsync(StartupManager.TaskId)); }
        catch { return null; }
    }

    /// <summary>
    /// 同步等待 WinRT 异步操作。StartupTask 的完成回调由 WinRT 在自己的线程上触发，
    /// 不依赖调用线程的消息泵，所以在 UI 线程上阻塞不会死锁；单次调用在毫秒级。
    /// </summary>
    private static T Await<T>(Windows.Foundation.IAsyncOperation<T> operation) =>
        operation.AsTask().GetAwaiter().GetResult();
}
