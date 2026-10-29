using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace WinTips.Core;

/// <summary>界面语言：跟随系统 / 简体中文 / English。</summary>
public enum AppLanguage
{
    System,
    Chinese,
    English,
}

/// <summary>
/// 运行时本地化：一张中英对照词条表 + 语言切换通知。
/// XAML 用 <c>Text="{loc:Tr SettingsTitle}"</c>（见 <see cref="TrExtension"/>）绑定到索引器，
/// 切换语言时以 "Item[]" 通知让所有绑定在同一帧刷新，无需重建窗口；
/// 代码里用 <see cref="T"/> / <see cref="Format"/> 取词。
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, (string Zh, string En)> Table = new(StringComparer.Ordinal);

    static Loc()
    {
        // —— 设置窗口 ——
        Add("SettingsTitle", "WinTips 设置", "WinTips Settings");
        Add("SettingsHeader", "键盘状态提示", "Keyboard status tips");
        Add("SettingsSubtitle", "按下对应按键时，在屏幕顶部或任务栏上方显示系统风格提示",
            "A system-style tip at the top or above the taskbar on each key press");

        Add("SectionKeys", "提示键位", "Keys");
        Add("KeyCapsLockLong", "大写锁定（Caps Lock）", "Caps Lock");
        Add("KeyNumLockLong", "数字锁定（Num Lock）", "Num Lock");
        Add("KeyScrollLockLong", "滚动锁定（Scroll Lock）", "Scroll Lock");
        Add("RowInsert", "Insert 键（事件提示，无开/关状态）", "Insert key (event only)");
        Add("RowCopy", "复制提示（Ctrl+C）", "Copy tip (Ctrl+C)");
        Add("RowPaste", "粘贴提示（Ctrl+V）", "Paste tip (Ctrl+V)");
        Add("RowCut", "剪切提示（Ctrl+X）", "Cut tip (Ctrl+X)");
        Add("RowAnyCopy", "任意复制方式都提示", "Tip for any copy method");
        Add("RowAnyCopyDesc", "右键菜单、应用内“复制”按钮等非键盘复制也弹出提示（需开启复制提示）",
            "Also tip for context menus and in-app Copy buttons (requires the Copy tip)");

        Add("SectionGeneral", "通用", "General");
        Add("RowDuration", "提示停留时长", "Display duration");
        Add("RowAnimSpeed", "浮现动画速度", "Animation speed");
        Add("RowPosition", "提示位置", "Tip position");
        Add("PositionTop", "顶部", "Top");
        Add("PositionBottom", "底部", "Bottom");
        Add("PositionLeft", "左侧", "Left");
        Add("PositionCenter", "居中", "Center");
        Add("PositionRight", "右侧", "Right");
        Add("RowCustomPos", "自定义位置", "Custom position");
        Add("RowCustomPosDesc", "把提示固定到屏幕任意位置", "Pin the tip anywhere on screen");
        Add("BtnAdjustPos", "调整位置", "Adjust");
        Add("BtnResetPos", "还原默认", "Reset");
        Add("RowStartup", "开机自动运行", "Run at startup");

        Add("RowLanguage", "语言", "Language");
        Add("LangSystem", "跟随系统", "System");
        Add("LangChinese", "简体中文", "简体中文");
        Add("LangEnglish", "English", "English");

        // —— 数值标签 ——
        Add("ValueSeconds", "{0} 秒", "{0} s");
        Add("ValueAnimSpeed", "{0}（{1} 毫秒）", "{0} ({1} ms)");
        Add("AnimNone", "无动画", "No animation");

        // —— 浮窗文案 ——
        Add("KeyCapsLock", "大写锁定", "Caps Lock");
        Add("KeyNumLock", "数字锁定", "Num Lock");
        Add("KeyScrollLock", "滚动锁定", "Scroll Lock");
        Add("StateOn", "开启", "On");
        Add("StateOff", "关闭", "Off");
        Add("TipToggleFormat", "{0} {1}", "{0} {1}");
        Add("TipInsertPressed", "Insert 键已按下", "Insert pressed");
        Add("TipCopied", "已复制", "Copied");
        Add("TipPasted", "已粘贴", "Pasted");
        Add("TipCut", "已剪切", "Cut");
        Add("SampleTipOn", "大写锁定 开启", "Caps Lock On");

        // —— 托盘菜单 ——
        Add("MenuSettings", "设置", "Settings");
        Add("MenuTest", "测试提示", "Test tips");
        Add("MenuExit", "退出", "Exit");
        Add("TrayTooltip", "WinTips 键盘状态提示", "WinTips keyboard status tips");

        // —— 自定义位置模式 ——
        Add("PickTitle", "自定义提示位置", "Custom tip position");
        Add("PickHint", "按住浮窗拖到目标位置", "Drag the tip where you want it");
        Add("PickCancel", "取消", "Cancel");
        Add("PickSave", "保存位置", "Save position");

        // —— 单实例提示窗 ——
        Add("NoticeRunningTitle", "WinTips 已在运行", "WinTips is already running");
        Add("NoticeRunningMessage", "另一个 WinTips 实例已经在运行", "Another instance of WinTips is already running");
        Add("NoticeOk", "确定", "OK");
    }

    private static void Add(string key, string zh, string en) => Table[key] = (zh, en);

    /// <summary>当前语言设置（可能是 System）。</summary>
    public static AppLanguage Language { get; private set; } = AppLanguage.System;

    /// <summary>当前实际生效的是否为英文（System 时按系统 UI 语言判定）。</summary>
    public static bool IsEnglish { get; private set; }

    /// <summary>语言切换后触发（用于刷新代码里临时拼好的文案，如滑杆数值标签）。</summary>
    public static event Action? Changed;

    /// <summary>应用语言。必须在创建任何窗口之前调用一次。</summary>
    public static void Apply(AppLanguage language)
    {
        Language = language;
        IsEnglish = language switch
        {
            AppLanguage.English => true,
            AppLanguage.Chinese => false,
            _ => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("zh", StringComparison.OrdinalIgnoreCase),
        };

        TrSource.Instance.Refresh();
        Changed?.Invoke();
    }

    /// <summary>设置值 → 枚举（非法值回落为跟随系统）。</summary>
    public static AppLanguage Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "zh" or "cn" or "zh-cn" or "chinese" => AppLanguage.Chinese,
        "en" or "en-us" or "english" => AppLanguage.English,
        _ => AppLanguage.System,
    };

    /// <summary>枚举 → 设置值。</summary>
    public static string ToSetting(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "zh",
        AppLanguage.English => "en",
        _ => "System",
    };

    /// <summary>取词条；缺词条时原样返回键名（便于发现漏翻）。</summary>
    public static string T(string key) =>
        Table.TryGetValue(key, out var entry) ? (IsEnglish ? entry.En : entry.Zh) : key;

    /// <summary>取带占位符的词条并格式化。</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}

/// <summary>
/// XAML 绑定的数据源：索引器承担取词，切换语言时以 "Item[]" 通知刷新全部索引器绑定。
/// </summary>
public sealed class TrSource : INotifyPropertyChanged
{
    public static TrSource Instance { get; } = new();

    private TrSource() { }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>
/// XAML 标记扩展：<c>Text="{loc:Tr SettingsTitle}"</c>。
/// 返回绑定而非静态字符串，语言切换时文案随之更新，窗口无需重建。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return string.Empty;

        return new Binding($"[{Key}]")
        {
            Source = TrSource.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        }.ProvideValue(serviceProvider);
    }
}
