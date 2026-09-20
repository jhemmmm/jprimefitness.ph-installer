using System.Text;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime;
using JPrime.Panel.Runtime.Templates;

namespace JPrime.Panel.Setup;

/// <summary>Everything needed to turn an extracted app folder + runtimes into a working install:
/// config files, .env, SQLite file, key, migrations, seed, caches. Idempotent; used by the wizard, Re-run setup and the updater.</summary>
public sealed class AppProvisioner
{
    private readonly AppPaths _paths;
    private readonly PanelConfig _config;
    private readonly Action<string> _log;

    public AppProvisioner(AppPaths paths, PanelConfig config, Action<string> log)
    {
        _paths = paths;
        _config = config;
        _log = log;
    }

    public sealed record Secrets(
        string SuperAdminName,
        string SuperAdminEmail,
        string? SuperAdminPassword,
        string KioskToken,
        string BiometricToken,
        string? LiveSyncToken,
        string? DevicePassword);

    public void EnsureDirectories()
    {
        foreach (var d in _paths.AllRuntimeDirs) Directory.CreateDirectory(d);
        foreach (var rel in new[]
                 {
                     "storage/app/public", "storage/app/private", "storage/framework/cache/data",
                     "storage/framework/sessions", "storage/framework/views", "storage/logs", "bootstrap/cache",
                 })
        {
            Directory.CreateDirectory(Path.Combine(_paths.AppDir, rel.Replace('/', Path.DirectorySeparatorChar)));
        }
        // Stale caches from the build machine break `artisan` on first run.
        if (Directory.Exists(_paths.AppBootstrapCache))
        {
            foreach (var f in Directory.GetFiles(_paths.AppBootstrapCache, "*.php")) File.Delete(f);
        }
        var hot = Path.Combine(_paths.AppPublicDir, "hot");
        if (File.Exists(hot)) File.Delete(hot);
    }

    public void WriteRuntimeConfigs()
    {
        _log("Writing nginx.conf");
        ConfigWriter.WriteNginxConf(_config, _paths);
        _log("Writing php.ini");
        ConfigWriter.WritePhpIni(_config, _paths);
    }

    /// <summary>Merges panel-controlled keys + secrets into .env (created from .env.example when missing).</summary>
    public void WriteEnv(Secrets secrets, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var env = File.Exists(_paths.AppEnvFile) ? EnvFile.Load(_paths.AppEnvFile)
            : File.Exists(_paths.AppEnvExample) ? EnvFile.Load(_paths.AppEnvExample)
            : EnvFile.Empty();

        env.SetAll(ConfigWriter.BaseEnv(_config, _paths));
        env.Set("KIOSK_TOKEN", secrets.KioskToken);
        env.Set("BIOMETRIC_TOKEN", secrets.BiometricToken);
        env.Set("PRODUCTION_SUPER_ADMIN_NAME", secrets.SuperAdminName);
        env.Set("PRODUCTION_SUPER_ADMIN_EMAIL", secrets.SuperAdminEmail);
        if (!string.IsNullOrEmpty(secrets.SuperAdminPassword)) env.Set("PRODUCTION_SUPER_ADMIN_PASSWORD", secrets.SuperAdminPassword);
        if (secrets.LiveSyncToken is not null) env.Set("LIVE_SYNC_TOKEN", secrets.LiveSyncToken);
        if (string.IsNullOrEmpty(env.Get("MAIL_MAILER"))) env.Set("MAIL_MAILER", "log");
        if (extra is not null) env.SetAll(extra);
        env.Save(_paths.AppEnvFile);
        _log("Wrote .env");
    }

    public void WriteHikAppSettings(string devicePassword, string forwardToken)
    {
        var settings = new HikAppSettings(
            Username: _config.Biometric.DeviceUsername,
            Password: devicePassword,
            ForwardUrl: $"http://127.0.0.1:{_config.Web.Port}/api/biometric/hikvision/callback",
            ForwardToken: forwardToken,
            Debug: _config.Biometric.Debug);
        ConfigWriter.WriteHikAppSettings(settings, _paths);
        _log("Wrote hikvision appsettings.json");
    }

    public void EnsureSqliteFile()
    {
        Directory.CreateDirectory(_paths.DataDir);
        if (!File.Exists(_paths.SqliteDb))
        {
            File.WriteAllBytes(_paths.SqliteDb, Array.Empty<byte>());
            _log($"Created {_paths.SqliteDb}");
        }
    }

    /// <summary>key:generate (only when APP_KEY empty) → migrate → [sync:bootstrap] → seed (when requested) → optimize.
    /// A local node must pull the live snapshot BEFORE seeding: the seeders upsert by natural key (role name, rate plan
    /// name, admin email), so seeding first would create rows with fresh uuids that sync pushes to live as duplicates.</summary>
    public async Task BootstrapLaravelAsync(bool seedProduction, CancellationToken ct, bool syncBootstrap = false)
    {
        var php = new PhpRunner(_paths, _config.Web.MaxRequests);
        var artisan = new ArtisanRunner(_paths, php);

        _log("Clearing caches");
        await artisan.OptimizeClear(ct, _log).ConfigureAwait(false); // may fail before key exists; ignore

        var env = EnvFile.Load(_paths.AppEnvFile);
        if (string.IsNullOrEmpty(env.Get("APP_KEY")))
        {
            _log("Generating APP_KEY");
            ArtisanRunner.Require(await artisan.KeyGenerate(ct, _log).ConfigureAwait(false), "key:generate");
        }

        _log("Running migrations");
        ArtisanRunner.Require(await artisan.Migrate(ct, _log).ConfigureAwait(false), "migrate");

        if (syncBootstrap)
        {
            _log("Pulling the initial snapshot from the live server (sync:bootstrap)");
            ArtisanRunner.Require(await artisan.SyncBootstrap(ct, _log).ConfigureAwait(false),
                "sync:bootstrap (check LIVE_API_URL, the sync token and the internet connection, then retry)");
        }

        if (seedProduction)
        {
            _log("Seeding roles, defaults and the super admin");
            ArtisanRunner.Require(await artisan.SeedProduction(ct, _log).ConfigureAwait(false), "db:seed ProductionSeeder");
        }

        _log("Building production caches");
        ArtisanRunner.Require(await artisan.Optimize(ct, _log).ConfigureAwait(false), "optimize");
    }

    /// <summary>Removes the plain-text super admin password from .env once the seeder has consumed it.</summary>
    public void ScrubSuperAdminPassword()
    {
        if (!File.Exists(_paths.AppEnvFile)) return;
        var env = EnvFile.Load(_paths.AppEnvFile);
        if (env.Has("PRODUCTION_SUPER_ADMIN_PASSWORD"))
        {
            env.Set("PRODUCTION_SUPER_ADMIN_PASSWORD", "");
            env.Save(_paths.AppEnvFile);
        }
    }
}
