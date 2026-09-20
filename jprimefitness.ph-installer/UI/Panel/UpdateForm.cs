using JPrime.Panel.App;
using JPrime.Panel.Runtime;
using JPrime.Panel.Setup;

namespace JPrime.Panel.UI.Panel;

/// <summary>Check GitHub for a newer app release (or pick a local bundle zip) and apply it.</summary>
public sealed class UpdateForm : Form
{
    private readonly AppServices _ctx;
    private readonly Label _status;
    private readonly Button _check;
    private readonly Button _install;
    private readonly Button _local;
    private ReleaseInfo? _latest;

    public UpdateForm(AppServices ctx)
    {
        _ctx = ctx;
        Text = "Update JPrime app";
        Width = 560;
        Height = 240;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = AppIcon.Main;

        var current = string.IsNullOrEmpty(ctx.Config.Versions.App) ? "(unknown)" : ctx.Config.Versions.App;
        var info = new Label { Text = $"Installed version: {current}", Left = 14, Top = 14, Width = 520, AutoSize = true };
        _status = new Label { Text = "Click \"Check for updates\" to look for a newer release on GitHub.", Left = 14, Top = 44, Width = 520, Height = 60 };
        _check = new Button { Text = "Check for updates", Left = 14, Top = 120, Width = 150 };
        _install = new Button { Text = "Download && install", Left = 174, Top = 120, Width = 150, Enabled = false };
        _local = new Button { Text = "Install from zip...", Left = 334, Top = 120, Width = 150 };
        _check.Click += async (_, _) => await CheckAsync();
        _install.Click += (_, _) => InstallLatest();
        _local.Click += (_, _) => InstallLocal();
        Controls.AddRange(new Control[] { info, _status, _check, _install, _local });
    }

    private async Task CheckAsync()
    {
        _check.Enabled = false;
        _status.Text = "Checking GitHub...";
        try
        {
            _latest = await new AppUpdater(_ctx).GetLatestAsync(CancellationToken.None);
            if (_latest is null)
            {
                _status.Text = "No release with an app bundle was found on GitHub yet.";
            }
            else
            {
                var same = string.Equals(_latest.Tag, _ctx.Config.Versions.App, StringComparison.OrdinalIgnoreCase);
                _status.Text = same
                    ? $"You already have the latest release ({_latest.Tag})."
                    : $"Latest release: {_latest.Tag} ({_latest.Size / 1024 / 1024} MB, published {_latest.PublishedAt:yyyy-MM-dd}).";
                _install.Enabled = !same;
            }
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

    private void InstallLatest()
    {
        if (_latest is null) return;
        var rel = _latest;
        Apply($"Updating to {rel.Tag}", async (log, ct) =>
        {
            var dm = new DownloadManager(_ctx.Http, _ctx.Paths.DownloadsDir);
            string? sha = null;
            if (rel.Sha256Url is not null)
            {
                try
                {
                    var txt = await _ctx.Http.GetStringAsync(rel.Sha256Url, ct);
                    sha = txt.Split(' ', '\t', '\n')[0].Trim();
                }
                catch (Exception ex) { log("SHA-256 sidecar unavailable: " + ex.Message); }
            }
            var payload = new Payload("app", $"JPrime app {rel.Tag}", rel.ZipUrl, $"jprimefitness.ph-{rel.Tag}.zip", PayloadKind.Zip, rel.Tag, Sha256: sha, ExpectedSize: rel.Size);
            log($"Downloading {payload.FileName}");
            var progress = new Progress<DownloadProgress>(p => { if (p.Fraction is { } f) log($"  {f:P0} ({p.Received / 1024 / 1024} MB)"); });
            var zip = await dm.FetchAsync(payload, progress, ct);
            return (zip, rel.Tag);
        });
    }

    private void InstallLocal()
    {
        using var ofd = new OpenFileDialog { Filter = "App bundle (*.zip)|*.zip", Title = "Select a jprimefitness.ph bundle" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var path = ofd.FileName;
        var version = Path.GetFileNameWithoutExtension(path).Replace("jprimefitness.ph-", "");
        Apply($"Installing {Path.GetFileName(path)}", (_, _) => Task.FromResult((path, version)));
    }

    private void Apply(string title, Func<Action<string>, CancellationToken, Task<(string Zip, string Version)>> obtain)
    {
        var ok = TaskDialog.Run(this, title, async (log, ct) =>
        {
            var (zip, version) = await obtain(log, ct);
            var svc = _ctx.Services;
            var wasNginx = svc.Nginx.Status.IsActive;
            var wasPhp = svc.Php.Status.IsActive;
            var wasSched = svc.Scheduler.Status.IsActive;

            log("Backing up the database first");
            await new SqliteBackup(_ctx.Paths, _ctx.Config).RunAsync(ct, log);

            log("Stopping web services");
            await svc.Nginx.StopAsync(ct);
            await svc.Scheduler.StopAsync(ct);
            await svc.Php.StopAsync(ct);

            try
            {
                await new AppUpdater(_ctx).ApplyAsync(zip, version, log, ct);
            }
            finally
            {
                // Also after a failed (rolled back) update: leave the gym with a running app, not a stopped one.
                if (File.Exists(Path.Combine(_ctx.Paths.AppDir, "artisan")))
                {
                    log("Starting services");
                    if (wasPhp) await svc.Php.StartAsync(CancellationToken.None);
                    if (wasNginx) await svc.Nginx.StartAsync(CancellationToken.None);
                    if (wasSched) await svc.Scheduler.StartAsync(CancellationToken.None);
                }
            }
        });
        if (ok) Close();
    }
}
