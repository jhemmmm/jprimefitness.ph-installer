using System.Diagnostics;
using System.Text;
using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Setup;
using JPrime.Panel.Config;

namespace JPrime.Panel.UI.Wizard.Pages;

public sealed class PrereqPage : WizardPage
{
    private readonly WizardForm _host;
    private readonly ListView _list;
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 8, 0, 0) };
    private readonly Button _recheck = new() { Text = "Check again", AutoSize = true };
    private bool _blocked;

    public override string Title => "System check";
    public override string Subtitle => "Making sure this PC is ready";

    public PrereqPage(WizardForm host)
    {
        _host = host;
        _list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        _list.Columns.Add("Check", 230);
        _list.Columns.Add("Result", 80);
        _list.Columns.Add("Details", 360);
        _list.Resize += (_, _) => _list.Columns[2].Width = Math.Max(200, _list.ClientSize.Width - 230 - 80 - 4);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        bottom.Controls.Add(_summary);
        bottom.Controls.Add(_recheck);
        Controls.Add(_list);
        Controls.Add(bottom);
        _recheck.Click += async (_, _) => await RunAsync(_host.State);
    }

    public override void OnEnter(WizardState s)
    {
        _ = RunAsync(s);
    }

    private async Task RunAsync(WizardState s)
    {
        _recheck.Enabled = false;
        _host.SetNextEnabled(false);
        _list.Items.Clear();
        _summary.Text = "Checking...";
        var results = await Task.Run(() => PrereqChecker.CheckSystem(s.WebPort, PanelConfig.BiometricSettings.DefaultHelperPort, 9001, 4, PanelConfig.TunnelSettings.DefaultMetricsPort, s.InstallHelper, s.InstallHelper && s.InstallTunnel, s.InstallRoot).ToList());
        var internet = await PrereqChecker.HasInternetAsync(_host.Http, CancellationToken.None);
        results.Insert(2, internet
            ? new CheckResult("Internet connection", CheckLevel.Ok, "online")
            : new CheckResult("Internet connection", CheckLevel.Blocker, "No internet. Downloads are needed unless the files were placed in the downloads folder beforehand."));
        s.NeedVcRedist = !PrereqChecker.IsVcRedist2015PlusInstalled();
        s.NeedAspNetRuntime = !PrereqChecker.IsAspNetRuntime8Installed();
        if (!internet && Directory.Exists(Path.Combine(s.InstallRoot, "downloads")))
        {
            results[2] = new CheckResult("Internet connection", CheckLevel.Warning, "Offline. Files already in the downloads folder will be used.");
        }

        _blocked = false;
        foreach (var r in results)
        {
            var item = new ListViewItem(r.Name);
            item.SubItems.Add(r.Level switch { CheckLevel.Ok => "OK", CheckLevel.Warning => "Warning", _ => "Problem" });
            item.SubItems.Add(r.Detail);
            item.ForeColor = r.Level switch { CheckLevel.Ok => Color.FromArgb(30, 120, 50), CheckLevel.Warning => Color.DarkOrange, _ => Color.Crimson };
            _list.Items.Add(item);
            if (r.Level == CheckLevel.Blocker) _blocked = true;
        }
        _summary.ForeColor = _blocked ? Color.Crimson : Color.FromArgb(30, 120, 50);
        _summary.Text = _blocked
            ? "Fix the problems above (or go back and change the port/folder), then click Check again."
            : "This PC is ready. Warnings are handled automatically during install.";
        _host.SetNextEnabled(!_blocked);
        _recheck.Enabled = true;
    }

    public override ValidationResult Validate(WizardState s) =>
        _blocked ? ValidationResult.Error("Please resolve the problems listed before continuing.") : ValidationResult.Valid;
}

public sealed class DownloadPage : WizardPage
{
    private readonly WizardForm _host;
    private readonly ListView _list;
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 16, Margin = new Padding(0, 10, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Dictionary<string, ListViewItem> _rows = new();
    private bool _done;

    public override string Title => "Download";
    public override string Subtitle => "Fetching PHP, nginx, the app and the selected components";
    public override bool ShowBack => false;
    public override string NextText => "Next >";

    public DownloadPage(WizardForm host)
    {
        _host = host;
        _list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        _list.Columns.Add("Component", 280);
        _list.Columns.Add("Version", 100);
        _list.Columns.Add("Status", 300);
        _list.Resize += (_, _) => _list.Columns[2].Width = Math.Max(200, _list.ClientSize.Width - 280 - 100 - 4);
        var bottom = new System.Windows.Forms.Panel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        _status.Dock = DockStyle.Bottom;
        _status.AutoSize = true;
        bottom.Controls.Add(_status);
        bottom.Controls.Add(_bar);
        Controls.Add(_list);
        Controls.Add(bottom);
    }

    public override void OnEnter(WizardState s)
    {
        if (_done) return;
        _ = RunAsync(s);
    }

    private async Task RunAsync(WizardState s)
    {
        _host.SetBusy(true);
        _list.Items.Clear();
        _rows.Clear();
        _status.Text = "Resolving latest versions...";
        var http = _host.Http;
        try
        {
            var payloads = new List<Payload>();
            payloads.Add(await PayloadCatalog.ResolvePhpAsync(http, CancellationToken.None));
            payloads.Add(PayloadCatalog.Nginx);
            payloads.Add(PayloadCatalog.CaBundle);
            try
            {
                payloads.Add(await PayloadCatalog.ResolveAppAsync(http, CancellationToken.None));
            }
            catch (Exception ex)
            {
                var local = Directory.Exists(Path.Combine(s.InstallRoot, "downloads"))
                    ? Directory.GetFiles(Path.Combine(s.InstallRoot, "downloads"), "jprimefitness.ph-*.zip").OrderByDescending(f => f).FirstOrDefault()
                    : null;
                if (local is null) throw new InvalidOperationException("No JPrime app release is published on GitHub yet and no jprimefitness.ph-*.zip was found in the downloads folder. " + ex.Message);
                var ver = Path.GetFileNameWithoutExtension(local).Replace("jprimefitness.ph-", "");
                payloads.Add(new Payload("app", $"JPrime app {ver} (local file)", "file://" + local, Path.GetFileName(local), PayloadKind.Zip, ver));
            }
            if (s.InstallHelper) payloads.Add(await PayloadCatalog.ResolveHikVisionAsync(http, CancellationToken.None));
            if (s.InstallHelper && s.InstallTunnel) payloads.Add(PayloadCatalog.Cloudflared);
            if (s.NeedVcRedist) payloads.Add(PayloadCatalog.VcRedist);
            if (s.InstallHelper && s.NeedAspNetRuntime) payloads.Add(PayloadCatalog.AspNetRuntime);

            s.Payloads.Clear();
            foreach (var p in payloads)
            {
                s.Payloads[p.Id] = p;
                var item = new ListViewItem(p.Title);
                item.SubItems.Add(p.Version);
                item.SubItems.Add("queued");
                _list.Items.Add(item);
                _rows[p.Id] = item;
            }

            var dm = new DownloadManager(http, Path.Combine(s.InstallRoot, "downloads"));
            var index = 0;
            foreach (var p in payloads)
            {
                index++;
                var row = _rows[p.Id];
                _status.Text = $"Downloading {p.Title} ({index} of {payloads.Count})";
                if (p.Url.StartsWith("file://"))
                {
                    s.Downloaded[p.Id] = p.Url["file://".Length..];
                    row.SubItems[2].Text = "using local file";
                    continue;
                }
                var progress = new Progress<DownloadProgress>(dp =>
                {
                    row.SubItems[2].Text = dp.Fraction is { } f
                        ? $"{f:P0}  {dp.Received / 1024 / 1024} / {dp.Total / 1024 / 1024} MB  {(dp.BytesPerSecond > 0 ? $"{dp.BytesPerSecond / 1024 / 1024:F1} MB/s" : "")}"
                        : $"{dp.Received / 1024 / 1024} MB";
                    _bar.Style = ProgressBarStyle.Continuous;
                    var overall = ((index - 1) + (dp.Fraction ?? 0)) / payloads.Count;
                    _bar.Value = Math.Clamp((int)(overall * 100), 0, 100);
                });
                var path = await dm.FetchAsync(p, progress, CancellationToken.None);
                s.Downloaded[p.Id] = path;
                row.SubItems[2].Text = dm.IsCached(p) && new FileInfo(path).Length > 0 ? "ready" : "done";
            }
            if (s.InstallHelper && s.Downloaded.TryGetValue("hikvision", out var hik))
            {
                s.HelperIsSelfContained = ZipExtractor.ContainsFile(hik, "hostfxr.dll");
            }
            _bar.Value = 100;
            _status.ForeColor = Color.FromArgb(30, 120, 50);
            _status.Text = "All files are ready. Click Next to install.";
            _done = true;
            _host.SetBusy(false);
        }
        catch (Exception ex)
        {
            Log.Error("Download failed", ex);
            _status.ForeColor = Color.Crimson;
            _status.Text = "Download failed: " + ex.Message + "\nCheck the internet connection and click Cancel to try again later, or place the files in " + Path.Combine(s.InstallRoot, "downloads");
            _host.SetBusy(false);
            _host.SetNextEnabled(false);
        }
    }

    public override ValidationResult Validate(WizardState s) =>
        _done ? ValidationResult.Valid : ValidationResult.Error("Downloads have not finished.");
}

public sealed class InstallPage : WizardPage
{
    private readonly WizardForm _host;
    private readonly Label _step = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 16 };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9),
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.Gainsboro,
    };
    private bool _started;
    private bool _done;

    public override string Title => "Installing";
    public override string Subtitle => "This takes a few minutes. Approve the administrator prompt when it appears.";
    public override bool ShowBack => false;
    public override bool CanCancel => false;

    public InstallPage(WizardForm host)
    {
        _host = host;
        var top = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 56 };
        top.Controls.Add(_bar);
        top.Controls.Add(_step);
        _step.Dock = DockStyle.Top;
        Controls.Add(_log);
        Controls.Add(top);
    }

    public override void OnEnter(WizardState s)
    {
        if (_started) return;
        _started = true;
        _ = RunAsync(s);
    }

    private async Task RunAsync(WizardState s)
    {
        _host.SetBusy(true);
        var progress = new Progress<StepProgress>(p =>
        {
            _step.Text = p.Step + (p.Message.Length > 0 ? " - " + p.Message : "");
            if (p.Fraction is { } f) _bar.Value = Math.Clamp((int)(f * 100), 0, 100);
        });
        void L(string line)
        {
            if (_log.IsDisposed) return;
            if (_log.InvokeRequired) { _log.BeginInvoke(() => L(line)); return; }
            _log.AppendText((_log.TextLength > 0 ? Environment.NewLine : "") + line);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        try
        {
            var engine = new InstallEngine(s, _host.Http, progress, L);
            await Task.Run(() => engine.RunAsync(CancellationToken.None));
            s.InstallCompleted = true;
            _done = true;
            _step.Text = "Installation complete";
            _host.SetBusy(false);
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            s.InstallError = ex.Message;
            L("");
            L("INSTALL FAILED: " + ex.Message);
            L("You can close this window and run setup again; completed steps are skipped.");
            _step.Text = "Installation failed";
            _step.ForeColor = Color.Crimson;
            _host.SetBusy(false);
            _host.SetNextText("Close");
            _done = true;
        }
    }

    public override ValidationResult Validate(WizardState s) => _done ? ValidationResult.Valid : ValidationResult.Error("Installation is still running.");

    public override Task<bool> OnLeaveAsync(WizardState s, CancellationToken ct)
    {
        if (s.InstallError is not null)
        {
            _host.Close();
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }
}

public sealed class FinishPage : WizardPage
{
    private readonly WizardForm _host;
    private readonly TextBox _info = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5f),
        BackColor = Color.White,
    };
    private readonly CheckBox _open = new() { Text = "Open the app in the browser", Checked = true, AutoSize = true, Dock = DockStyle.Bottom };

    public override string Title => "Finished";
    public override string Subtitle => "JPrime is installed. The control panel will start now.";
    public override bool ShowBack => false;
    public override string NextText => "Finish";
    public override bool CanCancel => false;

    public FinishPage(WizardForm host)
    {
        _host = host;
        Controls.Add(_info);
        Controls.Add(_open);
    }

    public override void OnEnter(WizardState s)
    {
        var lan = LanAddresses.List();
        var sb = new StringBuilder();
        sb.AppendLine("Save this information somewhere safe. Tokens are not shown again.");
        sb.AppendLine();
        sb.AppendLine($"Admin login        http://localhost:{s.WebPort}/login");
        sb.AppendLine($"  Email            {s.SuperAdminEmail}");
        sb.AppendLine($"  Password         {(s.SuperAdminPassword.Length > 0 ? "(the one you chose)" : "(unchanged)")}");
        sb.AppendLine();
        sb.AppendLine("From phones / kiosk tablets on the gym Wi-Fi:");
        if (lan.Count == 0) sb.AppendLine("  (no network address found yet)");
        foreach (var a in lan) sb.AppendLine($"  http://{a.Ip}:{s.WebPort}      ({a.Adapter}{(a.IsDhcp ? " - DHCP: reserve this IP on the router" : "")})");
        sb.AppendLine();
        sb.AppendLine($"KIOSK_TOKEN        {s.KioskToken}");
        if (s.InstallHelper) sb.AppendLine($"BIOMETRIC_TOKEN    {s.BiometricToken}");
        if (s.InstallHelper && s.InstallTunnel)
        {
            sb.AppendLine($"Helper public URL  https://{s.PublicHostname}   (open to the internet; restrict with a Cloudflare WAF rule if needed)");
            sb.AppendLine();
            sb.AppendLine("Live server .env (helper via the tunnel):");
            foreach (var line in WizardState.LiveServerEnvSnippet(s.PublicHostname, s.BiometricToken).Split('\n')) sb.AppendLine("  " + line);
        }
        sb.AppendLine();
        sb.AppendLine($"Install folder     {s.InstallRoot}");
        sb.AppendLine($"Database           {s.InstallRoot}\\data\\jprime.sqlite   (backups in data\\backups)");
        sb.AppendLine($"Control panel      {s.InstallRoot}\\panel\\JPrimePanel.exe   (also in the Start Menu; runs at sign-in)");
        _info.Text = sb.ToString();
        _info.SelectionStart = 0;
        _info.SelectionLength = 0;
    }

    public override Task<bool> OnLeaveAsync(WizardState s, CancellationToken ct)
    {
        var paths = new AppPaths(s.InstallRoot);
        try
        {
            var psi = new ProcessStartInfo(paths.PanelExe) { UseShellExecute = false, WorkingDirectory = paths.PanelDir };
            psi.ArgumentList.Add("--show");
            if (_open.Checked) psi.ArgumentList.Add("--open-browser");
            // Release our single-instance mutex first so the installed copy can take it.
            SingleInstanceHandle.ReleaseCurrent();
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the installed panel", ex);
            MessageBox.Show(_host, "The panel could not be started automatically: " + ex.Message + "\nOpen it from the Start Menu.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return Task.FromResult(true);
    }
}

/// <summary>Lets the wizard release the process-wide single-instance mutex before spawning the installed panel.</summary>
public static class SingleInstanceHandle
{
    public static SingleInstance? Current { get; set; }
    public static void ReleaseCurrent() => Current?.Release();
}
