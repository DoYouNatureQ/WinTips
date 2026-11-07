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

public partial class LicenseWindow : Window
{
    private const string AgreementResourceSuffix = "LICENSE_Fonts.txt";

    private const double PreferredWidth = 580;

    private bool _agreementOpen;

    public LicenseWindow()
    {
        InitializeComponent();
        ApplyPlatformChrome();
        RefreshSubtitle();
        RefreshAgreementToggle();
        ApplyTheme();
        LoadAgreement();

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

    private void ApplyPlatformChrome()
    {
        if (DwmChrome.SupportsFluent) return;
        WindowStyle = WindowStyle.None;
        LegacyCaption.Visibility = Visibility.Visible;
    }

    private void RefreshSubtitle() =>
        Subtitle.Text = Loc.Format("LicenseSubtitle", AppInfo.DisplayVersion);

    private void OnLocChanged()
    {
        RefreshSubtitle();
        RefreshAgreementToggle();
    }

    private void RefreshAgreementToggle()
    {
        AgreementToggle.Inlines.Clear();
        AgreementToggle.Inlines.Add(new Run(Loc.T(_agreementOpen ? "LicenseHideAgreement" : "LicenseShowAgreement")));
    }

    private void AgreementToggle_Click(object sender, RoutedEventArgs e)
    {
        _agreementOpen = !_agreementOpen;
        TextCard.Visibility = _agreementOpen ? Visibility.Visible : Visibility.Collapsed;
        RefreshAgreementToggle();

        if (!_agreementOpen) return;
        AgreementText.CaretIndex = 0;
        AgreementText.ScrollToHome();
    }

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

        AgreementText.Foreground = primary;
        AgreementText.CaretBrush = primary;
        AgreementText.SelectionBrush = new SolidColorBrush(ThemeHelper.Tint(ThemeHelper.AccentColor, 0x66));

        ApplyTitleBar();
    }

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
        FitToScreen();
    }

    private void FitToScreen() => WindowFit.ClampToScreen(this, PreferredWidth);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        FitToScreen();
    }

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
            AgreementText.Text = "License text could not be loaded.";
        }
    }

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

            if (text.Trim('-').Length == 0)
            {
                FlushParagraph();
                output.AppendLine(text);
                output.AppendLine();
                continue;
            }

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
        try { DragMove(); } catch (InvalidOperationException) {  }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OfficialLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception)
        {
        }

        e.Handled = true;
    }
}
