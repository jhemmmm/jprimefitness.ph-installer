using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime;
using JPrime.Panel.Runtime.Templates;
using JPrime.Panel.UI.Panel;

namespace JPrime.Panel.Setup;

public sealed record StepProgress(string Step, string Message, double? Fraction);

/// <summary>Ordered, idempotent install steps. Every step can be re-run (Repair / cancelled install).</summary>
public sealed class InstallEngine
{
    private readonly WizardState _s;
    private readonly HttpClient _http;
    private readonly IProgress<StepProgress> _progress;
    private readonly Action<string> _log;

    public InstallEngine(WizardState state, HttpClient http, IProgress<StepProgress> progress, Action<string> log)
    {
        _s = state;
        _http = http;
        _progress = progress;
        _log = log;
    }

    private AppPaths Paths => new(_s.InstallRoot);

    /// <summary>Dev harness only: skip the pointer file / Run key / shortcut so a scratch install never hijacks the real one.</summary>
    public bool SkipRegistration { get; init; }

    public async Task RunAsync(CancellationToken ct)
    {
        var paths = Paths;
        var steps = new List<(string Name, Func<CancellationToken, Task> Run)>
        {
            ("Preparing folders", PrepareDirsAsync),
            ("Copying the control panel", CopySelfAsync),
            ("Installing PHP", ct2 => ExtractAsync("php", paths.PhpDir, ct2)),
            ("Installing nginx", ct2 => ExtractAsync("nginx", paths.NginxDir, ct2)),
            ("Installing the JPrime app", ct2 => ExtractAsync("app", paths.AppDir, ct2)),
            ("Installing certificates", InstallCaBundleAsync),
        };
        if (_s.InstallHelper) steps.Add(("Installing the biometric helper", ct2 => ExtractAsync("hikvision", paths.HikVisionDir, ct2)));
        if (_s.InstallHelper && _s.InstallTunnel) steps.Add(("Installing Cloudflare Tunnel", InstallCloudflaredAsync));
        steps.Add(("Windows components and firewall (administrator)", AdminTasksAsync));
        steps.Add(("Writing configuration", WriteConfigsAsync));
        steps.Add(("Setting up the database", BootstrapAsync));
        if (!SkipRegistration) steps.Add(("Registering the panel", RegisterAsync));

        var total = steps.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (name, run) = steps[i];
            _progress.Report(new StepProgress(name, "", (double)i / total));
            _log($"== {name}");
            await run(ct).ConfigureAwait(false);
        }
        _progress.Report(new StepProgress("Finished", "", 1));
    }

    private Task PrepareDirsAsync(CancellationToken ct)
    {
        var paths = Paths;
        // Created by the non-elevated instance so the user owns the tree (helper writes logs next to its exe).
        foreach (var d in paths.AllRuntimeDirs) Directory.CreateDirectory(d);
        Log.Configure(paths.LogsDir);
        return Task.CompletedTask;
    }

    private Task CopySelfAsync(CancellationToken ct)
    {
        var paths = Paths;
        var src = Environment.ProcessPath!;
        var dst = paths.PanelExe;
        if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
        {
            // A running exe cannot be overwritten but can be renamed.
            if (File.Exists(dst))
            {
                var old = dst + ".old";
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                try { File.Move(dst, old); } catch { File.Delete(dst); }
            }
            File.Copy(src, dst, overwrite: true);
            _log($"Copied panel to {dst}");
        }
        return Task.CompletedTask;
    }

    private async Task ExtractAsync(string id, string dest, CancellationToken ct)
    {
        var payload = _s.Payloads[id];
        var zip = _s.Downloaded[id];
        var marker = Path.Combine(dest, ".installed-" + payload.Version);
        if (File.Exists(marker) && id != "app")
        {
            _log($"{payload.Title} already extracted ({payload.Version})");
            return;
        }
        if (id == "app")
        {
            // Keep .env, storage and the sqlite (lives in data\) across a repair; replace code.
            var paths = Paths;
            string? envBackup = null;
            if (File.Exists(paths.AppEnvFile))
            {
                envBackup = Path.Combine(paths.ConfigDir, ".env.repair-backup");
                File.Copy(paths.AppEnvFile, envBackup, overwrite: true);
            }
            await Task.Run(() => ZipExtractor.Extract(zip, dest, stripTopLevel: true, new Progress<(int, int)>(p => _progress.Report(new StepProgress("Installing the JPrime app", $"{p.Item1}/{p.Item2} files", null))), ct), ct).ConfigureAwait(false);
            if (envBackup is not null) File.Copy(envBackup, paths.AppEnvFile, overwrite: true);
        }
        else
        {
            await Task.Run(() => ZipExtractor.Extract(zip, dest, stripTopLevel: payload.StripTopLevel, new Progress<(int, int)>(p => _progress.Report(new StepProgress(payload.Title, $"{p.Item1}/{p.Item2} files", null))), ct), ct).ConfigureAwait(false);
        }
        foreach (var old in Directory.GetFiles(dest, ".installed-*")) File.Delete(old);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
        _log($"{payload.Title} extracted to {dest}");
    }

    private Task InstallCaBundleAsync(CancellationToken ct)
    {
        File.Copy(_s.Downloaded["cacert"], Paths.CaBundle, overwrite: true);
        return Task.CompletedTask;
    }

    private Task InstallCloudflaredAsync(CancellationToken ct)
    {
        var paths = Paths;
        Directory.CreateDirectory(paths.CloudflaredDir);
        File.Copy(_s.Downloaded["cloudflared"], paths.CloudflaredExe, overwrite: true);
        return Task.CompletedTask;
    }

    private async Task AdminTasksAsync(CancellationToken ct)
    {
        var paths = Paths;
        var tasks = new List<AdminTaskRequest>();
        if (_s.NeedVcRedist && _s.Downloaded.TryGetValue("vcredist", out var vc))
            tasks.Add(new("run-installer", new[] { vc, "/install", "/quiet", "/norestart" }));
        if (_s.InstallHelper && _s.NeedAspNetRuntime && !_s.HelperIsSelfContained && _s.Downloaded.TryGetValue("aspnet8", out var aspnet))
            tasks.Add(new("run-installer", new[] { aspnet, "/install", "/quiet", "/norestart" }));
        if (_s.LanAccess) tasks.Add(new("firewall", new[] { _s.WebPort.ToString(), "LocalSubnet" }));
        if (_s.InstallHelper) tasks.Add(new("firewall-helper", Array.Empty<string>()));

        if (tasks.Count == 0)
        {
            _log("Nothing to do");
            return;
        }
        _log("Requesting administrator approval for: " + string.Join(", ", tasks.Select(t => t.Name)));
        var code = await Elevation.RunAdminTasksAsync(tasks, paths.LogsDir, ct).ConfigureAwait(false);
        if (code == Elevation.ExitCancelled)
        {
            throw new OperationCanceledException("Administrator approval was declined. Runtime components and firewall rules were not installed. Run setup again and approve the prompt.");
        }
        if (code != 0)
        {
            _log($"Administrator tasks reported code {code}; see logs\\admin-task.log. Continuing.");
        }
        else
        {
            _log("Administrator tasks completed");
        }
    }

    private Task WriteConfigsAsync(CancellationToken ct)
    {
        var paths = Paths;
        var store = new ConfigStore(paths.PanelConfigFile);
        var config = _s.ToPanelConfig(store.Exists ? TryLoad(store) : null);
        store.Save(config);

        var prov = new AppProvisioner(paths, config, _log);
        prov.EnsureDirectories();
        prov.WriteRuntimeConfigs();
        prov.WriteEnv(new AppProvisioner.Secrets(
            _s.SuperAdminName, _s.SuperAdminEmail, _s.SuperAdminPassword,
            _s.KioskToken, _s.BiometricToken,
            _s.NodeRole == "local" ? _s.LiveSyncToken : null,
            _s.InstallHelper ? _s.DevicePassword : null), _s.AdvancedEnv());
        if (_s.InstallHelper) prov.WriteHikAppSettings(_s.DevicePassword, _s.BiometricToken);
        if (_s.InstallHelper && _s.InstallTunnel && _s.TunnelToken.Length > 0)
        {
            new SecretStore(paths.SecretsFile).Set(SecretStore.TunnelToken, _s.TunnelToken);
        }
        prov.EnsureSqliteFile();
        return Task.CompletedTask;
    }

    private static PanelConfig? TryLoad(ConfigStore store)
    {
        try { return store.Load(); } catch { return null; }
    }

    private async Task BootstrapAsync(CancellationToken ct)
    {
        var paths = Paths;
        var config = new ConfigStore(paths.PanelConfigFile).Load();
        var prov = new AppProvisioner(paths, config, _log);
        await prov.BootstrapLaravelAsync(
            seedProduction: _s.SuperAdminPassword.Length > 0,
            ct,
            syncBootstrap: _s.NodeRole == "local" && !_s.IsRepair).ConfigureAwait(false);
        prov.ScrubSuperAdminPassword();
    }

    private Task RegisterAsync(CancellationToken ct)
    {
        var paths = Paths;
        InstallLocator.WritePointer(paths.InstallRoot);
        StartupRegistration.Apply(true, paths);
        CreateShortcut(paths);
        return Task.CompletedTask;
    }

    private void CreateShortcut(AppPaths paths)
    {
        try
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var lnk = Path.Combine(programs, "JPrime Control Panel.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = paths.PanelExe;
            shortcut.WorkingDirectory = paths.PanelDir;
            shortcut.Description = "JPrime Control Panel";
            shortcut.Save();
            _log($"Start Menu shortcut created");
        }
        catch (Exception ex)
        {
            _log("Shortcut not created: " + ex.Message);
        }
    }
}
