using System.Text;
using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 置顶看守：卡片显示期间盯着自己有没有被别人压下去，被压住就重新排到置顶带最前面。
/// </summary>
/// <remarks>
/// WS_EX_TOPMOST 只说明"我在置顶带里"，不说明"我在置顶带的最前面"——带内谁在上谁在下，
/// 只看最后一次 SetWindowPos 的先后。边界全屏的游戏、录制软件、外壳覆盖层在拿到焦点时
/// 都会把自家窗口顶一次，有的游戏干脆在渲染循环里反复顶。于是提示弹出来一两百毫秒就被
/// 压回游戏画面底下，用户看到的就是"提示还是被游戏挡住了"，甚至以为根本没弹。
///
/// 这里的做法是"只在被压住时动作"：每隔 <see cref="IntervalMs"/> 看一眼自己上面有没有
/// 别的进程的可见窗口盖住了卡片中心，盖住了才置顶一次。置顶成功后自己就排到最前，下一次
/// 检查通常什么都不做——不会跟别的置顶程序来回拉锯，也不会因为反复 SetWindowPos 而闪。
/// </remarks>
internal sealed class TopmostGuard
{
    /// <summary>
    /// 检查间隔。100ms 是折中：对方把窗口顶上来之后，卡片最多晚 100ms 重新露出来，
    /// 这点延迟人眼看不出"先被挡一下"；再密就只是白白唤醒 UI 线程。
    /// </summary>
    private const int IntervalMs = 100;

    /// <summary>
    /// 被反复抢占时改用的节奏。有的游戏（尤其是无边框全屏 + 自带覆盖层的那种）在渲染循环
    /// 里反复把自己顶到最前，100ms 一拍就成了"露一下、挡一下"的闪烁。这时候干脆加快到
    /// 30ms 跟它抢，至少大部分时间是卡片在上面。
    /// </summary>
    private const int FastIntervalMs = 30;

    /// <summary>同一次显示里补抢几次之后开始加快节奏。</summary>
    private const int RaisesBeforeFast = 3;

    /// <summary>
    /// 沿 Z 序往上最多看这么多层。正常情况停在第一层（自己在最前就立即结束），
    /// 这个上限只是防枚举兜圈子。
    /// </summary>
    private const int MaxWalk = 64;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(IntervalMs) };

    private IntPtr _hwnd;

    private int _raises;

    public TopmostGuard() => _timer.Tick += (_, _) => Check();

    /// <summary>卡片要现身了（或又换了一次内容）：先抢一次，然后开始盯。</summary>
    public void Begin(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _raises = 0;
        _timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
        Raise(hwnd);
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>卡片收起来了：别再碰 Z 序。</summary>
    public void End() => _timer.Stop();

    /// <summary>
    /// 把自己插到置顶带的最前面。不动位置、不抢焦点——弹出时那一下和看守补的那一下都走这里。
    /// </summary>
    public static void Raise(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void Check()
    {
        var hwnd = _hwnd;
        if (hwnd == IntPtr.Zero)
        {
            _timer.Stop();
            return;
        }

        // 窗口已经收起来了（WPF 的 Hide 不一定会走到 End），这一拍直接跳过；
        // 下一次 Begin 会重新带起定时器。
        if (!NativeMethods.IsWindowVisible(hwnd)) return;

        IntPtr cover = FindCover(hwnd);
        if (cover == IntPtr.Zero) return;

        Raise(hwnd);
        _raises++;
        TraceRaise(cover);

        if (_raises <= RaisesBeforeFast) return;
        if (_timer.Interval.TotalMilliseconds > FastIntervalMs)
            _timer.Interval = TimeSpan.FromMilliseconds(FastIntervalMs);
    }

    /// <summary>
    /// 诊断用：把每次补抢记一行，被谁压住、抢了几次。只有设了环境变量才会写文件。
    /// 用户报"还是被遮挡"时，看这行就知道对方到底是哪个窗口、是抢一次就够还是一直在抢。
    /// </summary>
    private static readonly string? DebugTrace =
        Environment.GetEnvironmentVariable("WINTIPS_DEBUG_TOPMOST") is { Length: > 0 } path
            ? path
            : null;

    private void TraceRaise(IntPtr cover)
    {
        if (DebugTrace is null) return;

        var title = new StringBuilder(256);
        NativeMethods.GetWindowTextW(cover, title, title.Capacity);
        NativeMethods.GetWindowThreadProcessId(cover, out uint pid);

        try
        {
            System.IO.File.AppendAllText(DebugTrace,
                $"{DateTime.Now:HH:mm:ss.fff} raise#{_raises} over pid={pid} \"{title}\"\n");
        }
        catch { }
    }

    /// <summary>
    /// 卡片中心被哪个窗口盖住了；没被盖住返回 0。
    /// </summary>
    /// <remarks>
    /// 只判卡片中心那一个点，不判整块：任务栏、通知横幅、工具提示这类只蹭到卡片边缘的
    /// 置顶窗口不该触发抢占（它们本来就该压在卡片边上），而全屏游戏、最大化的窗口一定
    /// 盖住中心。判据窄一点，误抢的次数就少一点。
    /// </remarks>
    private static IntPtr FindCover(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var self)) return IntPtr.Zero;
        if (NativeMethods.GetWindowThreadProcessId(hwnd, out uint selfPid) == 0) return IntPtr.Zero;

        int cx = self.Left + (self.Right - self.Left) / 2;
        int cy = self.Top + (self.Bottom - self.Top) / 2;

        IntPtr walk = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV);
        for (int i = 0; i < MaxWalk && walk != IntPtr.Zero; i++)
        {
            if (NativeMethods.IsWindowVisible(walk) && !NativeMethods.IsIconic(walk) &&
                NativeMethods.GetWindowThreadProcessId(walk, out uint pid) != 0 && pid != selfPid &&
                NativeMethods.GetWindowRect(walk, out var other) &&
                cx >= other.Left && cx < other.Right && cy >= other.Top && cy < other.Bottom)
            {
                return walk;
            }

            walk = NativeMethods.GetWindow(walk, NativeMethods.GW_HWNDPREV);
        }

        return IntPtr.Zero;
    }
}
