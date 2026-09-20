using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

public enum ServiceState
{
    Disabled,
    Stopped,
    Starting,
    Running,
    Degraded,
    Stopping,
    WaitingRestart,
    DeviceNotFound,
    Failed,
}

public sealed record ServiceStatus(
    ServiceState State,
    IReadOnlyList<int> Pids,
    string? Message,
    DateTime? NextRetryAtUtc,
    TimeSpan? Uptime)
{
    public static readonly ServiceStatus Stopped = new(ServiceState.Stopped, Array.Empty<int>(), null, null, null);
    public static readonly ServiceStatus Disabled = new(ServiceState.Disabled, Array.Empty<int>(), null, null, null);

    public bool IsActive => State is ServiceState.Starting or ServiceState.Running or ServiceState.Degraded or ServiceState.WaitingRestart or ServiceState.DeviceNotFound;
}

/// <summary>Where the panel can show logs for a service: a captured stdio sink and/or files.</summary>
public sealed record LogSource(string Name, OutputSink? Sink, string? Directory, string? FilePattern);

/// <summary>Common shape of every row in the panel.</summary>
public abstract class ServiceBase : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ServiceStatus _status = ServiceStatus.Stopped;

    protected ServiceBase(string id, string displayName, AppServices ctx)
    {
        Id = id;
        DisplayName = displayName;
        Ctx = ctx;
        Job = new JobObject($"JPrime-{id}");
        Sink = new OutputSink(id, ctx.Paths.LogsDir);
    }

    public string Id { get; }
    public string DisplayName { get; }
    protected AppServices Ctx { get; }
    protected JobObject Job { get; }
    public OutputSink Sink { get; }

    /// <summary>Short label for the port column (e.g. ":8001").</summary>
    public abstract string PortLabel { get; }
    public abstract string Description { get; }
    public abstract IReadOnlyList<LogSource> LogSources { get; }

    public bool Enabled => Ctx.Config.Services.Get(Id).Enabled;
    public bool Autostart
    {
        get => Ctx.Config.Services.Get(Id).Autostart;
        set
        {
            Ctx.Config.Services.Get(Id).Autostart = value;
            Ctx.SaveConfig();
        }
    }

    /// <summary>Whether the operator asked for this service to be running.</summary>
    public bool Desired { get; protected set; }

    public ServiceStatus Status
    {
        get => Enabled ? _status : ServiceStatus.Disabled;
        protected set
        {
            if (_status == value) return;
            _status = value;
            try { StatusChanged?.Invoke(this); }
            catch (Exception ex) { Log.Error($"{Id}: StatusChanged handler failed", ex); }
        }
    }

    public event Action<ServiceBase>? StatusChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!Enabled) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Desired = true;
            await DoStartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"{Id}: start failed", ex);
            Status = new ServiceStatus(ServiceState.Failed, Array.Empty<int>(), ex.Message, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Desired = false;
            await DoStopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"{Id}: stop failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(false);
        await StartAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Best-effort immediate kill (Windows shutdown, panel exit).</summary>
    public abstract void KillNow();

    /// <summary>Called by the manager's poll loop while the service is active; refreshes <see cref="Status"/>.</summary>
    public abstract Task RefreshAsync(CancellationToken ct);

    protected abstract Task DoStartAsync(CancellationToken ct);
    protected abstract Task DoStopAsync(CancellationToken ct);

    /// <summary>Validation to run before starting (binary exists, config renders, port free). Throw to abort.</summary>
    protected void RequireFile(string path, string what)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{what} not found: {path}");
    }

    public virtual void Dispose()
    {
        KillNow();
        Sink.Dispose();
        Job.Dispose();
        _gate.Dispose();
    }
}
