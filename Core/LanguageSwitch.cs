using System.Text;
using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

internal readonly record struct InputState(uint ThreadId, IntPtr Hkl, IntPtr ImeWnd, int ConversionMode)
{
    public bool IsUsable => Hkl != IntPtr.Zero;

    public bool HasIme => ImeWnd != IntPtr.Zero && ConversionMode >= 0;

    public bool IsNative => (ConversionMode & NativeMethods.IME_CMODE_NATIVE) != 0;

    public static InputState Current()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return default;

        uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (tid == 0) return default;

        IntPtr hkl = NativeMethods.GetKeyboardLayout(tid);
        if (hkl == IntPtr.Zero) return default;

        IntPtr ime = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
        return new InputState(tid, hkl, ime, ime == IntPtr.Zero ? -1 : ReadConversionMode(ime));
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
    private const int PollMs = 60;
    private const int WindowMs = 1200;

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

        _ticks = 0;
        if (!_poll.IsEnabled) _poll.Start();
    }

    private void Poll()
    {
        if (++_ticks * PollMs > WindowMs)
        {
            _poll.Stop();
            return;
        }

        InputState now = InputState.Current();
        if (!now.IsUsable)
        {
            _poll.Stop();
            return;
        }

        if (!_hasLast)
        {
            _last = now;
            _hasLast = true;
            return;
        }

        if (now.ThreadId != _last.ThreadId || now.ImeWnd != _last.ImeWnd)
        {
            _last = now;
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

        if (now.HasIme && _last.HasIme && now.IsNative != _last.IsNative)
        {
            bool native = now.IsNative;
            _last = now;
            _poll.Stop();
            ImeModeChanged?.Invoke(native);
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
