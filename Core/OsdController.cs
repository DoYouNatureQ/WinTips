using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>统一管理 OSD 的显示、状态自校验与设置过滤。</summary>
internal sealed class OsdController
{
    private readonly DispatcherTimer _verify = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TipKind _verifyKind;
    private bool _verifyState;
    private bool _verifyPending;
    private UI.OsdWindow? _window;

    public OsdController() => _verify.Tick += VerifyTick;

    private UI.OsdWindow Window => _window ??= new UI.OsdWindow();

    /// <summary>启动时预热浮窗，消除首次提示的创建迟滞。</summary>
    public void PreWarm() => Window.PreWarm();

    /// <summary>Caps/Num/Scroll Lock 按下：钩子回调里系统尚未应用翻转，
    /// 因此新状态取“当前状态取反”；250ms 后用 GetKeyState 复核并纠正。</summary>
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

    private void VerifyTick(object? sender, EventArgs e)
    {
        _verify.Stop();
        if (!_verifyPending) return;
        _verifyPending = false;
        bool actual = (NativeMethods.GetKeyState(TipInfo.VirtualKey(_verifyKind)) & 1) != 0;
        if (actual != _verifyState) _window?.UpdateState(_verifyKind, actual);
    }

    private void Show(TipKind kind, bool? state) =>
        Window.ShowTip(kind, state, AppSettings.Instance.DisplaySeconds);

    public void ShowTest(TipKind kind, bool? state, double? hold = null) =>
        Window.ShowTip(kind, state, hold ?? AppSettings.Instance.DisplaySeconds);

    public static bool IsEnabled(TipKind k) => k switch
    {
        TipKind.CapsLock => AppSettings.Instance.ShowCapsLock,
        TipKind.NumLock => AppSettings.Instance.ShowNumLock,
        TipKind.ScrollLock => AppSettings.Instance.ShowScrollLock,
        _ => AppSettings.Instance.ShowInsert,
    };
}
