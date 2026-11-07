using System.Windows;
using WinTips.Native;

namespace WinTips.UI;

internal readonly record struct MonitorArea(
    IntPtr Handle, double Scale, double Left, double Top, double Right, double Bottom)
{
    public bool IsValid => Right > Left && Bottom > Top;

    public bool IsLive => Handle != IntPtr.Zero && TaskbarInfo.GetMonitor(Handle).HasValue;

    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public double CenterX => (Left + Right) / 2;

    public double CenterY => (Top + Bottom) / 2;

    public (double Left, double Top, double Right, double Bottom) Usable()
    {
        double scale = Scale > 0 ? Scale : 1.0;
        if (TaskbarInfo.UsableRect(Handle) is { } r)
            return (r.Left / scale, r.Top / scale, r.Right / scale, r.Bottom / scale);
        return (Left, Top, Right, Bottom);
    }

    public static MonitorArea Primary()
    {
        if (FromHandle(TaskbarInfo.PrimaryMonitor()) is { } area) return area;

        return new MonitorArea(IntPtr.Zero, 1,
            0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    public static MonitorArea Focused() =>
        ForegroundArea() ?? CursorArea() ?? Primary();

    private static MonitorArea? ForegroundArea()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        if (NativeMethods.GetWindowThreadProcessId(foreground, out uint pid) != 0 &&
            pid == (uint)Environment.ProcessId)
            return null;

        if (!NativeMethods.GetWindowRect(foreground, out var rect)) return null;

        var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (TaskbarInfo.GetMonitor(monitor) is not { } bounds) return null;
        if (!MostlyInside(rect, bounds.Bounds)) return null;

        return FromHandle(monitor);
    }

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

    private static MonitorArea? CursorArea()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return null;
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return FromHandle(monitor);
    }

    public static MonitorArea? FromHandle(IntPtr handle)
    {
        var mon = TaskbarInfo.GetMonitor(handle);
        if (!mon.HasValue) return null;

        double scale = ScaleFor(mon.Value.Handle);
        var b = mon.Value.Bounds;
        return new MonitorArea(mon.Value.Handle, scale,
            b.Left / scale, b.Top / scale, b.Right / scale, b.Bottom / scale);
    }

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
