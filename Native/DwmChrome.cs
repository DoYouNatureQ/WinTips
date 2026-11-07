namespace WinTips.Native;

internal static class DwmChrome
{
    private static readonly bool ForceLegacy =
        Environment.GetEnvironmentVariable("WINTIPS_LEGACY_UI") == "1";

    public static bool SupportsFluent { get; } =
        !ForceLegacy && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static bool SupportsBackdrop { get; } =
        SupportsFluent && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    public static bool IsCompositionEnabled()
    {
        try { return NativeMethods.DwmIsCompositionEnabled(out bool enabled) == 0 && enabled; }
        catch { return false; }
    }

    public static bool CanUseGlass => SupportsFluent && IsCompositionEnabled();

    public static bool TrySetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int flag = dark ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref flag, sizeof(int)) == 0)
            return true;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref flag, sizeof(int)) == 0;
    }

    public static bool TrySetCorner(IntPtr hwnd, int corner)
    {
        if (!SupportsFluent) return false;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) == 0;
    }

    public static bool TrySetBackdrop(IntPtr hwnd, int backdrop)
    {
        if (!SupportsBackdrop) return false;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    public static bool SetFrameExtended(IntPtr hwnd, bool extend)
    {
        var margins = extend
            ? new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 }
            : default;
        return NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0;
    }
}
