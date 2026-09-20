using System.Diagnostics;
using JPrime.Panel.App;
using JPrime.Panel.Health;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

public sealed class NginxService : SupervisedService
{
    public NginxService(AppServices ctx) : base("nginx", "Web server (nginx)", ctx)
    {
    }

    public override string PortLabel => $":{Ctx.Config.Web.Port}";
    public override string Description => "Serves the JPrime app to browsers, phones and kiosk tablets.";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("nginx (console)", Sink, null, null),
        new LogSource("nginx error.log", null, Ctx.Paths.NginxLogsDir, "error.log"),
        new LogSource("nginx access.log", null, Ctx.Paths.NginxLogsDir, "access.log"),
        new LogSource("laravel.log", null, Ctx.Paths.AppLogsDir, "laravel*.log"),
    };

    protected override TimeSpan StartupGrace => TimeSpan.FromSeconds(15);

    protected override RestartPolicy Policy => new()
    {
        Delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) },
        MaxConsecutiveFailures = 5,
        Describe = exit => exit.Uptime < TimeSpan.FromSeconds(3)
            ? "nginx exited immediately; see nginx error.log (port in use or bad config?)"
            : null,
    };

    protected override IHealthProbe Probe => new HttpProbe(Ctx.Http, $"http://127.0.0.1:{Ctx.Config.Web.Port}/up", (status, _) =>
        status is >= 200 and < 300 ? HealthResult.Healthy
        : status is 502 or 504 ? HealthResult.Degraded("PHP pool not answering (502)")
        : status is 503 ? HealthResult.Degraded("app in maintenance mode (503)")
        : HealthResult.Degraded($"HTTP {status}"));

    private string Prefix => AppPaths.Fwd(Ctx.Paths.NginxDir) + "/";

    protected override ManagedProcessOptions BuildOptions() => new()
    {
        FileName = Ctx.Paths.NginxExe,
        Arguments = new[] { "-p", Prefix, "-c", "conf/nginx.conf" },
        WorkingDirectory = Ctx.Paths.NginxDir,
        GracefulStop = GracefulStopMode.Custom,
        GracefulTimeout = TimeSpan.FromSeconds(5),
        CustomStop = (_, ct) => RunSignalAsync("quit", ct),
    };

    protected override async Task PreflightAsync(CancellationToken ct)
    {
        RequireFile(Ctx.Paths.NginxExe, "nginx.exe");
        RequireFile(Ctx.Paths.NginxConf, "nginx.conf");
        Directory.CreateDirectory(Ctx.Paths.NginxLogsDir);
        Directory.CreateDirectory(Path.Combine(Ctx.Paths.NginxDir, "temp"));

        var port = Ctx.Config.Web.Port;
        if (!PortScanner.IsFree(port))
        {
            var owner = PortScanner.WhoHolds(port);
            var who = owner is null ? "another program" : $"{owner.ProcessName} (pid {owner.Pid})";
            throw new InvalidOperationException($"Port {port} is already in use by {who}. {owner?.Hint}".Trim());
        }

        var (code, output) = await RunNginxAsync(new[] { "-t" }, ct).ConfigureAwait(false);
        if (code != 0)
        {
            throw new InvalidOperationException("nginx configuration test failed: " + output.Trim());
        }
    }

    private Task RunSignalAsync(string signal, CancellationToken ct) => RunNginxAsync(new[] { "-s", signal }, ct);

    private async Task<(int Code, string Output)> RunNginxAsync(IEnumerable<string> extraArgs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Ctx.Paths.NginxExe)
        {
            WorkingDirectory = Ctx.Paths.NginxDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(Prefix);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("conf/nginx.conf");
        foreach (var a in extraArgs) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("nginx did not start");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Sink.Append(line.TrimEnd('\r'));
        return (p.ExitCode, output);
    }
}
