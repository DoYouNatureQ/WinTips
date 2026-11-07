using System.Runtime.InteropServices;

namespace WinTips.Native;

internal static class TaskbarInfo
{
    public readonly record struct Band(NativeMethods.RECT Rect, uint Edge);

    public readonly record struct MonitorBounds(IntPtr Handle, NativeMethods.RECT Bounds, NativeMethods.RECT Work);

    private static uint? _dockedEdge;

    public static IntPtr PrimaryMonitor()
    {
        try
        {
            return NativeMethods.MonitorFromPoint(new NativeMethods.POINT(), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static bool IsPrimary(IntPtr monitor) =>
        monitor != IntPtr.Zero && monitor == PrimaryMonitor();

    public static MonitorBounds? GetMonitor(IntPtr monitor)
    {
        try
        {
            if (monitor == IntPtr.Zero) return null;
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref mi)) return null;
            return new MonitorBounds(monitor, mi.rcMonitor, mi.rcWork);
        }
        catch
        {
            return null;
        }
    }

    public static MonitorBounds? GetPrimaryMonitor() => GetMonitor(PrimaryMonitor());

    public static bool IsAutoHide()
    {
        try
        {
            var state = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
            return (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref state).ToInt64() & NativeMethods.ABS_AUTOHIDE) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Band? SecondaryBand(NativeMethods.RECT bounds)
    {
        try
        {
            if (SecondaryTaskbarWindow(bounds) is not { } live) return null;
            if (ClipTo(live, bounds) is not { } clipped) return null;
            return new Band(clipped, NearestEdge(clipped, bounds));
        }
        catch
        {
            return null;
        }
    }

    private static NativeMethods.RECT? SecondaryTaskbarWindow(NativeMethods.RECT bounds)
    {
        NativeMethods.RECT best = default;
        long bestArea = 0;

        IntPtr hwnd = IntPtr.Zero;
        while ((hwnd = NativeMethods.FindWindowExW(IntPtr.Zero, hwnd,
                   NativeMethods.SecondaryTaskbarClassName, null)) != IntPtr.Zero)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var live)) continue;
            if (ClipTo(live, bounds) is not { } clipped) continue;
            long area = (long)Width(clipped) * Height(clipped);
            if (area <= bestArea) continue;
            bestArea = area;
            best = live;
        }

        return bestArea > 0 ? best : null;
    }

    public static Band? PrimaryBand()
    {
        try
        {
            var mon = GetPrimaryMonitor();
            if (!mon.HasValue) return null;
            var bounds = mon.Value.Bounds;

            var hwnd = NativeMethods.FindWindowW(NativeMethods.TaskbarClassName, null);
            NativeMethods.RECT live;
            var clipped = hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out live)
                ? ClipTo(live, bounds)
                : null;

            if (!clipped.HasValue) return DockedBand(bounds);

            uint edge = _dockedEdge ?? QueryEdge();
            if (edge != uint.MaxValue && !Fits(clipped.Value, bounds, edge))
            {
                _dockedEdge = null;
                edge = QueryEdge();
            }
            if (edge == uint.MaxValue || !Fits(clipped.Value, bounds, edge))
                edge = NearestEdge(clipped.Value, bounds);

            return new Band(clipped.Value, edge);
        }
        catch
        {
            return null;
        }
    }

    public static Band? BandFor(MonitorBounds monitor) =>
        IsPrimary(monitor.Handle) ? PrimaryBand() : SecondaryBand(monitor.Bounds);

    public static NativeMethods.RECT? UsableRect(IntPtr monitor)
    {
        var mon = GetMonitor(monitor);
        if (!mon.HasValue) return null;

        var m = mon.Value;
        var band = BandFor(m);
        if (!band.HasValue) return m.Work;

        var r = m.Bounds;
        var b = band.Value.Rect;
        switch (band.Value.Edge)
        {
            case NativeMethods.ABE_TOP: r.Top = Math.Max(r.Top, b.Bottom); break;
            case NativeMethods.ABE_LEFT: r.Left = Math.Max(r.Left, b.Right); break;
            case NativeMethods.ABE_RIGHT: r.Right = Math.Min(r.Right, b.Left); break;
            default: r.Bottom = Math.Min(r.Bottom, b.Top); break;
        }
        return r;
    }

    public static NativeMethods.RECT? PrimaryUsableRect() => UsableRect(PrimaryMonitor());

    private static uint QueryEdge()
    {
        try
        {
            var pos = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
            if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref pos) == IntPtr.Zero)
                return uint.MaxValue;
            _dockedEdge = pos.uEdge;
            return pos.uEdge;
        }
        catch
        {
            return uint.MaxValue;
        }
    }

    private static Band? DockedBand(NativeMethods.RECT bounds)
    {
        var pos = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref pos) == IntPtr.Zero) return null;
        _dockedEdge = pos.uEdge;
        return ClipTo(pos.rc, bounds) is { } band ? new Band(band, pos.uEdge) : null;
    }

    private static bool Fits(NativeMethods.RECT r, NativeMethods.RECT bounds, uint edge) => edge switch
    {
        NativeMethods.ABE_TOP => r.Top <= bounds.Top && Width(r) >= Width(bounds),
        NativeMethods.ABE_BOTTOM => r.Bottom >= bounds.Bottom && Width(r) >= Width(bounds),
        NativeMethods.ABE_LEFT => r.Left <= bounds.Left && Height(r) >= Height(bounds),
        NativeMethods.ABE_RIGHT => r.Right >= bounds.Right && Height(r) >= Height(bounds),
        _ => false,
    };

    private static int Width(NativeMethods.RECT r) => r.Right - r.Left;

    private static int Height(NativeMethods.RECT r) => r.Bottom - r.Top;

    private static NativeMethods.RECT? ClipTo(NativeMethods.RECT r, NativeMethods.RECT bounds)
    {
        var c = new NativeMethods.RECT
        {
            Left = Math.Max(r.Left, bounds.Left),
            Top = Math.Max(r.Top, bounds.Top),
            Right = Math.Min(r.Right, bounds.Right),
            Bottom = Math.Min(r.Bottom, bounds.Bottom),
        };
        return c.Right > c.Left && c.Bottom > c.Top ? c : null;
    }

    private static uint NearestEdge(NativeMethods.RECT r, NativeMethods.RECT bounds)
    {
        int top = Math.Abs(r.Top - bounds.Top);
        int bottom = Math.Abs(bounds.Bottom - r.Bottom);
        int left = Math.Abs(r.Left - bounds.Left);
        int right = Math.Abs(bounds.Right - r.Right);

        bool horizontal = Width(r) >= Height(r);
        return horizontal
            ? (top <= bottom ? NativeMethods.ABE_TOP : NativeMethods.ABE_BOTTOM)
            : (left <= right ? NativeMethods.ABE_LEFT : NativeMethods.ABE_RIGHT);
    }
}
