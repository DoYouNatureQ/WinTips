using System.Windows;
using System.Windows.Interop;
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
