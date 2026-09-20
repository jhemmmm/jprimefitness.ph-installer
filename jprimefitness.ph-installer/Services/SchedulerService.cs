using JPrime.Panel.App;
using JPrime.Panel.Processes;

namespace JPrime.Panel.Services;

/// <summary><c>php artisan schedule:work</c>: runs the Laravel scheduler every minute (membership expiry,
/// notifications, and sync:* when the node role is local).</summary>
public sealed class SchedulerService : SupervisedService
{
    public SchedulerService(AppServices ctx) : base("scheduler", "Scheduler (artisan)", ctx)
    {
    }

    public override string PortLabel => "";
    public override string Description => "Runs scheduled Laravel jobs every minute (expiry, notifications, sync).";

    public override IReadOnlyList<LogSource> LogSources => new[]
    {
        new LogSource("scheduler (console)", Sink, null, null),
        new LogSource("laravel.log", null, Ctx.Paths.AppLogsDir, "laravel*.log"),
    };

    protected override RestartPolicy Policy => new()
    {
        Delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60) },
        MaxConsecutiveFailures = 0,
        Describe = exit => exit.Uptime < TimeSpan.FromSeconds(3) ? "artisan exited immediately (check laravel.log / .env)" : null,
    };

    protected override ManagedProcessOptions BuildOptions() => new()
    {
        FileName = Ctx.Paths.PhpExe,
        Arguments = new[] { Ctx.Paths.AppArtisan, "schedule:work", "-n", "--no-ansi" },
        WorkingDirectory = Ctx.Paths.AppDir,
        Environment = PhpCgiPoolService.PhpEnvironment(Ctx.Paths, Ctx.Config.Web.MaxRequests),
        GracefulStop = GracefulStopMode.None,
    };

    protected override Task PreflightAsync(CancellationToken ct)
    {
        RequireFile(Ctx.Paths.PhpExe, "php.exe");
        RequireFile(Ctx.Paths.AppArtisan, "Laravel app (artisan)");
        RequireFile(Ctx.Paths.AppEnvFile, ".env");
        return Task.CompletedTask;
    }
}
