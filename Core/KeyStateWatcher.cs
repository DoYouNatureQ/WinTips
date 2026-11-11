using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 提权窗口里的按键兜底：前台窗口比本进程级别高时，改用按键状态轮询。
/// </summary>
/// <remarks>
/// 前台窗口的进程完整性级别高于本进程时，系统不回调本进程的低级键盘钩子（UIPI 的既定
/// 行为，见 README 已知限制）。于是"在管理员身份运行的窗口里按 CapsLock"完全不弹提示，
/// 用户看着像是被那个窗口挡住了，其实是钩子根本没被叫醒。
///
/// 钩子绕不开，只能换一条不依赖钩子的读法：GetAsyncKeyState 读的是会话级按键状态，
/// 与焦点无关，也不做完整性级别过滤。代价是只能靠"轮询 + 自己记上一次状态"取按下边沿，
/// 而不是系统直接送事件。
///
/// 轮询按需开关：前台窗口级别不高于自己时立刻停表，不给常驻进程加无谓的 20Hz 唤醒。
/// 前台切换用 SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 监听——比定期探测前台窗口便宜，
/// 而且切过去的那一刻就开始盯键，不必等下一个探测周期。
/// </remarks>
internal sealed class KeyStateWatcher : IDisposable
{
    private const int PollIntervalMs = 50;

    private const int FallbackProbeMs = 200;

    /// <summary>要盯的键。顺序与 <see cref="_down"/> 下标一一对应。</summary>
    private static readonly TipKind[] Keys =
        { TipKind.CapsLock, TipKind.NumLock, TipKind.ScrollLock, TipKind.Insert };

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };

    private readonly bool[] _down = new bool[Keys.Length];

    private readonly NativeMethods.WinEventProc _foregroundProc;

    private IntPtr _winEvent;
    private DispatcherTimer? _probe;
    private bool _running;

    /// <summary>任一锁定键被按下（切换键，状态由 OsdController 现读）。</summary>
    public event Action<TipKind>? ToggleChanged;

    /// <summary>Insert 被单独按下。</summary>
    public event Action<TipKind>? Pressed;

    public KeyStateWatcher()
    {
        // 委托必须一直持有引用：SetWinEventHook 只认函数指针，回收了就是野指针。
        _foregroundProc = OnForegroundChanged;
        _poll.Tick += (_, _) => PollTick();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _winEvent = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_winEvent == IntPtr.Zero)
        {
            // 挂不上（极少见）也不能就此哑掉：退回定期探测，代价是切过去后最多晚 200ms 才开始盯键。
            _probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FallbackProbeMs) };
            _probe.Tick += (_, _) => Refresh();
            _probe.Start();
        }

        Refresh();
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time) => Refresh();

    private void Refresh()
    {
        if (!_running) return;
        if (ForegroundLevel.ForegroundOutranksSelf()) StartPolling();
        else StopPolling();
    }

    private void StartPolling()
    {
        if (_poll.IsEnabled) return;
        // 切过去时用户可能正按着某个键：先把当前状态记下来，别把它当成本次按下。
        for (int i = 0; i < Keys.Length; i++) _down[i] = IsDown(TipInfo.VirtualKey(Keys[i]));
        _poll.Start();
    }

    private void StopPolling()
    {
        _poll.Stop();
        Array.Clear(_down);
    }

    private void PollTick()
    {
        // WinEvent 万一漏了，这里也再确认一次：前台落回普通窗口就收手，钩子那条链路接着干活。
        if (!ForegroundLevel.ForegroundOutranksSelf())
        {
            StopPolling();
            return;
        }

        // 钩子那条链路里 Insert 也要看修饰键（Ctrl+Insert 是复制、Shift+Insert 是粘贴），
        // 这里同样处理，否则提权窗口里按 Ctrl+Insert 会被当成切到了改写模式。
        bool insertModified = IsDown(NativeMethods.VK_CONTROL) || IsDown(NativeMethods.VK_SHIFT);

        for (int i = 0; i < Keys.Length; i++)
        {
            var kind = Keys[i];
            bool down = IsDown(TipInfo.VirtualKey(kind));
            bool edge = down && !_down[i];
            _down[i] = down;
            if (!edge) continue;

            if (kind == TipKind.Insert)
            {
                if (!insertModified) Pressed?.Invoke(kind);
            }
            else
            {
                ToggleChanged?.Invoke(kind);
            }
        }
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        _running = false;
        _poll.Stop();
        _probe?.Stop();
        _probe = null;

        if (_winEvent != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEvent);
            _winEvent = IntPtr.Zero;
        }
    }
}
