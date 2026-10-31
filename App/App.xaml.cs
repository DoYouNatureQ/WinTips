using System.Threading;
using System.Windows;
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
    private OsdController? _osd;
    private TrayService? _tray;
    private SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AppSettings.LogPath)!);
                System.IO.File.AppendAllText(AppSettings.LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {args.Exception}\n\n");
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AppSettings.LogPath)!);
                System.IO.File.AppendAllText(AppSettings.LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL: {args.ExceptionObject}\n\n");
            }
            catch { }
        };

        _mutex = new Mutex(true, @"Local\WinTips.SingleInstance", out bool createdNew);
        _ownsMutex = createdNew;

        // 读配置并确定界面语言：任何窗口（含下面的单实例提示窗）创建前必须完成
        var settings = AppSettings.Load();
        Loc.Apply(Loc.Parse(settings.Language));

        if (!createdNew)
        {
            new NoticeWindow(Loc.T("NoticeRunningTitle"), Loc.T("NoticeRunningMessage")).ShowDialog();
            Shutdown();
            return;
        }

        settings.LaunchAtStartup = AppSettings.IsStartupSet();

        _osd = new OsdController();
        _hook = new KeyboardHook();
        _hook.ToggleChanged += k => _osd!.OnToggleChanged(k);
        _hook.Pressed += k => _osd!.OnInsertPressed(k);
        _hook.ClipboardPressed += k => _osd!.OnClipboardPressed(k);
        _hook.Install();

        _tray = new TrayService(ShowSettings, Shutdown, (k, s) => _osd.ShowTest(k, s));

        // 跟随系统主题 / 高对比度：切换后让已打开的窗口重刷配色与材质
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            _osd?.PreWarm();

            if (e.Args.Length > 0 && e.Args[0] == "--test")
                RunTest(e.Args.Skip(1).ToArray());
            else if (e.Args.Contains("--settings"))
                ShowSettings();
        }));
    }

    /// <summary>
    /// 系统主题 / 对比度变化回调。SystemEvents 在独立的系统线程触发，
    /// 必须切回 UI 线程再广播，否则会在非创建线程操作控件资源。
    /// </summary>
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle))
            return;

        try { Current?.Dispatcher.BeginInvoke(new Action(ThemeHelper.RaiseChanged)); }
        catch { /* 退出过程中可能已无 Dispatcher */ }
    }

    private void RunTest(string[] args)
    {
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
        _settings = new SettingsWindow((k, s) => _osd!.ShowTest(k, s));
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hook?.Dispose();
        _tray?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
