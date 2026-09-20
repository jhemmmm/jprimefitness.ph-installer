using System.Diagnostics;
using System.Text;
using JPrime.Panel.App;
using JPrime.Panel.Services;

namespace JPrime.Panel.Runtime;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
    public string Combined => (StdOut + (StdErr.Length > 0 ? Environment.NewLine + StdErr : "")).Trim();
}

/// <summary>Runs php.exe (CLI) in the app directory with the same environment the pool uses.</summary>
public sealed class PhpRunner
{
    private readonly AppPaths _paths;
    private readonly int _maxRequests;

    public PhpRunner(AppPaths paths, int maxRequests = 500)
    {
        _paths = paths;
        _maxRequests = maxRequests;
    }

    public Task<CommandResult> RunAsync(IEnumerable<string> args, CancellationToken ct, TimeSpan? timeout = null, Action<string>? onLine = null, string? workingDir = null)
        => RunExeAsync(_paths.PhpExe, args, ct, timeout, onLine, workingDir ?? _paths.AppDir);

    public async Task<CommandResult> RunExeAsync(string exe, IEnumerable<string> args, CancellationToken ct, TimeSpan? timeout, Action<string>? onLine, string workingDir)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in PhpCgiPoolService.PhpEnvironment(_paths, _maxRequests))
        {
            if (k == "PATH") psi.Environment["PATH"] = v + ";" + (psi.Environment.TryGetValue("PATH", out var cur) ? cur : "");
            else psi.Environment[k] = v;
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is null) return; stdout.AppendLine(e.Data); onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is null) return; stderr.AppendLine(e.Data); onLine?.Invoke(e.Data); };

        Log.Info($"run: {Path.GetFileName(exe)} {string.Join(' ', psi.ArgumentList.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a))}");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{Path.GetFileName(exe)} {string.Join(' ', args)} did not finish in time.");
        }
        p.WaitForExit();
        return new CommandResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }
}

/// <summary>Typed artisan commands. Always non-interactive and ANSI-free.</summary>
public sealed class ArtisanRunner
{
    private readonly PhpRunner _php;
    private readonly AppPaths _paths;

    public ArtisanRunner(AppPaths paths, PhpRunner php)
    {
        _paths = paths;
        _php = php;
    }

    public Task<CommandResult> RunAsync(CancellationToken ct, Action<string>? onLine, params string[] artisanArgs)
    {
        var args = new List<string> { _paths.AppArtisan };
        args.AddRange(artisanArgs);
        args.Add("-n");
        args.Add("--no-ansi");
        return _php.RunAsync(args, ct, TimeSpan.FromMinutes(15), onLine);
    }

    public Task<CommandResult> KeyGenerate(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "key:generate", "--force");
    public Task<CommandResult> Migrate(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "migrate", "--force");
    public Task<CommandResult> SeedProduction(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "db:seed", "--class=ProductionSeeder", "--force");
    public Task<CommandResult> Optimize(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "optimize");
    public Task<CommandResult> OptimizeClear(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "optimize:clear");
    public Task<CommandResult> SyncBootstrap(CancellationToken ct, Action<string>? onLine = null) => RunAsync(ct, onLine, "sync:bootstrap");
    public Task<CommandResult> About(CancellationToken ct) => RunAsync(ct, null, "about");

    /// <summary>Throws with the command output when the exit code is non-zero.</summary>
    public static CommandResult Require(CommandResult r, string what)
    {
        if (!r.Ok) throw new InvalidOperationException($"{what} failed (exit {r.ExitCode}):\n{r.Combined}");
        return r;
    }
}
