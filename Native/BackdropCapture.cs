using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinTips.Native;

internal static class BackdropCapture
{
    private const int SRCCOPY = 0x00CC0020;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    private const int BlurPasses = 3;

    private static IntPtr _memDc;
    private static IntPtr _hBitmap;
    private static IntPtr _previousObject;

    private static int _bufferWidth;
    private static int _bufferHeight;

    private static byte[] _pixels = Array.Empty<byte>();
    private static byte[] _scratch = Array.Empty<byte>();

    private static WriteableBitmap? _output;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint start, uint lines,
        byte[] bits, ref BITMAPINFO info, uint usage);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    public static BitmapSource? CaptureBlurred(IntPtr hwnd, int padding, int radius)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var box)) return null;

        int width = box.Right - box.Left;
        int height = box.Bottom - box.Top;
        if (width <= 0 || height <= 0) return null;

        int grabX = box.Left - padding;
        int grabY = box.Top - padding;
        int grabW = width + padding * 2;
        int grabH = height + padding * 2;

        if (!EnsureBuffers(grabW, grabH, width, height)) return null;

        // 上一块提示可能刚淡出隐藏，等一次桌面合成再抓，免得把自家卡片拍进快照
        // （那样快照就是一片纯色，卡片看起来就成了纯色背景）。
        try { DwmFlush(); } catch { }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        try
        {
            if (!BitBlt(_memDc, 0, 0, grabW, grabH, screenDc, grabX, grabY, SRCCOPY)) return null;
            if (!ReadPixels(grabW, grabH)) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        Blur(_pixels, _scratch, grabW, grabH, radius);

        _output!.WritePixels(
            new Int32Rect(0, 0, width, height),
            _pixels,
            grabW * 4,
            (padding * grabW + padding) * 4);

        return _output;
    }

    private static bool EnsureBuffers(int grabW, int grabH, int width, int height)
    {
        if (_output is not null && _bufferWidth == grabW && _bufferHeight == grabH) return true;

        ReleaseBuffers();

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        try
        {
            _memDc = CreateCompatibleDC(screenDc);
            _hBitmap = CreateCompatibleBitmap(screenDc, grabW, grabH);
            if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero)
            {
                ReleaseBuffers();
                return false;
            }
            _previousObject = SelectObject(_memDc, _hBitmap);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        _bufferWidth = grabW;
        _bufferHeight = grabH;
        _pixels = new byte[grabW * grabH * 4];
        _scratch = new byte[_pixels.Length];
        _output = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        return true;
    }

    private static void ReleaseBuffers()
    {
        if (_memDc != IntPtr.Zero && _previousObject != IntPtr.Zero)
        {
            SelectObject(_memDc, _previousObject);
        }
        if (_hBitmap != IntPtr.Zero) DeleteObject(_hBitmap);
        if (_memDc != IntPtr.Zero) DeleteDC(_memDc);

        _memDc = IntPtr.Zero;
        _hBitmap = IntPtr.Zero;
        _previousObject = IntPtr.Zero;
        _bufferWidth = 0;
        _bufferHeight = 0;
        _pixels = Array.Empty<byte>();
        _scratch = Array.Empty<byte>();
        _output = null;
    }

    private static bool ReadPixels(int width, int height)
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

        if (GetDIBits(_memDc, _hBitmap, 0, (uint)height, _pixels, ref info, DIB_RGB_COLORS) == 0)
            return false;

        for (int i = 3; i < _pixels.Length; i += 4) _pixels[i] = 255;
        return true;
    }

    private static void Blur(byte[] pixels, byte[] scratch, int width, int height, int radius)
    {
        if (radius <= 0) return;

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
