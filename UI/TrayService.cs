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
        _notify.Text = $"WinTips 键盘状态提示 ({DateTime.Now:HH:mm:ss} 构建)";
        _notify.Visible = true;
        _notify.DoubleClick += (_, _) => _openSettings();
        _notify.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _openSettings();
            else if (e.Button == MouseButtons.Right) ShowMenu();
        };
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
