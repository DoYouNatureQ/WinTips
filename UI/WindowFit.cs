using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinTips.Native;

namespace WinTips.UI;

internal static class WindowFit
{
    private const double EdgeMargin = 8;

    private const double MinimumHeight = 220;

    private const double MinimumWidth = 620;

    public static void ClampToScreen(Window window, double preferredWidth)
    {
        var (width, height) = UsableSize(window);
        window.Width = Math.Min(preferredWidth, width);
        window.MaxHeight = height;

        // 尺寸钳制后位置未必还对：SizeToContent 窗口由 WPF 按"未钳制的期望
        // 高度"居中定位，内容比屏幕高时 Top 会是负的（标题栏顶出屏幕外，
        // 见 issue #15）。等实际尺寸量出来再修正位置。
        SchedulePositionClamp(window);
    }

    public static void ClampHeightToScreen(Window window) =>
        window.MaxHeight = UsableSize(window).Height;

    public static (double Width, double Height) UsableSize(Window window)
    {
        var area = WorkArea(window);
        if (area.Width > 0 && area.Height > 0)
            return (
                Math.Max(MinimumWidth, area.Width / area.Scale - EdgeMargin * 2),
                Math.Max(MinimumHeight, area.Height / area.Scale - EdgeMargin * 2));

        var work = SystemParameters.WorkArea;
        return (
            Math.Max(MinimumWidth, work.Width - EdgeMargin * 2),
            Math.Max(MinimumHeight, work.Height - EdgeMargin * 2));
    }

    public static void SchedulePositionClamp(Window window)
    {
        // Loaded 优先级在布局完成之后触发，此时 SizeToContent 的实际尺寸已就绪；
        // DpiChanged 等后续场景会经由 ClampToScreen 再排一次，天然覆盖。
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ClampPosition(window)));
    }

    public static void ClampPosition(Window window)
    {
        if (!window.IsLoaded) return;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (TaskbarInfo.UsableRect(monitor) is not { } rect) return;

            double scale = MonitorArea.ScaleFor(monitor);
            if (scale <= 0) scale = 1.0;

            // 与 MonitorArea 同一约定：物理像素 / 该屏 DPI = WPF 坐标。
            double workLeft = rect.Left / scale;
            double workTop = rect.Top / scale;
            double workWidth = (rect.Right - rect.Left) / scale;
            double workHeight = (rect.Bottom - rect.Top) / scale;

            double width = window.ActualWidth;
            double height = window.ActualHeight;
            if (width <= 0 || height <= 0) return;

            double left = window.Left;
            double top = window.Top;
            if (double.IsNaN(left) || double.IsNaN(top)) return;

            // 窗口整体收进工作区；比工作区还大时（MinimumWidth 兜底等）钉在左上。
            double minLeft = workLeft;
            double maxLeft = width >= workWidth ? minLeft : workLeft + workWidth - width;
            double minTop = workTop;
            double maxTop = height >= workHeight ? minTop : workTop + workHeight - height;

            double clampedLeft = Math.Min(Math.Max(left, minLeft), maxLeft);
            double clampedTop = Math.Min(Math.Max(top, minTop), maxTop);

            if (!AreClose(left, clampedLeft)) window.Left = clampedLeft;
            if (!AreClose(top, clampedTop)) window.Top = clampedTop;
        }
        catch
        {
            // 拿不到显示器信息就放弃位置修正，保持原行为。
        }
    }

    private static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.5;

    private static (double Width, double Height, double Scale) WorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return default;

        try
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (TaskbarInfo.UsableRect(monitor) is not { } rect) return default;

            double scale = MonitorArea.ScaleFor(monitor);
            return (rect.Right - rect.Left, rect.Bottom - rect.Top, scale > 0 ? scale : 1.0);
        }
        catch
        {
            return default;
        }
    }
}
