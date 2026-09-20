using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Health;
using JPrime.Panel.Runtime;
using JPrime.Panel.Setup;
using Microsoft.Win32;

namespace JPrime.Panel.UI.Panel;

/// <summary>Panel + web settings. Changing the port/pool re-renders nginx.conf/php.ini/.env and restarts the affected rows.</summary>
public sealed class SettingsForm : Form
{
    private readonly AppServices _ctx;
    private readonly NumericUpDown _port;
    private readonly NumericUpDown _pool;
    private readonly NumericUpDown _basePort;
    private readonly CheckBox _lan;
    private readonly ComboBox _lanScope;
    private readonly CheckBox _runAtLogin;
    private readonly CheckBox _minimize;
    private readonly CheckBox _confirmExit;
    private readonly CheckBox _backupEnabled;
    private readonly NumericUpDown _backupKeep;
    private readonly NumericUpDown _backupHours;
    private readonly CheckBox _hikDebug;
    private readonly TextBox _appName;

    public SettingsForm(AppServices ctx)
    {
        _ctx = ctx;
        var c = ctx.Config;
        Text = "Settings";
        Width = 600;
        Height = 640;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14), AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Section(string title)
        {
            var l = new Label { Text = title, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
            grid.Controls.Add(l);
            grid.SetColumnSpan(l, 2);
        }
        void Row(string label, Control control, string? tip = null)
        {
            if (label.Length == 0)
            {
                control.Margin = new Padding(0, 3, 0, 3);
                grid.Controls.Add(control);
                grid.SetColumnSpan(control, 2);
            }
            else
            {
                var l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
                grid.Controls.Add(l);
                grid.Controls.Add(control);
            }
            if (tip is not null) new ToolTip().SetToolTip(control, tip);
        }

        Section("Application");
        _appName = new TextBox { Text = c.App.Name, Width = 260 };
        Row("Gym name (APP_NAME)", _appName);

        Section("Web server");
        _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = c.Web.Port, Width = 90 };
        Row("Web port", _port, "Browsers, phones and kiosk tablets connect here. Default 8001.");
        _pool = new NumericUpDown { Minimum = 1, Maximum = 16, Value = c.Web.PoolSize, Width = 90 };
        Row("PHP workers", _pool, "Each worker serves one request at a time. 4 is plenty for a gym counter.");
        _basePort = new NumericUpDown { Minimum = 1025, Maximum = 65000, Value = c.Web.PoolBasePort, Width = 90 };
        Row("PHP worker base port", _basePort, "Internal loopback ports. Change only if they collide with other software.");
        _lan = new CheckBox { Text = "Allow Wi-Fi / LAN devices (opens the web port in Windows Firewall)", Checked = c.Web.LanAccess, AutoSize = true };
        Row("", _lan);
        _lanScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _lanScope.Items.AddRange(new object[] { "LocalSubnet", "Any" });
        _lanScope.SelectedItem = c.Web.LanScope == "Any" ? "Any" : "LocalSubnet";
        Row("Firewall scope", _lanScope, "LocalSubnet = only devices on the gym network. Any = every network the PC joins.");

        Section("Control panel");
        _runAtLogin = new CheckBox { Text = "Start the panel when I sign in to Windows", Checked = c.Ui.RunAtLogin, AutoSize = true };
        Row("", _runAtLogin);
        _minimize = new CheckBox { Text = "Closing the window keeps the panel running in the tray", Checked = c.Ui.MinimizeToTray, AutoSize = true };
        Row("", _minimize);
        _confirmExit = new CheckBox { Text = "Ask before quitting while services run", Checked = c.Ui.ConfirmStopOnExit, AutoSize = true };
        Row("", _confirmExit);

        Section("Database backups");
        _backupEnabled = new CheckBox { Text = "Automatic backups", Checked = c.Backup.Enabled, AutoSize = true };
        Row("", _backupEnabled);
        _backupHours = new NumericUpDown { Minimum = 1, Maximum = 168, Value = Math.Clamp(c.Backup.IntervalHours, 1, 168), Width = 90 };
        Row("Every (hours)", _backupHours);
        _backupKeep = new NumericUpDown { Minimum = 1, Maximum = 365, Value = Math.Clamp(c.Backup.RetentionCount, 1, 365), Width = 90 };
        Row("Keep last (copies)", _backupKeep);

        Section("Biometric helper");
        _hikDebug = new CheckBox { Text = "Verbose helper logging (shows device details in health)", Checked = c.Biometric.Debug, AutoSize = true, Enabled = c.Biometric.Enabled };
        Row("", _hikDebug);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var ok = new Button { Text = "Save", Width = 90 };
        ok.Click += async (_, _) => await SaveAsync();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private async Task SaveAsync()
    {
        var c = _ctx.Config;
        var newPort = (int)_port.Value;
        var newPool = (int)_pool.Value;
        var newBase = (int)_basePort.Value;
        var portChanged = newPort != c.Web.Port || newPool != c.Web.PoolSize || newBase != c.Web.PoolBasePort;
        var nameChanged = _appName.Text.Trim() != c.App.Name;
        var lanChanged = _lan.Checked != c.Web.LanAccess || (string)_lanScope.SelectedItem! != c.Web.LanScope || newPort != c.Web.Port;
        var oldPort = c.Web.Port;

        if (newPort != c.Web.Port && !PortScanner.IsFree(newPort))
        {
            var owner = PortScanner.WhoHolds(newPort);
            MessageBox.Show(this, $"Port {newPort} is in use by {owner?.ProcessName ?? "another program"}.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        c.App.Name = string.IsNullOrWhiteSpace(_appName.Text) ? c.App.Name : _appName.Text.Trim();
        c.Web.Port = newPort;
        c.Web.PoolSize = newPool;
        c.Web.PoolBasePort = newBase;
        c.Web.LanAccess = _lan.Checked;
        c.Web.LanScope = (string)_lanScope.SelectedItem!;
        c.Ui.RunAtLogin = _runAtLogin.Checked;
        c.Ui.MinimizeToTray = _minimize.Checked;
        c.Ui.ConfirmStopOnExit = _confirmExit.Checked;
        c.Backup.Enabled = _backupEnabled.Checked;
        c.Backup.IntervalHours = (int)_backupHours.Value;
        c.Backup.RetentionCount = (int)_backupKeep.Value;
        var hikDebugChanged = _hikDebug.Checked != c.Biometric.Debug;
        c.Biometric.Debug = _hikDebug.Checked;
        _ctx.SaveConfig();

        StartupRegistration.Apply(c.Ui.RunAtLogin, _ctx.Paths);

        UseWaitCursor = true;
        Enabled = false;
        try
        {
            if (portChanged || nameChanged)
            {
                await Task.Run(async () =>
                {
                    var svc = _ctx.Services;
                    var wasNginx = svc.Nginx.Status.IsActive;
                    var wasPhp = svc.Php.Status.IsActive;
                    var wasSched = svc.Scheduler.Status.IsActive;
                    var wasHik = svc.HikVision.Status.IsActive;
                    await svc.Nginx.StopAsync().ConfigureAwait(false);
                    await svc.Scheduler.StopAsync().ConfigureAwait(false);
                    await svc.Php.StopAsync().ConfigureAwait(false);

                    ConfigWriter.WriteNginxConf(c, _ctx.Paths);
                    ConfigWriter.WritePhpIni(c, _ctx.Paths);
                    var env = Runtime.Templates.EnvFile.Load(_ctx.Paths.AppEnvFile);
                    env.Set("APP_NAME", c.App.Name);
                    env.Set("APP_URL", $"http://localhost:{c.Web.Port}");
                    env.Save(_ctx.Paths.AppEnvFile);
                    var php = new PhpRunner(_ctx.Paths, c.Web.MaxRequests);
                    var artisan = new ArtisanRunner(_ctx.Paths, php);
                    await artisan.OptimizeClear(CancellationToken.None).ConfigureAwait(false);
                    await artisan.Optimize(CancellationToken.None).ConfigureAwait(false);

                    if (c.Biometric.Enabled && newPort != oldPort)
                    {
                        var hik = Runtime.Templates.HikAppSettings.TryLoad(_ctx.Paths.HikVisionAppSettings);
                        if (hik is not null)
                        {
                            hik = hik with { ForwardUrl = $"http://127.0.0.1:{c.Web.Port}/api/biometric/hikvision/callback", Debug = c.Biometric.Debug };
                            ConfigWriter.WriteHikAppSettings(hik, _ctx.Paths);
                            if (wasHik) await svc.HikVision.RestartAsync().ConfigureAwait(false);
                        }
                    }

                    if (wasPhp) await svc.Php.StartAsync().ConfigureAwait(false);
                    if (wasNginx) await svc.Nginx.StartAsync().ConfigureAwait(false);
                    if (wasSched) await svc.Scheduler.StartAsync().ConfigureAwait(false);
                });
            }
            else if (hikDebugChanged && c.Biometric.Enabled)
            {
                var hik = Runtime.Templates.HikAppSettings.TryLoad(_ctx.Paths.HikVisionAppSettings);
                if (hik is not null)
                {
                    ConfigWriter.WriteHikAppSettings(hik with { Debug = c.Biometric.Debug }, _ctx.Paths);
                    if (_ctx.Services.HikVision.Status.IsActive) await _ctx.Services.HikVision.RestartAsync();
                }
            }

            if (lanChanged)
            {
                var tasks = new List<AdminTaskRequest>();
                if (oldPort != newPort) tasks.Add(new("firewall-remove", new[] { oldPort.ToString() }));
                if (c.Web.LanAccess) tasks.Add(new("firewall", new[] { newPort.ToString(), c.Web.LanScope }));
                else tasks.Add(new("firewall-remove", new[] { newPort.ToString() }));
                var code = await Elevation.RunAdminTasksAsync(tasks, _ctx.Paths.LogsDir, CancellationToken.None);
                if (code == Elevation.ExitCancelled)
                {
                    MessageBox.Show(this, "Firewall changes were skipped because administrator approval was declined. Other settings were saved.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (code != 0)
                {
                    MessageBox.Show(this, $"Firewall update reported an error (code {code}). See logs\\admin-task.log.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Applying settings failed", ex);
            MessageBox.Show(this, "Applying settings failed:\n\n" + ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            Enabled = true;
        }
    }
}

/// <summary>HKCU Run registration for "start at login".</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JPrimePanel";

    public static void Apply(bool enabled, AppPaths paths)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey)!;
            if (enabled)
            {
                var exe = File.Exists(paths.PanelExe) ? paths.PanelExe : Environment.ProcessPath!;
                key.SetValue(ValueName, $"\"{exe}\" --autostart");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Run key update failed: {ex.Message}");
        }
    }

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }
}
