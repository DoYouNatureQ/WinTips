using System.Windows;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>浮窗位置计算：预设（顶部/底部 × 左/中/右）、自定义位置与主屏范围钳制。</summary>
internal static class OsdPlacement
{
    public const double Gap = 16;

    /// <summary>主显示器有效 DPI 缩放（物理像素 → DIP）。查询失败返回 1.0。</summary>
    public static double PrimaryScale()
    {
        try
        {
            var mon = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            if (mon != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(mon, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                dpiX > 0)
                return dpiX / 96.0;
        }
        catch { }
        return 1.0;
    }

    /// <summary>内容底边锚点（DIP）：任务栏上方 16px（任务栏在底部且未自动隐藏时），否则工作区底边上方 16px。</summary>
    public static double BottomAnchor(double scale)
    {
        var tb = TaskbarInfo.GetPrimaryTaskbar();
        if (tb.HasValue && tb.Value.Edge == NativeMethods.ABE_BOTTOM && !tb.Value.AutoHide)
            return tb.Value.Rect.Top / scale - Gap;
        return SystemParameters.WorkArea.Bottom - Gap;
    }

    /// <summary>内容顶边锚点（DIP）：任务栏下方 16px（任务栏在顶部且未自动隐藏时），否则工作区顶边下方 16px。</summary>
    public static double TopAnchor(double scale)
    {
        var tb = TaskbarInfo.GetPrimaryTaskbar();
        if (tb.HasValue && tb.Value.Edge == NativeMethods.ABE_TOP && !tb.Value.AutoHide)
            return tb.Value.Rect.Bottom / scale + Gap;
        return SystemParameters.WorkArea.Top + Gap;
    }

    /// <summary>
    /// 按预设计算浮窗左上角（DIP，主屏坐标系）：垂直贴屏幕顶部或任务栏上方，
    /// 水平按 Left/Center/Right 摆放（左右贴工作区边缘，居中为屏幕几何中心）。
    /// </summary>
    public static (double Left, double Top) DefaultFor(string placement, string vertical, double width, double height, double scale)
    {
        double left = placement switch
        {
            AppSettings.PlacementLeft => SystemParameters.WorkArea.Left + Gap,
            AppSettings.PlacementRight => SystemParameters.WorkArea.Right - Gap - width,
            _ => SystemParameters.PrimaryScreenWidth / 2 - width / 2,
        };
        double top = vertical == AppSettings.PlacementTop
            ? TopAnchor(scale)
            : BottomAnchor(scale) - height;
        return (left, top);
    }

    /// <summary>限制在主显示器范围内（含任务栏区域），保证浮窗完整可见。</summary>
    public static (double Left, double Top) ClampToPrimary(double left, double top, double width, double height) =>
        (Math.Clamp(left, 0, Math.Max(0, SystemParameters.PrimaryScreenWidth - width)),
         Math.Clamp(top, 0, Math.Max(0, SystemParameters.PrimaryScreenHeight - height)));
}
