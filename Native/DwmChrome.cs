namespace WinTips.Native;

/// <summary>
/// Win11 专有 DWM 外观能力的统一入口：能力探测 + 返回值检查 + 降级判定。
///
/// 背景：圆角(DWMWA_WINDOW_CORNER_PREFERENCE=33) 需 Win11 22000+，
/// 系统材质(DWMWA_SYSTEMBACKDROP_TYPE=38) 需 Win11 22621+，深色标题栏属性号在
/// 1809–1903 是 19、2004+ 才是 20。这些调用在 Win10 上一律失败且不会抛异常，
/// 若继续沿用“扩展边框把整窗变成玻璃”(margins=-1)——那是 Win7 Aero 时代的手法——
/// 客户区会退化成 DWM 的实心玻璃填充色，看上去就是一圈 Win7 样式的粗边框。
/// 因此所有窗口都必须先问一句 <see cref="SupportsFluent"/>，拿不到就走不透明直角卡片。
/// </summary>
internal static class DwmChrome
{
    /// <summary>
    /// 调试用：设置 WINTIPS_LEGACY_UI=1 可在 Win11 上强制走 Win10 降级外观，
    /// 便于在不装 Win10 的情况下验收兜底路径。
    /// </summary>
    private static readonly bool ForceLegacy =
        Environment.GetEnvironmentVariable("WINTIPS_LEGACY_UI") == "1";

    /// <summary>是否具备 Win11 的圆角 + Mica/Acrylic 能力。</summary>
    public static bool SupportsFluent { get; } =
        !ForceLegacy && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>Mica/Acrylic 材质需 22621+（Win11 22H2）。</summary>
    public static bool SupportsBackdrop { get; } =
        SupportsFluent && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    /// <summary>桌面合成是否可用。关闭时即便系统是 Win11 也只能退化成不透明窗口。</summary>
    public static bool IsCompositionEnabled()
    {
        try { return NativeMethods.DwmIsCompositionEnabled(out bool enabled) == 0 && enabled; }
        catch { return false; }
    }

    /// <summary>Win11 圆角/材质可用且合成正常，才值得走玻璃路径。</summary>
    public static bool CanUseGlass => SupportsFluent && IsCompositionEnabled();

    /// <summary>深色标题栏：先试 20（2004+），失败回退 19（1809–1903）。</summary>
    public static bool TrySetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int flag = dark ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref flag, sizeof(int)) == 0)
            return true;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref flag, sizeof(int)) == 0;
    }

    /// <summary>圆角偏好。Win10 上属性号未知，返回 false。</summary>
    public static bool TrySetCorner(IntPtr hwnd, int corner)
    {
        if (!SupportsFluent) return false;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) == 0;
    }

    /// <summary>系统材质（Mica/Acrylic）。Win10 上属性号未知，返回 false。</summary>
    public static bool TrySetBackdrop(IntPtr hwnd, int backdrop)
    {
        if (!SupportsBackdrop) return false;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>
    /// 把客户区交给 DWM 做“玻璃”合成（margins=-1），这是 AllowsTransparency=false
    /// 时保留硬件加速 + 逐像素透明的唯一办法。仅在 Win11 上与 Mica 配套使用；
    /// 传 false 恢复为普通边框，避免 Win10 上出现实心玻璃色带。
    /// </summary>
    public static bool SetFrameExtended(IntPtr hwnd, bool extend)
    {
        var margins = extend
            ? new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 }
            : default;
        return NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0;
    }
}
