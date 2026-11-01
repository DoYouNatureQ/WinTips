using System.Drawing;
using System.Windows.Forms;
using WinTips.Core;

namespace WinTips.UI;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notify = new();
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly Action<TipKind, bool?> _showTest;

    public TrayService(Action openSettings, Action exit, Action<TipKind, bool?> showTest)
    {
        _openSettings = openSettings;
        _exit = exit;
        _showTest = showTest;

        _notify.Icon = LoadIcon();
        // 悬停提示只报版本号（与设置页底部一致），不再拼构建时间等易过期信息
        _notify.Text = TooltipText;
        _notify.Visible = true;
        _notify.DoubleClick += (_, _) => _openSettings();
        _notify.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _openSettings();
            else if (e.Button == MouseButtons.Right) ShowMenu();
        };
    }

    /// <summary>托盘悬停提示：应用名 + 版本号。NotifyIcon.Text 上限 63 字符，超长会被截断保护。</summary>
    private static string TooltipText
    {
        get
        {
            string text = $"WinTips {AppInfo.DisplayVersion}";
            return text.Length <= 63 ? text : text[..63];
        }
    }

    private void ShowMenu()
    {
        var menu = new TrayMenuWindow(
            _openSettings,
            _exit,
            enable =>
            {
                AppSettings.SetStartup(enable);
                AppSettings.Instance.LaunchAtStartup = enable;
                AppSettings.Instance.Save();
            },
            AppSettings.IsStartupSet(),
            _showTest);
        menu.ShowMenuAtCursor();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
        }
        catch { }
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(255, 24, 119, 242));
            g.FillEllipse(brush, 0, 0, 32, 32);
            using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("W", font, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
    }
}
