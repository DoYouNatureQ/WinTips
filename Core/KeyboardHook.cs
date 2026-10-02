using System.Runtime.InteropServices;
using System.Windows;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// WH_KEYBOARD_LL 全局钩子。回调在安装线程（WPF UI 线程）上触发，
/// 事件经 Dispatcher 最高优先级异步分发，不阻塞输入。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private IntPtr _hook;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly HashSet<uint> _down = new();

    /// <summary>Caps/Num/Scroll Lock 按下（系统随后会翻转状态）。</summary>
    public event Action<TipKind>? ToggleChanged;

    /// <summary>Insert 按下（事件式，无系统级状态）。</summary>
    public event Action<TipKind>? Pressed;

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
                // 过滤按住不放的自动重复，只响应第一次 keydown
                if (_down.Add(info.vkCode))
                {
                    TipKind? kind = info.vkCode switch
                    {
                        0x14 => TipKind.CapsLock,
                        0x90 => TipKind.NumLock,
                        0x91 => TipKind.ScrollLock,
                        0x2D => TipKind.Insert,
                        _ => null,
                    };
                    if (kind != null)
                    {
                        var k = kind.Value;
                        bool isToggle = TipInfo.IsToggle(k);
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Send, () =>
                        {
                            if (isToggle) ToggleChanged?.Invoke(k);
                            else Pressed?.Invoke(k);
                        });
                    }
                }
            }
            else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                _down.Remove(info.vkCode);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
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
