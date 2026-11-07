using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinTips.Native;

namespace WinTips.UI;

internal static class AppIcons
{
    private static BitmapSource? _source;
    private static bool _sourceTried;

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

    public static (IntPtr Handle, bool Owned) TrayIcon() => Load(NativeMethods.SM_CXSMICON);

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

    private static (IntPtr Handle, bool Owned) Load(int metric)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
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
