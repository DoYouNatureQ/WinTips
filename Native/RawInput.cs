using System.Runtime.InteropServices;

namespace WinTips.Native;

/// <summary>
/// Raw Input 相关声明。单独开一个 partial 文件，免得往 <c>NativeMethods.cs</c> 那个大文件里再塞。
/// </summary>
/// <remarks>
/// 为什么需要它：低级键盘钩子（WH_KEYBOARD_LL）在前台窗口的进程完整性级别高于本进程时
/// 会被 UIPI 掐断，系统干脆不回调——这就是"管理员窗口里按 CapsLock 没反应"的根因。
/// 键状态轮询（GetAsyncKeyState）能绕过它，但采样有周期，极快的轻敲可能落在两次采样之间，
/// 而且 50ms 一次的常驻唤醒本身也不优雅。
///
/// Raw Input 走的是另一条路：设备栈 → 原始输入线程 → 直接投递给注册了 RIDEV_INPUTSINK
/// 的窗口。它不经过前台窗口的消息过滤，所以跟完整性级别无关，也不需要高频轮询。
/// </remarks>
internal static partial class NativeMethods
{
    public const uint WM_INPUT = 0x00FF;

    public const uint RID_INPUT = 0x10000003;

    public const uint RIM_TYPEKEYBOARD = 1;

    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;

    public const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;

    /// <summary>不管窗口是否在前台都收输入；这正是"提权窗口在前台时仍要收到"所需的标志。</summary>
    public const uint RIDEV_INPUTSINK = 0x00000100;

    /// <summary>这一条是按键释放，不是按下。</summary>
    public const ushort RI_KEY_BREAK = 0x01;

    /// <summary>该键带 E0 前缀（如右侧 Ctrl、Insert、方向键）。</summary>
    public const ushort RI_KEY_E0 = 0x02;

    /// <summary>该键带 E1 前缀（Pause）。</summary>
    public const ushort RI_KEY_E1 = 0x04;

    /// <summary>Raw Input 给的 VKey 在"这个键没有对应虚拟键"时是 0xFF，此时只能靠扫描码认。</summary>
    public const ushort VK_NONE_RAW = 0xFF;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;

        /// <summary>文档要求忽略此字段。</summary>
        public ushort Reserved;

        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    /// <summary>
    /// 失败返回 <c>(uint)-1</c>。第一次调用传 <paramref name="pData"/> = NULL 只为拿所需缓冲区大小。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
}
