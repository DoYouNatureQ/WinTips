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

    /// <summary>
    /// Shift 单独抬起后的宽限。读数规范的输入法（微软拼音）在这一拍内就会给出新的转换
    /// 模式，系统路径先报；宽限过完读数还纹丝不动，说明这台输入法不把内部中英状态同步
    /// 进 IMM32 转换模式（搜狗、微信输入法、小狼毫都有版本如此），改按按键推断翻面。
    /// </summary>
    private const int InferGraceMs = 350;

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(PollMs) };

    private InputState _last;
    private bool _hasLast;
    private int _ticks;

    /// <summary>
    /// 推断的中英文状态。系统读得出来就跟着读数走；完全读不出来（第三方输入法不处理
    /// WM_IME_CONTROL）时只能靠 Shift 单独抬起翻面。这类输入法的中英状态通常是全局
    /// 同步的（在哪个窗口切都会全局生效），所以这份缓存跨窗口、跨线程保留，不随焦点
    /// 变化作废——作废了读不出数的输入法就永远等不到锚点。
    /// </summary>
    private bool _inferredNative = true;

    /// <summary>观察窗口内收到的 Shift 单独抬起次数。连按两次净变化为零，不报。</summary>
    private int _hintCount;

    /// <summary>最近一次 Shift 单独抬起的时刻，-1 表示窗口内没有待处理的切换动作。</summary>
    private long _hintAt = -1;

    public event Action<IntPtr>? LanguageChanged;

    public event Action<bool>? ImeModeChanged;

    /// <summary>
    /// 每读到一份可用的输入状态就发一次。焦点监听（<see cref="ForegroundWatcher"/>）
    /// 靠它保持基准新鲜：语言只会因为按键（这里在喂）或焦点切换（它自己采）而变，
    /// 两条路都记下来，基准就不会陈旧到张冠李戴。
    /// </summary>
    public event Action<InputState>? Sampled;

    public InputStateWatcher() => _poll.Tick += (_, _) => Poll();

    public void Touch(InputState baseline)
    {
        if (!AppSettings.Instance.ShowLanguageTip) return;

        if (baseline.IsUsable)
        {
            _last = baseline;
            _hasLast = true;
            Sampled?.Invoke(baseline);
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

    /// <summary>
    /// Shift 单独按下并抬起（中间没碰别的键）。第三方中文输入法的中英切换就发生在
    /// Shift 抬起那一刻；读数规范的走系统路径，读数不规范的由推断兜底。
    /// </summary>
    public void OnSoloShift()
    {
        if (!AppSettings.Instance.ShowLanguageTip) return;

        InputState now = InputState.Current();

        // 推断只对中文输入法生效：美式键盘按 Shift 是正常输入不是切换；日文等其他
        // IME 的 Shift 语义也不同，翻了只会报错"中文/英文"。
        if (!now.IsUsable || !IsChineseImeLayout(now.Hkl)) return;

        _hintCount++;
        Touch(now);
        _hintAt = EventTime.Now;

        Diag.Write($"solo shift hint #{_hintCount}");
    }

    private void Poll()
    {
        if (++_ticks * PollMs > WindowMs)
        {
            StopWindow();
            return;
        }

        InputState now = InputState.Current();

        // 读不到就跳过这一拍，不要停：全屏游戏切分辨率、重建窗口时前台状态会短暂为空，
        // 一停就再也等不到后面的变化
        if (!now.IsUsable) return;

        Sampled?.Invoke(now);

        if (!_hasLast)
        {
            _last = now;
            _hasLast = true;
        }
        else if (now.ThreadId != _last.ThreadId)
        {
            // 同一个程序换了线程：游戏全屏跳转后重建窗口、输入法按需加载出新的 IME 窗口，
            // 都是这种情形。语言确实变了照报；换成别的程序（Alt+Tab 过去）就只当新基准，
            // 免得每切一次窗口都弹一条提示。
            bool sameApp = now.ProcessId == _last.ProcessId;
            bool hklChanged = now.Hkl != _last.Hkl;
            _last = now;
            if (sameApp && hklChanged)
            {
                StopWindow();
                LanguageChanged?.Invoke(now.Hkl);
            }
            return;
        }
        else if (now.Hkl != _last.Hkl)
        {
            IntPtr hkl = now.Hkl;
            _last = now;
            StopWindow();
            LanguageChanged?.Invoke(hkl);
            return;
        }

        if (ModePoll(now)) return;

        _last = now;
    }

    /// <summary>
    /// 中英文模式检测：先问系统（IMM32 转换模式），系统答不上来再按按键推断。
    /// 返回 true 表示这一拍已经收尾（报了变化、判定无变化或还在等推断宽限），
    /// 外层不必再拿 <paramref name="now"/> 刷新基准。
    /// </summary>
    private bool ModePoll(InputState now)
    {
        if (now.HasIme)
        {
            if (_last.HasIme)
            {
                if (now.IsNative == _last.IsNative)
                {
                    // 读数活着但值没变——先让缓存跟上系统，再去看 Shift 有没有刚抬起
                    _inferredNative = now.IsNative;
                    return InferTick();
                }

                bool native = now.IsNative;
                _last = now;
                _inferredNative = native;
                StopWindow();
                Diag.Write($"ime mode change (system) native={native}");
                ImeModeChanged?.Invoke(native);
                return true;
            }

            // 基准在按下那一刻没读出输入法状态（线程忙、窗口正建）。Shift 刚单独抬起
            // 过而读数与推断缓存不符，说明切换就发生在读不到的那段——按读数报。
            // 读数与缓存一致时不能当变化：缓存本来就跟着读数走。
            if (_hintCount > 0 && now.IsNative != _inferredNative)
            {
                bool native = now.IsNative;
                _last = now;
                _inferredNative = native;
                StopWindow();
                Diag.Write($"ime mode change (system, late read) native={native}");
                ImeModeChanged?.Invoke(native);
                return true;
            }

            _last = now;
            _inferredNative = now.IsNative;
            return InferTick();
        }

        if (_last.HasIme)
        {
            // 基准里有读数、这一拍没读出来——多半是目标线程正忙（游戏加载、读盘），
            // 40ms 的同步消息超时了。基准既然读出过，说明这台输入法会应答，等后面
            // 几拍恢复就行；这时候推断没有依据，不动基准也不翻面。
            return true;
        }

        // 基准和现在都读不到输入法状态：这台输入法压根不提供 IMM32 读数（没有默认
        // IME 窗口，或从不处理 WM_IME_CONTROL）。Shift 单独抬起的那一刻状态已经
        // 翻转，没有可等的读数，直接按推断翻面。
        if (_hintCount > 0) return InferFlip();

        return false;
    }

    /// <summary>读数活着但不反映模式：给系统一个宽限，宽限过完仍无变化就按按键推断。</summary>
    private bool InferTick()
    {
        if (_hintCount == 0) return false;
        if (_hintAt >= 0 && EventTime.Now - _hintAt < InferGraceMs) return true;
        return InferFlip();
    }

    /// <summary>按 Shift 抬起次数翻面并上报。连按两次（净变化为零）只结账不报。</summary>
    private bool InferFlip()
    {
        bool native = _inferredNative;
        if (_hintCount % 2 == 1) native = !native;
        _inferredNative = native;
        StopWindow();

        if (_hintCount % 2 == 1)
        {
            Diag.Write($"ime mode change (inferred from shift) native={native}");
            ImeModeChanged?.Invoke(native);
        }
        return true;
    }

    private void StopWindow()
    {
        _poll.Stop();
        _hasLast = false;
        _hintCount = 0;
        _hintAt = -1;
    }

    /// <summary>
    /// 前台布局是否为中文输入法。ImmIsIME 过滤掉"中文(简体)——美式键盘"这类无 IME
    /// 布局；再放行高位非零的 HKL——TSF 输入法的布局设备号在高位，个别纯 TSF 输入法
    /// 对 ImmIsIME 应答不完整，不能只信它一个。
    /// </summary>
    private static bool IsChineseImeLayout(IntPtr hkl) =>
        hkl != IntPtr.Zero
        && (hkl.ToInt64() >> 16 != 0 || NativeMethods.ImmIsIME(hkl))
        && (hkl.ToInt64() & 0x3FF) == 0x0004;

    public void Dispose() => _poll.Stop();
}

/// <summary>
/// 焦点切换驱动的输入状态监听：对应系统的「允许我为每个应用窗口使用不同输入法」。
/// </summary>
/// <remarks>
/// 输入语言没有系统级通知，<see cref="InputStateWatcher"/> 只能靠"切换按键"唤醒观察窗。
/// 开启每窗口输入法后，语言是在窗口焦点切换时被系统静默恢复的——没有任何按键事件，
/// 观察窗根本不启动，切窗口后的语言变化就不弹提示（issue #12）。这里的补法：前台切换
/// 事件（SetWinEventHook，用法与 <see cref="KeyStateWatcher"/> 相同）唤醒一个更短的观察
/// 窗，采样新前台的输入状态，与切换前最后一次读到的状态对比，真的变了才弹卡。系统没开
/// 每窗口输入法时，切窗口不改变输入状态，这里自然安静；开了之后语言或中英文模式恢复成
/// 另一个窗口的样子，才响一声。
/// </remarks>
internal sealed class ForegroundWatcher : IDisposable
{
    private const int PollMs = 50;

    /// <summary>焦点观察窗比按键的 2 秒短：每窗口恢复发生在切换落地后的头几拍。</summary>
    private const int WindowMs = 1000;

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(PollMs) };

    private readonly NativeMethods.WinEventProc _proc;

    private IntPtr _hook;

    /// <summary>最近一次读到的可用输入状态。按键监听（Sampled）与焦点观察都会写入。</summary>
    /// <remarks>
    /// 按键基线来自钩子线程，这里在 UI 线程读——结构体可能被撕裂读到。最坏结果是一次
    /// 误报或漏报，与 InputStateWatcher.Touch 跨线程写 _last 的既有做法同级，不值得
    /// 为它上锁。
    /// </remarks>
    private InputState _observed;
    private bool _hasObserved;

    private InputState _base;
    private bool _hasBase;
    private int _ticks;

    /// <summary>焦点切换让输入状态变了。第一个参数是新前台的完整状态，第二个是切换前的基准。</summary>
    public event Action<InputState, InputState>? InputStateRestored;

    public ForegroundWatcher()
    {
        // 委托必须一直持有引用：SetWinEventHook 只认函数指针，回收了就是野指针。
        _proc = OnForeground;
        _poll.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        Diag.Write($"focus watcher start, winevent=0x{_hook:X}");
    }

    /// <summary>按键监听每读到一份可用状态就喂进来，保住基准的新鲜度。</summary>
    /// <remarks>调用方可能在钩子线程上（按键基线），所以只写字段、不碰定时器。</remarks>
    public void Observe(InputState state)
    {
        _observed = state;
        _hasObserved = true;
    }

    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (!AppSettings.Instance.ShowLanguageTip || !AppSettings.Instance.ShowLanguageOnWindowSwitch)
            return;

        // 基准必须早于切换：此刻旧窗口已经让出前台，想读也读不到了。好在语言只会因为
        // 按键（按键监听在喂 Observe）或焦点切换（这里自己采）而变，_observed 不会陈旧；
        // 鼠标点语言栏切输入法是两边都覆盖不到的既有盲区，基准因此旧掉时最多白等一场。
        _base = _observed;
        _hasBase = _hasObserved;
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

        // 读不到就等下一拍：全屏切换、桌面重组中前台会短暂为空（同按键监听的做法）。
        if (!now.IsUsable) return;

        _observed = now;
        _hasObserved = true;

        if (!_hasBase)
        {
            // 没有基准（启动后的第一次切换、或此前一直读不到状态）：立为新基准，不报。
            _base = now;
            _hasBase = true;
            return;
        }

        // 没变就继续等到观察窗结束：每窗口输入法的恢复可能晚几拍才落地，提前收手会
        // 漏掉"第一拍还是旧状态、第二拍才恢复"的切换。
        if (!Differs(now, _base)) return;

        _poll.Stop();
        InputStateRestored?.Invoke(now, _base);
    }

    private static bool Differs(InputState a, InputState b) =>
        a.Hkl != b.Hkl || (a.HasIme && b.HasIme && a.IsNative != b.IsNative);

    public void Dispose()
    {
        _poll.Stop();
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
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
