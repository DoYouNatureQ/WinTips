using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinTips.Core;
using WinTips.UI;

namespace WinTips;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private KeyboardHook? _hook;
    private KeyStateWatcher? _poll;
    private RawInputWatcher? _raw;
    private OsdController? _osd;
    private InputStateWatcher? _input;
    private TrayService? _tray;
    private SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Environment.GetEnvironmentVariable("WINTIPS_GPU") != "1")
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogException(args.ExceptionObject as Exception
                         ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"), fatal: true);
        };

        _mutex = new Mutex(true, @"Local\WinTips.SingleInstance", out bool createdNew);
        _ownsMutex = createdNew;

        var settings = AppSettings.Load();
        Loc.Apply(Loc.Parse(settings.Language));
        AppFont.Apply(settings.UiFont);

        if (!createdNew)
        {
            new NoticeWindow(Loc.T("NoticeRunningTitle"), Loc.T("NoticeRunningMessage")).ShowDialog();
            Shutdown();
            return;
        }

        settings.LaunchAtStartup = StartupManager.IsEnabled();

        // 启动时借前台线程的输入队列读一次锁定键基线。之后状态全部靠"按下即翻转"，
        // 不再读 GetKeyState——那个镜像对没有焦点的线程根本不更新。
        LockState.Initialize();

        _osd = new OsdController();
        _input = new InputStateWatcher();
        _input.LanguageChanged += hkl => _osd!.OnLanguageChanged(hkl);
        _input.ImeModeChanged += native => _osd!.OnImeModeChanged(native);
        _hook = new KeyboardHook();

        // 三条捕获路径统一走 LockState.Toggle：谁先到谁翻转，后到的由去重挡掉。
        // 不读 GetKeyState 取状态——那个镜像会卡住，连按四次读回同一个值。
        // 传进去的是按键发生的时刻，不是处理时刻：UI 线程被抓背景拖住时，同一次按下
        // 在两条路径上的处理时刻能差几百毫秒，比处理时刻就会翻两遍。
        _hook.ToggleChanged += (k, at) =>
        {
            if (LockState.Toggle(k, at, out bool on)) _osd!.OnLockToggled(k, on);
        };
        _hook.Pressed += (k, at) => { if (LockState.Accept(k, at)) _osd!.OnInsertPressed(k); };
        _hook.ClipboardPressed += k => _osd!.OnClipboardPressed(k);
        _hook.SwitchKeyDown += s => _input!.Touch(s);

        // 调试用：设 WINTIPS_NO_HOOK=1 就不装低级钩子。目的是能单独验证"没有钩子时
        // Raw Input 是否自足"——提权的管理员窗口前台时，钩子本来就等于不存在，
        // 这个开关能在普通窗口上把同样的处境复现出来。
        if (Environment.GetEnvironmentVariable("WINTIPS_NO_HOOK") != "1")
            _hook.Install();

        // 低级钩子在前台是提权窗口时会被 UIPI 掐断，系统干脆不回调。两条兜底并着上：
        //   Raw Input —— 走设备栈，跟完整性级别无关，是这里的主力；
        //   轮询     —— 万一 Raw Input 被设备/驱动层挡掉（远控、虚拟键盘），它还能兜。
        // 去重与状态翻转都在 LockState 里，三条路径不会各说各话。
        _poll = new KeyStateWatcher();
        _poll.ToggleChanged += (k, on) => _osd!.OnLockToggled(k, on);
        _poll.Pressed += k => _osd!.OnInsertPressed(k);
        // 前台是提权/受保护进程时钩子收不到按键，粘贴不动剪贴板、没有监听可兜，
        // 由轮询识别 Ctrl+C/V/X、Shift+Delete 补上提示；与钩子的重合由 LockState 去重。
        _poll.ClipboardPressed += k => _osd!.OnClipboardPressed(k);
        _poll.Start();

        _raw = new RawInputWatcher();
        _raw.ToggleChanged += (k, on) => _osd!.OnLockToggled(k, on);
        _raw.Pressed += k => _osd!.OnInsertPressed(k);

        // Raw Input 能用 E0 前缀分清小键盘 0 和真 Insert，GetAsyncKeyState 分不清
        // （Num Lock 关闭时小键盘 0 的虚拟键就是 VK_INSERT）。它注册成功后，轮询对
        // Insert 就别再插话；注册失败时这个委托返回 false，轮询照旧兜底。
        _poll.InsertCoveredByRawInput = () => _raw!.Registered;

        _raw.Start();

        _tray = new TrayService(ShowSettings, Shutdown, (k, s) => _osd.ShowTest(k, s));

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            _osd?.PreWarm();

            if (e.Args.Length > 0 && e.Args[0] == "--test")
            {
                RunTest(e.Args.Skip(1).ToArray());
                return;
            }

            if (e.Args.Contains("--settings")) ShowSettings();

            ShowWelcomeIfFirstRun(settings);
        }));
    }

    private void ShowWelcomeIfFirstRun(AppSettings settings)
    {
        if (settings.WelcomeShown) return;

        settings.WelcomeShown = true;
        settings.Save();

        try { _osd?.ShowWelcome(); }
        catch (Exception ex) { LogException(ex); }
    }

    private static void LogException(Exception ex, bool fatal = false)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AppSettings.LogPath)!);
            string tag = fatal ? "FATAL: " : "";
            System.IO.File.AppendAllText(AppSettings.LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}{ex}\n\n");
        }
        catch { }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle))
            return;

        try { Current?.Dispatcher.BeginInvoke(new Action(ThemeHelper.RaiseChanged)); }
        catch {  }
    }

    private void RunTest(string[] args)
    {
        if (args.Length >= 1 && args[0].Equals("menu", StringComparison.OrdinalIgnoreCase))
        {
            _tray?.ShowMenuAtCursor();
            return;
        }

        if (args.Length >= 1 && TipInfo.TryParseKind(args[0], out var kind))
        {
            bool? state = args.Length >= 2
                ? args[1] switch { "on" => true, "off" => false, _ => (bool?)null }
                : null;
            double hold = args.Length >= 3 && double.TryParse(args[2], out var h) ? h : 0;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                _osd!.ShowTest(kind, state, hold > 0 ? hold : null)));
        }
    }

    private void ShowSettings()
    {
        if (_settings != null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(
            (k, s) => _osd!.ShowTest(k, s),
            s => _osd!.PreviewSize(s),
            s => _osd!.PreviewAppearance(s));
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        AppSettings.FlushPendingSave();
        _hook?.Dispose();
        _poll?.Dispose();
        _raw?.Dispose();
        _input?.Dispose();
        _tray?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
