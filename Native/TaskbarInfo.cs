using System.Runtime.InteropServices;

namespace WinTips.Native;

internal static class TaskbarInfo
{
    /// <summary>任务栏当前实际占用的那条带（物理像素，已裁到显示器范围内）与所在边。</summary>
    public readonly record struct Band(NativeMethods.RECT Rect, uint Edge);

    /// <summary>显示器句柄与屏幕矩形、工作区矩形（物理像素）。</summary>
    public readonly record struct MonitorBounds(IntPtr Handle, NativeMethods.RECT Bounds, NativeMethods.RECT Work);

    /// <summary>
    /// 缓存的停靠边。<c>SHAppBarMessage</c> 是跨进程调用（本机实测 ABM_GETTASKBARPOS 约 365µs，
    /// 其余查询都是 1–5µs），放进跟随轮询的热路径会白烧 CPU。边值只在任务栏被拖到别的边时才变，
    /// 所以缓存起来，用 <see cref="Fits"/> 自检失效后再重查一次。
    /// </summary>
    private static uint? _dockedEdge;

    /// <summary>主显示器句柄（原点所在的那块屏）。</summary>
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

    /// <summary>是否主显示器。同一块屏幕每次查询返回的 HMONITOR 相同，可直接比较。</summary>
    public static bool IsPrimary(IntPtr monitor) =>
        monitor != IntPtr.Zero && monitor == PrimaryMonitor();

    /// <summary>指定显示器的矩形与工作区（物理像素）。失败返回 null。</summary>
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

    /// <summary>主显示器矩形与工作区（物理像素）。失败返回 null。</summary>
    public static MonitorBounds? GetPrimaryMonitor() => GetMonitor(PrimaryMonitor());

    /// <summary>是否启用「自动隐藏任务栏」：只有开启时任务栏才会滑出/收起，浮窗才需要实时跟随。</summary>
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

    /// <summary>
    /// 副屏任务栏此刻占用的那条带（物理像素）。副屏任务栏没有 ABM 目标，
    /// 直接取 <c>Shell_SecondaryTrayWnd</c> 里与这块屏重叠面积最大的那个窗口的实时矩形，
    /// 贴边方向按几何推断（见 <see cref="NearestEdge"/>）。该屏没有任务栏时返回 null。
    /// </summary>
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

    /// <summary>
    /// 找到铺在这块显示器上的副屏任务栏窗口（未裁剪的实时窗口矩形）。
    /// 多屏时按裁剪后的重叠面积取最大的那个，避免把邻屏任务栏算进来。
    /// </summary>
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

    /// <summary>
    /// 主显示器上任务栏“此刻”占用的那条带（物理像素）。热路径只做 GetWindowRect，
    /// 不碰代价高昂的 ABM 查询（见 <see cref="_dockedEdge"/>）。
    /// 取的是 <c>Shell_TrayWnd</c> 的真实窗口矩形而不是停靠矩形：任务栏自动隐藏收起后
    /// 停靠矩形依然报整条 48px（实测窗口已滑到 [0,1438,2560,1486]、屏内只剩 2px 唤出条，
    /// ABM 却仍报 [0,1392,2560,1440]），只有窗口矩形会跟着收起/滑出一起变。
    /// 主屏上没有任务栏（任务栏在副屏）或读不到时返回 null。
    /// </summary>
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

            if (!clipped.HasValue) return DockedBand(bounds); // 读不到窗口（Explorer 重启等）：退回停靠矩形

            uint edge = _dockedEdge ?? QueryEdge();
            if (edge != uint.MaxValue && !Fits(clipped.Value, bounds, edge))
            {
                // 缓存的边对不上当前矩形：任务栏多半被拖到别的边了，重查一次
                _dockedEdge = null;
                edge = QueryEdge();
            }
            if (edge == uint.MaxValue || !Fits(clipped.Value, bounds, edge))
                edge = NearestEdge(clipped.Value, bounds); // ABM 也失败时按几何推断

            return new Band(clipped.Value, edge);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 指定显示器上任务栏“此刻”占用的那条带（物理像素）。主屏走 <see cref="PrimaryBand"/>
    /// （带 ABM 停靠边缓存），副屏用 <c>Shell_SecondaryTrayWnd</c> 的实时窗口矩形按几何推断贴边方向——
    /// 副屏任务栏没有 ABM 目标，也不需要：它只会贴在它所在这块屏的某条边上。
    /// 该屏没有任务栏（或读不到）时返回 null。
    /// </summary>
    public static Band? BandFor(MonitorBounds monitor) =>
        IsPrimary(monitor.Handle) ? PrimaryBand() : SecondaryBand(monitor.Bounds);

    /// <summary>
    /// 任意显示器的可用区域（物理像素）：显示器矩形扣掉任务栏此刻占用的那条边。
    /// 任务栏自动隐藏并收起时这条边只剩 2px 的唤出条，可用区域几乎等于整块屏幕，
    /// 贴着它摆的东西（浮窗）随之落到屏幕边缘；任务栏滑出后又自动让位。
    /// 读不到任务栏时退回系统工作区。显示器句柄无效时返回 null。
    /// </summary>
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
            default: r.Bottom = Math.Min(r.Bottom, b.Top); break; // ABE_BOTTOM，也是边值异常时的兜底
        }
        return r;
    }

    /// <summary>主显示器可用区域（物理像素），见 <see cref="UsableRect"/>。</summary>
    public static NativeMethods.RECT? PrimaryUsableRect() => UsableRect(PrimaryMonitor());

    /// <summary>查询并缓存停靠边；失败返回 <see cref="uint.MaxValue"/>。</summary>
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

    /// <summary>拿不到任务栏窗口时的兜底：用停靠矩形（等价于旧版行为）。</summary>
    private static Band? DockedBand(NativeMethods.RECT bounds)
    {
        var pos = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref pos) == IntPtr.Zero) return null;
        _dockedEdge = pos.uEdge;
        return ClipTo(pos.rc, bounds) is { } band ? new Band(band, pos.uEdge) : null;
    }

    /// <summary>
    /// 判定矩形是否确实贴在指定边上、且沿该边铺满显示器。
    /// 这样缓存的边一旦过期（任务栏被拖到别的边）就能被发现，避免把侧边任务栏当成底边。
    /// </summary>
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

    /// <summary>裁到显示器范围内；没有交集（任务栏不在这个屏上）返回 null。</summary>
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

    /// <summary>停靠边完全拿不到时，按矩形位置推断它贴在屏幕哪条边。</summary>
    private static uint NearestEdge(NativeMethods.RECT r, NativeMethods.RECT bounds)
    {
        int top = Math.Abs(r.Top - bounds.Top);
        int bottom = Math.Abs(bounds.Bottom - r.Bottom);
        int left = Math.Abs(r.Left - bounds.Left);
        int right = Math.Abs(bounds.Right - r.Right);

        // 任务栏横向铺满时左右边距也是 0，所以先按“哪个方向更薄”定横竖，再比距离
        bool horizontal = Width(r) >= Height(r);
        return horizontal
            ? (top <= bottom ? NativeMethods.ABE_TOP : NativeMethods.ABE_BOTTOM)
            : (left <= right ? NativeMethods.ABE_LEFT : NativeMethods.ABE_RIGHT);
    }
}
