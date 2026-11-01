using System.Windows;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 浮窗位置计算：预设（顶部/底部 × 左/中/右）、自定义位置、贴边判定与主屏范围钳制。
/// 所有预设位置都以任务栏为锚点，且跟随任务栏“此刻”的形态：任务栏收起（自动隐藏）时
/// 屏内只剩一条 2px 唤出条，浮窗就跟着贴到屏幕边缘；任务栏滑出后浮窗自动让到它上方。
/// </summary>
internal static class OsdPlacement
{
    public const double Gap = 16;

    /// <summary>贴边弹出判定区：屏幕四边各占 15%，浮窗中心落入其中即从该边滑入/滑出（左右优先）。</summary>
    public const double ZoneRatio = 0.15;

    /// <summary>贴边弹出方向。</summary>
    public enum PopSide { Top, Bottom, Left, Right }

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

    /// <summary>
    /// 主显示器可用区域（DIP，主屏坐标系）：屏幕矩形扣掉任务栏此刻实际占用的那条边
    /// （见 <see cref="TaskbarInfo.PrimaryUsableRect"/>）。任务栏收起时几乎等于整块屏幕，
    /// 滑出后自动让位。拿不到显示器/任务栏信息时退回系统工作区，即旧版行为。
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) UsableArea(double scale)
    {
        if (scale <= 0) scale = 1.0;

        if (TaskbarInfo.PrimaryUsableRect() is { } r)
            return (r.Left / scale, r.Top / scale, r.Right / scale, r.Bottom / scale);

        var wa = SystemParameters.WorkArea;
        return (wa.Left, wa.Top, wa.Right, wa.Bottom);
    }

    /// <summary>内容底边锚点（DIP）：任务栏当前上沿再往上 16px——任务栏收起时就是屏幕底边上方 16px。</summary>
    public static double BottomAnchor(double scale) => UsableArea(scale).Bottom - Gap;

    /// <summary>内容顶边锚点（DIP）：任务栏当前下沿再往下 16px——任务栏收起时就是屏幕顶边下方 16px。</summary>
    public static double TopAnchor(double scale) => UsableArea(scale).Top + Gap;

    /// <summary>
    /// 按预设计算浮窗左上角（DIP，主屏坐标系）：垂直贴任务栏的相反一侧，
    /// 水平按 Left/Center/Right 摆放（左右贴可用区域边缘，居中为屏幕几何中心）。
    /// 可用区域已按任务栏当前形态扣除那条边。
    /// </summary>
    public static (double Left, double Top) DefaultFor(string placement, string vertical, double width, double height, double scale)
    {
        var area = UsableArea(scale);
        double left = placement switch
        {
            AppSettings.PlacementLeft => area.Left + Gap,
            AppSettings.PlacementRight => area.Right - Gap - width,
            _ => SystemParameters.PrimaryScreenWidth / 2 - width / 2,
        };
        double top = vertical == AppSettings.PlacementTop
            ? area.Top + Gap
            : area.Bottom - Gap - height;
        return (left, top);
    }

    /// <summary>限制在主显示器范围内（含任务栏区域），保证浮窗完整可见。</summary>
    public static (double Left, double Top) ClampToPrimary(double left, double top, double width, double height) =>
        (Math.Clamp(left, 0, Math.Max(0, SystemParameters.PrimaryScreenWidth - width)),
         Math.Clamp(top, 0, Math.Max(0, SystemParameters.PrimaryScreenHeight - height)));

    /// <summary>浮窗中心落在左/右 15% 区内则返回该侧，否则返回 null（屏幕中部）。</summary>
    public static PopSide? HorizontalSide(double left, double width)
    {
        double screen = Math.Max(1, SystemParameters.PrimaryScreenWidth);
        double zone = screen * ZoneRatio;
        double center = left + width / 2;
        if (center < zone) return PopSide.Left;
        if (center > screen - zone) return PopSide.Right;
        return null;
    }

    /// <summary>浮窗中心落在上/下 15% 区内则返回该侧，否则返回 null（屏幕中部）。</summary>
    public static PopSide? VerticalSide(double top, double height)
    {
        double screen = Math.Max(1, SystemParameters.PrimaryScreenHeight);
        double zone = screen * ZoneRatio;
        double center = top + height / 2;
        if (center < zone) return PopSide.Top;
        if (center > screen - zone) return PopSide.Bottom;
        return null;
    }

    /// <summary>
    /// 生效的弹出方向：横向优先——浮窗中心落在左/右 15% 区内就只从该侧弹出，纵向不再参与
    /// （四角也只横向滑动）；不在左右区内才看上下 15% 区；都不在则返回 null（屏幕中部）。
    /// 保证任一时刻只有一个轴生效，不会出现斜向滑动或左右位置却上下弹的情况。
    /// </summary>
    public static PopSide? ActiveSide(double left, double top, double width, double height) =>
        HorizontalSide(left, width) ?? VerticalSide(top, height);

    /// <summary>
    /// 入场/退场滑动偏移（DIP）：只沿生效方向单轴滑动——贴左边从左侧滑入、贴右边从右侧滑入，
    /// 上下同理（左右优先于上下）。浮窗在屏幕中部时按上下半屏回落到竖向，保持旧版手感。
    /// </summary>
    public static (double Dx, double Dy) SlideOffset(double left, double top, double width, double height)
    {
        const double Distance = 20;
        switch (ActiveSide(left, top, width, height))
        {
            case PopSide.Left: return (-Distance, 0);
            case PopSide.Right: return (Distance, 0);
            case PopSide.Top: return (0, -Distance);
            case PopSide.Bottom: return (0, Distance);
            default:
                return top + height / 2 < SystemParameters.PrimaryScreenHeight / 2
                    ? (0, -Distance)
                    : (0, Distance);
        }
    }
}
