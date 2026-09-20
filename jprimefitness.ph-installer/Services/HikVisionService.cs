using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;
using JPrime.Panel.Services;

namespace JPrime.Panel.Services;

/// <summary>The .NET biometric helper. Startup blocks on SADP discovery and exits with code 1 when the Hikvision
/// terminal is not reachable on the LAN; that is surfaced as <see cref="ServiceState.DeviceNotFound"/> with backoff.</summary>
public sealed class HikVisionService : SupervisedService
{
    public HikVisionService(AppServices ctx) : base("hikvision", "Biometric helper (HikVision)", ctx)
    {
    }

    public int Port => Ctx.Config.Biometric.HelperPort;
    public override string PortLabel => $":{Port}";
    public override string Description => "Talks to the Hikvision fingerprint terminal and forwards taps to the app.";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("helper (console)", Sink, null, null),
        new LogSource("hikvision-helper log", null, Ctx.Paths.HikVisionLogsDir, "hikvision-helper-*.txt"),
    };

    protected override TimeSpan StartupGrace => TimeSpan.FromSeconds(90);

    protected override RestartPolicy Policy => new()
    {
        Delays = new[] { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(300) },
        MaxConsecutiveFailures = 0,
        StableUptime = TimeSpan.FromMinutes(2),
        Describe = exit => exit.ExitCode == 1
            ? "Fingerprint terminal not found on the LAN (or the Hikvision SADP tool is open). Retrying."
            : $"helper exited with code {exit.ExitCode}",
    };

    protected override ServiceState MapWaiting(ProcessExit? exit) =>
        exit?.ExitCode == 1 ? ServiceState.DeviceNotFound : ServiceState.WaitingRestart;

    protected override IHealthProbe Probe => new HttpProbe(Ctx.Http, $"http://127.0.0.1:{Port}/health", (status, body) =>
    {
        if (status is < 200 or >= 300) return HealthResult.Degraded($"HTTP {status}");
        if (HttpProbe.TryGetJsonBool(body, "deviceReady", out var ready) && !ready)
        {
            return HealthResult.Degraded("helper up, terminal not ready");
        }
        return HealthResult.Healthy;
    });

    protected override ManagedProcessOptions BuildOptions()
    {
        var env = new Dictionary<string, string>();
        var dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        if (Directory.Exists(dotnetRoot)) env["DOTNET_ROOT"] = dotnetRoot;
        return new ManagedProcessOptions
        {
            FileName = Ctx.Paths.HikVisionExe,
            Arguments = Array.Empty<string>(),
            WorkingDirectory = Ctx.Paths.HikVisionDir,
            Environment = env,
            GracefulStop = GracefulStopMode.CtrlC,
            GracefulTimeout = TimeSpan.FromSeconds(10),
        };
    }

    protected override Task PreflightAsync(CancellationToken ct)
    {
        RequireFile(Ctx.Paths.HikVisionExe, "HikVision.exe");
        RequireFile(Ctx.Paths.HikVisionAppSettings, "appsettings.json");
        if (!File.Exists(Path.Combine(Ctx.Paths.HikVisionDir, "lib", "HCNetSDK.dll")) && !File.Exists(Path.Combine(Ctx.Paths.HikVisionDir, "HCNetSDK.dll")))
        {
            throw new FileNotFoundException("Hikvision SDK (lib\\HCNetSDK.dll) is missing from the helper folder.");
        }
        if (!PortScanner.IsFree(Port))
        {
            var owner = PortScanner.WhoHolds(Port);
            throw new InvalidOperationException($"Port {Port} is in use by {owner?.ProcessName ?? "another program"} (pid {owner?.Pid}).");
        }
        return Task.CompletedTask;
    }
}
