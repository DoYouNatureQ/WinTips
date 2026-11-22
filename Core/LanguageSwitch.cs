using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

internal readonly record struct InputState(uint ThreadId, uint ProcessId, IntPtr Hkl, IntPtr ImeWnd, int ConversionMode, int OpenStatus)
{
    public bool IsUsable => Hkl != IntPtr.Zero;

    public bool HasIme => ImeWnd != IntPtr.Zero && ConversionMode >= 0;

    /// <summary>
    /// 是否处于中文（_native_）输入模式。中英判定不能只看转换模式：微信输入法这类纯
    /// TSF 输入法切中英文时 IMC_GETCONVERSIONMODE 恒定不变（issue #14），状态改在
    /// IMC_GETOPENSTATUS 上精确翻转（中文=1、英文=0）。复合判定：
    /// - 开启状态读不到（无 IME 窗口、消息超时）→ 退回只看转换模式的老判定；
    /// - 读得到 → 关闭即英文；开着再看转换模式的 NATIVE 位。
    /// 微软拼音（开启状态恒 1、转换模式 0↔1）照旧走 NATIVE 位；微信输入法（转换
    /// 模式恒 1、开启状态 0↔1）走开启状态。两个应答习惯相反的输入法都判定正确。
    /// 注意不能照抄"转换模式为 0 就信开启状态"的兜底：微软拼音的英文态恰是
    /// cmode=0 且开启状态恒 1，那样会把它的英文态误判成中文。
    /// </summary>
    public bool IsNative =>
        OpenStatus < 0
            ? (ConversionMode & NativeMethods.IME_CMODE_NATIVE) != 0
            : OpenStatus > 0 && (ConversionMode & NativeMethods.IME_CMODE_NATIVE) != 0;

    /// <summary>
    /// 诊断用的一行状态：HKL、线程、IME 窗口、转换模式与开启状态读数。排查"读数丢失"
    /// 类问题（issue #13、#14）时，靠它区分窗口不存在、消息超时和线程/HKL 变化。
    /// </summary>
    public string Describe() =>
        $"hkl=0x{Hkl.ToInt64():X8} tid={ThreadId} ime=0x{ImeWnd.ToInt64():X} cmode={ConversionMode} open={OpenStatus}";

    public static InputState Current()
    {
        IntPtr hwnd = FocusWindow();
        if (hwnd == IntPtr.Zero) return default;

        uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (tid == 0) return default;

        IntPtr hkl = NativeMethods.GetKeyboardLayout(tid);
        if (hkl == IntPtr.Zero) return default;

        IntPtr ime = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
        if (ime == IntPtr.Zero) return new InputState(tid, pid, hkl, IntPtr.Zero, -1, -1);
        return new InputState(tid, pid, hkl, ime, ReadConversionMode(ime), ReadOpenStatus(ime));
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

    /// <summary>
    /// 读取输入法开启状态。默认 IME 窗口自己应答这条消息，所以应答习惯稳定的输入法
    /// （微软拼音恒 1）和不把中英状态同步进转换模式的输入法（微信输入法 0↔1）都能
    /// 读到值；读不到记 -1，判定退回只看转换模式。
    /// </summary>
    private static int ReadOpenStatus(IntPtr imeWnd)
    {
        try
        {
            IntPtr ok = NativeMethods.SendMessageTimeoutW(imeWnd, NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_GETOPENSTATUS, IntPtr.Zero,
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

    /// <summary>
    /// 系统读数是否被观察到真的跟随过一次切换。被证实过的读数值得信任：
    /// 它沉默就是"没有变化"，Shift 手势不再去顶它。但"证实"会过期——搜狗这类
    /// 输入法的同步是半可靠的：有的版本同步、有的不同步，同一台输入法也可能在
    /// 会话中途改藏状态。连续静默超时后由 <see cref="_readingDistrusted"/> 接管
    /// （见观察窗超时分支），读数重新被观察到跟随切换（系统路径报一次）时恢复。
    /// </summary>
    private bool _readMoved;

    /// <summary>
    /// 带手势的观察窗静默超时多少个之后，把曾被证实过的读数降级为不可信。
    /// 压制逻辑的前提是"读数沉默 = 没变化"，这个前提在半同步的输入法（搜狗）
    /// 身上会过期：读数藏起来之后，每次 Shift 切换都被压到观察窗超时、整次
    /// 吞掉——正是用户报的"提示经常不弹"。两个整窗（各 2 秒）的沉默足以证明
    /// 读数不再跟随手势，此后改回推断主导。
    /// </summary>
    private const int SilentWindowsBeforeDistrust = 2;

    /// <summary>带 Shift 手势的观察窗静默超时的连续计数。被系统路径的成功上报清零。</summary>
    private int _silentWindows;

    /// <summary>
    /// 读数已降级：不再用它的沉默压制 Shift 手势，改回推断主导。只有读数重新被
    /// 观察到跟随切换（系统路径报了一次）才解除——手势上报证明不了读数活着。
    /// </summary>
    private bool _readingDistrusted;

    /// <summary>本次观察窗内"读数丢失"是否已经写过诊断日志，避免每拍刷屏。</summary>
    private bool _loggedReadLost;

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

        // 两个早退点必须留痕：用户报"按了 Shift 不弹"时，先要分清手势是根本没送
        // 到这里（钩子层问题），还是在这两道门槛上被丢的。之前这里静默返回，现场
        // 完全无法排查。
        if (!now.IsUsable)
        {
            Diag.Write("solo shift hint dropped: foreground state unreadable");
            return;
        }

        // 推断只对中文输入法生效：美式键盘按 Shift 是正常输入不是切换；日文等其他
        // IME 的 Shift 语义也不同，翻了只会报错"中文/英文"。
        if (!IsChineseImeLayout(now.Hkl))
        {
            Diag.Write($"solo shift hint dropped: not a Chinese IME layout, hkl=0x{now.Hkl.ToInt64():X8}");
            return;
        }

        _hintCount++;

        // 不在此处无条件重采样基准（issue #16）：微信输入法等在 Shift 抬起瞬间即翻转
        // 状态，此刻采样会把刚发生的切换刷进基准，轮询从此读数恒等于基准、永远报不出
        // 变化——Edge/Electron 等前台线程翻转落点早于本回调的场景 100% 静默超时。
        // 基准应保持 Shift 按下时 SwitchKeyDown 采样的翻转前状态，抬起翻转后轮询才能
        // 对比出差异，由系统路径稳定上报。观察窗已在跑就保留旧基准，对齐 Windhawk 版
        // StartInputWatch 的做法；仅当按下时没采到可用基准（前台那一刻读不到）才在此
        // 补采——此刻多半已是翻转后状态，但总比没有基准强。
        if (!_hasLast)
        {
            Touch(now);
        }
        else
        {
            Sampled?.Invoke(now);
            _ticks = 0;
            if (!_poll.IsEnabled) _poll.Start();
        }

        _hintAt = EventTime.Now;

        Diag.Write($"solo shift hint #{_hintCount}");
    }

    private void Poll()
    {
        if (++_ticks * PollMs > WindowMs)
        {
            // 带着待处理的 Shift 超时说明这一轮什么都报不出来——issue #13 的
            // 静默丢失。留一行诊断，让人能看出超时那一刻的读数状态。
            if (_hintCount > 0)
            {
                Diag.Write($"watch window timed out, drop {_hintCount} shift hint(s), last[{_last.Describe()}]");

                // 连续多个带手势的窗口都静默超时，而读数曾被证实过跟随切换——说明
                // 这台输入法又把状态藏起来了（搜狗的同步是半可靠的）。降级它，别让
                // 压制逻辑把之后的每次切换都吞掉。
                _silentWindows++;
                if (_readMoved && !_readingDistrusted && _silentWindows >= SilentWindowsBeforeDistrust)
                {
                    _readingDistrusted = true;
                    Diag.Write($"reading silent across {_silentWindows} shift windows, distrust it: gesture inference takes over");
                }
            }
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
                    if (_readMoved && !_readingDistrusted)
                    {
                        // 读数被证实跟随过切换、也没被降级：沉默就是没有变化，
                        // Shift 手势不值得去顶——这类输入法切中英文可能根本不用
                        // Shift，报了反而错。但"证实"会过期：搜狗的同步是半可靠的，
                        // 连续静默超时后超时分支会把它降级，这里就不再压制。
                        return true;
                    }

                    // 读数活着但从未被观察到动过（或已被降级）：这台输入法把状态
                    // 藏着（搜狗、微信输入法、小狼毫都有版本如此）。推断缓存保持
                    // 不动——用一条不可能变化的死读数播种它，会在下一拍就抵消掉刚
                    // 推断出的翻面，把卡片钉死在错误标签上。Shift 手势接手。
                    return InferTick();
                }

                bool native = now.IsNative;
                _last = now;
                _inferredNative = native;
                _readMoved = true;
                // 读数真的动了：重新证实，信任与计数一并恢复。
                _silentWindows = 0;
                _readingDistrusted = false;
                StopWindow();
                Diag.Write($"ime mode change (system) native={native}");
                ImeModeChanged?.Invoke(native);
                return true;
            }

            // 基准在按下那一刻没读出输入法状态（线程忙、窗口正建）。读数被证实过、
            // 且与推断缓存不符，说明切换就发生在读不到的那段——按读数报。读数没被
            // 证实过时不能当变化：说不定它天生就只会应这一个值。
            if (_hintCount > 0 && _readMoved && now.IsNative != _inferredNative)
            {
                bool native = now.IsNative;
                _last = now;
                _inferredNative = native;
                _silentWindows = 0;
                _readingDistrusted = false;
                StopWindow();
                Diag.Write($"ime mode change (system, late read) native={native}");
                ImeModeChanged?.Invoke(native);
                return true;
            }

            if (_readMoved)
            {
                _last = now;
                _inferredNative = now.IsNative;
            }
            return InferTick();
        }

        if (_last.HasIme)
        {
            // 基准里有读数、这一拍没读出来——多半是目标线程正忙（游戏加载、读盘），
            // 40ms 的同步消息超时了。基准既然读出过，说明这台输入法会应答，等后面
            // 几拍恢复就行；这时候推断没有依据，不动基准也不翻面。
            //
            // 但不能无限等：issue #13 就卡在这里。个别纯 TSF 输入法（微信输入法）
            // 按 Shift 那一拍还答得上，之后连续读不到，死等满 2 秒观察窗后整个
            // 静默收场，这次切换就丢了。读数没被证实过、或已被降级（搜狗半同步
            // 的"证实"过期之后）时，有待处理的 Shift 且宽限过完仍读不到，就按
            // 按键推断翻面——宁可按手势报，也不把用户的切换整次吞掉。仍被信任
            // 的读数继续等：它可靠，丢失多半是暂时的，翻错比多等一拍代价大。
            if (!_loggedReadLost)
            {
                _loggedReadLost = true;
                Diag.Write($"mode read lost: last[{_last.Describe()}] now[{now.Describe()}] hint={_hintCount} provenRead={_readMoved} distrusted={_readingDistrusted}");
            }

            if (_hintCount > 0 && (!_readMoved || _readingDistrusted)
                && _hintAt >= 0 && EventTime.Now - _hintAt > InferGraceMs)
            {
                Diag.Write($"mode read not restored {EventTime.Now - _hintAt}ms after shift, infer fallback");
                return InferFlip();
            }

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
        _loggedReadLost = false;
        // _readMoved / _readingDistrusted / _silentWindows 故意不清：信任与降级都
        // 跨观察窗保留。读数重新被观察到跟随切换（系统路径上报）才恢复信任；
        // 手势上报证明不了读数活着，不动降级标记。
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
///
/// 一个例外要主动压掉：点任务栏的输入指示器选语言。浮层抢一下前台再还给原窗口，本来
/// 会被当成一次"输入状态恢复"弹卡——但那是用户亲手用鼠标选的，结果看得见，不该再多
/// 提示一遍。识别方式：壳层表面（任务栏、TextInputHost 浮层）占用前台后短时间内回到
/// 原窗口，视为任务栏交互，只重新采样不报变化。
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

    /// <summary>
    /// 壳层表面（任务栏、输入指示器浮层）最近一次占用前台的时刻，-1 表示不在壳层流程里。
    /// </summary>
    private long _shellAt = -1;

    /// <summary>最近一个非壳层的前台窗口。任务是栏交互结束时前台会还给它，靠它认出"回来了"。</summary>
    private IntPtr _appHwnd;

    /// <summary>
    /// 离开壳层表面后多久之内回到原窗口，仍算同一次任务栏交互。浮层是轻Dismiss关闭的，
    /// 关闭、前台归还、语言落地之间隔着几十到几百毫秒，留足余量；超过它就是用户真的
    /// 先点开浮层、又自己点去了别的程序，按正常前台切换处理。
    /// </summary>
    private const long ShellReturnMs = 1500;

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

        // 任务栏/输入指示器浮层自己抢了前台：记下时刻，等它把前台还给原窗口。
        // 浮层本身不参与语言提示——在它上面读到的输入状态是浮层进程自己的，没有意义。
        if (IsShellSurface(hwnd))
        {
            _shellAt = EventTime.Now;
            return;
        }

        // 离开浮层后短时间内回到原窗口：用户刚用鼠标亲手选完语言。结果就摆在
        // 屏幕上，用户肯定看到了，不再多提示一遍（产品决策：任务栏点击切换语言
        // 一律不弹卡）。清空基准让这一轮观察窗只重新采样、不报变化。
        bool taskbarReturn = _shellAt >= 0
            && EventTime.Now - _shellAt < ShellReturnMs
            && hwnd == _appHwnd;
        _shellAt = -1;
        _appHwnd = hwnd;

        if (taskbarReturn)
        {
            Diag.Write("taskbar language switch, suppress restore tip");
            _base = _observed;
            _hasBase = false;
            _ticks = 0;
            if (!_poll.IsEnabled) _poll.Start();
            return;
        }

        // 基准必须早于切换：此刻旧窗口已经让出前台，想读也读不到了。好在语言只会因为
        // 按键（按键监听在喂 Observe）或焦点切换（这里自己采）而变，_observed 不会陈旧。
        _base = _observed;
        _hasBase = _hasObserved;
        _ticks = 0;
        if (!_poll.IsEnabled) _poll.Start();
    }

    /// <summary>
    /// 前台窗口是不是壳层表面：任务栏（Shell_TrayWnd，含副屏任务栏）或输入指示器/
    /// 触摸键盘浮层（TextInputHost.exe 托管的 CoreWindow）。开始菜单、搜索、操作中心
    /// 虽然同为 CoreWindow 宿主，但进程不同，不算——它们打开再关闭不影响语言，
    /// 混进来反而会吞掉紧随其后的正常恢复提示。
    /// </summary>
    private static bool IsShellSurface(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        try
        {
            var cls = new System.Text.StringBuilder(64);
            if (NativeMethods.GetClassNameW(hwnd, cls, cls.Capacity) <= 0) return false;

            switch (cls.ToString())
            {
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                    return true;
                case "Windows.UI.Core.CoreWindow":
                    return IsProcess(hwnd, "TextInputHost.exe");
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcess(IntPtr hwnd, string name)
    {
        try
        {
            uint pid = NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;

            IntPtr proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (proc == IntPtr.Zero) return false;
            try
            {
                var path = new System.Text.StringBuilder(512);
                uint size = (uint)path.Capacity;
                if (!NativeMethods.QueryFullProcessImageNameW(proc, 0, path, ref size)) return false;
                return string.Equals(System.IO.Path.GetFileName(path.ToString(0, (int)size)),
                    name, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                NativeMethods.CloseHandle(proc);
            }
        }
        catch
        {
            return false;
        }
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
