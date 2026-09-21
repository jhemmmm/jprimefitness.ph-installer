using System.Drawing.Drawing2D;
using JPrime.Panel.App;
using JPrime.Panel.Services;

namespace JPrime.Panel.UI;

/// <summary>Notification-area icon whose colour mirrors the overall service state.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly PanelAppContext _app;
    private readonly NotifyIcon _icon;
    private readonly Dictionary<Color, Icon> _icons = new();
    private bool _balloonShown;

    public TrayIcon(PanelAppContext app)
    {
        _app = app;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open panel", null, (_, _) => _app.ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start all services", null, (_, _) => _ = _app.StartAllAsync());
        menu.Items.Add("Stop all services", null, (_, _) => _ = _app.StopAllAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open JPrime app in browser", null, (_, _) => _app.OpenApp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit (stops services)", null, (_, _) => _ = _app.ExitAsync());

        _icon = new NotifyIcon
        {
            Text = "JPrime Control Panel",
            Icon = IconFor(Color.Gray),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _app.ShowMain();
        _icon.BalloonTipClicked += (_, _) => _app.ShowMain();
    }

    public void SetText(string text)
    {
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    /// <summary>Notification with a click-through to the panel (used for update notices).</summary>
    public void ShowBalloon(string text, ToolTipIcon kind = ToolTipIcon.Info)
    {
        try { _icon.ShowBalloonTip(8000, "JPrime Control Panel", text, kind); } catch { }
    }

    public void ShowBalloonOnce(string text)
    {
        if (_balloonShown) return;
        _balloonShown = true;
        try { _icon.ShowBalloonTip(3000, "JPrime Control Panel", text, ToolTipIcon.Info); } catch { }
    }

    public void RefreshState(ServiceManager services)
    {
        var states = services.Enabled.Select(s => s.Status.State).ToList();
        Color color;
        string text;
        if (states.Count == 0 || states.All(s => s is ServiceState.Stopped or ServiceState.Disabled))
        {
            color = Color.Gray;
            text = "JPrime: all services stopped";
        }
        else if (states.Any(s => s == ServiceState.Failed))
        {
            color = Color.Crimson;
            text = "JPrime: a service failed";
        }
        else if (states.Any(s => s is ServiceState.Degraded or ServiceState.DeviceNotFound or ServiceState.WaitingRestart or ServiceState.Starting or ServiceState.Stopping))
        {
            color = Color.Orange;
            text = "JPrime: some services need attention";
        }
        else if (states.Any(s => s == ServiceState.Running))
        {
            color = Color.LimeGreen;
            text = states.All(s => s is ServiceState.Running or ServiceState.Disabled) ? "JPrime: all services running" : "JPrime: running";
        }
        else
        {
            color = Color.Gray;
            text = "JPrime Control Panel";
        }
        _icon.Icon = IconFor(color);
        SetText(text);
    }

    private Icon IconFor(Color color)
    {
        if (_icons.TryGetValue(color, out var cached)) return cached;
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            using (var logo = AppIcon.Bitmap(32)) g.DrawImage(logo, 0, 0, 32, 32);
            // Status dot bottom-right, white ring so it reads on the red logo and on light/dark taskbars.
            using var ring = new SolidBrush(Color.White);
            g.FillEllipse(ring, 17, 17, 15, 15);
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, 19, 19, 11, 11);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        _icons[color] = icon;
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var i in _icons.Values) i.Dispose();
    }
}
