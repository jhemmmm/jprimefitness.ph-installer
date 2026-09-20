using System.Text;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime.Templates;

namespace JPrime.Panel.Runtime;

/// <summary>Renders nginx.conf and php.ini from <see cref="PanelConfig"/> + <see cref="AppPaths"/>. The single place
/// the wizard, Settings and the updater use, so the files are always regenerated the same way.</summary>
public static class ConfigWriter
{
    public static void WriteNginxConf(PanelConfig config, AppPaths paths)
    {
        var upstream = new StringBuilder();
        for (var i = 0; i < config.Web.PoolSize; i++)
        {
            upstream.Append($"        server 127.0.0.1:{config.Web.PoolBasePort + i} max_fails=3 fail_timeout=5s;\n");
        }
        var values = new Dictionary<string, string>
        {
            ["PORT"] = config.Web.Port.ToString(),
            ["APP_PUBLIC"] = AppPaths.Fwd(paths.AppPublicDir),
            ["APP_STORAGE_PUBLIC"] = AppPaths.Fwd(Path.Combine(paths.AppStorageDir, "app", "public")),
            ["UPSTREAM_SERVERS"] = upstream.ToString().TrimEnd('\n'),
        };
        var text = TemplateEngine.RenderResource("nginx.conf.tmpl", values);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.NginxConf)!);
        File.WriteAllText(paths.NginxConf, text, new UTF8Encoding(false));
        Directory.CreateDirectory(paths.NginxLogsDir);
        Directory.CreateDirectory(Path.Combine(paths.NginxDir, "temp"));
    }

    public static void WritePhpIni(PanelConfig config, AppPaths paths)
    {
        var source = File.Exists(paths.PhpIniProduction)
            ? paths.PhpIniProduction
            : Path.Combine(paths.PhpDir, "php.ini-development");
        var patcher = File.Exists(source) ? PhpIniPatcher.FromFile(source) : new PhpIniPatcher("[PHP]\n");
        var opcacheDir = Path.Combine(paths.PhpDir, "opcache");
        Directory.CreateDirectory(opcacheDir);
        Directory.CreateDirectory(paths.LogsDir);

        patcher
            .Set("extension_dir", Q(paths.PhpExtDir))
            .Set("memory_limit", "512M")
            .Set("upload_max_filesize", "64M")
            .Set("post_max_size", "64M")
            .Set("max_execution_time", "120")
            .Set("max_input_time", "120")
            .Set("date.timezone", "Asia/Manila")
            .Set("expose_php", "Off")
            .Set("display_errors", "Off")
            .Set("log_errors", "On")
            .Set("error_log", Q(Path.Combine(paths.LogsDir, "php-errors.log")))
            .Set("cgi.force_redirect", "0")
            .Set("cgi.fix_pathinfo", "0")
            .Set("realpath_cache_size", "4096K")
            .Set("realpath_cache_ttl", "600")
            .Set("curl.cainfo", Q(paths.CaBundle))
            .Set("openssl.cafile", Q(paths.CaBundle))
            .Set("sys_temp_dir", Q(Path.Combine(paths.DataDir, "tmp")))
            .Set("opcache.enable", "1")
            .Set("opcache.enable_cli", "0")
            .Set("opcache.memory_consumption", "128")
            .Set("opcache.interned_strings_buffer", "16")
            .Set("opcache.max_accelerated_files", "20000")
            .Set("opcache.validate_timestamps", "1")
            .Set("opcache.revalidate_freq", "2")
            .Set("opcache.file_cache", Q(opcacheDir))
            .Set("opcache.file_cache_fallback", "1")
            .Set("opcache.jit", "off")
            .Set("opcache.jit_buffer_size", "0");

        foreach (var ext in new[] { "curl", "fileinfo", "gd", "mbstring", "openssl", "pdo_sqlite", "sqlite3", "zip", "sodium" })
        {
            patcher.EnableExtension(ext);
        }
        patcher.EnableZendExtension("opcache");

        Directory.CreateDirectory(Path.Combine(paths.DataDir, "tmp"));
        patcher.Save(paths.PhpIni);
    }

    public static void WriteHikAppSettings(HikAppSettings settings, AppPaths paths)
    {
        Directory.CreateDirectory(paths.HikVisionDir);
        settings.Save(paths.HikVisionAppSettings);
    }

    /// <summary>Forward-slash quoted path for ini/nginx.</summary>
    private static string Q(string path) => "\"" + AppPaths.Fwd(path) + "\"";

    /// <summary>Env keys the panel controls; everything else in .env is the user's.</summary>
    public static IReadOnlyDictionary<string, string?> BaseEnv(PanelConfig config, AppPaths paths) => new Dictionary<string, string?>
    {
        ["APP_NAME"] = config.App.Name,
        ["APP_ENV"] = "production",
        ["APP_DEBUG"] = "false",
        ["APP_URL"] = $"http://localhost:{config.Web.Port}",
        ["LOG_CHANNEL"] = "daily",
        ["LOG_LEVEL"] = "warning",
        ["DB_CONNECTION"] = "sqlite",
        ["DB_DATABASE"] = AppPaths.Fwd(paths.SqliteDb),
        ["DB_FOREIGN_KEYS"] = "true",
        ["DB_BUSY_TIMEOUT"] = "5000",
        ["DB_JOURNAL_MODE"] = "WAL",
        ["DB_SYNCHRONOUS"] = "NORMAL",
        ["CACHE_STORE"] = "file",
        ["SESSION_DRIVER"] = "file",
        ["QUEUE_CONNECTION"] = "sync",
        ["QUEUE_FAILED_DRIVER"] = "null",
        ["BROADCAST_CONNECTION"] = "log",
        ["FILESYSTEM_DISK"] = "local",
        ["TRUSTED_PROXIES"] = "127.0.0.1",
        ["APP_NODE_ROLE"] = config.App.NodeRole,
        ["NODE_ID"] = config.App.NodeId,
        ["LIVE_API_URL"] = config.App.LiveApiUrl,
        ["BIOMETRIC_HELPER_ENABLED"] = config.Biometric.Enabled ? "true" : "false",
        ["BIOMETRIC_HELPER_BASE_URL"] = config.Biometric.Enabled ? $"http://127.0.0.1:{config.Biometric.HelperPort}" : "",
    };
}
