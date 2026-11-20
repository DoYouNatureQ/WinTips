using System.IO;
using System.Text;
using WinTips.Native;

namespace WinTips.Core;

/// <summary>
/// 诊断日志。默认写到 <c>%TEMP%\wintips.log</c>，设 <c>WINTIPS_DEBUG=off</c> 可关掉。
/// </summary>
/// <remarks>
/// 提示"该弹却没弹"这类问题没法靠读代码定论——断在"按键没被捕获"还是断在"卡片被压住"，
/// 两种表现一模一样。现场只有一条线索：把链路上几个关键点记下来。
///
///   （默认）                                       %TEMP%\wintips.log
///   set WINTIPS_DEBUG=D:\path\wintips.log          换位置
///   set WINTIPS_DEBUG=off                          关掉
///   set WINTIPS_DEBUG_TOPMOST=%TEMP%\wintips.top   只看置顶抢占
///
/// 为什么默认开着：这个问题只能在现场那台机器上复现，要求用户先设环境变量再重启等于
/// 多一道门槛、多半就断了反馈。日志量很小（每次按键两三行），超过上限自动截断，关掉的
/// 开关留着。
///
/// 写文件全程 try/catch 且加锁：日志出问题绝不允许影响提示本身，也不允许两线程写串行。
/// 类名不叫 Trace 是因为 OsdWindow 里有 using System.Diagnostics，会和里面的 Trace 撞。
/// </remarks>
internal static class Diag
{
    private const long MaxBytes = 512 * 1024;

    private const int TrimEvery = 256;

    private static readonly object Gate = new();

    private static int _writes;

    private static readonly string? Main = Resolve("WINTIPS_DEBUG");

    private static readonly string? Topmost = Resolve("WINTIPS_DEBUG_TOPMOST") ?? Main;

    /// <summary>全链路日志是否开着。用来跳过开销大的拼字符串。</summary>
    public static bool Enabled => Main is not null;

    /// <summary>置顶专项日志是否开着（只设了那个开关时也要能写）。</summary>
    public static bool TopmostEnabled => Topmost is not null;

    public static void Write(string line) => Append(Main, line);

    /// <summary>置顶抢占的专项日志，没单独设路径时并入全链路日志。</summary>
    public static void WriteTopmost(string line) => Append(Topmost, line);

    /// <summary>没设就落到 %TEMP%；显式设成 off/0 才是关。</summary>
    private static string? Resolve(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (value is not { Length: > 0 })
            return Path.Combine(Path.GetTempPath(), "wintips.log");

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value == "0") return null;

        return value;
    }

    private static void Append(string? path, string line)
    {
        if (path is null) return;
        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
                if (++_writes % TrimEvery == 0) Trim(path);
            }
        }
        catch
        {
            // 路径不可写、磁盘满、文件被占用——都不该让提示消失。
        }
    }

    /// <summary>放任增长会变成一个几百 MB 的垃圾文件；到上限就重开一个。</summary>
    private static void Trim(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxBytes) info.Delete();
        }
        catch { }
    }

    /// <summary>
    /// 窗口的"类名#pid '标题'"。提权窗口的标题这里读不到（GetWindowText 内部要发
    /// WM_GETTEXT，被 UIPI 拦掉），空标题本身就是"这是个高权限窗口"的证据。
    /// </summary>
    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(none)";

        var cls = new StringBuilder(128);
        NativeMethods.GetClassNameW(hwnd, cls, cls.Capacity);

        var title = new StringBuilder(128);
        NativeMethods.GetWindowTextW(hwnd, title, title.Capacity);

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return $"{cls}#{pid} \"{title}\"";
    }
}
