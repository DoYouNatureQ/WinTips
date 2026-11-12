using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 锁定键状态的唯一真相来源：一次「按下即翻转」的累加器，外加同键去重。
/// </summary>
/// <remarks>
/// 为什么不能直接读系统：
/// <list type="bullet">
/// <item><c>GetKeyState</c> 返回的是**本线程消息队列的镜像**，只有该线程自己处理过键盘
/// 消息才会同步。WinTips 的 UI 线程没有键盘焦点，这个镜像基本停在启动那一刻——实测连按
/// 四次 CapsLock，四次都读回同一个值。旧代码拿它当答案，于是"一直显示同一个状态"。</item>
/// <item><c>GetAsyncKeyState</c> 只告诉你"是否按住"，低位是"自上次调用以来是否被按过"，
/// 不是锁定态，按一次就置位，同样不能用来判断开/关。</item>
/// </list>
///
/// 所以状态只能自己记：每捕获到一次按下就翻一次。<see cref="Initialize"/> 在启动时用
/// <see cref="AttachThreadInput"/> 借前台线程的输入队列读一次基线，之后全靠翻转——
/// 这样"每次按键状态必然改变"是结构性保证的，不依赖系统给不给面子。
///
/// 去重（<see cref="SameKindWindowMs"/>）必须挡在翻转之前：三条捕获路径会为同一次按下各报
/// 一次，被挡下的那条若还翻了状态，状态就会和实际错位。
///
/// 而且比较的必须是**按键发生的时刻**，不是各路径处理它的时刻，见 <see cref="Allow"/>。
/// </remarks>
internal static class LockState
{
    /// <summary>同一个键在这个窗口内只放行一次。人不可能 200ms 内连按两次锁定键。</summary>
    private const int SameKindWindowMs = 200;

    /// <summary>没有采样区间的调用方传这个值：只按按键时刻判定。</summary>
    private const long NoFloor = long.MinValue;

    private static readonly object Gate = new();

    private static readonly long[] LastFired = new long[8];

    private static readonly bool[] On = new bool[8];

    /// <summary>
    /// 启动时读一次基线。只在这时借前台线程的输入队列——之后全部靠翻转，不再问系统。
    /// </summary>
    public static void Initialize()
    {
        var parts = new System.Text.StringBuilder();

        foreach (var kind in new[] { TipKind.CapsLock, TipKind.NumLock, TipKind.ScrollLock })
        {
            int index = Index(kind);
            if (index < 0) continue;

            bool? value = TryReadFromForeground(kind);
            if (value is not null) On[index] = value.Value;

            if (parts.Length > 0) parts.Append(' ');
            parts.Append(kind).Append('=').Append(value is null ? "?" : value.Value ? "on" : "off");
        }

        Diag.Write($"lock baseline {parts}");
    }

    /// <summary>
    /// 捕获到一次按下：去重通过就翻转并返回新状态。
    /// </summary>
    /// <param name="at">按键发生的时刻，见 <see cref="EventTime"/>。</param>
    /// <param name="since">
    /// 采样路径专用：上一轮采样的时刻。轮询说不出按下发生在哪一毫秒，只知道自己在这一段
    /// (<paramref name="since"/>, <paramref name="at"/>] 里看见了它。事件驱动的那两条路径
    /// 传默认值即可。
    /// </param>
    public static bool Toggle(TipKind kind, long at, out bool on, long since = NoFloor)
    {
        on = false;
        int index = Index(kind);
        if (index < 0) return false;

        lock (Gate)
        {
            if (!Allow(kind, at, since, SameKindWindowMs)) return false;

            On[index] = !On[index];
            on = On[index];
        }

        return true;
    }

    /// <summary>不涉及状态切换的键（Insert）也要走去重。</summary>
    public static bool Accept(TipKind kind, long at, long since = NoFloor)
    {
        lock (Gate) return Allow(kind, at, since, SameKindWindowMs);
    }

    /// <summary>
    /// 剪贴板组合键（Ctrl+C/V/X、Shift+Delete）的去重：不带 200ms 同键窗口。
    /// </summary>
    /// <remarks>
    /// 200ms 窗口对锁定键成立——人不可能那么快连按两次 CapsLock；对 Ctrl+V 不成立，
    /// 快速连按粘贴是真实操作，窗口会把第二次按下当重复挡掉。同一次按下在钩子与轮询
    /// 两条路径上的重合，由 since 区间（轮询知道自己是在哪一段里看见按下的）与「按键
    /// 时刻单调」两条规则兜住，不依赖这个窗口，所以这里设 0。
    /// </remarks>
    public static bool AcceptClipboard(TipKind kind, long at, long since = NoFloor)
    {
        lock (Gate) return Allow(kind, at, since, 0);
    }

    /// <summary>
    /// 判定这一次上报该不该放行。<paramref name="at"/> 是按键发生的时刻，不是这条路径
    /// 处理它的时刻——这个区别就是整段的核心。
    /// </summary>
    /// <remarks>
    /// Raw Input 的 WM_INPUT 落在 UI 线程上，而 UI 线程可能正卡在抓背景那一帧里
    /// （ShowTip 取背景 + DwmFlush，几十到几百毫秒）。同一次按下的第二次上报于是远远晚于
    /// 钩子的那次；拿处理时刻去比，一次按键看着像两次，状态被翻两遍，卡片显示的就和真实的
    /// 锁定态相反——而且当场看不出来，要等下一次按键才暴露。比按键时刻就没这个问题。
    /// </remarks>
    private static bool Allow(TipKind kind, long at, long since, int windowMs)
    {
        int index = (int)kind;
        if ((uint)index >= (uint)LastFired.Length) return true;

        long last = LastFired[index];

        // 采样路径：这一段区间里已经有别人报过一次，那就是我这次看见的同一个按下。
        // 不能改成比"多久以前"——采样本身会被拖慢，够不到窗口的那一头。
        if (since != NoFloor && last > since) return false;

        // 按键时刻只会往前走。不晚于上一次上报的时刻，就不可能是新的一次按下，只可能是
        // 同一次按下的迟到上报（轮询抢先报过、Raw Input 带着更早的真实时刻随后赶到）。
        if (at <= last) return false;

        // 两条路径对同一次按下的时刻本就该相等：钩子的 KBDLLHOOKSTRUCT.time 与 Raw Input
        // 的 GetMessageTime 实测完全一致。留 200ms 是给路径间的抖动——人手不可能在 200ms
        // 内连按两次同一个锁定键。剪贴板组合键传 0 跳过这条，见 <see cref="AcceptClipboard"/>。
        if (at - last < windowMs) return false;

        LastFired[index] = at;
        return true;
    }

    /// <summary>
    /// 借前台线程的输入队列读一次真实状态。读不到返回 null（受保护进程、提权窗口都会失败），
    /// 调用方保留原值即可。
    /// </summary>
    private static bool? TryReadFromForeground(TipKind kind)
    {
        int vk = TipInfo.VirtualKey(kind);
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        uint target = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        if (target == 0) return null;

        uint self = NativeMethods.GetCurrentThreadId();
        if (target == self) return (NativeMethods.GetKeyState(vk) & 1) != 0;

        // 附加失败通常意味着对方级别更高（UIPI），此时没有可信来源，交给翻转推算。
        if (!NativeMethods.AttachThreadInput(self, target, true)) return null;
        try { return (NativeMethods.GetKeyState(vk) & 1) != 0; }
        finally { NativeMethods.AttachThreadInput(self, target, false); }
    }

    /// <summary>只有锁定键才有可维护的状态；其它类型返回 -1。</summary>
    private static int Index(TipKind kind) => kind switch
    {
        TipKind.CapsLock => 0,
        TipKind.NumLock => 1,
        TipKind.ScrollLock => 2,
        _ => -1,
    };
}
