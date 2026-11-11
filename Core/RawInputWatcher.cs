using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 用 Raw Input 收锁定键，替代/补强"钩子 + 轮询"那套在提权窗口里会哑掉的链路。
/// </summary>
/// <remarks>
/// 注册时用 <c>RIDEV_INPUTSINK</c>：窗口不在前台也照收。这一点是关键——前台换成管理员
/// 窗口时，本进程既不是焦点窗口、低级钩子又被 UIPI 掐断，只有 INPUTSINK 这条通道还在。
///
/// 接收窗口是一个 1×1、坐标 (-32000,-32000)、带 WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE 的
/// 弹出窗口：不进 Alt+Tab、不抢焦点、看不见。它是纯消息泵，没有 WPF 内容。
///
/// 状态（Caps 当前开还是关）同样拿不到系统真值：GetKeyState 读的是本线程消息队列的镜像，
/// 前台是提权窗口时那个镜像根本不更新。所以和轮询路径一样，自己按下标记住并翻转。
/// </remarks>
internal sealed class RawInputWatcher : IDisposable
{
    /// <summary>要盯的键，下标与 <see cref="_on"/> 对应。</summary>
    private static readonly TipKind[] Keys =
        { TipKind.CapsLock, TipKind.NumLock, TipKind.ScrollLock, TipKind.Insert };

    /// <summary>
    /// 扫描码兜底表。Raw Input 对某些键给出的 VKey 是 0xFF（尤其带 E0 前缀的 Insert），
    /// 那时只能按 <c>MakeCode</c> 认。
    /// </summary>
    private static readonly (ushort MakeCode, TipKind Kind)[] ByMakeCode =
    {
        (0x3A, TipKind.CapsLock),
        (0x45, TipKind.NumLock),
        (0x46, TipKind.ScrollLock),
        (0x52, TipKind.Insert),
    };

    /// <summary>上一次的按下态，用于滤掉长按时系统重复投递的按下。</summary>
    private readonly bool[] _down = new bool[Keys.Length];

    /// <summary>上一次按下的时刻，配合 <see cref="RepeatGuardMs"/> 兜住"释放事件丢失"。</summary>
    private readonly long[] _downAt = new long[Keys.Length];

    /// <summary>
    /// 长按时系统会每几十毫秒重复投递一次按下，靠 <see cref="_down"/> 滤掉。
    /// 但万一释放事件没收到（设备热插拔、驱动异常），这个标记会永远卡在 true，
    /// 之后每一次按下都被当成重复吞掉——表现就是"怎么按都不变"。所以给它一个寿命：
    /// 超过这么久没有释放，就当那是一次新的按下。正常长按的重复间隔只有几十毫秒，
    /// 不会误判。
    /// </summary>
    private const int RepeatGuardMs = 1500;

    private HwndSource? _source;

    /// <summary>注册是否成功。失败时说明 Raw Input 这条链没接上，只能靠轮询兜。</summary>
    public bool Registered { get; private set; }

    /// <summary>锁定键被按下，参数是翻转后的新状态。</summary>
    public event Action<TipKind, bool>? ToggleChanged;

    /// <summary>Insert 被单独按下（未带 Ctrl/Shift）。</summary>
    public event Action<TipKind>? Pressed;

    public void Start()
    {
        if (_source is not null) return;

        var parameters = new HwndSourceParameters("WinTipsRawInputSink")
        {
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                usUsage = NativeMethods.HID_USAGE_GENERIC_KEYBOARD,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _source.Handle
            }
        };

        Registered = NativeMethods.RegisterRawInputDevices(devices, 1,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());

        Diag.Write($"rawinput start hwnd=0x{_source.Handle:X} registered={Registered} " +
                   $"err={Marshal.GetLastWin32Error()}");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_INPUT) return IntPtr.Zero;

        try { ReadKey(lParam); }
        catch (Exception ex) { Diag.Write($"rawinput read failed: {ex.Message}"); }

        // 交给后续（DefWindowProc 对 WM_INPUT 有清理动作），不标记 handled。
        return IntPtr.Zero;
    }

    private void ReadKey(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();

        // 这条消息被投递的时刻，也就是按键发生的时刻。必须在这里取：WM_INPUT 落在 UI
        // 线程上，而 UI 线程可能正卡在抓背景那一帧里，等处理到这里已经过去几百毫秒。
        long at = EventTime.Rebase((uint)NativeMethods.GetMessageTime());

        uint size = 0;
        NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 1024) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint read = size;
            if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref read, headerSize)
                == uint.MaxValue)
                return;

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RIM_TYPEKEYBOARD) return;

            var key = Marshal.PtrToStructure<NativeMethods.RAWKEYBOARD>(
                IntPtr.Add(buffer, (int)headerSize));

            OnKey(key, at);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void OnKey(NativeMethods.RAWKEYBOARD key, long at)
    {
        // 只认按下边沿；释放忽略。长按会由系统重复投递，这里靠"上一次是按下"过滤掉。
        if ((key.Flags & NativeMethods.RI_KEY_BREAK) != 0)
        {
            int up = IndexOf(key);
            if (up >= 0) _down[up] = false;
            return;
        }

        int index = IndexOf(key);
        if (index < 0) return;

        // 用按键时刻而不是处理时刻：UI 线程被拖住时，处理时刻会把长按的自动重复间隔
        // 撑大到看不出是重复。
        if (_down[index] && at - _downAt[index] < RepeatGuardMs) return;

        _down[index] = true;
        _downAt[index] = at;

        var kind = Keys[index];

        if (kind == TipKind.Insert)
        {
            // Ctrl+Insert 是复制、Shift+Insert 是粘贴，都不能当成切改写模式。
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0) return;
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0) return;

            // 去重放在提交之前：被挡的那条绝不能动状态，否则状态会和实际错位。
            if (!LockState.Accept(kind, at)) return;

            Diag.Write("rawinput edge Insert");
            Pressed?.Invoke(kind);
            return;
        }

        if (!LockState.Toggle(kind, at, out bool on))
        {
            // 钩子已经报过这一次了。记一行是有用的：它证明 Raw Input 这条通道确实
            // 收到了按键——排查"提权窗口里到底哪条路径还活着"时，这一行就是证据。
            Diag.Write($"rawinput edge {kind} deduped");
            return;
        }

        Diag.Write($"rawinput edge {kind} on={on}");
        ToggleChanged?.Invoke(kind, on);
    }

    /// <summary>上一次的按下态，用于滤掉系统的自动重复。</summary>
    private static int IndexOf(NativeMethods.RAWKEYBOARD key)
    {
        if (key.VKey != NativeMethods.VK_NONE_RAW)
        {
            for (int i = 0; i < Keys.Length; i++)
                if (TipInfo.VirtualKey(Keys[i]) == key.VKey) return i;
        }

        // VKey 缺失（0xFF）时退回扫描码。E0 前缀的 Insert 也是 0x52，不影响判断。
        for (int i = 0; i < ByMakeCode.Length; i++)
            if (ByMakeCode[i].MakeCode == key.MakeCode)
                return Array.IndexOf(Keys, ByMakeCode[i].Kind);

        return -1;
    }

    public void Dispose()
    {
        if (_source is null) return;

        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
        Registered = false;
    }
}
