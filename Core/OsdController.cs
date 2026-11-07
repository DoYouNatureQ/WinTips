using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

internal sealed class OsdController
{
    private readonly DispatcherTimer _verify = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TipKind _verifyKind;
    private bool _verifyState;
    private bool _verifyPending;

    private readonly DispatcherTimer _clipPending = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private TipKind _clipPendingKind;

    private const int ClipMergeMs = 250;
    private long _lastShowTick;

    private UI.OsdWindow? _window;

    public OsdController()
    {
        _verify.Tick += VerifyTick;
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

    public void OnToggleChanged(TipKind kind)
    {
        if (!IsEnabled(kind)) return;
        bool newOn = (NativeMethods.GetKeyState(TipInfo.VirtualKey(kind)) & 1) == 0;
        Show(kind, newOn);
        _verifyKind = kind;
        _verifyState = newOn;
        _verifyPending = true;
        _verify.Stop();
        _verify.Start();
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

    private void VerifyTick(object? sender, EventArgs e)
    {
        _verify.Stop();
        if (!_verifyPending) return;
        _verifyPending = false;
        bool actual = (NativeMethods.GetKeyState(TipInfo.VirtualKey(_verifyKind)) & 1) != 0;
        if (actual != _verifyState) _window?.UpdateState(_verifyKind, actual);
    }

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

    private void ShowLanguageTip(string text)
    {
        _lastShowTick = Environment.TickCount64;
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
