using System.Runtime.InteropServices;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 前台窗口的完整性级别（IL）。
/// </summary>
/// <remarks>
/// 用来判断"低级键盘钩子此刻是不是哑了"：前台窗口的进程级别高于本进程时，系统不再
/// 回调本进程的 WH_KEYBOARD_LL——不是钩子装失败，是 UIPI 的既定行为，所以在提权的
/// 管理员窗口里按 CapsLock 什么都不会发生。判定出来之后由 <see cref="KeyStateWatcher"/>
/// 换成按键状态轮询，这条链路不经过钩子，因此不受级别限制。
///
/// 这里只做只读查询，不改任何东西：OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) 读
/// 令牌里的 TokenIntegrityLevel，取 SID 最后一段子授权（Medium=0x2000、High=0x3000）。
/// </remarks>
internal static class ForegroundLevel
{
    private const uint RidMedium = 0x2000;

    private static readonly uint SelfLevel = ReadSelf();

    private static IntPtr _lastHwnd;
    private static bool _lastRaised;
    private static bool _hasLast;

    /// <summary>本进程的完整性级别；读不到时按 Medium（asInvoker + UAC 的常态）。</summary>
    public static uint SelfRid => SelfLevel == 0 ? RidMedium : SelfLevel;

    /// <summary>
    /// 当前前台窗口是否比本进程级别更高。为真时钩子收不到任何按键。
    /// 判不出来（受保护进程、打开失败）时保守返回 true：多轮询一轮没有副作用，
    /// 漏判一轮用户就什么提示都看不见。
    /// </summary>
    public static bool ForegroundOutranksSelf()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();

        // 读不到前台窗口时不能假设"钩子还在工作"。全屏切换、显示模式重建、安全桌面刚
        // 退出的那几拍，GetForegroundWindow 会短暂返回 0；此时前台多半仍是那个高权限
        // 窗口，按"钩子正常"处理的结果是整段提示全哑。反过来多轮询一轮只是几次
        // GetAsyncKeyState，代价可以忽略。
        if (hwnd == IntPtr.Zero) return true;

        // 前台窗口在一次按键期间不会变，按句柄缓存，免得每 50ms 开一次进程令牌。
        if (_hasLast && hwnd == _lastHwnd) return _lastRaised;

        uint rid = OfWindow(hwnd);
        bool raised = rid == 0 || rid > SelfRid;

        if (Diag.Enabled)
        {
            Diag.Write($"fg {Diag.Describe(hwnd)} il=0x{rid:X} self=0x{SelfRid:X} polling={raised}");
        }

        _lastHwnd = hwnd;
        _lastRaised = raised;
        _hasLast = true;
        return raised;
    }

    private static uint OfWindow(IntPtr hwnd)
    {
        if (NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return 0;

        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return 0;
        try { return Read(process); }
        finally { NativeMethods.CloseHandle(process); }
    }

    private static uint ReadSelf() => Read(NativeMethods.GetCurrentProcess());

    private static uint Read(IntPtr process)
    {
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out IntPtr token)) return 0;
        try
        {
            NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel,
                IntPtr.Zero, 0, out int size);
            if (size <= 0) return 0;

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel,
                        buffer, size, out _))
                    return 0;

                IntPtr sid = Marshal.PtrToStructure<NativeMethods.TOKEN_MANDATORY_LABEL>(buffer).Label.Sid;
                if (sid == IntPtr.Zero) return 0;

                IntPtr countPtr = NativeMethods.GetSidSubAuthorityCount(sid);
                if (countPtr == IntPtr.Zero) return 0;

                byte count = Marshal.ReadByte(countPtr);
                if (count == 0) return 0;

                // 级别是 SID 的最后一段子授权：S-1-16-<RID>。
                IntPtr ridPtr = NativeMethods.GetSidSubAuthority(sid, count - 1);
                return ridPtr == IntPtr.Zero ? 0 : (uint)Marshal.ReadInt32(ridPtr);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { NativeMethods.CloseHandle(token); }
    }
}
