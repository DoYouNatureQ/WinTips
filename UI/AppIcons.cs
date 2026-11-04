using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 应用图标（取自本进程 exe 的关联图标）的唯一取用处。
///
/// 之前分散在三个地方各写一遍 `System.Drawing.Icon.ExtractAssociatedIcon`：既把
/// System.Drawing/GDI+ 拖进常驻内存，又从不释放取到的 HICON（GDI 句柄随每次开设置页增长）。
/// 这里统一用 shell32 的 ExtractIconEx 取，转换完立即 DestroyIcon；共享图标路径
/// （LoadIcon 的系统图标）则不销毁。
/// </summary>
internal static class AppIcons
{
    private static BitmapSource? _source;
    private static bool _sourceTried;

    /// <summary>窗口图标（大图标）。已 Freeze，可跨窗口共享同一个实例。</summary>
    public static BitmapSource? Source
    {
        get
        {
            if (_sourceTried) return _source;
            _sourceTried = true;
            var (handle, owned) = Load(NativeMethods.SM_CXICON);
            try { _source = ToBitmapSource(handle); }
            finally { if (owned && handle != IntPtr.Zero) NativeMethods.DestroyIcon(handle); }
            return _source;
        }
    }

    /// <summary>
    /// 托盘用小图标句柄。返回值里的 Owned 为 true 时调用方必须 DestroyIcon。
    /// </summary>
    public static (IntPtr Handle, bool Owned) TrayIcon() => Load(NativeMethods.SM_CXSMICON);

    /// <summary>HICON → BitmapSource（像素已复制，转换后即可销毁源句柄）。</summary>
    public static BitmapSource? ToBitmapSource(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    /// <summary>
    /// 按系统度量取 exe 关联图标。取不到时退回系统默认应用图标（共享资源，不销毁）。
    /// </summary>
    private static (IntPtr Handle, bool Owned) Load(int metric)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                // 两个尺寸都会拿到，只留需要的那个，另一个立刻释放，避免每次调用漏一个句柄
                if (NativeMethods.ExtractIconExW(exe, 0, out var large, out var small, 1) > 0)
                {
                    bool wantSmall = metric == NativeMethods.SM_CXSMICON;
                    var keep = wantSmall ? small : large;
                    var drop = wantSmall ? large : small;
                    if (drop != IntPtr.Zero) NativeMethods.DestroyIcon(drop);
                    if (keep != IntPtr.Zero) return (keep, true);
                }
            }
        }
        catch { }

        return (NativeMethods.LoadIconW(IntPtr.Zero, NativeMethods.IDI_APPLICATION), false);
    }
}
