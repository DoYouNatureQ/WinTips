using System.Windows;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 提示浮窗的落点显示器（DIP）：屏幕矩形已按该屏的有效 DPI 换算成 DIP，
/// 也就是 WPF 的窗口坐标（Left/Top 乘以该屏缩放即物理像素）。
/// 可用区域（扣掉任务栏）不缓存，每次现查——任务栏随时会滑出/收起。
/// </summary>
/// <param name="Handle">HMONITOR；拿不到时为 <see cref="IntPtr.Zero"/>（此时按整块屏幕算）。</param>
/// <param name="Scale">该屏有效 DPI 缩放（物理像素 → DIP）。</param>
internal readonly record struct MonitorArea(
    IntPtr Handle, double Scale, double Left, double Top, double Right, double Bottom)
{
    public bool IsValid => Right > Left && Bottom > Top;

    /// <summary>
    /// 句柄此刻仍然是一块真实存在的显示器。显示配置一变（拔线、改分辨率、改排列）
    /// 旧的 HMONITOR 就会失效，拿它算出来的矩形是"已经不存在的屏幕"，浮窗会被落到屏外。
    /// </summary>
    public bool IsLive => Handle != IntPtr.Zero && TaskbarInfo.GetMonitor(Handle).HasValue;

    public double Width => Right - Left;

    public double Height => Bottom - Top;

    /// <summary>显示器横向中点（DIP），用于“居中”预设与指示条上下镜像的判定。</summary>
    public double CenterX => (Left + Right) / 2;

    /// <summary>显示器纵向中点（DIP）。</summary>
    public double CenterY => (Top + Bottom) / 2;

    /// <summary>
    /// 可用区域（DIP）：显示器矩形扣掉任务栏此刻实际占用的那条边
    /// （见 <see cref="TaskbarInfo.UsableRect"/>）。任务栏收起时几乎等于整块屏幕，
    /// 滑出后自动让位。读不到任务栏/显示器信息时退回整块屏幕。
    /// </summary>
    public (double Left, double Top, double Right, double Bottom) Usable()
    {
        double scale = Scale > 0 ? Scale : 1.0;
        if (TaskbarInfo.UsableRect(Handle) is { } r)
            return (r.Left / scale, r.Top / scale, r.Right / scale, r.Bottom / scale);
        return (Left, Top, Right, Bottom);
    }

    /// <summary>主显示器。</summary>
    public static MonitorArea Primary()
    {
        if (FromHandle(TaskbarInfo.PrimaryMonitor()) is { } area) return area;

        // 连显示器信息都拿不到（会话初始化早期等）：退回系统给的主屏尺寸。
        // SystemParameters 给的已经是 DIP，所以这里的 scale 取 1 是正确的。
        return new MonitorArea(IntPtr.Zero, 1,
            0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    /// <summary>
    /// 焦点窗口所在的显示器：用户在哪个屏上操作，提示就落在哪个屏。
    /// 采信顺序：前台窗口真正所在的那块屏（见 <see cref="ForegroundArea"/>）→ 鼠标所在屏 → 主屏。
    /// 鼠标那一步不是死代码：前台是桌面、跨屏窗口、最小化窗口或本进程自己的窗口时，
    /// 前三者会被判为"说不清在哪个屏"，后者说明用户在操作设置/托盘菜单，都该看光标在哪。
    /// </summary>
    public static MonitorArea Focused() =>
        ForegroundArea() ?? CursorArea() ?? Primary();

    /// <summary>
    /// 前台窗口真正所在的那块屏。只有当窗口面积的 90% 以上落在它被判给的那块屏上才采信：
    /// 桌面（Progman/WorkerW）、跨屏最大化、全屏视频这类铺满整个虚拟桌面的窗口，
    /// MonitorFromWindow 会按"最大重叠面积"把它判给面积最大的那块屏，与用户正在看的屏无关；
    /// 最小化窗口的矩形在 -32000，也过不了这个检查。前台是本进程自己的窗口时同样不采信。
    /// </summary>
    private static MonitorArea? ForegroundArea()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        // 本进程的窗口（设置页、托盘菜单、位置拾取器）：提示该跟着光标走，而不是跟着自己
        if (NativeMethods.GetWindowThreadProcessId(foreground, out uint pid) != 0 &&
            pid == (uint)Environment.ProcessId)
            return null;

        if (!NativeMethods.GetWindowRect(foreground, out var rect)) return null;

        var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (TaskbarInfo.GetMonitor(monitor) is not { } bounds) return null;
        if (!MostlyInside(rect, bounds.Bounds)) return null;

        return FromHandle(monitor);
    }

    /// <summary>窗口面积的 90% 是否落在该显示器内（跨屏窗口、最小化窗口都不满足）。</summary>
    private static bool MostlyInside(NativeMethods.RECT window, NativeMethods.RECT screen)
    {
        long w = (long)window.Right - window.Left;
        long h = (long)window.Bottom - window.Top;
        if (w <= 0 || h <= 0) return false;

        long iw = Math.Min(window.Right, screen.Right) - Math.Max(window.Left, screen.Left);
        long ih = Math.Min(window.Bottom, screen.Bottom) - Math.Max(window.Top, screen.Top);
        if (iw <= 0 || ih <= 0) return false;

        return iw * ih * 10 >= w * h * 9;
    }

    /// <summary>鼠标所在显示器：用户按锁键/复制时手就在那儿。</summary>
    private static MonitorArea? CursorArea()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return null;
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return FromHandle(monitor);
    }

    /// <summary>按显示器句柄构造；句柄无效返回 null。</summary>
    public static MonitorArea? FromHandle(IntPtr handle)
    {
        var mon = TaskbarInfo.GetMonitor(handle);
        if (!mon.HasValue) return null;

        double scale = ScaleFor(mon.Value.Handle);
        var b = mon.Value.Bounds;
        return new MonitorArea(mon.Value.Handle, scale,
            b.Left / scale, b.Top / scale, b.Right / scale, b.Bottom / scale);
    }

    /// <summary>
    /// 指定显示器的有效 DPI 缩放（物理像素 → DIP）。
    /// 查不到这块屏（句柄失效、shcore 不可用）时退到系统 DPI 而不是 1.0：
    /// 之前直接返回 1.0，在 150% 的屏上等于把整条换算链按 100% 走，浮窗会整体偏三分之一屏。
    /// </summary>
    public static double ScaleFor(IntPtr handle)
    {
        try
        {
            if (handle != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                dpiX > 0)
                return dpiX / 96.0;
        }
        catch { }

        try
        {
            uint dpi = NativeMethods.GetDpiForSystem();
            if (dpi > 0) return dpi / 96.0;
        }
        catch { }

        return 1.0;
    }
}
