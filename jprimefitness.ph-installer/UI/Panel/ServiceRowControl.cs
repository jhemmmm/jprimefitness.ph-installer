using JPrime.Panel.App;
using JPrime.Panel.Services;

namespace JPrime.Panel.UI.Panel;

/// <summary>One line in the panel: status dot, name, port, PIDs, message, Start/Stop/Restart, Autostart, Logs, Config.</summary>
public sealed class ServiceRowControl : UserControl
{
    private readonly ServiceBase _service;
    private readonly System.Windows.Forms.Panel _dot;
    private readonly Label _name;
    private readonly Label _detail;
    private readonly Label _pids;
    private readonly Label _message;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _restart;
    private readonly CheckBox _autostart;
    private readonly Button _logs;
    private readonly Button _config;
    private readonly ToolTip _tip = new();

    public event Action<ServiceBase>? LogsRequested;
    public event Action<ServiceBase>? ConfigRequested;

    public ServiceRowControl(ServiceBase service)
    {
        _service = service;
        Height = 58;
        Dock = DockStyle.Top;
        Padding = new Padding(8, 4, 8, 4);
        BackColor = Color.White;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));   // dot
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));  // name + detail
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));   // pids
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // message
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // buttons: sized by their text/DPI, never clipped
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // autostart/logs/config
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _dot = new System.Windows.Forms.Panel { Width = 14, Height = 14, Anchor = AnchorStyles.None, BackColor = Color.Gray };
        _dot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(_dot.BackColor);
            e.Graphics.Clear(BackColor);
            e.Graphics.FillEllipse(b, 0, 0, 13, 13);
        };
        _dot.BackColorChanged += (_, _) => _dot.Invalidate();

        var namePanel = new System.Windows.Forms.Panel { Dock = DockStyle.Fill };
        _name = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 22, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = service.DisplayName, TextAlign = ContentAlignment.BottomLeft };
        _detail = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DimGray, Text = service.PortLabel, TextAlign = ContentAlignment.TopLeft };
        namePanel.Controls.Add(_detail);
        namePanel.Controls.Add(_name);
        _tip.SetToolTip(_name, service.Description);

        _pids = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 8.5f) };
        _message = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

        var buttons = ButtonGroup();
        _start = MakeButton("Start", () => Run(_service.StartAsync));
        _stop = MakeButton("Stop", () => Run(_service.StopAsync));
        _restart = MakeButton("Restart", () => Run(_service.RestartAsync));
        buttons.Controls.AddRange(new Control[] { _start, _stop, _restart });

        var right = ButtonGroup();
        _autostart = new CheckBox { Text = "Auto", Checked = service.Autostart, AutoSize = true, Margin = new Padding(0, 4, 8, 0) };
        _tip.SetToolTip(_autostart, "Start this service automatically when the panel launches at login.");
        _autostart.CheckedChanged += (_, _) => _service.Autostart = _autostart.Checked;
        _logs = MakeButton("Logs", () => LogsRequested?.Invoke(_service));
        _config = MakeButton("Config", () => ConfigRequested?.Invoke(_service));
        right.Controls.AddRange(new Control[] { _autostart, _logs, _config });

        grid.Controls.Add(_dot, 0, 0);
        grid.Controls.Add(namePanel, 1, 0);
        grid.Controls.Add(_pids, 2, 0);
        grid.Controls.Add(_message, 3, 0);
        grid.Controls.Add(buttons, 4, 0);
        grid.Controls.Add(right, 5, 0);
        Controls.Add(grid);

        service.StatusChanged += _ => UiThread.Post(Refresh);
        Refresh();
    }

    /// <summary>Horizontal, natural-width group that sits vertically centred in its AutoSize column.</summary>
    private static FlowLayoutPanel ButtonGroup() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 8, 0),
    };

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 6, 0), Padding = new Padding(4, 0, 4, 0), MinimumSize = new Size(62, 26) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static void Run(Func<CancellationToken, Task> action)
    {
        _ = Task.Run(async () =>
        {
            try { await action(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error("Service action failed", ex); }
        });
    }

    public new void Refresh()
    {
        var status = _service.Status;
        _detail.Text = _service.PortLabel;
        _dot.BackColor = ColorFor(status.State);
        _pids.Text = status.Pids.Count switch
        {
            0 => "",
            1 => $"pid {status.Pids[0]}",
            _ => $"{status.Pids.Count} pids",
        };
        _tip.SetToolTip(_pids, status.Pids.Count > 1 ? string.Join(", ", status.Pids) : "");

        var text = status.State switch
        {
            ServiceState.Disabled => "Not installed",
            ServiceState.Stopped => "Stopped",
            ServiceState.Starting => "Starting..." + Suffix(status.Message),
            ServiceState.Running => "Running" + Suffix(status.Message) + Uptime(status),
            ServiceState.Degraded => "Running, but " + (status.Message ?? "unhealthy"),
            ServiceState.Stopping => "Stopping...",
            ServiceState.WaitingRestart => $"Restarting in {Countdown(status)}: {status.Message}",
            ServiceState.DeviceNotFound => $"Device not found. Retry in {Countdown(status)}",
            ServiceState.Failed => "Failed: " + (status.Message ?? "see logs"),
            _ => status.State.ToString(),
        };
        _message.Text = text;
        _message.ForeColor = status.State is ServiceState.Failed ? Color.Crimson
            : status.State is ServiceState.Degraded or ServiceState.DeviceNotFound or ServiceState.WaitingRestart ? Color.DarkOrange
            : Color.Black;
        _tip.SetToolTip(_message, status.Message ?? "");

        var enabled = _service.Enabled;
        var active = status.IsActive;
        _start.Enabled = enabled && !active && status.State != ServiceState.Stopping;
        _stop.Enabled = enabled && (active || status.State == ServiceState.Failed);
        _restart.Enabled = enabled && active;
        _restart.Text = status.State is ServiceState.DeviceNotFound or ServiceState.WaitingRestart ? "Retry now" : "Restart";
        _autostart.Enabled = enabled;
        _config.Enabled = true;
        _logs.Enabled = true;
        Enabled = true;
        BackColor = enabled ? Color.White : Color.WhiteSmoke;
    }

    private static string Suffix(string? message) => string.IsNullOrEmpty(message) ? "" : $" ({message})";

    private static string Uptime(ServiceStatus s) => s.Uptime is { } u && u.TotalSeconds >= 1 ? $"  ·  up {Format(u)}" : "";

    private static string Countdown(ServiceStatus s)
    {
        if (s.NextRetryAtUtc is not { } at) return "a moment";
        var left = at - DateTime.UtcNow;
        return left <= TimeSpan.Zero ? "a moment" : Format(left);
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{(int)Math.Ceiling(t.TotalSeconds)}s";

    public static Color ColorFor(ServiceState state) => state switch
    {
        ServiceState.Running => Color.LimeGreen,
        ServiceState.Starting or ServiceState.Stopping => Color.Gold,
        ServiceState.Degraded or ServiceState.WaitingRestart or ServiceState.DeviceNotFound => Color.Orange,
        ServiceState.Failed => Color.Crimson,
        ServiceState.Disabled => Color.LightGray,
        _ => Color.Gray,
    };

    /// <summary>Called by the form timer to keep countdown / uptime text fresh.</summary>
    public void Tick()
    {
        var st = _service.Status.State;
        if (st is ServiceState.Running or ServiceState.WaitingRestart or ServiceState.DeviceNotFound) Refresh();
    }
}
