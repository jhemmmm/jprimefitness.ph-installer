using JPrime.Panel.App;

namespace JPrime.Panel.Runtime.Updates;

/// <summary>Background policy: check GitHub once per interval, announce new versions once, and (opt-in) install them
/// inside the nightly window while the app is idle. Failures are logged and retried the next night, never in a loop.</summary>
public sealed class AutoUpdateScheduler : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(20);

    private readonly AppServices _ctx;
    private readonly System.Threading.Timer _timer;
    private int _busy;
    private string _lastAttemptKey = "";
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    public UpdateChecker Checker { get; }
    public UpdateRunner Runner { get; }

    /// <summary>Raised (worker thread) with a one-line status: new versions found, unattended install started/finished.</summary>
    public event Action<string>? Notice;

    public bool IsApplying => Volatile.Read(ref _busy) == 1;

    public AutoUpdateScheduler(AppServices ctx)
    {
        _ctx = ctx;
        Checker = new UpdateChecker(ctx);
        Runner = new UpdateRunner(ctx);
        // First look ~3 minutes after start (services are up by then), then every 15 minutes.
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromMinutes(3), Tick);
    }

    private async Task TickAsync()
    {
        if (IsApplying) return;
        try
        {
            var cfg = _ctx.Config.Updates;
            if (cfg.AutoCheck)
            {
                var due = cfg.LastCheckUtc is not { } last || DateTime.UtcNow - last >= TimeSpan.FromHours(Math.Max(1, cfg.CheckIntervalHours));
                if (due) await Checker.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var available = UpdateRunner.InInstallOrder(Checker.Available).ToList();
            if (available.Count == 0) return;
            var key = string.Join(",", available.Select(u => $"{u.Target}:{u.Latest!.Tag}"));

            if (cfg.NotifiedKey != key)
            {
                cfg.NotifiedKey = key;
                _ctx.SaveConfig();
                Notice?.Invoke("Update available: " + string.Join(", ", available.Select(u => $"{u.Title} {u.Latest!.Tag}")) + ". Open the panel to install.");
            }

            if (!cfg.AutoApply) return;
            if (!Runner.InApplyWindow(DateTime.Now) || !Runner.IsIdle()) return;
            if (key == _lastAttemptKey && DateTime.UtcNow - _lastAttemptUtc < RetryAfter) return;
            _lastAttemptKey = key;
            _lastAttemptUtc = DateTime.UtcNow;
            await ApplyAllAsync(available).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Update scheduler tick failed", ex);
        }
    }

    private async Task ApplyAllAsync(List<UpdateInfo> updates)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            foreach (var u in updates)
            {
                Log.Info($"Automatic update: {u.Title} {u.Installed} -> {u.Latest!.Tag}");
                Notice?.Invoke($"Installing {u.Title} {u.Latest.Tag} (automatic update)...");
                try
                {
                    await Runner.InstallAsync(u, s => Log.Info("  " + s), CancellationToken.None).ConfigureAwait(false);
                    Notice?.Invoke($"{u.Title} updated to {u.Latest.Tag}.");
                }
                catch (Exception ex)
                {
                    Log.Error($"Automatic update of {u.Title} failed", ex);
                    Notice?.Invoke($"Automatic update of {u.Title} failed: {ex.Message}. Open the panel for details.");
                    // Do not go on to the panel swap after a failed app/helper update; the whole set is retried later.
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose() => _timer.Dispose();
}
