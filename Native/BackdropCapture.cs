using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinTips.Native;

/// <summary>
/// 自绘毛玻璃的唯一来源：抓取窗口正后方的屏幕像素，做高斯近似模糊，再把 tint 混进去。
///
/// 为什么非得自己画：系统材质给不出任务栏那个观感。
/// - Mica（DWMSBT_MAINWINDOW）只采样桌面壁纸，背后窗口根本不会出现在材质里；
/// - Desktop Acrylic（DWMSBT_TRANSIENTWINDOW）的亮度层吃掉约 85%，透上来的只剩色温；
/// - Win11 的任务栏用的是系统内部特权实现（Composition backdrop），第三方 Win32 窗口
///   没有对等 API；而 Win10 时代那条 ACCENT_ENABLE_ACRYLICBLURBEHIND 后门，
///   在 Win11 24H2 之后已被 DWM 静默忽略（本机实测：调用返回成功，画面毫无变化）。
///
/// 所以"和任务栏一样"只有一个办法——照着任务栏的做法自己算：抓屏 + 模糊 + 叠加底色。
/// 浮窗只在弹出瞬间抓一次（位置固定、不拖动），代价是毫秒级的一次 BitBlt 与几万像素的模糊。
/// </summary>
internal static class BackdropCapture
{
    private const int SRCCOPY = 0x00CC0020;

    /// <summary>模糊重复次数。三次盒式模糊已足够逼近高斯，再多对 160×50 的小图没有可见收益。</summary>
    private const int BlurPasses = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int width, int height,
        IntPtr hdcSrc, int srcX, int srcY, int rop);

    /// <summary>
    /// 抓取 <paramref name="hwnd"/> 背后的画面，返回**只做了模糊、没有上色**的位图。
    /// 调用前窗口必须处于隐藏状态（或整体 Opacity=0），否则会把自己也抓进去。
    ///
    /// 底色刻意不在这里混：混进像素就等于把不透明度焊死在这张图上，用户拖动
    /// "材质透明度"滑杆时只能重新抓屏重算——那既会闪，又抓不到自己背后的画面
    /// （窗口正显示着）。留给上层当独立图层叠，改 alpha 就是改一个画刷。
    /// </summary>
    /// <param name="padding">
    /// 向窗口四周额外抓取的像素。模糊是卷积，边界像素需要窗口外的真实内容才不会发黑；
    /// 算完后这一圈会被裁掉，不参与显示。
    /// </param>
    /// <param name="radius">盒式模糊半径（物理像素）。</param>
    public static BitmapSource? CaptureBlurred(IntPtr hwnd, int padding, int radius)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var box)) return null;

        int width = box.Right - box.Left;
        int height = box.Bottom - box.Top;
        if (width <= 0 || height <= 0) return null;

        // 多屏时窗口可能有一半在屏幕外：BitBlt 对超出屏幕的部分会填黑，
        // 但模糊之后那点黑只在边缘、且会被裁掉，不值得为此单独处理。
        int grabX = box.Left - padding;
        int grabY = box.Top - padding;
        int grabW = width + padding * 2;
        int grabH = height + padding * 2;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            memDc = CreateCompatibleDC(screenDc);
            hBitmap = CreateCompatibleBitmap(screenDc, grabW, grabH);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero) return null;

            previous = SelectObject(memDc, hBitmap);
            if (!BitBlt(memDc, 0, 0, grabW, grabH, screenDc, grabX, grabY, SRCCOPY)) return null;

            var raw = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            raw.Freeze();

            var pixels = ReadPixels(raw, grabW, grabH);
            if (pixels is null) return null;

            Blur(pixels, grabW, grabH, radius);

            var composed = BitmapSource.Create(
                grabW, grabH, 96, 96, PixelFormats.Bgra32, null, pixels, grabW * 4);
            composed.Freeze();

            // 裁掉外扩的一圈，只留窗口自己那块
            var cropped = new CroppedBitmap(composed, new Int32Rect(padding, padding, width, height));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memDc, previous);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static byte[]? ReadPixels(BitmapSource source, int width, int height)
    {
        // CreateBitmapSourceFromHBitmap 拿到的不一定是 Bgra32（可能是 Bgr32 或带调色板），
        // 统一转一次，后面才能按 4 字节像素直接算。
        BitmapSource normalized = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = width * 4;
        var buffer = new byte[stride * height];
        normalized.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    /// <summary>三次水平+垂直盒式模糊。小图上比 WPF 的 BlurEffect 快得多，也不占渲染线程。</summary>
    private static void Blur(byte[] pixels, int width, int height, int radius)
    {
        if (radius <= 0) return;
        var scratch = new byte[pixels.Length];

        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BlurHorizontal(pixels, scratch, width, height, radius);
            BlurVertical(scratch, pixels, width, height, radius);
        }
    }

    private static void BlurHorizontal(byte[] source, byte[] target, int width, int height, int radius)
    {
        int window = radius * 2 + 1;
        int last = width - 1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;

            for (int channel = 0; channel < 4; channel++)
            {
                // 起始窗口：左侧越界的部分用第 0 列补齐（钳位），避免边缘被"平均成透明"
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += source[row + Math.Clamp(i, 0, last) * 4 + channel];

                for (int x = 0; x < width; x++)
                {
                    target[row + x * 4 + channel] = (byte)(sum / window);
                    sum += source[row + Math.Clamp(x + radius + 1, 0, last) * 4 + channel];
                    sum -= source[row + Math.Clamp(x - radius, 0, last) * 4 + channel];
                }
            }
        }
    }

    private static void BlurVertical(byte[] source, byte[] target, int width, int height, int radius)
    {
        int window = radius * 2 + 1;
        int last = height - 1;
        int stride = width * 4;

        for (int x = 0; x < width; x++)
        {
            int column = x * 4;

            for (int channel = 0; channel < 4; channel++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += source[Math.Clamp(i, 0, last) * stride + column + channel];

                for (int y = 0; y < height; y++)
                {
                    target[y * stride + column + channel] = (byte)(sum / window);
                    sum += source[Math.Clamp(y + radius + 1, 0, last) * stride + column + channel];
                    sum -= source[Math.Clamp(y - radius, 0, last) * stride + column + channel];
                }
            }
        }
    }
}
