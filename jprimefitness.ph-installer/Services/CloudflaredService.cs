using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

/// <summary>Remotely-managed Cloudflare Tunnel publishing the biometric helper as a public hostname.
/// The token comes from the DPAPI secret store and is passed via the TUNNEL_TOKEN environment variable.</summary>
public sealed class CloudflaredService : SupervisedService
{
    /// <summary>Loopback port for cloudflared's metrics endpoint (/ready). Chosen at each start: the preferred port is
    /// often taken by another cloudflared (a WSL instance's relay, for example), and cloudflared refuses to start then.</summary>
    private int _metricsPort;

    public CloudflaredService(AppServices ctx) : base("cloudflared", "Cloudflare Tunnel", ctx)
    {
        _metricsPort = ctx.Config.Tunnel.MetricsPort;
    }

    public override string PortLabel => Ctx.Config.Tunnel.PublicHostname;
    public override string Description => "Publishes the biometric helper to the live server through Cloudflare.";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("cloudflared (console)", Sink, null, null),
    };

    protected override TimeSpan StartupGrace => TimeSpan.FromSeconds(45);

    protected override RestartPolicy Policy => new()
    {
        Delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) },
        MaxConsecutiveFailures = 0,
        Describe = exit => exit.Uptime < TimeSpan.FromSeconds(5) ? "cloudflared exited immediately (invalid token or no network?)" : null,
    };

    protected override IHealthProbe Probe => new HttpProbe(Ctx.Http, $"http://127.0.0.1:{_metricsPort}/ready", (status, _) =>
        status is >= 200 and < 300 ? HealthResult.Healthy
        : status == 503 ? HealthResult.Degraded("connecting to Cloudflare edge")
        : HealthResult.Degraded($"HTTP {status}"));

    protected override ManagedProcessOptions BuildOptions()
    {
        var token = Ctx.Secrets.Get(SecretStore.TunnelToken) ?? "";
        _metricsPort = PickMetricsPort();
        return new ManagedProcessOptions
        {
            FileName = Ctx.Paths.CloudflaredExe,
            Arguments = new[] { "tunnel", "--no-autoupdate", "--metrics", $"127.0.0.1:{_metricsPort}", "run" },
            WorkingDirectory = Ctx.Paths.CloudflaredDir,
            Environment = new Dictionary<string, string> { ["TUNNEL_TOKEN"] = token },
            GracefulStop = GracefulStopMode.CtrlC,
            GracefulTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private int PickMetricsPort()
    {
        var preferred = Ctx.Config.Tunnel.MetricsPort;
        for (var port = preferred; port < preferred + 20; port++)
        {
            if (PortScanner.IsFree(port, System.Net.IPAddress.Loopback))
            {
                if (port != preferred) Log.Info($"cloudflared metrics port {preferred} is in use ({PortScanner.WhoHolds(preferred)?.ProcessName ?? "unknown"}); using {port}");
                return port;
            }
        }
        throw new InvalidOperationException($"No free loopback port between {preferred} and {preferred + 19} for cloudflared metrics.");
    }

    protected override Task PreflightAsync(CancellationToken ct)
    {
        RequireFile(Ctx.Paths.CloudflaredExe, "cloudflared.exe");
        if (!Ctx.Secrets.Has(SecretStore.TunnelToken))
        {
            throw new InvalidOperationException("No Cloudflare tunnel token is configured. Open Config for this row and paste the token.");
        }
        return Task.CompletedTask;
    }
}
