using System.Windows.Interop;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

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
            _added = false;
            ReleaseIcon();
            AddIcon();
            return IntPtr.Zero;
        }

        if ((uint)msg != WM_TRAYICON) return IntPtr.Zero;

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

    /// <summary>
    /// 命令行 <c>--test menu</c> 用：和托盘右键走同一条路径（落点、玻璃快照、主题都一致）。
    /// </summary>
    internal void ShowMenuAtCursor() => ShowMenu();

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

                if (result != StartupChangeResult.Ok) ShowStartupFailure(result);
            },
            StartupManager.IsEnabled(),
            _showTest);
        menu.ShowMenuAtCursor();
    }

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
