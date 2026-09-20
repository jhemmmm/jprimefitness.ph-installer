using System.Diagnostics;
using System.Text;

namespace JPrime.Panel.Processes;

/// <summary>One supervised child process: hidden window, job-object membership, captured stdio, graceful stop.</summary>
public sealed class ManagedProcess : IDisposable
{
    private readonly ManagedProcessOptions _options;
    private readonly JobObject _job;
    private readonly OutputSink _sink;
    private Process? _process;
    private DateTime _startedAt;
    private volatile bool _stopRequested;
    private volatile bool _exitRaised;

    public ManagedProcess(ManagedProcessOptions options, JobObject job, OutputSink sink)
    {
        _options = options;
        _job = job;
        _sink = sink;
    }

    public int? Pid => _process is { } p && !HasExitedSafe(p) ? p.Id : null;
    public bool IsRunning => _process is { } p && !HasExitedSafe(p);
    public int? ExitCode { get; private set; }
    public DateTime? StartedAt => _process is null ? null : _startedAt;
    public TimeSpan Uptime => _process is null ? TimeSpan.Zero : DateTime.UtcNow - _startedAt;
    public bool StopRequested => _stopRequested;
    public string CommandLine => $"{_options.FileName} {string.Join(' ', _options.Arguments)}";

    /// <summary>Raised once per process, after stdio is fully drained.</summary>
    public event Action<ManagedProcess, ProcessExit>? Exited;

    public void Start()
    {
        if (_process is not null) throw new InvalidOperationException("Process already started");

        var psi = new ProcessStartInfo
        {
            FileName = _options.FileName,
            WorkingDirectory = _options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in _options.Arguments) psi.ArgumentList.Add(a);
        foreach (var (k, v) in _options.Environment)
        {
            if (k.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                var existing = psi.Environment.TryGetValue("PATH", out var cur) ? cur : System.Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.Environment["PATH"] = v + ";" + existing;
            }
            else
            {
                psi.Environment[k] = v;
            }
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) _sink.Append(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _sink.Append(e.Data, isError: true); };
        process.Exited += (_, _) => Task.Run(() => OnExited(process));

        _sink.AppendMarker($"start: {CommandLine}");
        lock (ConsoleCtrl.Lock)
        {
            process.Start();
            _startedAt = DateTime.UtcNow;
            _job.Assign(process);
        }
        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        App.Log.Info($"Started {Path.GetFileName(_options.FileName)} pid {process.Id} in {_job.Name}");
    }

    /// <summary>Graceful stop then kill. Returns true when the process ended before the graceful timeout.</summary>
    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        var p = _process;
        if (p is null || HasExitedSafe(p)) return true;
        _stopRequested = true;

        var graceful = false;
        try
        {
            switch (_options.GracefulStop)
            {
                case GracefulStopMode.CtrlC:
                    _sink.AppendMarker("stop: ctrl+c");
                    if (ConsoleCtrl.SendCtrlC(p.Id))
                    {
                        graceful = await WaitForExitAsync(p, _options.GracefulTimeout, ct).ConfigureAwait(false);
                    }
                    break;
                case GracefulStopMode.Custom when _options.CustomStop is not null:
                    _sink.AppendMarker("stop: graceful");
                    try
                    {
                        await _options.CustomStop(this, ct).ConfigureAwait(false);
                        graceful = await WaitForExitAsync(p, _options.GracefulTimeout, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        App.Log.Warn($"Graceful stop failed for pid {p.Id}: {ex.Message}");
                    }
                    break;
            }
        }
        finally
        {
            if (!HasExitedSafe(p))
            {
                _sink.AppendMarker(graceful ? "stop: exited" : "stop: kill");
                Kill();
            }
        }
        // Ensure the Exited event has been raised before returning so callers see a consistent state.
        await WaitForExitAsync(p, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return graceful;
    }

    public void Kill()
    {
        var p = _process;
        if (p is null) return;
        _stopRequested = true;
        try
        {
            if (!HasExitedSafe(p)) p.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Kill pid {p.Id} failed: {ex.Message}");
            try { ProcessTree.KillTree(p.Id); } catch { }
        }
    }

    private static async Task<bool> WaitForExitAsync(Process p, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return HasExitedSafe(p);
        }
    }

    private void OnExited(Process process)
    {
        if (_exitRaised) return;
        _exitRaised = true;
        try
        {
            process.WaitForExit(); // drains async stdout/stderr
        }
        catch { }
        int code;
        try { code = process.ExitCode; } catch { code = -1; }
        ExitCode = code;
        var uptime = DateTime.UtcNow - _startedAt;
        _sink.AppendMarker($"exit code {code} after {uptime:hh\\:mm\\:ss}");
        App.Log.Info($"{Path.GetFileName(_options.FileName)} pid {process.Id} exited {code} after {uptime:hh\\:mm\\:ss} (requested={_stopRequested})");
        var info = new ProcessExit(code, uptime, _stopRequested, _sink.Tail(10));
        try { Exited?.Invoke(this, info); }
        catch (Exception ex) { App.Log.Error("Exited handler failed", ex); }
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }
}
