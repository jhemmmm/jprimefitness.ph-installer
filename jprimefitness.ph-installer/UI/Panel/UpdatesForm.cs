using JPrime.Panel.App;
using JPrime.Panel.Runtime.Updates;

namespace JPrime.Panel.UI.Panel;

/// <summary>One row per component (app, biometric helper, panel): installed vs latest GitHub release, install per row
/// or all at once. The app can also be installed from a local bundle zip.</summary>
public sealed class UpdatesForm : Form
{
    private readonly AppServices _ctx;
    private readonly AutoUpdateScheduler _updates;
    private readonly TableLayoutPanel _grid;
    private readonly Label _status;
    private readonly Button _check;
    private readonly Button _installAll;
    private readonly Button _localZip;
    private readonly Dictionary<UpdateTarget, (Label Installed, Label Latest, Label State, Button Install)> _rows = new();

    public UpdatesForm(AppServices ctx, AutoUpdateScheduler updates)
    {
        _ctx = ctx;
        _updates = updates;
        Text = "Updates";
        Width = 720;
        Height = 340;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = AppIcon.Main;

        _grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(12, 12, 12, 0) };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Header("Component"); Header("Installed"); Header("Latest"); Header("Status"); Header("");
        foreach (var t in updates.Checker.Targets()) AddRow(t);

        _status = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(12, 8, 12, 0), ForeColor = Color.DimGray };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(8) };
        _check = new Button { Text = "Check now", AutoSize = true };
        _installAll = new Button { Text = "Install all updates", AutoSize = true, Enabled = false };
        _localZip = new Button { Text = "Install app from zip...", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        _check.Click += async (_, _) => await CheckAsync();
        _installAll.Click += (_, _) => InstallAll();
        _localZip.Click += (_, _) => InstallLocalZip();
        buttons.Controls.AddRange(new Control[] { _check, _installAll, _localZip, close });

        Controls.Add(_status);
        Controls.Add(_grid);
        Controls.Add(buttons);
        CancelButton = close;

        Render(updates.Checker.Last);
        _status.Text = updates.Checker.Last.Count == 0
            ? "Click \"Check now\" to look for newer releases on GitHub."
            : $"Last checked {LocalTime(ctx.Config.Updates.LastCheckUtc)}." + (ctx.Config.Updates.AutoApply ? " Automatic installs are on (see Settings)." : "");
        Shown += async (_, _) => { if (updates.Checker.Last.Count == 0) await CheckAsync(); };
    }

    private void Header(string text) => _grid.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) });

    private void AddRow(UpdateTarget t)
    {
        var name = new Label { Text = UpdateChecker.Title(t), AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        var installed = new Label { Text = Show(_updates.Checker.InstalledVersion(t)), AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        var latest = new Label { Text = "?", AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        var state = new Label { Text = "", AutoSize = true, Margin = new Padding(0, 6, 0, 6), ForeColor = Color.DimGray };
        var install = new Button { Text = "Install", AutoSize = true, Enabled = false, Margin = new Padding(0, 2, 0, 2) };
        install.Click += (_, _) => Install(t);
        _grid.Controls.AddRange(new Control[] { name, installed, latest, state, install });
        _rows[t] = (installed, latest, state, install);
    }

    private static string Show(string v) => string.IsNullOrEmpty(v) ? "(unknown)" : v;
    private static string LocalTime(DateTime? utc) => utc is { } u ? u.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "never";

    private void Render(IReadOnlyList<UpdateInfo> list)
    {
        var any = false;
        foreach (var u in list)
        {
            if (!_rows.TryGetValue(u.Target, out var r)) continue;
            r.Installed.Text = Show(u.Installed);
            r.Latest.Text = u.Latest?.Tag ?? "-";
            r.State.ForeColor = Color.DimGray;
            if (u.Error is not null) { r.State.Text = "Check failed: " + u.Error; r.State.ForeColor = Color.Crimson; }
            else if (u.Latest is null) r.State.Text = "No release published yet";
            else if (u.Available) { r.State.Text = $"Update available ({u.Latest.Size / 1024 / 1024} MB, {u.Latest.PublishedAt:yyyy-MM-dd})"; r.State.ForeColor = Color.FromArgb(30, 120, 50); any = true; }
            else r.State.Text = "Up to date";
            r.Install.Enabled = u.Available;
        }
        _installAll.Enabled = any;
    }

    private async Task CheckAsync()
    {
        _check.Enabled = false;
        _status.Text = "Checking GitHub...";
        try
        {
            var list = await _updates.Checker.CheckAsync(CancellationToken.None);
            Render(list);
            _status.Text = list.Any(u => u.Available)
                ? "Updates are available. The panel restarts itself when it is updated; the app is unavailable for about a minute while it updates."
                : $"Everything is up to date (checked {LocalTime(DateTime.UtcNow)}).";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not check for updates: " + ex.Message;
        }
        finally
        {
            _check.Enabled = true;
        }
    }

    private void Install(UpdateTarget t)
    {
        var u = _updates.Checker.Last.FirstOrDefault(x => x.Target == t);
        if (u is null || !u.Available) return;
        RunInstalls(new[] { u });
    }

    private void InstallAll() => RunInstalls(UpdateRunner.InInstallOrder(_updates.Checker.Available).ToList());

    private void RunInstalls(IReadOnlyList<UpdateInfo> updates)
    {
        if (updates.Count == 0) return;
        if (_updates.IsApplying)
        {
            MessageBox.Show(this, "An automatic update is running right now. Try again in a few minutes.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var title = updates.Count == 1 ? $"Updating {updates[0].Title} to {updates[0].Latest!.Tag}" : "Installing updates";
        var ok = TaskDialog.Run(this, title, async (log, ct) =>
        {
            foreach (var u in updates)
            {
                log($"== {u.Title} {u.Installed} -> {u.Latest!.Tag}");
                await _updates.Runner.InstallAsync(u, log, ct);
            }
        });
        Render(_updates.Checker.Last);
        foreach (var t in _rows.Keys) _rows[t].Installed.Text = Show(_updates.Checker.InstalledVersion(t));
        if (ok && updates.All(u => u.Target != UpdateTarget.Panel)) _ = CheckAsync();
    }

    private void InstallLocalZip()
    {
        using var ofd = new OpenFileDialog { Filter = "App bundle (*.zip)|*.zip", Title = "Select a jprimefitness.ph bundle" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var path = ofd.FileName;
        var version = Path.GetFileNameWithoutExtension(path).Replace("jprimefitness.ph-", "");
        var ok = TaskDialog.Run(this, $"Installing {Path.GetFileName(path)}", (log, ct) => _updates.Runner.ApplyAppAsync(path, version, log, ct));
        if (ok) _rows[UpdateTarget.App].Installed.Text = Show(_updates.Checker.InstalledVersion(UpdateTarget.App));
    }
}
