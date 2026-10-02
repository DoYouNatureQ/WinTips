using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
        if (!createdNew)
        {
            new NoticeWindow("WinTips 已在运行", "另一个 WinTips 实例已经在运行").ShowDialog();
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        settings.LaunchAtStartup = AppSettings.IsStartupSet();

        _osd = new OsdController();
        _hook = new KeyboardHook();
        _hook.ToggleChanged += k => _osd!.OnToggleChanged(k);
        _hook.Pressed += k => _osd!.OnInsertPressed(k);
        _hook.Install();

        _tray = new TrayService(ShowSettings, Shutdown, (k, s) => _osd.ShowTest(k, s));

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            _osd?.PreWarm();

            if (e.Args.Length > 0 && e.Args[0] == "--test")
                RunTest(e.Args.Skip(1).ToArray());
            else if (e.Args.Contains("--settings"))
                ShowSettings();
        }));
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
        _settings = new SettingsWindow();
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
