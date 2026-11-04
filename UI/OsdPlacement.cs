using WinTips.Core;

namespace WinTips.UI;

/// <summary>
/// 浮窗位置计算：预设（顶部/底部 × 左/中/右）、自定义位置、贴边判定与屏内钳制。
/// 所有计算都以“目标显示器”的矩形为参照（<see cref="MonitorArea"/>）：默认是主显示器，
/// 开启「跟随焦点显示器」后就是焦点窗口所在的那块屏，副屏同样按它自己的 DPI 与任务栏摆放。
/// 预设位置一律以任务栏为锚点，且跟随任务栏“此刻”的形态：任务栏收起（自动隐藏）时
/// 屏内只剩一条 2px 唤出条，浮窗就跟着贴到屏幕边缘；任务栏滑出后浮窗自动让到它上方。
/// </summary>
internal static class OsdPlacement
{
    public const double Gap = 16;

    /// <summary>贴边弹出判定区：屏幕四边各占 15%，浮窗中心落入其中即从该边滑入/滑出（左右优先）。</summary>
    public const double ZoneRatio = 0.15;

    /// <summary>贴边弹出方向。</summary>
    public enum PopSide { Top, Bottom, Left, Right }

    /// <summary>
    /// 按预设计算浮窗左上角（DIP）：垂直贴任务栏的相反一侧，水平按 Left/Center/Right 摆放
    /// （左右贴可用区域边缘，居中为该屏几何中心）。可用区域已按任务栏当前形态扣除那条边。
    /// </summary>
    public static (double Left, double Top) DefaultFor(
        string placement, string vertical, double width, double height, MonitorArea area)
    {
        var usable = area.Usable();
        double left = placement switch
        {
            AppSettings.PlacementLeft => usable.Left + Gap,
            AppSettings.PlacementRight => usable.Right - Gap - width,
            _ => area.CenterX - width / 2,
        };
        double top = vertical == AppSettings.PlacementTop
            ? usable.Top + Gap
            : usable.Bottom - Gap - height;
        return (left, top);
    }

    /// <summary>
    /// 自定义位置（DIP）：设置里存的是相对显示器左上角的偏移（主屏坐标系下即绝对坐标），
    /// 这里套到目标显示器的左上角再钳进该屏——换到副屏同样落在“同一个相对位置”，
    /// 屏比主屏小时自动收回屏内。
    /// </summary>
    public static (double Left, double Top) CustomFor(
        double offsetLeft, double offsetTop, double width, double height, MonitorArea area) =>
        ClampTo(area.Left + offsetLeft, area.Top + offsetTop, width, height, area);

    /// <summary>限制在目标显示器范围内（含任务栏区域），保证浮窗完整可见。</summary>
    public static (double Left, double Top) ClampTo(
        double left, double top, double width, double height, MonitorArea area) =>
        (Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width)),
         Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height)));

    /// <summary>浮窗中心落在该屏左/右 15% 区内则返回该侧，否则返回 null（屏幕中部）。</summary>
    public static PopSide? HorizontalSide(double left, double width, MonitorArea area)
    {
        double zone = Math.Max(1, area.Width) * ZoneRatio;
        double center = left + width / 2 - area.Left;
        if (center < zone) return PopSide.Left;
        if (center > area.Width - zone) return PopSide.Right;
        return null;
    }

    /// <summary>浮窗中心落在该屏上/下 15% 区内则返回该侧，否则返回 null（屏幕中部）。</summary>
    public static PopSide? VerticalSide(double top, double height, MonitorArea area)
    {
        double zone = Math.Max(1, area.Height) * ZoneRatio;
        double center = top + height / 2 - area.Top;
        if (center < zone) return PopSide.Top;
        if (center > area.Height - zone) return PopSide.Bottom;
        return null;
    }

    /// <summary>
    /// 生效的弹出方向：横向优先——浮窗中心落在左/右 15% 区内就只从该侧弹出，纵向不再参与
    /// （四角也只横向滑动）；不在左右区内才看上下 15% 区；都不在则返回 null（屏幕中部）。
    /// 保证任一时刻只有一个轴生效，不会出现斜向滑动或左右位置却上下弹的情况。
    /// </summary>
    public static PopSide? ActiveSide(double left, double top, double width, double height, MonitorArea area) =>
        HorizontalSide(left, width, area) ?? VerticalSide(top, height, area);

    /// <summary>
    /// 入场/退场滑动偏移（DIP）：只沿生效方向单轴滑动——贴左边从左侧滑入、贴右边从右侧滑入，
    /// 上下同理（左右优先于上下）。浮窗在屏幕中部时按上下半屏回落到竖向，保持旧版手感。
    /// </summary>
    public static (double Dx, double Dy) SlideOffset(double left, double top, double width, double height, MonitorArea area)
    {
        const double Distance = 20;
        switch (ActiveSide(left, top, width, height, area))
        {
            case PopSide.Left: return (-Distance, 0);
            case PopSide.Right: return (Distance, 0);
            case PopSide.Top: return (0, -Distance);
            case PopSide.Bottom: return (0, Distance);
            default:
                return top + height / 2 < area.CenterY ? (0, -Distance) : (0, Distance);
        }
    }
}
