using JPrime.Panel.App;
using JPrime.Panel.Setup;

namespace JPrime.Panel.Runtime.Updates;

/// <summary>Downloads and applies one component update, stopping and restarting the affected services around it.
/// Shared by the Updates dialog and the overnight auto-update, so both behave identically.</summary>
public sealed class UpdateRunner
{
    private readonly AppServices _ctx;

    public UpdateRunner(AppServices ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Panel updates restart the process, so they must always go last.</summary>
    public static IEnumerable<UpdateInfo> InInstallOrder(IEnumerable<UpdateInfo> updates) =>
        updates.OrderBy(u => u.Target switch { UpdateTarget.App => 0, UpdateTarget.Helper => 1, _ => 2 });

    public async Task<string> DownloadAsync(UpdateInfo u, Action<string> log, CancellationToken ct)
    {
        var rel = u.Latest ?? throw new InvalidOperationException($"No release known for {u.Title}.");
        var sha = await GitHubReleases.ReadSha256Async(_ctx.Http, rel.Sha256Url, ct).ConfigureAwait(false);
        if (sha is null && u.Target == UpdateTarget.Panel) throw new InvalidOperationException("The panel release has no .sha256 file; refusing to replace the panel without a checksum.");
        var (id, fileName, kind) = u.Target switch
        {
            UpdateTarget.App => ("app", $"jprimefitness.ph-{rel.Tag}.zip", PayloadKind.Zip),
            UpdateTarget.Helper => ("hikvision", $"hikvision-{rel.Tag}.zip", PayloadKind.Zip),
            _ => ("panel", $"JPrimePanel-{rel.Tag}.exe", PayloadKind.Exe),
        };
        var payload = new Payload(id, $"{u.Title} {rel.Tag}", rel.AssetUrl, fileName, kind, rel.Tag, Sha256: sha, ExpectedSize: rel.Size);
        log($"Downloading {fileName} ({rel.Size / 1024 / 1024} MB)");
        var last = -1;
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Fraction is not { } f) return;
            var pct = (int)(f * 100) / 10 * 10;
            if (pct != last) { last = pct; log($"  {pct}% ({p.Received / 1024 / 1024} MB)"); }
        });
        return await new DownloadManager(_ctx.Http, _ctx.Paths.DownloadsDir).FetchAsync(payload, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Download + apply. For the panel this does not return normally: the process restarts.</summary>
    public async Task InstallAsync(UpdateInfo u, Action<string> log, CancellationToken ct)
    {
        var file = await DownloadAsync(u, log, ct).ConfigureAwait(false);
        var version = u.Latest!.Tag;
        switch (u.Target)
        {
            case UpdateTarget.App: await ApplyAppAsync(file, version, log, ct).ConfigureAwait(false); break;
            case UpdateTarget.Helper: await ApplyHelperAsync(file, version, log, ct).ConfigureAwait(false); break;
            default: await ApplyPanelAsync(file, log, ct).ConfigureAwait(false); break;
        }
    }

    public async Task ApplyAppAsync(string zip, string version, Action<string> log, CancellationToken ct)
    {
        var svc = _ctx.Services;
        var wasNginx = svc.Nginx.Status.IsActive;
        var wasPhp = svc.Php.Status.IsActive;
        var wasSched = svc.Scheduler.Status.IsActive;

        log("Backing up the database first");
        await new SqliteBackup(_ctx.Paths, _ctx.Config).RunAsync(ct, log).ConfigureAwait(false);

        log("Stopping web services");
        await svc.Nginx.StopAsync(ct).ConfigureAwait(false);
        await svc.Scheduler.StopAsync(ct).ConfigureAwait(false);
        await svc.Php.StopAsync(ct).ConfigureAwait(false);

        try
        {
            await new AppUpdater(_ctx).ApplyAsync(zip, version, log, ct).ConfigureAwait(false);
        }
        finally
        {
            // Also after a failed (rolled back) update: leave the gym with a running app, not a stopped one.
            if (File.Exists(Path.Combine(_ctx.Paths.AppDir, "artisan")))
            {
                log("Starting services");
                if (wasPhp) await svc.Php.StartAsync(CancellationToken.None).ConfigureAwait(false);
                if (wasNginx) await svc.Nginx.StartAsync(CancellationToken.None).ConfigureAwait(false);
                if (wasSched) await svc.Scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task ApplyHelperAsync(string zip, string version, Action<string> log, CancellationToken ct)
    {
        var svc = _ctx.Services;
        var wasRunning = svc.HikVision.Status.IsActive;
        log("Stopping the biometric helper");
        await svc.HikVision.StopAsync(ct).ConfigureAwait(false);
        try
        {
            new HelperUpdater(_ctx).Apply(zip, version, log);
        }
        finally
        {
            if (wasRunning && File.Exists(_ctx.Paths.HikVisionExe))
            {
                log("Starting the biometric helper");
                await svc.HikVision.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public Task ApplyPanelAsync(string exe, Action<string> log, CancellationToken ct)
    {
        var app = PanelAppContext.Current ?? throw new InvalidOperationException("The panel can only update itself while running as the panel.");
        return app.RestartForUpdateAsync(exe, log, ct);
    }

    /// <summary>True when the web app has not served a request for <see cref="PanelConfig.UpdateSettings.IdleMinutes"/>
    /// (nginx access log untouched) — the only safe moment for an unattended update.</summary>
    public bool IsIdle()
    {
        var accessLog = Path.Combine(_ctx.Paths.NginxDir, "logs", "access.log");
        if (!File.Exists(accessLog)) return true;
        var idle = TimeSpan.FromMinutes(Math.Max(1, _ctx.Config.Updates.IdleMinutes));
        return DateTime.UtcNow - File.GetLastWriteTimeUtc(accessLog) >= idle;
    }

    /// <summary>Local time inside the nightly window (handles windows that cross midnight).</summary>
    public bool InApplyWindow(DateTime localNow)
    {
        var from = Math.Clamp(_ctx.Config.Updates.AutoApplyFromHour, 0, 23);
        var to = Math.Clamp(_ctx.Config.Updates.AutoApplyToHour, 0, 23);
        var h = localNow.Hour;
        return from <= to ? h >= from && h < to : h >= from || h < to;
    }
}
