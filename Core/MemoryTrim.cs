using System.Runtime;
using System.Windows;
using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 空闲时把内存占用收回来：先做一次全量阻塞式 GC（含大对象堆压缩），再把工作集清到最小。
/// 常驻托盘应用绝大多数时间都闲着，这些页留着只是把任务管理器里的数字撑大。
/// </summary>
/// <remarks>
/// 为什么 GC 放后台线程：主线程装着低级键盘钩子，系统对钩子回调有超时（LowLevelHooksTimeout，
/// 默认 300ms），主线程上做全量 GC 的几十到几百毫秒可能造成输入迟滞、甚至钩子被系统静默摘除。
/// 后台线程上只有每次 GC 挂起的几毫秒会碰到主线程，代价可以忽略。
///
/// 忙碌判定（<paramref name="isBusy"/>）：任何窗口可见（设置、菜单、通知对话框）或卡片在场。
/// 空闲满 <see cref="IdleAfterMs"/> 才收缩，且两次收缩至少也隔这么久——闲着时 GC 很便宜，
/// 重复收缩只是无害的例行公事，还能把「偶发事件撑回来的工作集」再次收掉。
///
/// 代价诚实说：清掉的工作集页下次访问要按需缺页回来，卡片再次弹出时可能多付一次几十毫秒
/// 的软页错误。设置页关闭后也走一次即时收缩（<see cref="TrimSoon"/>），那时候用户刚操作完，
/// 对首响不敏感。调试可用 WINTIPS_TRIM_IDLE_MS 覆盖空闲阈值（毫秒）。
/// </remarks>
internal static class MemoryTrim
{
    /// <summary>UI 线程判定忙碌的委托：返回 true 表示此刻不该收缩。</summary>
    private static Func<bool>? _isBusy;

    private static DispatcherTimer? _timer;
    private static long _lastBusyTick;
    private static long _lastTrimTick;

    /// <summary>空闲多久才开始收缩。窗口关掉、卡片淡出后，WPF/托管堆的峰值还没落地，
    /// 立刻收会跟正在收尾的分配打架，白做一遍。</summary>
    private static readonly long IdleAfterMs = ResolveIdleMs();

    private const int CheckEverySeconds = 30;

    private static long ResolveIdleMs()
    {
        string? raw = Environment.GetEnvironmentVariable("WINTIPS_TRIM_IDLE_MS");
        return long.TryParse(raw, out long ms) && ms is > 0 and <= 3_600_000 ? ms : 3 * 60 * 1000;
    }

    public static void Start(Dispatcher dispatcher, Func<bool> isBusy)
    {
        _isBusy = isBusy;
        _lastBusyTick = Environment.TickCount64;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(CheckEverySeconds),
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private static void Tick()
    {
        if (_isBusy is null) return;

        long now = Environment.TickCount64;
        if (_isBusy())
        {
            _lastBusyTick = now;
            return;
        }

        if (now - _lastBusyTick < IdleAfterMs || now - _lastTrimTick < IdleAfterMs) return;

        _lastTrimTick = now;
        RunTrim();
    }

    /// <summary>
    /// 设置页等重 UI 关闭后的即时收缩：等 Dispatcher 闲下来（窗口树已拆完）、确认没有
    /// 其他窗口/卡片在场，再在后台跑。不满足就退回常规空闲节奏，不抢这一锤。
    /// </summary>
    public static void TrimSoon(Dispatcher dispatcher)
    {
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_isBusy?.Invoke() == true)
            {
                _lastBusyTick = Environment.TickCount64;
                return;
            }
            _lastTrimTick = Environment.TickCount64;
            RunTrim();
        }));
    }

    private static void RunTrim()
    {
        _ = Task.Run(() =>
        {
            try
            {
                // Aggressive 会连大对象堆一起压缩——背景抓屏的池化缓冲归还后，LOH 里
                // 留着的洞只有压缩才能真正还给 OS（超出.ArrayPool 的持有策略时）。
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                NativeMethods.EmptyWorkingSet();
            }
            catch
            {
                // 收缩是锦上添花，任何一步失败都静默放弃，等下一轮。
            }
        });
    }
}
