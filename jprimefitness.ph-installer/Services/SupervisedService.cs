using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

/// <summary>A service backed by exactly one supervised process plus an optional readiness probe.</summary>
public abstract class SupervisedService : ServiceBase
{
    private Supervisor? _supervisor;
    private HealthResult? _lastHealth;

    protected SupervisedService(string id, string displayName, AppServices ctx) : base(id, displayName, ctx)
    {
    }

    /// <summary>How long after launch a failing probe still counts as "Starting" rather than "Degraded".</summary>
    protected virtual TimeSpan StartupGrace => TimeSpan.FromSeconds(20);

    protected abstract ManagedProcessOptions BuildOptions();
    protected abstract RestartPolicy Policy { get; }
    protected virtual IHealthProbe? Probe => null;

    /// <summary>Pre-flight checks; throw to refuse the start with a message.</summary>
    protected virtual Task PreflightAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Maps a waiting-restart supervisor state to a service state (HikVision uses DeviceNotFound).</summary>
    protected virtual ServiceState MapWaiting(ProcessExit? exit) => ServiceState.WaitingRestart;

    protected Supervisor Supervisor => _supervisor ??= CreateSupervisor();

    private Supervisor CreateSupervisor()
    {
        var s = new Supervisor(Id, BuildOptions, Job, Sink, Policy);
        s.StateChanged += _ => Recompute();
        return s;
    }

    protected override async Task DoStartAsync(CancellationToken ct)
    {
        Status = new ServiceStatus(ServiceState.Starting, Array.Empty<int>(), null, null, null);
        await PreflightAsync(ct).ConfigureAwait(false);
        _lastHealth = null;
        await Supervisor.StartAsync(ct).ConfigureAwait(false);
        Recompute();
    }

    protected override async Task DoStopAsync(CancellationToken ct)
    {
        if (_supervisor is null)
        {
            Status = ServiceStatus.Stopped;
            return;
        }
        Status = Status with { State = ServiceState.Stopping };
        await _supervisor.StopAsync(ct).ConfigureAwait(false);
        _lastHealth = null;
        Recompute();
    }

    public override void KillNow()
    {
        _supervisor?.KillNow();
    }

    public override async Task RefreshAsync(CancellationToken ct)
    {
        var s = _supervisor;
        if (s is null || s.State != SupervisorState.Running || Probe is null)
        {
            Recompute();
            return;
        }
        _lastHealth = await Probe.ProbeAsync(ct).ConfigureAwait(false);
        Recompute();
    }

    private void Recompute()
    {
        var s = _supervisor;
        if (s is null)
        {
            Status = ServiceStatus.Stopped;
            return;
        }
        var pids = s.Pid is { } pid ? new[] { pid } : Array.Empty<int>();
        var uptime = s.Current?.IsRunning == true ? s.Current.Uptime : (TimeSpan?)null;
        Status = s.State switch
        {
            SupervisorState.Stopped => ServiceStatus.Stopped,
            SupervisorState.Starting => new ServiceStatus(ServiceState.Starting, pids, null, null, uptime),
            SupervisorState.Stopping => new ServiceStatus(ServiceState.Stopping, pids, null, null, uptime),
            SupervisorState.Failed => new ServiceStatus(ServiceState.Failed, pids, s.LastMessage, null, null),
            SupervisorState.WaitingRestart => new ServiceStatus(MapWaiting(s.LastExit), pids, s.LastMessage, s.NextRetryAtUtc, null),
            SupervisorState.Running => RunningStatus(pids, uptime),
            _ => ServiceStatus.Stopped,
        };
    }

    private ServiceStatus RunningStatus(int[] pids, TimeSpan? uptime)
    {
        if (Probe is null) return new ServiceStatus(ServiceState.Running, pids, null, null, uptime);
        var h = _lastHealth;
        if (h is null || h.Level == HealthLevel.Down)
        {
            if (uptime is { } up && up < StartupGrace)
            {
                return new ServiceStatus(ServiceState.Starting, pids, h?.Message ?? "waiting for readiness", null, uptime);
            }
            return new ServiceStatus(ServiceState.Degraded, pids, h?.Message ?? "not responding", null, uptime);
        }
        return h.Level == HealthLevel.Ok
            ? new ServiceStatus(ServiceState.Running, pids, null, null, uptime)
            : new ServiceStatus(ServiceState.Degraded, pids, h.Message, null, uptime);
    }

    public Task RetryNowAsync(CancellationToken ct = default) => _supervisor?.RetryNowAsync(ct) ?? Task.CompletedTask;

    public override void Dispose()
    {
        base.Dispose();
        _supervisor?.Dispose();
    }
}
