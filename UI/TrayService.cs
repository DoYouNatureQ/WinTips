using System.Windows.Interop;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 托盘图标：原生 Shell_NotifyIcon 实现。
///
/// 取代原来的 WinForms NotifyIcon —— 这一处替换把 System.Windows.Forms（13.1MB）、
/// Forms.Primitives（3.4MB）、System.Drawing 与 GDI+ 从常驻内存里整体去掉，只为一个图标。
///
/// 托盘消息需要一个真实窗口承载：这里用一个不可见的顶层窗口（WS_POPUP，不带 WS_VISIBLE）
/// 而不是 message-only 窗口，因为 Explorer 重启后的 <c>TaskbarCreated</c> 是广播消息，
/// message-only 窗口收不到，托盘图标就会一去不回。
/// </summary>
internal sealed class TrayService : IDisposable
{
    private const uint TrayIconId = 1;
    private const uint WM_TRAYICON = NativeMethods.WM_APP + 1;

    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly Action<TipKind, bool?> _showTest;

    private readonly HwndSource _src;
    private readonly HwndSourceHook _hook;
    private readonly uint _taskbarCreated;

    private IntPtr _icon;
    private bool _iconOwned;
    private bool _added;

    public TrayService(Action openSettings, Action exit, Action<TipKind, bool?> showTest)
    {
        _openSettings = openSettings;
        _exit = exit;
        _showTest = showTest;

        var p = new HwndSourceParameters("WinTipsTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
        };
        _src = new HwndSource(p);
        _hook = WndProc;
        _src.AddHook(_hook);

        _taskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        AddIcon();
    }

    /// <summary>托盘悬停提示：应用名 + 版本号。szTip 上限 127 字符，超长会被截断保护。</summary>
    private static string TooltipText
    {
        get
        {
            string text = $"WinTips {AppInfo.DisplayVersion}";
            return text.Length <= 63 ? text : text[..63];
        }
    }

    private NativeMethods.NOTIFYICONDATAW MakeData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _src.Handle,
        uID = TrayIconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>添加图标并切到 V4 行为（回调更可靠，lParam 直接给出是哪种鼠标消息）。</summary>
    private void AddIcon()
    {
        (_icon, _iconOwned) = AppIcons.TrayIcon();

        var data = MakeData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        data.uCallbackMessage = WM_TRAYICON;
        data.hIcon = _icon;
        data.szTip = TooltipText;

        _added = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data);
        if (!_added) return;

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            // Explorer 重启：旧图标随任务栏一起消失，必须重新注册
            _added = false;
            ReleaseIcon();
            AddIcon();
            return IntPtr.Zero;
        }

        if ((uint)msg != WM_TRAYICON) return IntPtr.Zero;

        // V4：lParam 低字是鼠标消息；键盘选中托盘图标时给的是 WM_CONTEXTMENU
        uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
        switch (evt)
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
                _openSettings();
                break;
            case NativeMethods.WM_CONTEXTMENU:
            case NativeMethods.WM_RBUTTONUP:
                ShowMenu();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = new TrayMenuWindow(
            _openSettings,
            _exit,
            enable =>
            {
                var result = StartupManager.SetEnabled(enable);
                AppSettings.Instance.LaunchAtStartup = StartupManager.IsEnabled();
                AppSettings.Instance.Save();

                // 菜单已经关掉了，看不出开关有没有真的生效，必须明确告知
                if (result != StartupChangeResult.Ok) ShowStartupFailure(result);
            },
            StartupManager.IsEnabled(),
            _showTest);
        menu.ShowMenuAtCursor();
    }

    /// <summary>
    /// 托盘菜单里切换自启失败时的提示。菜单点完即关，开关状态看不见了，
    /// 必须给一句明确的话；用户手动关过的情况下再多走一步，直接把他送到系统设置页。
    /// </summary>
    private static void ShowStartupFailure(StartupChangeResult result)
    {
        string key = result switch
        {
            StartupChangeResult.BlockedByUser => "StartupHintBlockedByUser",
            StartupChangeResult.BlockedByPolicy => "StartupHintBlockedByPolicy",
            _ => "StartupHintFailed",
        };

        try { new NoticeWindow(Loc.T("RowStartup"), Loc.T(key)).ShowDialog(); }
        catch { }

        if (result == StartupChangeResult.BlockedByUser) StartupManager.OpenSystemStartupSettings();
    }

    private void ReleaseIcon()
    {
        if (_icon != IntPtr.Zero && _iconOwned) NativeMethods.DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _iconOwned = false;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = MakeData();
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        ReleaseIcon();

        try
        {
            _src.RemoveHook(_hook);
            _src.Dispose();
        }
        catch { }
    }
}
