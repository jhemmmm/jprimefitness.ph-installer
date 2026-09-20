namespace JPrime.Panel.Config;

/// <summary>Persisted panel configuration (<c>config\panel.json</c>). Secrets live in <see cref="SecretStore"/>.</summary>
public sealed class PanelConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string InstallRoot { get; set; } = "";
    public DateTime? InstalledAtUtc { get; set; }

    public WebSettings Web { get; set; } = new();
    public AppSettings App { get; set; } = new();
    public ServiceSettings Services { get; set; } = new();
    public BiometricSettings Biometric { get; set; } = new();
    public TunnelSettings Tunnel { get; set; } = new();
    public BackupSettings Backup { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public VersionInfo Versions { get; set; } = new();

    public sealed class WebSettings
    {
        /// <summary>nginx listen port (0.0.0.0).</summary>
        public int Port { get; set; } = 8001;
        public int PoolSize { get; set; } = 4;
        public int PoolBasePort { get; set; } = 9001;
        /// <summary>PHP_FCGI_MAX_REQUESTS; php-cgi exits cleanly after this many requests and is respawned.</summary>
        public int MaxRequests { get; set; } = 500;
        /// <summary>Open the web port on the Windows firewall for Wi-Fi/LAN devices.</summary>
        public bool LanAccess { get; set; } = true;
        /// <summary>LocalSubnet or Any.</summary>
        public string LanScope { get; set; } = "LocalSubnet";
    }

    public sealed class AppSettings
    {
        public string Name { get; set; } = "JPrime Fitness PH";
        /// <summary>standalone | local</summary>
        public string NodeRole { get; set; } = "standalone";
        public string NodeId { get; set; } = "standalone";
        public string LiveApiUrl { get; set; } = "";
        public string SuperAdminEmail { get; set; } = "";
    }

    public sealed class ServiceSettings
    {
        public ServiceToggle Nginx { get; set; } = new() { Enabled = true, Autostart = true };
        public ServiceToggle Php { get; set; } = new() { Enabled = true, Autostart = true };
        public ServiceToggle Scheduler { get; set; } = new() { Enabled = true, Autostart = true };
        public ServiceToggle HikVision { get; set; } = new() { Enabled = false, Autostart = true };
        public ServiceToggle Cloudflared { get; set; } = new() { Enabled = false, Autostart = true };

        public ServiceToggle Get(string id) => id switch
        {
            "nginx" => Nginx,
            "php" => Php,
            "scheduler" => Scheduler,
            "hikvision" => HikVision,
            "cloudflared" => Cloudflared,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown service id"),
        };
    }

    public sealed class ServiceToggle
    {
        /// <summary>Component installed / shown in the panel.</summary>
        public bool Enabled { get; set; }
        /// <summary>Started automatically when the panel launches.</summary>
        public bool Autostart { get; set; }
    }

    public sealed class BiometricSettings
    {
        public bool Enabled { get; set; }
        /// <summary>HikVision:Debug (verbose helper logs + device details in /health).</summary>
        public bool Debug { get; set; }
        public string DeviceUsername { get; set; } = "admin";
        /// <summary>Hard-coded bind port of HikVision.exe.</summary>
        public const int DefaultHelperPort = 5077;
        public int HelperPort { get; set; } = DefaultHelperPort;
    }

    public sealed class TunnelSettings
    {
        public bool Enabled { get; set; }
        public const string DefaultPublicHostname = "hikvision.jprimefitness.ph";
        public const int DefaultMetricsPort = 20241;
        public string PublicHostname { get; set; } = DefaultPublicHostname;
        public int MetricsPort { get; set; } = DefaultMetricsPort;
    }

    public sealed class BackupSettings
    {
        public bool Enabled { get; set; } = true;
        public int RetentionCount { get; set; } = 14;
        public int IntervalHours { get; set; } = 24;
        public DateTime? LastRunUtc { get; set; }
    }

    public sealed class UiSettings
    {
        public bool RunAtLogin { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool ConfirmStopOnExit { get; set; } = true;
    }

    public sealed class VersionInfo
    {
        public string Php { get; set; } = "";
        public string Nginx { get; set; } = "";
        public string Cloudflared { get; set; } = "";
        public string HikVision { get; set; } = "";
        public string App { get; set; } = "";
        public string Panel { get; set; } = "";
    }
}
