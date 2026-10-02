using System.Runtime.InteropServices;

namespace WinTips.Native;

internal static class TaskbarInfo
{
    public readonly record struct Info(NativeMethods.RECT Rect, uint Edge, bool AutoHide);

    /// <summary>主显示器任务栏信息（物理像素）。失败返回 null。</summary>
    public static Info? GetPrimaryTaskbar()
    {
        try
        {
            var data = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
            if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) == IntPtr.Zero) return null;
            var state = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>() };
            bool autoHide = (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref state).ToInt64() & NativeMethods.ABS_AUTOHIDE) != 0;
            return new Info(data.rc, data.uEdge, autoHide);
        }
        catch
        {
            return null;
        }
    }
}
