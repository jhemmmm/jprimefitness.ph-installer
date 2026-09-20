using JPrime.Panel.App;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

/// <summary>Owns the ordered service list, start/stop-all, autostart and the 2-second health poll.</summary>
public sealed class ServiceManager : IDisposable
{
    private static readonly string[] RuntimeImages = { "nginx", "php-cgi", "php", "HikVision", "cloudflared" };

    private readonly AppServices _ctx;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private Task? _pollLoop;

    public ServiceManager(AppServices ctx)
    {
        _ctx = ctx;
        Nginx = new NginxService(ctx);
        Php = new PhpCgiPoolService(ctx);
        Scheduler = new SchedulerService(ctx);
        HikVision = new HikVisionService(ctx);
        Cloudflared = new CloudflaredService(ctx);
        // Start order: pool -> nginx -> scheduler -> helper -> tunnel. Stop order is the reverse.
        All = new ServiceBase[] { Php, Nginx, Scheduler, HikVision, Cloudflared };
        foreach (var s in All) s.StatusChanged += svc => AnyStatusChanged?.Invoke(svc);
    }

    public NginxService Nginx { get; }
    public PhpCgiPoolService Php { get; }
    public SchedulerService Scheduler { get; }
    public HikVisionService HikVision { get; }
    public CloudflaredService Cloudflared { get; }
    public IReadOnlyList<ServiceBase> All { get; }
    public IEnumerable<ServiceBase> Enabled => All.Where(s => s.Enabled);

    public event Action<ServiceBase>? AnyStatusChanged;

    public ServiceBase? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    public int SweepOrphans() => ProcessTree.SweepOrphans(_ctx.Paths.InstallRoot, RuntimeImages);

    public void StartPolling()
    {
        _pollLoop ??= Task.Run(PollLoopAsync);
    }

    private async Task PollLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                await PollOnceAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task PollOnceAsync()
    {
        if (!await _pollGate.WaitAsync(0).ConfigureAwait(false)) return; // single-flight
        try
        {
            foreach (var s in Enabled)
            {
                if (!s.Status.IsActive) continue;
                try
                {
                    await s.RefreshAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Warn($"{s.Id}: probe failed: {ex.Message}");
                }
            }
        }
        finally
        {
            _pollGate.Release();
        }
    }

    public async Task StartAllAsync(bool onlyAutostart = false, CancellationToken ct = default)
    {
        foreach (var s in All)
        {
            if (!s.Enabled) continue;
            if (onlyAutostart && !s.Autostart) continue;
            await s.StartAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        foreach (var s in All.Reverse())
        {
            if (!s.Enabled) continue;
            await s.StopAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Windows is shutting down / logging off: best effort, no waiting. Job objects finish the rest.</summary>
    public void KillAllNow()
    {
        foreach (var s in All.Reverse())
        {
            try { s.KillNow(); } catch { }
        }
    }

    public bool AnyRunning => Enabled.Any(s => s.Status.IsActive);

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var s in All) s.Dispose();
        _cts.Dispose();
        _pollGate.Dispose();
    }
}
