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

        _osd = new OsdController();
        _input = new InputStateWatcher();
        _input.LanguageChanged += hkl => _osd!.OnLanguageChanged(hkl);
        _input.ImeModeChanged += native => _osd!.OnImeModeChanged(native);
        _hook = new KeyboardHook();
        _hook.ToggleChanged += k => _osd!.OnToggleChanged(k);
        _hook.Pressed += k => _osd!.OnInsertPressed(k);
        _hook.ClipboardPressed += k => _osd!.OnClipboardPressed(k);
        _hook.SwitchKeyDown += s => _input!.Touch(s);
        _hook.Install();

        // 钩子只在"前台窗口不比本进程高"时才有回调。提权的管理员窗口一抢到焦点，
        // 系统就不叫我们了，得靠轮询兜底（只在那种时刻才跑，平时不耗电）。
        _poll = new KeyStateWatcher();
        _poll.ToggleChanged += k => _osd!.OnToggleChanged(k);
        _poll.Pressed += k => _osd!.OnInsertPressed(k);
        _poll.Start();

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
