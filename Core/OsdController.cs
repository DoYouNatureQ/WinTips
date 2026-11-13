using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

internal sealed class OsdController
{
    private readonly DispatcherTimer _clipPending = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private TipKind _clipPendingKind;

    private const int ClipMergeMs = 250;
    private long _lastShowTick;

    private UI.OsdWindow? _window;

    public OsdController()
    {
        _clipPending.Tick += (_, _) => _clipPending.Stop();
    }

    private UI.OsdWindow Window
    {
        get
        {
            if (_window == null)
            {
                _window = new UI.OsdWindow();
                _window.ClipboardUpdated += OnClipboardUpdated;
            }
            return _window;
        }
    }

    public void PreWarm() => Window.PreWarm();

    /// <summary>
    /// 锁定键的显示入口。状态由 <see cref="LockState"/> 统一给出，这里不再自己去问系统。
    /// </summary>
    /// <remarks>
    /// 早先这里会再读一次 GetKeyState 并起个 250ms 的定时器"校验"：那是本线程消息队列的
    /// 镜像，前台是提权窗口时根本不更新，校验只会把正确状态又改回错的。现在状态来源只有
    /// 一个，也就没有需要校验的第二个答案了。
    /// </remarks>
    public void OnLockToggled(TipKind kind, bool on)
    {
        if (!IsEnabled(kind)) return;
        Show(kind, on);
    }

    public void OnInsertPressed(TipKind kind)
    {
        if (!IsEnabled(kind)) return;
        Show(kind, null);
    }

    public void OnClipboardPressed(TipKind kind)
    {
        if (!IsEnabled(kind)) return;
        if (kind == TipKind.Paste)
        {
            Show(kind, null);
            return;
        }
        _clipPendingKind = kind;
        _clipPending.Stop();
        _clipPending.Start();
    }

    private void OnClipboardUpdated()
    {
        if (_clipPending.IsEnabled)
        {
            _clipPending.Stop();
            Show(_clipPendingKind, null);
            return;
        }

        if (!AppSettings.Instance.ShowCopy || !AppSettings.Instance.ShowAnyCopy) return;
        if (!ClipboardHasContent()) return;
        if (Environment.TickCount64 - _lastShowTick < ClipMergeMs) return;
        Show(TipKind.Copy, null);
    }

    private static bool ClipboardHasContent() =>
        NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT) ||
        NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP) ||
        NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_BITMAP) ||
        NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB) ||
        NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_ENHMETAFILE) ||
        NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_METAFILEPICT);

    private void Show(TipKind kind, bool? state)
    {
        _lastShowTick = Environment.TickCount64;
        Window.ShowTip(kind, state, AppSettings.Instance.DisplaySeconds);
    }

    public void ShowTest(TipKind kind, bool? state, double? hold = null)
    {
        double seconds = hold ?? AppSettings.Instance.DisplaySeconds;
        if (kind == TipKind.Language) Window.ShowLanguage(InputTipText.For(InputState.Current()), seconds);
        else Window.ShowTip(kind, state, seconds);
    }

    public void ShowWelcome(double? hold = null)
    {
        _lastShowTick = Environment.TickCount64;
        Window.ShowWelcome(Loc.T("WelcomeTitle"), Loc.T("WelcomeSubtitle"), hold ?? WelcomeSeconds);
    }

    private const double WelcomeSeconds = 5;

    public void OnLanguageChanged(IntPtr hkl)
    {
        if (!IsEnabled(TipKind.Language)) return;
        ShowLanguageTip(LanguageNames.Display(hkl));
    }

    public void OnImeModeChanged(bool native)
    {
        if (!IsEnabled(TipKind.Language)) return;
        ShowLanguageTip(Loc.T(native ? "ImeChinese" : "ImeEnglish"));
    }

    /// <summary>
    /// 焦点切换带来的输入状态变化（对应系统的「允许我为每个应用窗口使用不同输入法」）。
    /// 文案与按键路径完全一致：语言变了报语言名，只有中英文模式变了报模式——这样
    /// 两条路径对同一次变化弹出的卡逐字相同，<see cref="ShowLanguageTip"/> 的去重
    /// 才认得出来。
    /// </summary>
    public void OnInputStateRestored(InputState now, InputState before)
    {
        if (!IsEnabled(TipKind.Language)) return;
        if (now.Hkl != before.Hkl)
        {
            ShowLanguageTip(LanguageNames.Display(now.Hkl));
        }
        else
        {
            ShowLanguageTip(Loc.T(now.IsNative ? "ImeChinese" : "ImeEnglish"));
        }
    }

    private string _lastLanguageText = string.Empty;
    private long _lastLanguageTick;

    private void ShowLanguageTip(string text)
    {
        long tick = Environment.TickCount64;
        // 按键监听和焦点监听可能对同一次变化各报一次（观察窗重叠、Win+Space 的
        // 输入指示器浮层也会送来前台事件），同一句话 700ms 内弹两次看起来像故障，
        // 挡掉。两条路径的文案格式刻意保持一致，这里才比得出"同一句话"。
        if (text == _lastLanguageText && tick - _lastLanguageTick < 700) return;
        _lastLanguageText = text;
        _lastLanguageTick = tick;
        _lastShowTick = tick;
        Window.ShowLanguage(text, AppSettings.Instance.DisplaySeconds);
    }

    public void PreviewSize(double seconds = 1.2) => Window.PreviewSize(seconds);

    public void PreviewAppearance(double seconds = 1.2) => Window.PreviewAppearance(seconds);

    public static bool IsEnabled(TipKind k) => k switch
    {
        TipKind.CapsLock => AppSettings.Instance.ShowCapsLock,
        TipKind.NumLock => AppSettings.Instance.ShowNumLock,
        TipKind.ScrollLock => AppSettings.Instance.ShowScrollLock,
        TipKind.Copy => AppSettings.Instance.ShowCopy,
        TipKind.Paste => AppSettings.Instance.ShowPaste,
        TipKind.Cut => AppSettings.Instance.ShowCut,
        TipKind.Language => AppSettings.Instance.ShowLanguageTip,
        _ => AppSettings.Instance.ShowInsert,
    };
}
