using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

internal readonly record struct InputState(uint ThreadId, uint ProcessId, IntPtr Hkl, IntPtr ImeWnd, int ConversionMode)
{
    public bool IsUsable => Hkl != IntPtr.Zero;

    public bool HasIme => ImeWnd != IntPtr.Zero && ConversionMode >= 0;

    public bool IsNative => (ConversionMode & NativeMethods.IME_CMODE_NATIVE) != 0;

    public static InputState Current()
    {
        IntPtr hwnd = FocusWindow();
        if (hwnd == IntPtr.Zero) return default;

        uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (tid == 0) return default;

        IntPtr hkl = NativeMethods.GetKeyboardLayout(tid);
        if (hkl == IntPtr.Zero) return default;

        IntPtr ime = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
        return new InputState(tid, pid, hkl, ime, ime == IntPtr.Zero ? -1 : ReadConversionMode(ime));
    }

    /// <summary>
    /// 真正持有输入语言的那个窗口：前台线程的焦点窗口，退一步取它的活动窗口。
    /// </summary>
    /// <remarks>
    /// 桌面上直接用 GetForegroundWindow 就够，游戏里经常不够。全屏游戏常把输入交给子窗口，
    /// UWP/Xbox 游戏交给宿主进程里的 CoreWindow，外壳覆盖层（Steam、Discord、录制软件）
    /// 又可能反过来顶在最前面。这些情况下顶层窗口和真正处理按键的不是同一个线程，而
    /// GetKeyboardLayout 和输入法上下文都按线程算——照着顶层窗口去读，读到的语言从头到尾
    /// 没变过，提示于是永远不弹。Caps/Num 那几条提示不碰这些，所以只有输入法提示哑掉。
    /// </remarks>
    private static IntPtr FocusWindow()
    {
        try
        {
            var info = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            // idThread 传 0 表示前台线程
            if (NativeMethods.GetGUIThreadInfo(0, ref info))
            {
                if (info.hwndFocus != IntPtr.Zero) return info.hwndFocus;
                if (info.hwndActive != IntPtr.Zero) return info.hwndActive;
            }
        }
        catch { }

        return NativeMethods.GetForegroundWindow();
    }

    private static int ReadConversionMode(IntPtr imeWnd)
    {
        try
        {
            IntPtr ok = NativeMethods.SendMessageTimeoutW(imeWnd, NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG, 40, out IntPtr result);
            return ok == IntPtr.Zero ? -1 : result.ToInt32();
        }
        catch
        {
            return -1;
        }
    }
}

internal sealed class InputStateWatcher : IDisposable
{
    private const int PollMs = 50;

    /// <summary>
    /// 观察窗口。原来只有 1.2 秒，游戏里不够：全屏切换、加载读盘时前台的消息循环会被卡住，
    /// Win+Space 的语言切换落地得晚，短窗口直接错过。
    /// </summary>
    private const int WindowMs = 2000;

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(PollMs) };

    private InputState _last;
    private bool _hasLast;
    private int _ticks;

    public event Action<IntPtr>? LanguageChanged;

    public event Action<bool>? ImeModeChanged;

    public InputStateWatcher() => _poll.Tick += (_, _) => Poll();

    public void Touch(InputState baseline)
    {
        if (!AppSettings.Instance.ShowLanguageTip) return;

        if (baseline.IsUsable)
        {
            _last = baseline;
            _hasLast = true;
        }
        else
        {
            // 按切换键的那一刻读不到前台状态（全屏跳转中、桌面正在重新合成），就别留着
            // 上一次的旧状态当基准——那是别的程序的输入语言，拿它比只会张冠李戴
            _hasLast = false;
        }

        _ticks = 0;
        if (!_poll.IsEnabled) _poll.Start();
    }

    private void Poll()
    {
        if (++_ticks * PollMs > WindowMs)
        {
            _poll.Stop();
            _hasLast = false;
            return;
        }

        InputState now = InputState.Current();

        // 读不到就跳过这一拍，不要停：全屏游戏切分辨率、重建窗口时前台状态会短暂为空，
        // 一停就再也等不到后面的变化
        if (!now.IsUsable) return;

        if (!_hasLast)
        {
            _last = now;
            _hasLast = true;
            return;
        }

        if (now.ThreadId != _last.ThreadId)
        {
            // 同一个程序换了线程：游戏全屏跳转后重建窗口、输入法按需加载出新的 IME 窗口，
            // 都是这种情形。语言确实变了照报；换成别的程序（Alt+Tab 过去）就只当新基准，
            // 免得每切一次窗口都弹一条提示。
            bool sameApp = now.ProcessId == _last.ProcessId;
            bool hklChanged = now.Hkl != _last.Hkl;
            _last = now;
            if (!sameApp || !hklChanged) return;

            _poll.Stop();
            LanguageChanged?.Invoke(now.Hkl);
            return;
        }

        if (now.Hkl != _last.Hkl)
        {
            IntPtr hkl = now.Hkl;
            _last = now;
            _poll.Stop();
            LanguageChanged?.Invoke(hkl);
            return;
        }

        if (now.HasIme && _last.HasIme)
        {
            if (now.IsNative == _last.IsNative)
            {
                _last = now;
                return;
            }

            bool native = now.IsNative;
            _last = now;
            _poll.Stop();
            ImeModeChanged?.Invoke(native);
            return;
        }

        if (_last.HasIme)
        {
            // 基准里有输入法模式，这一拍没读出来——多半是目标线程正忙（游戏加载、读盘），
            // 40ms 的同步消息超时了。不更新基准，让后面几拍接着拿它比；把基准洗成
            // 「没有输入法」，Shift 切中英文就再也不会提示了。
            return;
        }

        _last = now;
    }

    public void Dispose() => _poll.Stop();
}

internal static class LanguageNames
{
    private static readonly Dictionary<IntPtr, string> Cache = new();

    public static string Display(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return string.Empty;
        if (Cache.TryGetValue(hkl, out string? cached)) return cached;

        string name = Resolve(hkl);

        if (Cache.Count >= 32) Cache.Clear();
        Cache[hkl] = name;
        return name;
    }

    private static string Resolve(IntPtr hkl)
    {
        try
        {
            uint langId = (uint)(hkl.ToInt64() & 0xFFFF);
            var locale = new StringBuilder(96);
            if (NativeMethods.LCIDToLocaleName(langId, locale, locale.Capacity, 0) <= 1)
                return Fallback(hkl);

            var name = new StringBuilder(256);
            foreach (uint kind in new[]
                     {
                         NativeMethods.LOCALE_SLOCALIZEDDISPLAYNAME,
                         NativeMethods.LOCALE_SNATIVEDISPLAYNAME,
                     })
            {
                if (NativeMethods.GetLocaleInfoEx(locale.ToString(), kind, name, name.Capacity) > 1)
                    return name.ToString();
            }
        }
        catch { }

        return Fallback(hkl);
    }

    private static string Fallback(IntPtr hkl) => $"0x{hkl.ToInt64():X8}";
}

internal static class InputTipText
{
    public static string For(InputState state) =>
        state.HasIme
            ? Loc.T(state.IsNative ? "ImeChinese" : "ImeEnglish")
            : LanguageNames.Display(state.Hkl);
}
