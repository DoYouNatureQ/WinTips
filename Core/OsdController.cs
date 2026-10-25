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

    /// <summary>复制/剪切的“意图窗口”：按键后等剪贴板真正更新才提示，超时静默。</summary>
    private readonly DispatcherTimer _clipPending = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private TipKind _clipPendingKind;

    /// <summary>
    /// 无按键意图更新与真实复制的区分：剪贴板管理器（Ditto 等）收到复制后常会把数据
    /// 读出再写回一次，产生紧随其后的第二次 WM_CLIPBOARDUPDATE。真实复制至少要
    /// 重新选中再操作，间隔远大于该窗口，故 250ms 内的连续更新合并为一次。
    /// </summary>
    private const int ClipMergeMs = 250;
    private long _lastShowTick;

    private UI.OsdWindow? _window;

    public OsdController()
    {
        _verify.Tick += VerifyTick;
        _clipPending.Tick += (_, _) => _clipPending.Stop();
    }

    /// <summary>创建浮窗并挂接剪贴板更新事件（PreWarm 启动时即会创建）。</summary>
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

    /// <summary>Ctrl+C / Ctrl+V / Ctrl+X 按下。粘贴无法确认结果，按键即提示；
    /// 复制/剪切等 WM_CLIPBOARDUPDATE 确认（终端里的 Ctrl+C 中断不会误弹）。</summary>
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

    /// <summary>
    /// 系统剪贴板更新：处于复制/剪切意图窗口内则按意图提示（终端里的 Ctrl+C 中断
    /// 不写剪贴板，天然不弹）；否则视为非键盘来源的复制（右键菜单、应用内“复制”
    /// 按钮等），按设置以“复制”提示。清空剪贴板（如密码管理器超时自动清除）同样
    /// 触发更新事件，须先排除。
    /// </summary>
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

    /// <summary>剪贴板当前是否还有内容：常用标准格式均不在（被清空）时视为无内容。</summary>
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
        // 任何提示弹出都记入合并窗口：既覆盖剪贴板管理器的回写，也覆盖粘贴后
        // 个别程序改写剪贴板导致的伪“复制”更新
        _lastShowTick = Environment.TickCount64;
        Window.ShowTip(kind, state, AppSettings.Instance.DisplaySeconds);
    }

    public void ShowTest(TipKind kind, bool? state, double? hold = null) =>
        Window.ShowTip(kind, state, hold ?? AppSettings.Instance.DisplaySeconds);

    public static bool IsEnabled(TipKind k) => k switch
    {
        TipKind.CapsLock => AppSettings.Instance.ShowCapsLock,
        TipKind.NumLock => AppSettings.Instance.ShowNumLock,
        TipKind.ScrollLock => AppSettings.Instance.ShowScrollLock,
        TipKind.Copy => AppSettings.Instance.ShowCopy,
        TipKind.Paste => AppSettings.Instance.ShowPaste,
        TipKind.Cut => AppSettings.Instance.ShowCut,
        _ => AppSettings.Instance.ShowInsert,
    };
}
