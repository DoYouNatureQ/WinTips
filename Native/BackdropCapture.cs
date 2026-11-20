using System.Buffers;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinTips.Native;

/// <summary>
/// 一块留在内存里的桌面快照：比卡片大一圈，且已经模糊好。
/// </summary>
/// <remarks>
/// 留出比卡片大的一圈有两个用处：模糊核在卡片边缘要用到外侧的像素；卡片在两帧刷新之间
/// 可能挪了几个像素，取样框得在快照里跟着挪。出了留白就贴边取样。
/// 用 <see cref="Release"/> 归还池化的像素缓冲——实时刷新时每秒要抓十几次，每次都走
/// 大对象堆分配会把 GC 拖成卡顿。
/// </remarks>
internal sealed class BackdropSnapshot
{
    /// <summary>抓取区域左上角（物理像素，虚拟屏幕坐标）。</summary>
    public int Left { get; init; }

    public int Top { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>模糊后的 BGRA 像素，行距为 <see cref="Width"/> * 4（缓冲可能比这更大）。</summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();

    /// <summary>模糊用的中转缓冲，跟 <see cref="Pixels"/> 一起归还。</summary>
    internal byte[]? Scratch;

    /// <summary>取样结果。跟着快照走而不是全局共用，两块卡片同时在场也不会互相串图。</summary>
    internal WriteableBitmap? Output;

    /// <summary>缓冲是从 <see cref="ArrayPool{T}"/> 租的。</summary>
    internal bool Pooled;

    public void Release()
    {
        if (!Pooled) return;
        Pooled = false;

        ArrayPool<byte>.Shared.Return(Pixels);
        if (Scratch is not null) ArrayPool<byte>.Shared.Return(Scratch);
        Scratch = null;
    }
}

internal static class BackdropCapture
{
    private const int SRCCOPY = 0x00CC0020;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    /// <summary>
    /// 静态快照用三次方框模糊，逼近高斯。实时刷新承担不起这个成本（每一帧都要重来），
    /// 调用方会传 2 次并稍加大半径，用 sigma 换回同样的柔和度。
    /// </summary>
    private const int BlurPasses = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        public uint Color;
    }

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

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint start, uint lines,
        byte[] bits, ref BITMAPINFO info, uint usage);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>
    /// 能不能在卡片显示期间反复抓屏。
    /// </summary>
    /// <remarks>
    /// 靠 <see cref="NativeMethods.WDA_EXCLUDEFROMCAPTURE"/> 把自己排除在抓屏之外，否则
    /// 每次抓到的都是卡片自己。这条属性只有 Win10 2004（19041）起才认真对待——更早的
    /// 系统会退化成 WDA_MONITOR，抓出来是一个黑方块，比鬼影还难看。另外它靠 DWM 合成
    /// 生效：DWM 没在合成、或者跑在远程桌面会话里，排除都不可靠，一律退回"只在弹出前
    /// 抓一次"。
    /// </remarks>
    public static bool SupportsLiveCapture =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
        DwmChrome.IsCompositionEnabled() &&
        NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) == 0;

    /// <summary>
    /// 把窗口排除出抓屏，之后卡片显示期间也能反复抓桌面。
    /// 返回 false 表示这台机器上做不了，调用方应退回静态快照。
    /// </summary>
    public static bool TryExcludeFromCapture(IntPtr hwnd)
    {
#if DEBUG
        // 调试用：关掉排除，好让截图/录屏能看见卡片（也顺带退回静态快照）。
        if (Environment.GetEnvironmentVariable("WINTIPS_DEBUG_NO_CAPTURE_EXCLUDE") == "1") return false;
#endif
        if (hwnd == IntPtr.Zero || !SupportsLiveCapture) return false;
        return NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>撤掉排除。窗口隐藏后留着也没坏处，但不留尾巴更规矩。</summary>
    public static void AllowCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE);
    }

    /// <summary>
    /// 抓取窗口四周各 <paramref name="padding"/> 像素的桌面并原地模糊。
    /// </summary>
    /// <param name="waitForComposition">
    /// 抓之前先等一次桌面合成。窗口刚显示或刚隐藏时，屏幕上那一帧还没真的换成现在的样子，
    /// 不等就可能抓到上一块卡片。实时刷新时窗口一直显示、又被排除在抓屏之外，不必再等。
    /// </param>
    /// <remarks>
    /// 窗口在屏上时，调用方必须已经用 <see cref="TryExcludeFromCapture"/> 把它排除出抓屏，
    /// 否则拍进去的是卡片自己。
    /// </remarks>
    public static BackdropSnapshot? Capture(IntPtr hwnd, int padding, int radius, bool waitForComposition = true,
        int passes = BlurPasses)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var box)) return null;

        int width = box.Right - box.Left;
        int height = box.Bottom - box.Top;
        if (width <= 0 || height <= 0) return null;

        int grabW = width + padding * 2;
        int grabH = height + padding * 2;
        int bytes = grabW * grabH * 4;

        var pixels = ArrayPool<byte>.Shared.Rent(bytes);
        var scratch = ArrayPool<byte>.Shared.Rent(bytes);

        if (waitForComposition)
        {
            try { DwmFlush(); } catch { }
        }

        if (!Grab(box.Left - padding, box.Top - padding, grabW, grabH, bytes, pixels))
        {
            ArrayPool<byte>.Shared.Return(pixels);
            ArrayPool<byte>.Shared.Return(scratch);
            return null;
        }

        Blur(pixels, scratch, grabW, grabH, radius, passes);

        return new BackdropSnapshot
        {
            Left = box.Left - padding,
            Top = box.Top - padding,
            Width = grabW,
            Height = grabH,
            Pixels = pixels,
            Scratch = scratch,
            Pooled = true,
        };
    }

    /// <summary>
    /// 抓一张快照并立刻按窗口当前位置取出一块——给不移动、也不用实时刷新的窗口（托盘菜单）用。
    /// </summary>
    public static BitmapSource? CaptureBlurred(IntPtr hwnd, int padding, int radius)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var box)) return null;

        var snapshot = Capture(hwnd, padding, radius);
        if (snapshot is null) return null;

        try
        {
            return Render(snapshot, box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top);
        }
        finally
        {
            snapshot.Release();
        }
    }

    /// <summary>
    /// 按卡片当前的屏幕位置从快照里取一块（宽高为卡片尺寸）。取不到（位置或尺寸超出
    /// 留白）返回 null，调用方保持原样即可。
    /// </summary>
    public static BitmapSource? Render(BackdropSnapshot snapshot, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        if (width > snapshot.Width || height > snapshot.Height) return null;

        var output = snapshot.Output;
        if (output is null || output.PixelWidth != width || output.PixelHeight != height)
            snapshot.Output = output = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

        // 位置超出留白时贴边取，宁可模糊贴住不动，也不能让卡片有一块露空。
        int offsetX = Math.Clamp(x - snapshot.Left, 0, snapshot.Width - width);
        int offsetY = Math.Clamp(y - snapshot.Top, 0, snapshot.Height - height);

        output.WritePixels(
            new Int32Rect(0, 0, width, height),
            snapshot.Pixels,
            snapshot.Width * 4,
            (offsetY * snapshot.Width + offsetX) * 4);

        return output;
    }

    /// <summary>
    /// 按窗口当前屏幕位置，把快照里对应的一块拷进 <paramref name="target"/>（行距 width * 4）。
    /// </summary>
    /// <remarks>
    /// 实时刷新时每帧都要对准，但不必走 <see cref="Render"/> 建位图那一步——像素先落在一块
    /// 复用的普通缓冲里，跟上一帧混一下再一次性写进显示位图，这样 UI 线程每帧只碰一次位图。
    /// </remarks>
    public static bool CopyInto(BackdropSnapshot snapshot, int x, int y, int width, int height, byte[] target)
    {
        if (width <= 0 || height <= 0) return false;
        if (width > snapshot.Width || height > snapshot.Height) return false;

        int rowBytes = width * 4;
        if (target.Length < rowBytes * height) return false;

        // 位置超出留白时贴边取，宁可模糊贴住不动，也不能让卡片有一块露空。
        int offsetX = Math.Clamp(x - snapshot.Left, 0, snapshot.Width - width);
        int offsetY = Math.Clamp(y - snapshot.Top, 0, snapshot.Height - height);

        for (int row = 0; row < height; row++)
        {
            int src = ((offsetY + row) * snapshot.Width + offsetX) * 4;
            Buffer.BlockCopy(snapshot.Pixels, src, target, row * rowBytes, rowBytes);
        }

        return true;
    }

    /// <summary>
    /// 让 <paramref name="target"/> 往 <paramref name="previous"/> 靠一点，<paramref name="weight"/>
    /// 是新帧的权重（0..256，128 即各占一半）。
    /// </summary>
    /// <remarks>
    /// 抓一张屏要等一次桌面合成，实时刷新最多十几到二十几帧，背景里有视频、滚动这类连续变化
    /// 时，一帧一跳看着就是「闪」。跟上一帧混一下，画面变成连续过渡，观感立刻稳下来——这也
    /// 正是磨砂玻璃该有的样子：背后的东西在动，但透过来的是柔和的一片。
    /// alpha 恒为 255，不参与混合。
    /// </remarks>
    public static void Blend(byte[] previous, byte[] target, int bytes, int weight)
    {
        if (weight >= 256) return;
        if (weight < 0) weight = 0;

        int back = 256 - weight;

        for (int i = 0; i < bytes; i += 4)
        {
            target[i] = (byte)((target[i] * weight + previous[i] * back) >> 8);
            target[i + 1] = (byte)((target[i + 1] * weight + previous[i + 1] * back) >> 8);
            target[i + 2] = (byte)((target[i + 2] * weight + previous[i + 2] * back) >> 8);
        }
    }

    private static bool Grab(int x, int y, int width, int height, int bytes, byte[] pixels)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;

        IntPtr memDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero) return false;

            previous = SelectObject(memDc, bitmap);
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY)) return false;
            return ReadPixels(memDc, bitmap, width, height, bytes, pixels);
        }
        finally
        {
            if (memDc != IntPtr.Zero && previous != IntPtr.Zero) SelectObject(memDc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static bool ReadPixels(IntPtr memDc, IntPtr bitmap, int width, int height, int bytes, byte[] pixels)
    {
        var info = new BITMAPINFO
        {
            Header = new BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = BI_RGB,
            },
        };

        if (GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref info, DIB_RGB_COLORS) == 0)
            return false;

        for (int i = 3; i < bytes; i += 4) pixels[i] = 255;
        return true;
    }

    /// <summary>就地模糊。只处理 BGR 三个通道——alpha 恒为 255，跟着算纯属浪费。</summary>
    private static void Blur(byte[] pixels, byte[] scratch, int width, int height, int radius, int passes)
    {
        if (radius <= 0) return;

        for (int pass = 0; pass < passes; pass++)
        {
            BlurHorizontal(pixels, scratch, width, height, radius);
            BlurVertical(scratch, pixels, width, height, radius);
        }
    }

    private static void BlurHorizontal(byte[] source, byte[] target, int width, int height, int radius)
    {
        int window = radius * 2 + 1;
        double scale = 1.0 / window;
        int last = width - 1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;

            for (int channel = 0; channel < 3; channel++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += source[row + Math.Clamp(i, 0, last) * 4 + channel];

                int index = row + channel;
                for (int x = 0; x < width; x++)
                {
                    target[index] = (byte)(sum * scale + 0.5);
                    index += 4;
                    sum += source[row + Math.Clamp(x + radius + 1, 0, last) * 4 + channel];
                    sum -= source[row + Math.Clamp(x - radius, 0, last) * 4 + channel];
                }
            }
        }
    }

    private static void BlurVertical(byte[] source, byte[] target, int width, int height, int radius)
    {
        int window = radius * 2 + 1;
        double scale = 1.0 / window;
        int last = height - 1;
        int stride = width * 4;

        for (int x = 0; x < width; x++)
        {
            int column = x * 4;

            for (int channel = 0; channel < 3; channel++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += source[Math.Clamp(i, 0, last) * stride + column + channel];

                for (int y = 0; y < height; y++)
                {
                    target[y * stride + column + channel] = (byte)(sum * scale + 0.5);
                    sum += source[Math.Clamp(y + radius + 1, 0, last) * stride + column + channel];
                    sum -= source[Math.Clamp(y - radius, 0, last) * stride + column + channel];
                }
            }
        }
    }
}
