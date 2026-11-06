using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace WinTips.Core;

public sealed class AppSettings
{
    public bool ShowCapsLock { get; set; } = true;
    public bool ShowNumLock { get; set; } = true;
    public bool ShowScrollLock { get; set; } = true;
    public bool ShowInsert { get; set; } = true;
    public bool ShowCopy { get; set; } = true;
    public bool ShowPaste { get; set; } = true;
    public bool ShowCut { get; set; } = true;

    /// <summary>
    /// 任意复制方式都提示：右键菜单、应用内“复制”按钮等非键盘复制（剪贴板更新即触发，
    /// 期间清空剪贴板的操作不会提示）。需同时开启 ShowCopy 才生效。
    /// </summary>
    public bool ShowAnyCopy { get; set; } = true;
    public double DisplaySeconds { get; set; } = 2.0;

    /// <summary>浮窗缩放范围（1.0 = 原始 160×50 基准），文字/图标/指示条一并等比缩放。</summary>
    public const double MinTipScale = 0.6;
    public const double MaxTipScale = 2.0;

    /// <summary>浮窗大小倍率：0.6–2.0，默认 1.0。</summary>
    public double TipScale { get; set; } = 1.0;

    /// <summary>容错归一化：缺省/非法值回落 1.0，越界钳进取值范围并按 1% 取整（滑杆刻度感）。</summary>
    public static double NormalizeScale(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(Math.Round(value, 2), MinTipScale, MaxTipScale)
            : 1.0;

    /// <summary>
    /// 浮窗是否启用"真毛玻璃"材质（抓屏模糊 + 底色 + 受光描边）。
    ///
    /// 默认开启：自绘模糊才是和 Win11 任务栏一致的观感，代价是每次弹出多一次抓屏。
    /// 关闭后只剩系统亚克力（DWMSBT_TRANSIENTWINDOW）——零开销、零兼容风险，
    /// 但只带一点壁纸色温，透不出背后的内容。
    /// </summary>
    public bool TipGlass { get; set; } = true;

    public const double MinGlassOpacity = 0.20;
    public const double MaxGlassOpacity = 0.90;
    public const double DefaultGlassOpacity = 0.55;

    /// <summary>
    /// 毛玻璃底色（tint）的不透明度：越低越透、背景越清晰，越高越接近实心。
    /// 默认 0.55 是"看得见背后在动"与"文字不失读"的平衡点——再高就回到系统
    /// 材质那种看不出模糊的实心板，再低浅色壁纸上深色文字会糊。
    /// </summary>
    public double TipGlassOpacity { get; set; } = DefaultGlassOpacity;

    /// <summary>容错归一化：缺省/非法值回落默认值，越界钳进取值范围（两位小数 = 滑杆刻度）。</summary>
    public static double NormalizeGlassOpacity(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(Math.Round(value, 2), MinGlassOpacity, MaxGlassOpacity)
            : DefaultGlassOpacity;

    /// <summary>
    /// 开机自启的镜像值，仅供设置序列化与排查用；真实状态一律以
    /// <see cref="StartupManager.IsEnabled"/> 为准（注册表或 WinRT 启动任务）。
    /// </summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>
    /// 界面语言：System（跟随系统）/ zh / zh-Hant / en / de / ja / ru / es / pt。
    /// 解析与回写在 <see cref="Loc.Parse"/> / <see cref="Loc.ToSetting"/>，新增语言只需改这两处。
    /// </summary>
    public string Language { get; set; } = "System";

    /// <summary>
    /// 界面字体族名：空串 = 内置的 HarmonyOS Sans SC（随包分发，见 <see cref="AppFont"/>）。
    /// 存的是系统字族的英文族名（Windows 上唯一且不随界面语言变），运行时解析不到
    /// （字体被卸载或改名）会静默回落到内置字体。
    /// </summary>
    public string UiFont { get; set; } = "";

    public const string PlacementLeft = "Left";
    public const string PlacementCenter = "Center";
    public const string PlacementRight = "Right";

    public const string PlacementTop = "Top";
    public const string PlacementBottom = "Bottom";

    /// <summary>浮窗水平位置：Left / Center / Right（默认居中）。</summary>
    public string TipPlacement { get; set; } = PlacementCenter;

    /// <summary>浮窗垂直位置：Top（贴屏幕顶部）/ Bottom（贴任务栏上方，默认）。</summary>
    public string TipVertical { get; set; } = PlacementBottom;

    /// <summary>
    /// 提示浮窗跟随焦点显示器（默认开启）：弹出时取当前前台窗口所在的那块屏，
    /// 而不是固定落在主显示器。关闭后行为与旧版一致，只显示在主显示器；
    /// 自定义位置按“相对该屏左上角”解释。
    /// </summary>
    public bool TipFollowFocus { get; set; } = true;

    /// <summary>容错归一化：非法值一律回落为居中。</summary>
    public static string NormalizePlacement(string? value) =>
        value == PlacementLeft || value == PlacementRight ? value : PlacementCenter;

    /// <summary>容错归一化：非法值一律回落为底部（任务栏上方，与旧版本行为一致）。</summary>
    public static string NormalizeVertical(string? value) =>
        value == PlacementTop ? PlacementTop : PlacementBottom;

    /// <summary>自定义位置的浮窗左上角（DIP，主屏坐标系）。两项都有值才生效并覆盖 TipPlacement / TipVertical。</summary>
    public double? CustomLeft { get; set; }
    public double? CustomTop { get; set; }

    [JsonIgnore]
    public bool HasCustomPosition => CustomLeft.HasValue && CustomTop.HasValue;

    /// <summary>自定义位置字段损坏（NaN/Infinity/只剩一项）时整体作废，回落到预设位置。</summary>
    private static void NormalizeCustomPosition(AppSettings s)
    {
        if (s.CustomLeft is not double l || !double.IsFinite(l) ||
            s.CustomTop is not double t || !double.IsFinite(t))
        {
            s.CustomLeft = null;
            s.CustomTop = null;
        }
    }

    /// <summary>设置文件结构版本；V1.1 及以前没有该字段（AnimationSpeed 为整数档位），V3 起玻璃材质默认开启。</summary>
    public int SettingsVersion { get; set; } = CurrentSettingsVersion;

    private const int CurrentSettingsVersion = 3;

    /// <summary>浮现动画速度倍率：0 无动画，否则动画时长 = 300ms × 倍率（0.1–3.0，步进 0.1）。</summary>
    public double AnimationSpeed { get; set; } = 1.0;

    /// <summary>倍率对应的动画时长（毫秒）。</summary>
    public static int AnimationMs(double speed) =>
        speed <= 0 ? 0 : (int)Math.Round(300 * speed);

    public int AnimationMs() => AnimationMs(AnimationSpeed);

    [JsonIgnore]
    public static AppSettings Instance { get; private set; } = new();

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinTips");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static string LogPath => Path.Combine(Dir, "error.log");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(text);
                if (s != null)
                {
                    s.TipPlacement = NormalizePlacement(s.TipPlacement);
                    s.TipVertical = NormalizeVertical(s.TipVertical);
                    s.TipScale = NormalizeScale(s.TipScale);
                    s.TipGlassOpacity = NormalizeGlassOpacity(s.TipGlassOpacity);
                    NormalizeCustomPosition(s);
                    bool migrated = MigrateFromV1(s, text);
                    migrated |= MigrateGlassDefault(s);
                    if (migrated) s.Save();
                    Instance = s;
                    return s;
                }
            }
        }
        catch { }
        Instance = new AppSettings();
        return Instance;
    }

    /// <summary>
    /// V1.1 及以前 AnimationSpeed 是 0–5 整数档位（档距 0.5x），改为 0–3 倍率（步进 0.1）后，
    /// 旧档位 g 等价于倍率 0.5 × g（档位 3=1.5x → 倍率 1.5，动画毫秒数不变）。
    /// </summary>
    private static bool MigrateFromV1(AppSettings s, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                doc.RootElement.TryGetProperty(nameof(SettingsVersion), out _))
                return false;

            if (doc.RootElement.TryGetProperty(nameof(AnimationSpeed), out var el) &&
                el.ValueKind == JsonValueKind.Number)
            {
                double gear = el.GetDouble();
                s.AnimationSpeed = gear <= 0 ? 0 : Math.Min(gear * 0.5, 3.0);
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// V3 起玻璃材质改为默认开启：旧版设置文件里 TipGlass 为 false 的绝大多数只是
    /// 沿用了当时的默认值（要显式关掉得先开一次），所以这里统一扳到 true 并升版本，
    /// 让升级后的观感和新装一致。升到 V3 之后用户的开关不再被改写。
    /// </summary>
    private static bool MigrateGlassDefault(AppSettings s)
    {
        if (s.SettingsVersion >= CurrentSettingsVersion) return false;
        s.TipGlass = true;
        s.SettingsVersion = CurrentSettingsVersion;
        return true;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SaveOptions));
        }
        catch { }
    }

    /// <summary>
    /// 序列化选项必须是静态单例：<see cref="JsonSerializerOptions"/> 内部缓存着整套类型元数据，
    /// 每次 new 一个都等于重建一份缓存，用完即弃——滑杆这类高频保存会持续制造这份开销。
    /// </summary>
    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

    private static DispatcherTimer? _pendingSave;

    /// <summary>
    /// 延迟落盘：滑杆拖动时 ValueChanged 每 tick 触发一次保存，直接写文件会把一次拖拽变成
    /// 几十次磁盘写入。这里合并成 400ms 后的单次写入；退出前由 <see cref="FlushPendingSave"/> 兜底。
    /// </summary>
    public void SaveSoon()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) { Save(); return; } // 无 UI 线程（测试/退出中）直接写

        if (_pendingSave is null)
        {
            _pendingSave = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _pendingSave.Tick += (_, _) =>
            {
                _pendingSave!.Stop();
                Instance.Save();
            };
        }

        _pendingSave.Stop();
        _pendingSave.Interval = TimeSpan.FromMilliseconds(400);
        _pendingSave.Start();
    }

    /// <summary>把待写的设置立刻落盘，否则用户拖完滑杆直接退出会丢掉最后一次改动。</summary>
    public static void FlushPendingSave()
    {
        if (_pendingSave?.IsEnabled != true) return;
        _pendingSave.Stop();
        Instance.Save();
    }
}
