namespace JPrime.Panel.Processes;

public enum SupervisorState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    WaitingRestart,
    Failed,
}

/// <summary>Keeps one process alive according to a <see cref="RestartPolicy"/>. Options are produced by a factory
/// so ports/env can change between restarts.</summary>
public sealed class Supervisor : IDisposable
{
    private readonly Func<ManagedProcessOptions> _optionsFactory;
    private readonly JobObject _job;
    private readonly RestartPolicy _policy;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _restartCts;
    private int _consecutiveFailures;
    private ManagedProcess? _current;

    public Supervisor(string name, Func<ManagedProcessOptions> optionsFactory, JobObject job, OutputSink sink, RestartPolicy policy)
    {
        Name = name;
        _optionsFactory = optionsFactory;
        _job = job;
        Sink = sink;
        _policy = policy;
    }

    public string Name { get; }
    public OutputSink Sink { get; }
    public SupervisorState State { get; private set; } = SupervisorState.Stopped;
    public bool Desired { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public string? LastMessage { get; private set; }
    public ProcessExit? LastExit { get; private set; }
    public int? Pid => _current?.Pid;
    public ManagedProcess? Current => _current;
    public int ConsecutiveFailures => _consecutiveFailures;

    public event Action<Supervisor>? StateChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Desired = true;
            _consecutiveFailures = 0;
            CancelPendingRestart();
            if (_current is { IsRunning: true }) return;
            Launch();
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
            CancelPendingRestart();
            var p = _current;
            if (p is { IsRunning: true })
            {
                SetState(SupervisorState.Stopping, null);
                await p.StopAsync(ct).ConfigureAwait(false);
            }
            SetState(SupervisorState.Stopped, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Kill without waiting for graceful shutdown (used on Windows shutdown).</summary>
    public void KillNow()
    {
        Desired = false;
        CancelPendingRestart();
        _current?.Kill();
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(false);
        await StartAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Skip the backoff and try again now (only meaningful in WaitingRestart/Failed).</summary>
    public Task RetryNowAsync(CancellationToken ct = default) => StartAsync(ct);

    private void Launch()
    {
        SetState(SupervisorState.Starting, null);
        var options = _optionsFactory();
        var process = new ManagedProcess(options, _job, Sink);
        process.Exited += OnExited;
        _current?.Dispose();
        _current = process;
        try
        {
            process.Start();
            SetState(SupervisorState.Running, null);
        }
        catch (Exception ex)
        {
            App.Log.Error($"{Name}: failed to start {options.FileName}", ex);
            Sink.AppendMarker($"start failed: {ex.Message}");
            _consecutiveFailures++;
            if (_policy.MaxConsecutiveFailures > 0 && _consecutiveFailures > _policy.MaxConsecutiveFailures)
            {
                SetState(SupervisorState.Failed, ex.Message);
            }
            else
            {
                ScheduleRestart(TimeSpan.FromSeconds(5), ex.Message);
            }
        }
    }

    private void OnExited(ManagedProcess process, ProcessExit exit)
    {
        if (!ReferenceEquals(process, _current)) return;
        LastExit = exit;
        if (!Desired || exit.Requested)
        {
            SetState(SupervisorState.Stopped, null);
            return;
        }
        var delay = _policy.NextDelay(exit, ref _consecutiveFailures);
        var message = _policy.Describe(exit) ?? $"exited with code {exit.ExitCode}";
        if (delay is null)
        {
            SetState(SupervisorState.Failed, $"{message} (gave up after {_consecutiveFailures} attempts)");
            return;
        }
        ScheduleRestart(delay.Value, message);
    }

    private void ScheduleRestart(TimeSpan delay, string message)
    {
        CancelPendingRestart();
        var cts = new CancellationTokenSource();
        _restartCts = cts;
        NextRetryAtUtc = DateTime.UtcNow + delay;
        SetState(SupervisorState.WaitingRestart, message);
        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    if (!Desired || cts.IsCancellationRequested) return;
                    NextRetryAtUtc = null;
                    Launch();
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Log.Error($"{Name}: restart failed", ex);
            }
        });
    }

    private void CancelPendingRestart()
    {
        var cts = _restartCts;
        _restartCts = null;
        NextRetryAtUtc = null;
        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
    }

    private void SetState(SupervisorState state, string? message)
    {
        State = state;
        LastMessage = message;
        try { StateChanged?.Invoke(this); }
        catch (Exception ex) { App.Log.Error("StateChanged handler failed", ex); }
    }

    public void Dispose()
    {
        CancelPendingRestart();
        _current?.Kill();
        _current?.Dispose();
        _gate.Dispose();
    }
}
