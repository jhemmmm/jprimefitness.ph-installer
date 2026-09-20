namespace JPrime.Panel.App;

/// <summary>Every absolute path derived from the install root. Nothing else builds paths by hand.</summary>
public sealed class AppPaths
{
    public AppPaths(string installRoot)
    {
        InstallRoot = Path.GetFullPath(installRoot).TrimEnd('\\', '/');
    }

    public string InstallRoot { get; }

    public string PanelDir => Path.Combine(InstallRoot, "panel");
    public string PanelExe => Path.Combine(PanelDir, "JPrimePanel.exe");
    public string ConfigDir => Path.Combine(InstallRoot, "config");
    public string PanelConfigFile => Path.Combine(ConfigDir, "panel.json");
    public string SecretsFile => Path.Combine(ConfigDir, "secrets.json");
    public string LogsDir => Path.Combine(InstallRoot, "logs");
    public string DownloadsDir => Path.Combine(InstallRoot, "downloads");
    public string DataDir => Path.Combine(InstallRoot, "data");
    public string SqliteDb => Path.Combine(DataDir, "jprime.sqlite");
    public string BackupsDir => Path.Combine(DataDir, "backups");

    public string PhpDir => Path.Combine(InstallRoot, "php");
    public string PhpExe => Path.Combine(PhpDir, "php.exe");
    public string PhpCgiExe => Path.Combine(PhpDir, "php-cgi.exe");
    public string PhpIni => Path.Combine(PhpDir, "php.ini");
    public string PhpIniProduction => Path.Combine(PhpDir, "php.ini-production");
    public string PhpExtDir => Path.Combine(PhpDir, "ext");
    public string CaBundle => Path.Combine(PhpDir, "cacert.pem");

    public string NginxDir => Path.Combine(InstallRoot, "nginx");
    public string NginxExe => Path.Combine(NginxDir, "nginx.exe");
    public string NginxConf => Path.Combine(NginxDir, "conf", "nginx.conf");
    public string NginxLogsDir => Path.Combine(NginxDir, "logs");
    public string NginxErrorLog => Path.Combine(NginxLogsDir, "error.log");
    public string NginxAccessLog => Path.Combine(NginxLogsDir, "access.log");
    public string NginxPidFile => Path.Combine(NginxLogsDir, "nginx.pid");

    public string CloudflaredDir => Path.Combine(InstallRoot, "cloudflared");
    public string CloudflaredExe => Path.Combine(CloudflaredDir, "cloudflared.exe");

    public string HikVisionDir => Path.Combine(InstallRoot, "hikvision");
    public string HikVisionExe => Path.Combine(HikVisionDir, "HikVision.exe");
    public string HikVisionAppSettings => Path.Combine(HikVisionDir, "appsettings.json");
    public string HikVisionLogsDir => Path.Combine(HikVisionDir, "logs");

    public string AppDir => Path.Combine(InstallRoot, "app");
    public string AppNewDir => Path.Combine(InstallRoot, "app.new");
    public string AppOldDir => Path.Combine(InstallRoot, "app.old");
    public string AppPublicDir => Path.Combine(AppDir, "public");
    public string AppEnvFile => Path.Combine(AppDir, ".env");
    public string AppEnvExample => Path.Combine(AppDir, ".env.example");
    public string AppStorageDir => Path.Combine(AppDir, "storage");
    public string AppLogsDir => Path.Combine(AppStorageDir, "logs");
    public string AppArtisan => Path.Combine(AppDir, "artisan");
    public string AppBootstrapCache => Path.Combine(AppDir, "bootstrap", "cache");

    /// <summary>Forward-slash form for nginx.conf / php.ini / .env (all three prefer it on Windows).</summary>
    public static string Fwd(string path) => path.Replace('\\', '/');

    public IEnumerable<string> AllRuntimeDirs => new[]
    {
        PanelDir, ConfigDir, LogsDir, DownloadsDir, DataDir, BackupsDir, PhpDir, NginxDir, CloudflaredDir, HikVisionDir, AppDir
    };
}
