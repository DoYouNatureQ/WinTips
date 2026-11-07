using WinTips.Core;

namespace WinTips.UI;

internal static class OsdPlacement
{
    public const double Gap = 16;

    public const double ZoneRatio = 0.15;

    public enum PopSide { Top, Bottom, Left, Right }

    public static (double Left, double Top) DefaultFor(
        string placement, string vertical, double width, double height, MonitorArea area)
    {
        var usable = area.Usable();
        double left = placement switch
        {
            AppSettings.PlacementLeft => usable.Left + Gap,
            AppSettings.PlacementRight => usable.Right - Gap - width,
            _ => (usable.Left + usable.Right - width) / 2,
        };
        double top = vertical == AppSettings.PlacementTop
            ? usable.Top + Gap
            : usable.Bottom - Gap - height;
        return (left, top);
    }

    public static (double Left, double Top) CustomFor(
        double offsetLeft, double offsetTop, double width, double height, MonitorArea area) =>
        ClampToUsable(area.Left + offsetLeft, area.Top + offsetTop, width, height, area);

    public static (double Left, double Top) ClampToUsable(
        double left, double top, double width, double height, MonitorArea area)
    {
        var u = area.Usable();
        return (Math.Clamp(left, u.Left, Math.Max(u.Left, u.Right - width)),
                Math.Clamp(top, u.Top, Math.Max(u.Top, u.Bottom - height)));
    }

    public static PopSide? HorizontalSide(double left, double width, MonitorArea area)
    {
        double zone = Math.Max(1, area.Width) * ZoneRatio;
        double center = left + width / 2 - area.Left;
        if (center < zone) return PopSide.Left;
        if (center > area.Width - zone) return PopSide.Right;
        return null;
    }

    public static PopSide? VerticalSide(double top, double height, MonitorArea area)
    {
        double zone = Math.Max(1, area.Height) * ZoneRatio;
        double center = top + height / 2 - area.Top;
        if (center < zone) return PopSide.Top;
        if (center > area.Height - zone) return PopSide.Bottom;
        return null;
    }

    public static PopSide? ActiveSide(double left, double top, double width, double height, MonitorArea area) =>
        HorizontalSide(left, width, area) ?? VerticalSide(top, height, area);

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
