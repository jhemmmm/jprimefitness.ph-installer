namespace JPrime.Panel.Processes;

/// <summary>Decides whether/when a supervisor restarts an exited process.</summary>
public sealed class RestartPolicy
{
    /// <summary>Delays for consecutive unexpected exits; the last one repeats.</summary>
    public TimeSpan[] Delays { get; init; } = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) };

    /// <summary>Exit that is part of normal operation (php-cgi recycling): restart immediately, counter reset.</summary>
    public Func<ProcessExit, bool> IsExpectedExit { get; init; } = _ => false;

    /// <summary>Uptime after which the failure counter resets (the process was healthy).</summary>
    public TimeSpan StableUptime { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Give up after this many consecutive failures (0 = never).</summary>
    public int MaxConsecutiveFailures { get; init; } = 8;

    /// <summary>Optional classifier for the status message shown in the panel.</summary>
    public Func<ProcessExit, string?> Describe { get; init; } = _ => null;

    public static RestartPolicy Never => new() { MaxConsecutiveFailures = 1, Delays = Array.Empty<TimeSpan>() };

    /// <summary>Returns the delay before the next attempt, or null to give up.</summary>
    public TimeSpan? NextDelay(ProcessExit exit, ref int consecutiveFailures)
    {
        if (IsExpectedExit(exit))
        {
            consecutiveFailures = 0;
            return TimeSpan.Zero;
        }
        if (exit.Uptime >= StableUptime) consecutiveFailures = 0;
        consecutiveFailures++;
        if (MaxConsecutiveFailures > 0 && consecutiveFailures > MaxConsecutiveFailures) return null;
        if (Delays.Length == 0) return null;
        var idx = Math.Min(consecutiveFailures - 1, Delays.Length - 1);
        return Delays[idx];
    }
}
