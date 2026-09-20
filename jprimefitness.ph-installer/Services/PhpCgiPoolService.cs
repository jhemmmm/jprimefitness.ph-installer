using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

/// <summary>N php-cgi.exe FastCGI workers on consecutive loopback ports. Windows php-cgi has no fork, so each worker
/// serves one request at a time; nginx round-robins across the pool.</summary>
public sealed class PhpCgiPoolService : ServiceBase
{
    private readonly List<Supervisor> _workers = new();
    private readonly Dictionary<int, HealthResult> _health = new();

    public PhpCgiPoolService(AppServices ctx) : base("php", "PHP (FastCGI pool)", ctx)
    {
    }

    public override string PortLabel
    {
        get
        {
            var w = Ctx.Config.Web;
            return w.PoolSize == 1 ? $":{w.PoolBasePort}" : $":{w.PoolBasePort}-{w.PoolBasePort + w.PoolSize - 1}";
        }
    }

    public override string Description => "Runs the Laravel application code for nginx.";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("php-cgi (console)", Sink, null, null),
        new LogSource("laravel.log", null, Ctx.Paths.AppLogsDir, "laravel*.log"),
    };

    public int PoolSize => Ctx.Config.Web.PoolSize;
    public int WorkerPort(int index) => Ctx.Config.Web.PoolBasePort + index;

    public static IReadOnlyDictionary<string, string> PhpEnvironment(AppPaths paths, int maxRequests) => new Dictionary<string, string>
    {
        ["PHP_FCGI_MAX_REQUESTS"] = maxRequests.ToString(),
        ["PHP_BINARY"] = paths.PhpExe,
        ["PHPRC"] = paths.PhpDir,
        ["PATH"] = paths.PhpDir,
    };

    private ManagedProcessOptions BuildWorker(int port) => new()
    {
        FileName = Ctx.Paths.PhpCgiExe,
        Arguments = new[] { "-b", $"127.0.0.1:{port}", "-c", Ctx.Paths.PhpIni },
        WorkingDirectory = Ctx.Paths.AppDir,
        Environment = PhpEnvironment(Ctx.Paths, Ctx.Config.Web.MaxRequests),
        GracefulStop = GracefulStopMode.None,
    };

    private static RestartPolicy WorkerPolicy => new()
    {
        // Clean exit after PHP_FCGI_MAX_REQUESTS: respawn immediately.
        IsExpectedExit = exit => exit.ExitCode == 0 && exit.Uptime > TimeSpan.FromSeconds(2),
        Delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) },
        MaxConsecutiveFailures = 10,
        Describe = exit => exit.Uptime < TimeSpan.FromSeconds(2) ? "php-cgi exits immediately (php.ini or extension problem?)" : null,
    };

    private void EnsureWorkers()
    {
        var size = PoolSize;
        while (_workers.Count < size)
        {
            var index = _workers.Count;
            var s = new Supervisor($"php-cgi-{WorkerPort(index)}", () => BuildWorker(WorkerPort(index)), Job, Sink, WorkerPolicy);
            s.StateChanged += _ => Recompute();
            _workers.Add(s);
        }
        while (_workers.Count > size)
        {
            var last = _workers[^1];
            _workers.RemoveAt(_workers.Count - 1);
            last.Dispose();
        }
    }

    protected override async Task DoStartAsync(CancellationToken ct)
    {
        Status = new ServiceStatus(ServiceState.Starting, Array.Empty<int>(), null, null, null);
        RequireFile(Ctx.Paths.PhpCgiExe, "php-cgi.exe");
        RequireFile(Ctx.Paths.PhpIni, "php.ini");
        RequireFile(Ctx.Paths.AppArtisan, "Laravel app (artisan)");
        for (var i = 0; i < PoolSize; i++)
        {
            var port = WorkerPort(i);
            if (!PortScanner.IsFree(port))
            {
                var owner = PortScanner.WhoHolds(port);
                throw new InvalidOperationException($"Pool port {port} is in use by {owner?.ProcessName ?? "another program"}. Change the base port in Settings.");
            }
        }
        EnsureWorkers();
        foreach (var w in _workers)
        {
            await w.StartAsync(ct).ConfigureAwait(false);
        }
        Recompute();
    }

    protected override async Task DoStopAsync(CancellationToken ct)
    {
        Status = Status with { State = ServiceState.Stopping };
        foreach (var w in _workers)
        {
            await w.StopAsync(ct).ConfigureAwait(false);
        }
        _health.Clear();
        Recompute();
    }

    /// <summary>Restart workers one at a time so nginx always has a live upstream (used after php.ini/.env changes).</summary>
    public async Task RollingRestartAsync(CancellationToken ct = default)
    {
        if (!Desired) return;
        foreach (var w in _workers.ToList())
        {
            await w.RestartAsync(ct).ConfigureAwait(false);
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
    }

    public override void KillNow()
    {
        foreach (var w in _workers) w.KillNow();
    }

    public override async Task RefreshAsync(CancellationToken ct)
    {
        foreach (var w in _workers)
        {
            if (w.State != SupervisorState.Running) continue;
            var port = int.Parse(w.Name.Split('-')[^1]);
            _health[port] = await new TcpPortProbe(port).ProbeAsync(ct).ConfigureAwait(false);
        }
        Recompute();
    }

    private void Recompute()
    {
        if (_workers.Count == 0)
        {
            Status = ServiceStatus.Stopped;
            return;
        }
        var pids = _workers.Select(w => w.Pid).Where(p => p.HasValue).Select(p => p!.Value).ToArray();
        var running = _workers.Count(w => w.State == SupervisorState.Running);
        var waiting = _workers.Count(w => w.State == SupervisorState.WaitingRestart);
        var failed = _workers.Count(w => w.State == SupervisorState.Failed);
        var stopping = _workers.Any(w => w.State == SupervisorState.Stopping);
        var starting = _workers.Any(w => w.State == SupervisorState.Starting);
        var uptime = _workers.Select(w => w.Current?.IsRunning == true ? w.Current.Uptime : (TimeSpan?)null).Where(u => u.HasValue).Select(u => u!.Value).DefaultIfEmpty().Max();

        if (!Desired && running == 0 && !stopping)
        {
            Status = ServiceStatus.Stopped;
            return;
        }
        if (stopping)
        {
            Status = new ServiceStatus(ServiceState.Stopping, pids, null, null, uptime);
            return;
        }
        if (running == _workers.Count)
        {
            var unhealthy = _health.Values.Count(h => h.Level != HealthLevel.Ok);
            Status = unhealthy == 0
                ? new ServiceStatus(ServiceState.Running, pids, $"{running} workers", null, uptime)
                : new ServiceStatus(ServiceState.Degraded, pids, $"{unhealthy} of {running} workers not accepting connections", null, uptime);
            return;
        }
        if (failed == _workers.Count)
        {
            Status = new ServiceStatus(ServiceState.Failed, pids, _workers[0].LastMessage ?? "workers keep crashing", null, null);
            return;
        }
        if (running == 0 && starting)
        {
            Status = new ServiceStatus(ServiceState.Starting, pids, null, null, uptime);
            return;
        }
        var next = _workers.Where(w => w.NextRetryAtUtc.HasValue).Select(w => w.NextRetryAtUtc).Min();
        Status = new ServiceStatus(running > 0 ? ServiceState.Degraded : ServiceState.WaitingRestart, pids,
            $"{running} of {_workers.Count} workers running" + (waiting > 0 ? $", {waiting} restarting" : "") + (failed > 0 ? $", {failed} failed" : ""),
            next, uptime);
    }

    public override void Dispose()
    {
        base.Dispose();
        foreach (var w in _workers) w.Dispose();
        _workers.Clear();
    }
}
