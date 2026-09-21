using System.Diagnostics;
using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Services;

namespace JPrime.Panel.UI.Panel;

public sealed class MainForm : Form
{
    private readonly PanelAppContext _app;
    private readonly AppServices _ctx;
    private readonly List<ServiceRowControl> _rows = new();
    private readonly LogViewerControl _logs;
    private readonly ToolStripStatusLabel _lanLabel;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly ToolStripButton _updates;

    public MainForm(PanelAppContext app)
    {
        _app = app;
        _ctx = app.Ctx;

        Text = $"JPrime Control Panel - {_ctx.Config.App.Name}";
        Width = 1140;
        Height = 720;
        MinimumSize = new Size(960, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Main;

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        tools.Items.Add(new ToolStripButton("Start all", null, (_, _) => _ = _app.StartAllAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text });
        tools.Items.Add(new ToolStripButton("Stop all", null, (_, _) => _ = _app.StopAllAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text });
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(new ToolStripButton("Open app", null, (_, _) => _app.OpenApp()) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Open the JPrime app in your browser" });
        var lan = new ToolStripDropDownButton("Wi-Fi / LAN address") { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Addresses phones and kiosk tablets on the gym network can use" };
        lan.DropDownOpening += (_, _) => FillLanMenu(lan);
        tools.Items.Add(lan);
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(new ToolStripButton("Backup now", null, (_, _) => Tools.BackupNow(this)) { DisplayStyle = ToolStripItemDisplayStyle.Text });
        _updates = new ToolStripButton("Updates", null, (_, _) => Tools.Updates(this)) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "App, biometric helper and panel updates from GitHub" };
        tools.Items.Add(_updates);
        tools.Items.Add(new ToolStripButton("Artisan", null, (_, _) => Tools.ArtisanConsole(this)) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Run php artisan commands" });
        tools.Items.Add(new ToolStripButton("Edit .env", null, (_, _) => Tools.EditEnv(this)) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Edit Laravel environment settings" });
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(new ToolStripButton("Settings", null, (_, _) => Tools.Settings(this)) { DisplayStyle = ToolStripItemDisplayStyle.Text });
        tools.Items.Add(new ToolStripButton("Re-run setup", null, (_, _) => Tools.RerunSetup(this)) { DisplayStyle = ToolStripItemDisplayStyle.Text });

        var rowsPanel = new System.Windows.Forms.Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Gainsboro, Padding = new Padding(0, 0, 0, 1) };
        foreach (var service in _ctx.Services.All.Reverse())
        {
            if (!service.Enabled && service.Id is "hikvision" or "cloudflared") continue;
            var row = new ServiceRowControl(service);
            row.LogsRequested += s => _logs!.ShowService(s);
            row.ConfigRequested += s => Tools.ConfigureService(this, s);
            rowsPanel.Controls.Add(row);
            rowsPanel.Controls.Add(new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.Gainsboro });
            _rows.Add(row);
        }

        _logs = new LogViewerControl();
        _logs.SetServices(_ctx.Services.Enabled);

        var status = new StatusStrip();
        _lanLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        status.Items.Add(_lanLabel);
        status.Items.Add(new ToolStripStatusLabel($"Panel {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}  ·  {_ctx.Paths.InstallRoot}") { ForeColor = Color.DimGray });

        Controls.Add(_logs);
        Controls.Add(rowsPanel);
        Controls.Add(tools);
        Controls.Add(status);

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => { foreach (var r in _rows) r.Tick(); };
        _tick.Start();

        RefreshLanLabel();
        RefreshUpdatesBadge(_app.Updates.Checker.Last);
        _app.Updates.Checker.Checked += list => UiThread.Post(() => { if (!IsDisposed) RefreshUpdatesBadge(list); });
    }

    private void RefreshUpdatesBadge(IReadOnlyList<Runtime.Updates.UpdateInfo> list)
    {
        var n = list.Count(u => u.Available);
        _updates.Text = n == 0 ? "Updates" : $"Updates ({n})";
        _updates.ForeColor = n == 0 ? SystemColors.ControlText : Color.FromArgb(180, 40, 40);
        _updates.Font = new Font(Font, n == 0 ? FontStyle.Regular : FontStyle.Bold);
    }

    private void RefreshLanLabel()
    {
        var port = _ctx.Config.Web.Port;
        var addrs = LanAddresses.List();
        _lanLabel.Text = addrs.Count == 0
            ? $"Local: http://localhost:{port}   (no LAN address detected)"
            : $"Local: http://localhost:{port}   ·   Wi-Fi/LAN: " + string.Join("  |  ", addrs.Select(a => $"http://{a.Ip}:{port}"));
    }

    private void FillLanMenu(ToolStripDropDownButton button)
    {
        button.DropDownItems.Clear();
        var port = _ctx.Config.Web.Port;
        var addrs = LanAddresses.List();
        if (addrs.Count == 0)
        {
            button.DropDownItems.Add(new ToolStripMenuItem("No network address detected") { Enabled = false });
            return;
        }
        foreach (var a in addrs)
        {
            var url = $"http://{a.Ip}:{port}";
            var item = new ToolStripMenuItem($"{url}   ({a.Adapter}{(a.IsDhcp ? ", DHCP" : "")})");
            item.Click += (_, _) => Ui.TryCopy(url);
            item.ToolTipText = "Click to copy";
            button.DropDownItems.Add(item);
        }
        button.DropDownItems.Add(new ToolStripSeparator());
        button.DropDownItems.Add(new ToolStripMenuItem("Tip: give this PC a fixed IP (DHCP reservation) so tablets keep working.") { Enabled = false });
        RefreshLanLabel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
        }
        base.Dispose(disposing);
    }
}
