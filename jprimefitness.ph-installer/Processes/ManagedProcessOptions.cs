namespace JPrime.Panel.Processes;

public enum GracefulStopMode
{
    /// <summary>Kill immediately (php-cgi, scheduler).</summary>
    None,
    /// <summary>Send Ctrl+C to the child's hidden console, wait, then kill (HikVision, cloudflared).</summary>
    CtrlC,
    /// <summary>Run <see cref="ManagedProcessOptions.CustomStop"/> (nginx -s quit), wait, then kill.</summary>
    Custom,
}

public sealed record ManagedProcessOptions
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public required string WorkingDirectory { get; init; }
    /// <summary>Added to (never replaces) the inherited environment. Use key "PATH" to prepend to PATH.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public GracefulStopMode GracefulStop { get; init; } = GracefulStopMode.None;
    public TimeSpan GracefulTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public Func<ManagedProcess, CancellationToken, Task>? CustomStop { get; init; }
}

public sealed record ProcessExit(int ExitCode, TimeSpan Uptime, bool Requested, IReadOnlyList<string> LastLines);
