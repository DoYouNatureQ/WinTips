using System.Runtime.InteropServices;
using System.Windows;
using WinTips.Native;

namespace WinTips.Core;

public sealed class KeyboardHook : IDisposable
{
    private IntPtr _hook;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly HashSet<uint> _down = new();

    public event Action<TipKind>? ToggleChanged;

    public event Action<TipKind>? Pressed;

    public event Action<TipKind>? ClipboardPressed;

    internal event Action<InputState>? SwitchKeyDown;

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
                    if (IsSwitchCandidate(info.vkCode))
                    {
                        try { SwitchKeyDown?.Invoke(InputState.Current()); } catch { }
                    }

                    TipKind? kind = info.vkCode switch
                    {
                        0x14 => TipKind.CapsLock,
                        0x90 => TipKind.NumLock,
                        0x91 => TipKind.ScrollLock,
                        0x2D => TipKind.Insert,
                        _ => null,
                    };
                    if (kind == null &&
                        (_down.Contains(0xA2) || _down.Contains(0xA3) || _down.Contains(0x11)))
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
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Send, () =>
                        {
                            if (TipInfo.IsToggle(k)) ToggleChanged?.Invoke(k);
                            else if (k == TipKind.Insert) Pressed?.Invoke(k);
                            else ClipboardPressed?.Invoke(k);
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

    private bool IsSwitchCandidate(uint vk)
    {
        bool ctrl = _down.Contains(0xA2) || _down.Contains(0xA3) || _down.Contains(0x11);
        bool win = _down.Contains(0x5B) || _down.Contains(0x5C);
        bool alt = _down.Contains(0xA4) || _down.Contains(0xA5) || _down.Contains(0x12);

        return vk switch
        {
            0x20 => ctrl || win || alt,
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
