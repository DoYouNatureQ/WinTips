using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using WinTips.Core;
using WinTips.Native;

namespace WinTips.UI;

/// <summary>
/// 许可与致谢弹窗（点设置页底部的版本号打开）。
///
/// HarmonyOS Sans 字体许可第 2 条第 1 款要求在**软件内**注明使用了该字体，第 4 款要求随副本
/// 保留版权声明与协议；只在 README 里写不够（用户下载到的只有一个 exe）。因此这里展出字体
/// 出处，并把协议全文收在「查看协议全文」后面——默认不摊开（没人愿意读一屏法务文本），
/// 但离线也一定读得到：协议从程序集的 <c>EmbeddedResource</c> 读取，和字体一起打进 exe。
/// 官网原文另给一个外链，仅作核对用，不作为唯一来源（外链会失效，离线也打不开）。
/// </summary>
public partial class LicenseWindow : Window
{
    /// <summary>内嵌协议文件名（编译进程序集时的逻辑名以它为结尾）。</summary>
    private const string AgreementResourceSuffix = "LICENSE_Fonts.txt";

    /// <summary>协议全文当前是否展开（默认收起，弹窗只有一屏出头）。</summary>
    private bool _agreementOpen;

    public LicenseWindow()
    {
        InitializeComponent();
        ApplyPlatformChrome();
        RefreshSubtitle();
        RefreshAgreementToggle();
        ApplyTheme();
        LoadAgreement();

        // 语言/主题在弹窗开着时被改（另一个窗口改的）也要跟着变
        Loc.Changed += OnLocChanged;
        ThemeHelper.Changed += ApplyTheme;
        Closed += (_, _) =>
        {
            Loc.Changed -= OnLocChanged;
            ThemeHelper.Changed -= ApplyTheme;
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    /// <summary>
    /// 与设置窗口同一套窗口边框策略：Win11 用系统标题栏（自带圆角）；
    /// Win10 拿不到圆角，系统标题栏会露出方框，改成无边框 + 自绘标题栏。
    /// </summary>
    private void ApplyPlatformChrome()
    {
        if (DwmChrome.SupportsFluent) return;
        WindowStyle = WindowStyle.None;
        LegacyCaption.Visibility = Visibility.Visible;
    }

    /// <summary>副标题带版本号，只能代码里拼（XAML 标记扩展不认占位符）。</summary>
    private void RefreshSubtitle() =>
        Subtitle.Text = Loc.Format("LicenseSubtitle", AppInfo.DisplayVersion);

    /// <summary>语言变了：副标题与展开开关的文案都要重取（两者都是代码里写的，绑定管不到）。</summary>
    private void OnLocChanged()
    {
        RefreshSubtitle();
        RefreshAgreementToggle();
    }

    /// <summary>展开开关的文案：收起时是「查看协议全文」，展开后换成「收起」。</summary>
    private void RefreshAgreementToggle()
    {
        AgreementToggle.Inlines.Clear();
        AgreementToggle.Inlines.Add(new Run(Loc.T(_agreementOpen ? "LicenseHideAgreement" : "LicenseShowAgreement")));
    }

    /// <summary>
    /// 就地展开/收起协议全文。窗口是 SizeToContent="Height"，收起后自己会缩回一屏出头，
    /// 不必再开一层窗口，也不把整段法务文本摆在默认视图里。
    /// </summary>
    private void AgreementToggle_Click(object sender, RoutedEventArgs e)
    {
        _agreementOpen = !_agreementOpen;
        TextCard.Visibility = _agreementOpen ? Visibility.Visible : Visibility.Collapsed;
        RefreshAgreementToggle();

        if (!_agreementOpen) return;
        AgreementText.CaretIndex = 0;
        AgreementText.ScrollToHome();
    }

    /// <summary>
    /// 弹窗用实色窗口底（不走 Mica）：小尺寸对话框上 Mica 会让人分不清卡片边界，
    /// 系统自己的许可类对话框也是实色。
    /// </summary>
    private void ApplyTheme()
    {
        bool dark = ThemeHelper.IsDarkTheme;
        ThemeHelper.ApplyTemplateResources(this, dark);

        var primary = new SolidColorBrush(ThemeHelper.TextPrimary(dark));
        var secondary = new SolidColorBrush(ThemeHelper.TextSecondary(dark));

        Background = new SolidColorBrush(ThemeHelper.WindowSurface(dark));
        Foreground = primary;

        Subtitle.Foreground = secondary;
        FontDesc.Foreground = secondary;
        Credit.Foreground = secondary;
        Sep1.Background = new SolidColorBrush(ThemeHelper.Divider(dark));

        // 协议正文用主文字色：11.5px 的次要色在深色下已经不太好读了
        AgreementText.Foreground = primary;
        AgreementText.CaretBrush = primary;
        AgreementText.SelectionBrush = new SolidColorBrush(ThemeHelper.Tint(ThemeHelper.AccentColor, 0x66));

        ApplyTitleBar();
    }

    /// <summary>深色标题栏 + 圆角（Win10 上两个调用都直接返回 false，无副作用）。</summary>
    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        DwmChrome.TrySetDarkTitleBar(hwnd, ThemeHelper.IsDarkTheme);
        DwmChrome.TrySetCorner(hwnd, NativeMethods.DWMWCP_ROUND);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTitleBar();
    }

    /// <summary>
    /// 读内嵌的协议全文。按后缀找资源名而不是写死 "WinTips.Resources.Fonts.LICENSE_Fonts.txt"：
    /// 改文件名/改 RootNamespace 时不会静默变成空文本框。读不到就写一句说明，不至于让弹窗空白。
    /// </summary>
    private void LoadAgreement()
    {
        try
        {
            var asm = typeof(LicenseWindow).Assembly;
            string? name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith(AgreementResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (name is null)
            {
                AgreementText.Text = "License text is missing from this build.";
                return;
            }

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null)
            {
                AgreementText.Text = "License text is missing from this build.";
                return;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            AgreementText.Text = Reflow(reader.ReadToEnd());
            AgreementText.CaretIndex = 0;
            AgreementText.ScrollToHome();
        }
        catch (Exception)
        {
            // 资源损坏也不能让“看许可”这个功能把程序搞崩
            AgreementText.Text = "License text could not be loaded.";
        }
    }

    /// <summary>
    /// 排版归一：协议原文是按 78 列硬折行的纯文本，直接塞进自动换行的 TextBox 会出现
    /// “一行只有 and” 这种碎行。这里把同一段落内的硬换行拼回一句（空行才分段），
    /// 遇到编号条目或引号开头的定义行再另起一段；字符内容一个字不改，只调空白。
    /// </summary>
    private static string Reflow(string raw)
    {
        var output = new StringBuilder();
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            output.AppendLine(paragraph.ToString());
            paragraph.Clear();
        }

        foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            string text = line.Trim();

            if (text.Length == 0)
            {
                FlushParagraph();
                if (output.Length > 0) output.AppendLine();
                continue;
            }

            // 原文里的一条分隔线：单独成段，不参与拼行
            if (text.Trim('-').Length == 0)
            {
                FlushParagraph();
                output.AppendLine(text);
                output.AppendLine();
                continue;
            }

            // 编号（1.Definition / 2) ...）与引号开头的定义行，各自另起一段
            bool startsSection = text[0] == '"' ||
                                 (char.IsDigit(text[0]) && text.Length > 1 && (text[1] == '.' || text[1] == ')'));
            if (startsSection) FlushParagraph();

            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(text);
        }

        FlushParagraph();
        return output.ToString().TrimEnd();
    }

    private void LegacyCaption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { /* 鼠标已抬起，忽略 */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>官方原文外链：只是核对用的旁证，离线/失效时还有内嵌全文兜底。</summary>
    private void OfficialLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception)
        {
            // 没有默认浏览器之类的极端情况：不弹错误框，用户还能看内置全文
        }

        e.Handled = true;
    }
}
