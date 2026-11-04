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

        // 连显示器信息都拿不到（会话初始化早期等）：退回系统给的主屏尺寸
        return new MonitorArea(IntPtr.Zero, 1,
            0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    /// <summary>
    /// 焦点窗口所在的显示器：用户在哪个屏上操作，提示就落在哪个屏。
    /// 没有前台窗口（桌面刚启动、前台是本进程的工具窗）时退回鼠标所在屏，再退回主屏。
    /// </summary>
    public static MonitorArea Focused()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (FromHandle(monitor) is { } focused) return focused;
        }

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (FromHandle(monitor) is { } underCursor) return underCursor;
        }

        return Primary();
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

    /// <summary>指定显示器的有效 DPI 缩放（物理像素 → DIP）。查询失败返回 1.0。</summary>
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
        return 1.0;
    }
}
