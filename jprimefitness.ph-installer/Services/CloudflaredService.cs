using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

/// <summary>Remotely-managed Cloudflare Tunnel publishing the biometric helper as a public hostname.
/// The token comes from the DPAPI secret store and is passed via the TUNNEL_TOKEN environment variable.</summary>
public sealed class CloudflaredService : SupervisedService
{
    public CloudflaredService(AppServices ctx) : base("cloudflared", "Cloudflare Tunnel", ctx)
    {
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

    protected override IHealthProbe Probe => new HttpProbe(Ctx.Http, $"http://127.0.0.1:{Ctx.Config.Tunnel.MetricsPort}/ready", (status, _) =>
        status is >= 200 and < 300 ? HealthResult.Healthy
        : status == 503 ? HealthResult.Degraded("connecting to Cloudflare edge")
        : HealthResult.Degraded($"HTTP {status}"));

    protected override ManagedProcessOptions BuildOptions()
    {
        var token = Ctx.Secrets.Get(SecretStore.TunnelToken) ?? "";
        return new ManagedProcessOptions
        {
            FileName = Ctx.Paths.CloudflaredExe,
            Arguments = new[] { "tunnel", "--no-autoupdate", "--metrics", $"127.0.0.1:{Ctx.Config.Tunnel.MetricsPort}", "run" },
            WorkingDirectory = Ctx.Paths.CloudflaredDir,
            Environment = new Dictionary<string, string> { ["TUNNEL_TOKEN"] = token },
            GracefulStop = GracefulStopMode.CtrlC,
            GracefulTimeout = TimeSpan.FromSeconds(5),
        };
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
