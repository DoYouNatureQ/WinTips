using System.Runtime.InteropServices;
using System.Windows;
using WinTips.Native;

namespace WinTips.Core;

public sealed class KeyboardHook : IDisposable
{
    private IntPtr _hook;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly HashSet<uint> _down = new();

    /// <summary>锁定键被按下，第二个参数是按键发生的时刻（见 <see cref="EventTime"/>）。</summary>
    public event Action<TipKind, long>? ToggleChanged;

    /// <summary>Insert 被单独按下，第二个参数同上。</summary>
    public event Action<TipKind, long>? Pressed;

    public event Action<TipKind>? ClipboardPressed;

    internal event Action<InputState>? SwitchKeyDown;

    /// <summary>
    /// Shift 单独按下又抬起（按下与抬起时都没有别的键在按着，中间也没碰过键）。
    /// 这是第三方中文输入法最常用的中英切换动作——输入法在 Shift 抬起那一刻翻转
    /// 内部状态，而相当一部分第三方输入法不把这个状态同步给系统的 IMM32 读数，
    /// 只能靠这个动作自己推断（见 <see cref="InputStateWatcher.OnSoloShift"/>）。
    /// </summary>
    internal event Action? SoloShiftReleased;

    private bool _shiftSolo;

    private static bool IsShift(uint vk) => vk is 0xA0 or 0xA1 or 0x10;

    public KeyboardHook() => _proc = HookProc;

    public bool Installed => _hook != IntPtr.Zero;

    public void Install()
    {
        if (Installed) return;
        _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandleW(null), 0);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam.ToInt64();
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                if (_down.Add(info.vkCode))
                {
                    // Shift 是否算"单独按下"：按下那一刻只有它自己在按着。Alt+Shift、
                    // Ctrl+Shift 这类组合在按下瞬间就能排除；Shift 按住期间再碰任何
                    // 别的键（比如 Shift+字母）也会在下面把标志作废。
                    _shiftSolo = IsShift(info.vkCode) && _down.Count == 1;

                    if (IsSwitchCandidate(info.vkCode))
                    {
                        try { SwitchKeyDown?.Invoke(InputState.Current()); } catch { }
                    }

                    bool ctrl = IsDown(0xA2, 0xA3, 0x11);
                    bool shift = IsDown(0xA0, 0xA1, 0x10);

                    TipKind? kind = info.vkCode switch
                    {
                        // Windows has always had a second set of clipboard keys
                        // (Ctrl+Insert copies, Shift+Insert pastes, Shift+Delete
                        // cuts). Two of them land on keys this hook reports by
                        // itself, so the modifier is what tells a plain Insert —
                        // the overwrite toggle — apart from a copy or a paste.
                        0x2D when ctrl => TipKind.Copy,
                        0x2D when shift => TipKind.Paste,
                        0x2E when shift && !ctrl => TipKind.Cut,
                        0x14 => TipKind.CapsLock,
                        0x90 => TipKind.NumLock,
                        0x91 => TipKind.ScrollLock,
                        // Num Lock 关闭时，小键盘 0 上报的虚拟键就是 0x2D (VK_INSERT)
                        // —— Windows 一直如此。真 Insert 是扩展键（E0 前缀，钩子里是
                        // LLKHF_EXTENDED），小键盘 0 不是，扩展键位是把两者分开的唯一
                        // 线索。不查它，Num Lock 关着时每按一个 0 都会弹出 Insert 提示。
                        0x2D when (info.flags & NativeMethods.LLKHF_EXTENDED) != 0 => TipKind.Insert,
                        _ => null,
                    };
                    if (kind == null && ctrl)
                    {
                        kind = info.vkCode switch
                        {
                            0x43 => TipKind.Copy,
                            0x56 => TipKind.Paste,
                            0x58 => TipKind.Cut,
                            _ => null,
                        };
                    }
                    if (kind != null)
                    {
                        var k = kind.Value;

                        // 记的是按键发生的那一刻，不是回调被处理的那一刻：转发要过一次
                        // Dispatcher，UI 线程忙的时候这一步能拖几百毫秒，而 Raw Input
                        // 那条路会用同一个时刻来和这里比对。
                        long at = EventTime.Rebase(info.time);

                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Send, () =>
                        {
                            if (TipInfo.IsToggle(k)) ToggleChanged?.Invoke(k, at);
                            else if (k == TipKind.Insert) Pressed?.Invoke(k, at);
                            // 剪贴板组合键也走去重：前台级别判错时轮询兜底也在盯着同样的
                            // 组合键，两边各报一次就是两条重复提示。这里先到先记录，轮询
                            // 那头靠采样区间挡掉（见 AcceptClipboard 的注释）。
                            else if (LockState.AcceptClipboard(k, at)) ClipboardPressed?.Invoke(k);
                        });
                    }
                }
            }
            else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                _down.Remove(info.vkCode);
                if (IsShift(info.vkCode))
                {
                    bool solo = _shiftSolo && _down.Count == 0;
                    _shiftSolo = false;
                    if (solo)
                    {
                        try { SoloShiftReleased?.Invoke(); } catch { }
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsDown(uint left, uint right, uint generic) =>
        _down.Contains(left) || _down.Contains(right) || _down.Contains(generic);

    private bool IsSwitchCandidate(uint vk)
    {
        bool ctrl = IsDown(0xA2, 0xA3, 0x11);
        bool win = _down.Contains(0x5B) || _down.Contains(0x5C);
        bool alt = IsDown(0xA4, 0xA5, 0x12);

        return vk switch
        {
            0x20 => ctrl || win || alt,
            // Caps Lock：不少第三方输入法（小狼毫默认、搜狗等可选）拿它当中英切换键，
            // 也让观察窗口开起来——读数规范的照常报，不规范的走 Shift 推断兜底。
            0x14 => true,
            _ => vk is 0xA0 or 0xA1 or 0x10
                      or 0xA2 or 0xA3 or 0x11
                      or 0xA4 or 0xA5 or 0x12
                      or 0x5B or 0x5C,
        };
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
