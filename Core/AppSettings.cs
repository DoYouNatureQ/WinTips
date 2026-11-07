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

    public bool ShowLanguageTip { get; set; }

    public bool ShowAnyCopy { get; set; } = true;
    public double DisplaySeconds { get; set; } = 2.0;

    public const double MinTipScale = 0.6;
    public const double MaxTipScale = 2.0;

    public double TipScale { get; set; } = 1.0;

    public static double NormalizeScale(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(Math.Round(value, 2), MinTipScale, MaxTipScale)
            : 1.0;

    public bool TipGlass { get; set; } = true;

    public const double MinGlassOpacity = 0.20;
    public const double MaxGlassOpacity = 0.90;
    public const double DefaultGlassOpacity = 0.55;

    public double TipGlassOpacity { get; set; } = DefaultGlassOpacity;

    public static double NormalizeGlassOpacity(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(Math.Round(value, 2), MinGlassOpacity, MaxGlassOpacity)
            : DefaultGlassOpacity;

    public bool LaunchAtStartup { get; set; }

    public bool WelcomeShown { get; set; }

    public string Language { get; set; } = "System";

    public string UiFont { get; set; } = "";

    public const string PlacementLeft = "Left";
    public const string PlacementCenter = "Center";
    public const string PlacementRight = "Right";

    public const string PlacementTop = "Top";
    public const string PlacementBottom = "Bottom";

    public string TipPlacement { get; set; } = PlacementCenter;

    public string TipVertical { get; set; } = PlacementBottom;

    public bool TipFollowFocus { get; set; } = true;

    public static string NormalizePlacement(string? value) =>
        value == PlacementLeft || value == PlacementRight ? value : PlacementCenter;

    public static string NormalizeVertical(string? value) =>
        value == PlacementTop ? PlacementTop : PlacementBottom;

    public double? CustomLeft { get; set; }
    public double? CustomTop { get; set; }

    [JsonIgnore]
    public bool HasCustomPosition => CustomLeft.HasValue && CustomTop.HasValue;

    private static void NormalizeCustomPosition(AppSettings s)
    {
        if (s.CustomLeft is not double l || !double.IsFinite(l) ||
            s.CustomTop is not double t || !double.IsFinite(t))
        {
            s.CustomLeft = null;
            s.CustomTop = null;
        }
    }

    public int SettingsVersion { get; set; } = CurrentSettingsVersion;

    private const int CurrentSettingsVersion = 5;

    private const int GlassOnVersion = 3;

    private const int LanguageTipOffVersion = 4;

    private const int WelcomeShownVersion = 5;

    public double AnimationSpeed { get; set; } = 1.0;

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
                    migrated |= MigrateLanguageTipDefault(s);
                    migrated |= MarkWelcomeShownForUpgrades(s);
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

            s.WelcomeShown = true;
            return true;
        }
        catch { return false; }
    }

    private static bool MigrateGlassDefault(AppSettings s)
    {
        if (s.SettingsVersion >= GlassOnVersion) return false;
        s.TipGlass = true;
        s.SettingsVersion = GlassOnVersion;
        return true;
    }

    private static bool MigrateLanguageTipDefault(AppSettings s)
    {
        if (s.SettingsVersion >= LanguageTipOffVersion) return false;
        s.ShowLanguageTip = false;
        s.SettingsVersion = LanguageTipOffVersion;
        return true;
    }

    private static bool MarkWelcomeShownForUpgrades(AppSettings s)
    {
        if (s.SettingsVersion >= WelcomeShownVersion) return false;
        s.WelcomeShown = true;
        s.SettingsVersion = WelcomeShownVersion;
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

    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

    private static DispatcherTimer? _pendingSave;

    public void SaveSoon()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) { Save(); return; }

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

    public static void FlushPendingSave()
    {
        if (_pendingSave?.IsEnabled != true) return;
        _pendingSave.Stop();
        Instance.Save();
    }
}
