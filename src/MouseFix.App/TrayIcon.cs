using System.Windows;
using MouseFix.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MouseFix.App;

/// <summary>Notification-area icon with a small menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action open, Action fixNow, Action report, Action exit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mousefix.ico")).Stream;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L.T("Abrir MouseFix", "Open MouseFix"), null, (_, _) => open());
        menu.Items.Add(L.T("Remover aceleração agora", "Remove acceleration now"), null, (_, _) => fixNow());
        menu.Items.Add(L.T("Reportar problema", "Report a problem"), null, (_, _) => report());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.T("Sair", "Exit"), null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = $"MouseFix {typeof(TrayIcon).Assembly.GetName().Version?.ToString(3)}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                open();
        };
        _icon.BalloonTipClicked += (_, _) => open();
    }

    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        // Hide first, otherwise a ghost icon stays in the tray until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
    }
}
