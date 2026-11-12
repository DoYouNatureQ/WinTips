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
/// 但"该不该轮询"这件事本身不能只信前台切换事件——那事件来自 SetWinEventHook，前台
/// 一旦是比本进程高的窗口，系统有理由不给（UIPI 的同一套过滤），于是事件收不到、轮询
/// 永远起不来，表现就是"在管理员窗口里按什么都没反应"。所以这里还挂了一个 500ms 的
/// 低频探测，只在前台上不去的时候起兜底作用，平时靠缓存几乎不花代价。
/// </remarks>
internal sealed class KeyStateWatcher : IDisposable
{
    private const int PollIntervalMs = 50;

    /// <summary>
    /// 兜底探测的节奏。比前台切换事件慢，只负责"迟早发现前台上去了"，所以不必密。
    /// </summary>
    private const int FallbackProbeMs = 500;

    /// <summary>要盯的键。顺序与 <see cref="_down"/> / <see cref="_on"/> 下标一一对应。</summary>
    private static readonly TipKind[] Keys =
        { TipKind.CapsLock, TipKind.NumLock, TipKind.ScrollLock, TipKind.Insert };

    /// <summary>
    /// 剪贴板组合键的判定表，修饰键要求与钩子路径完全一致：C/V/X 看 Ctrl，
    /// Delete 看 Shift 且不要 Ctrl（Shift+Delete 是剪切）。0x2E 是 Delete，
    /// 其余与 <see cref="TipInfo.VirtualKey"/> 对 Copy/Paste/Cut 的取值相同。
    /// </summary>
    private static readonly (int Vk, TipKind Kind, bool NeedCtrl)[] ClipCombos =
    {
        (0x43, TipKind.Copy, true),
        (0x56, TipKind.Paste, true),
        (0x58, TipKind.Cut, true),
        (0x2E, TipKind.Cut, false),
    };

    private readonly bool[] _clipDown = new bool[ClipCombos.Length];

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };

    private readonly DispatcherTimer _probe = new() { Interval = TimeSpan.FromMilliseconds(FallbackProbeMs) };

    private readonly bool[] _down = new bool[Keys.Length];

    /// <summary>
    /// 上一轮采样的时刻。轮询给不出按下的准确时刻，只能给出"我在这一段里看见了它"，
    /// 去重要靠这个区间才认得出"同一次按下别人已经报过"。
    /// </summary>
    private long _lastSampleAt;

    private readonly NativeMethods.WinEventProc _foregroundProc;

    private IntPtr _winEvent;
    private bool _running;

    /// <summary>任一锁定键被按下（切换键）。第二参数是翻转后的新状态。</summary>
    public event Action<TipKind, bool>? ToggleChanged;

    /// <summary>Insert 被单独按下。</summary>
    public event Action<TipKind>? Pressed;

    /// <summary>
    /// 剪贴板组合键被按下（Ctrl+C/V/X、Shift+Delete）。钩子被 UIPI 掐断时（前台提权或
    /// 受保护进程），粘贴不动剪贴板、没有剪贴板监听可以兜，只有这条路能把提示补上；
    /// 复制还有监听兜着，粘贴是唯一信号源就在这里。
    /// </summary>
    public event Action<TipKind>? ClipboardPressed;

    /// <summary>
    /// 返回 true 时轮询不再上报 Insert。GetAsyncKeyState 只有虚拟键、没有扫描码，
    /// 分不清 0x2D 是真 Insert 还是小键盘 0（Num Lock 关闭时后者上报的虚拟键就是
    /// VK_INSERT）。Raw Input 拿得到 E0 前缀，能分——所以它注册成功后 Insert 由它
    /// 独家负责，轮询闭嘴；注册失败时保持 null，兜底链路照常工作，代价是提权窗口
    /// 加 Num Lock 关闭的组合下按小键盘 0 仍可能误报一次（已知限制，见 README）。
    /// </summary>
    public Func<bool>? InsertCoveredByRawInput { get; set; }

    public KeyStateWatcher()
    {
        // 委托必须一直持有引用：SetWinEventHook 只认函数指针，回收了就是野指针。
        _foregroundProc = OnForegroundChanged;
        _poll.Tick += (_, _) => PollTick();
        _probe.Tick += (_, _) => Refresh("probe");
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _winEvent = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        Diag.Write($"keys watcher start, winevent=0x{_winEvent:X}");

        // 兜底探测无条件跑。挂不上钩子时它是唯一入口；挂上了但收不到高权限窗口的事件时
        // 它是保底——这两种情况在用户眼里都是"按了没反应"，不能赌其中一个不会发生。
        _probe.Start();

        Refresh("start");
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time) => Refresh("winevent");

    private void Refresh(string source)
    {
        if (!_running) return;

        if (ForegroundLevel.ForegroundOutranksSelf()) StartPolling(source);
        else StopPolling(source);
    }

    private void StartPolling(string source)
    {
        if (_poll.IsEnabled) return;

        // 切过去时用户可能正按着某个键：先把当前状态记下来，别把它当成本次按下。
        for (int i = 0; i < Keys.Length; i++)
        {
            int vk = TipInfo.VirtualKey(Keys[i]);
            _down[i] = (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        for (int i = 0; i < _clipDown.Length; i++)
            _clipDown[i] = (NativeMethods.GetAsyncKeyState(ClipCombos[i].Vk) & 0x8000) != 0;

        // 区间起点也要重置：上次轮询可能是几分钟前停的，拿那个时刻当起点，会把这段时间里
        // 任何一次按下都算进"我的区间"，把新按下的当成旧上报挡掉。
        _lastSampleAt = EventTime.Now;

        _poll.Start();
        Diag.Write($"poll start ({source})");
    }

    private void StopPolling(string source)
    {
        if (!_poll.IsEnabled) return;

        _poll.Stop();
        Array.Clear(_down);
        Array.Clear(_clipDown);
        Diag.Write($"poll stop ({source})");
    }

    private void PollTick()
    {
        // WinEvent 和兜底探测都可能漏（前者被 UIPI 吞、后者最多晚半秒），这里也再确认
        // 一次：前台落回普通窗口就收手，钩子那条链路接着干活。
        if (!ForegroundLevel.ForegroundOutranksSelf())
        {
            StopPolling("level dropped");
            return;
        }

        // 钩子那条链路里 Insert 也要看修饰键（Ctrl+Insert 是复制、Shift+Insert 是粘贴），
        // 这里同样处理，否则提权窗口里按 Ctrl+Insert 会被当成切到了改写模式。
        bool insertModified = IsDown(NativeMethods.VK_CONTROL) || IsDown(NativeMethods.VK_SHIFT);

        // 这一轮采样的时刻。它只说明"按下发生在 (上一轮, 这一轮] 之间"，而这个区间也要
        // 交给 LockState：否则别的路径报过同一次按下之后，这里又来翻一遍。
        long sampledAt = EventTime.Now;
        long since = _lastSampleAt;

        for (int i = 0; i < Keys.Length; i++)
        {
            var kind = Keys[i];
            int vk = TipInfo.VirtualKey(kind);

            bool down = (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
            bool edge = down && !_down[i];
            _down[i] = down;
            if (!edge) continue;

            if (kind == TipKind.Insert)
            {
                if (insertModified) continue;
                if (InsertCoveredByRawInput?.Invoke() == true) continue;
                if (!LockState.Accept(kind, sampledAt, since)) continue;
                Diag.Write("poll edge Insert");
                Pressed?.Invoke(kind);
            }
            else
            {
                if (!LockState.Toggle(kind, sampledAt, out bool on, since)) continue;
                Diag.Write($"poll edge {kind} on={on}");
                ToggleChanged?.Invoke(kind, on);
            }
        }

        // 剪贴板组合键。修饰键在本轮采样里读，就是按下时刻的状态——同一拍里它不会变，
        // 而钩子也是在字母键 keydown 那一刻看 Ctrl 在不在，两边的判定天然一致。
        bool ctrl = IsDown(NativeMethods.VK_CONTROL);
        bool shift = IsDown(NativeMethods.VK_SHIFT);

        for (int i = 0; i < ClipCombos.Length; i++)
        {
            bool down = (NativeMethods.GetAsyncKeyState(ClipCombos[i].Vk) & 0x8000) != 0;
            bool edge = down && !_clipDown[i];
            _clipDown[i] = down;
            if (!edge) continue;

            bool combo = ClipCombos[i].NeedCtrl ? ctrl : shift && !ctrl;
            if (!combo) continue;

            // 钩子活着（前台级别判错、保护进程没真提权）时它会先报，这里按采样区间
            // 挡掉；钩子死了这里就是唯一上报。窗口设 0，见 AcceptClipboard。
            if (!LockState.AcceptClipboard(ClipCombos[i].Kind, sampledAt, since)) continue;
            Diag.Write($"poll edge {ClipCombos[i].Kind} (clipboard)");
            ClipboardPressed?.Invoke(ClipCombos[i].Kind);
        }

        _lastSampleAt = sampledAt;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        _running = false;
        _poll.Stop();
        _probe.Stop();

        if (_winEvent != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEvent);
            _winEvent = IntPtr.Zero;
        }
    }
}
