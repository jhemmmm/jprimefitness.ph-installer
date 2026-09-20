using JPrime.Panel.App;
using JPrime.Panel.Config;

namespace JPrime.Panel.Setup;

/// <summary>Hidden developer mode: <c>JPrimePanel.exe --dev-provision &lt;root&gt; [--with-hikvision &lt;devicePassword&gt;]</c>.
/// Assumes runtimes and the app are already extracted under root; writes configs, panel.json, .env and bootstraps
/// the database with a default super admin. Console-only, exit code 0 on success.</summary>
public static class DevProvision
{
    public static int Run(string root, string? devicePassword)
    {
        var paths = new AppPaths(root);
        Directory.CreateDirectory(paths.LogsDir);
        Log.Configure(paths.LogsDir);
        var lines = new List<string>();
        void L(string s)
        {
            lines.Add(s);
            Log.Info("[dev-provision] " + s);
        }

        try
        {
            var store = new ConfigStore(paths.PanelConfigFile);
            var config = store.Exists ? store.Load() : new PanelConfig();
            config.InstallRoot = paths.InstallRoot;
            config.InstalledAtUtc ??= DateTime.UtcNow;
            config.Services.HikVision.Enabled = devicePassword is not null && File.Exists(paths.HikVisionExe);
            config.Biometric.Enabled = config.Services.HikVision.Enabled;
            config.Services.Cloudflared.Enabled = File.Exists(paths.CloudflaredExe);
            config.Tunnel.Enabled = config.Services.Cloudflared.Enabled;
            store.Save(config);
            L($"panel.json saved (hikvision={config.Services.HikVision.Enabled}, cloudflared={config.Services.Cloudflared.Enabled})");

            var prov = new AppProvisioner(paths, config, L);
            prov.EnsureDirectories();
            prov.WriteRuntimeConfigs();

            var existingEnv = File.Exists(paths.AppEnvFile) ? Runtime.Templates.EnvFile.Load(paths.AppEnvFile) : null;
            var secrets = new AppProvisioner.Secrets(
                SuperAdminName: "JPrime Super Admin",
                SuperAdminEmail: "admin@jprimefitness.ph",
                SuperAdminPassword: "Admin#12345",
                KioskToken: existingEnv?.Get("KIOSK_TOKEN") is { Length: > 0 } k ? k : SecretGenerator.Hex(),
                BiometricToken: existingEnv?.Get("BIOMETRIC_TOKEN") is { Length: > 0 } b ? b : SecretGenerator.Hex(),
                LiveSyncToken: null,
                DevicePassword: devicePassword);
            prov.WriteEnv(secrets);
            if (config.Biometric.Enabled) prov.WriteHikAppSettings(devicePassword!, secrets.BiometricToken);
            prov.EnsureSqliteFile();
            prov.BootstrapLaravelAsync(seedProduction: true, CancellationToken.None).GetAwaiter().GetResult();
            prov.ScrubSuperAdminPassword();
            InstallLocator.WritePointer(paths.InstallRoot);
            L("Done. Login: admin@jprimefitness.ph / Admin#12345");
            File.WriteAllLines(Path.Combine(paths.LogsDir, "dev-provision.log"), lines);
            return 0;
        }
        catch (Exception ex)
        {
            L("FAILED: " + ex);
            File.WriteAllLines(Path.Combine(paths.LogsDir, "dev-provision.log"), lines);
            return 1;
        }
    }
}
