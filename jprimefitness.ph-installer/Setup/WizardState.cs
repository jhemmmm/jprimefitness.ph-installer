using JPrime.Panel.Config;

namespace JPrime.Panel.Setup;

/// <summary>All wizard inputs, in memory only. Secrets are written to their final files during Install and never to panel.json.</summary>
public sealed class WizardState
{
    public string InstallRoot { get; set; } = @"C:\JPrime";
    public bool IsRepair { get; set; }
    /// <summary>Repair only: a tunnel token is already in the secret store, so the Tunnel page may leave the box empty.</summary>
    public bool HasStoredTunnelToken { get; set; }

    // Components
    public bool InstallHelper { get; set; }
    public bool InstallTunnel { get; set; }

    // App settings
    public string GymName { get; set; } = "JPrime Fitness PH";
    public int WebPort { get; set; } = 8001;
    public bool LanAccess { get; set; } = true;
    public string SuperAdminName { get; set; } = "JPrime Super Admin";
    public string SuperAdminEmail { get; set; } = "admin@jprimefitness.ph";
    public string SuperAdminPassword { get; set; } = "";

    // Deployment mode
    public string NodeRole { get; set; } = "standalone";
    public string NodeId { get; set; } = "local-jprime-main";
    public string LiveApiUrl { get; set; } = "";
    public string LiveSyncToken { get; set; } = "";

    // Biometric
    public string DeviceUsername { get; set; } = "admin";
    public string DevicePassword { get; set; } = "";
    public string BiometricToken { get; set; } = SecretGenerator.Hex();
    public bool HelperDebug { get; set; }

    // Tunnel
    public string TunnelToken { get; set; } = "";
    public string PublicHostname { get; set; } = PanelConfig.TunnelSettings.DefaultPublicHostname;

    // Advanced
    public string KioskToken { get; set; } = SecretGenerator.Hex();
    public bool MailEnabled { get; set; }
    public string MailHost { get; set; } = "";
    public int MailPort { get; set; } = 587;
    public string MailUsername { get; set; } = "";
    public string MailPassword { get; set; } = "";
    public string MailFrom { get; set; } = "";
    public string MailEncryption { get; set; } = "tls";
    public string PaymongoPublicKey { get; set; } = "";
    public string PaymongoSecretKey { get; set; } = "";
    public string PaymongoWebhookSecret { get; set; } = "";
    public string RecaptchaSiteKey { get; set; } = "";
    public string RecaptchaSecretKey { get; set; } = "";

    // Resolved during Download page
    public Dictionary<string, Payload> Payloads { get; } = new();
    public Dictionary<string, string> Downloaded { get; } = new();
    public bool NeedVcRedist { get; set; }
    public bool NeedAspNetRuntime { get; set; }
    public bool HelperIsSelfContained { get; set; }

    // Outcome
    public bool InstallCompleted { get; set; }
    public string? InstallError { get; set; }

    /// <summary>.env lines the LIVE server needs to reach this PC's helper through the tunnel; shown on the Tunnel and Finish
    /// pages. Values must stay space-free: phpdotenv rejects unquoted values containing spaces.</summary>
    public static string LiveServerEnvSnippet(string publicHostname, string biometricToken) => $"""
        BIOMETRIC_HELPER_ENABLED=true
        BIOMETRIC_HELPER_BASE_URL=https://{publicHostname}
        BIOMETRIC_TOKEN={biometricToken}
        """.ReplaceLineEndings("\n");

    public PanelConfig ToPanelConfig(PanelConfig? existing = null)
    {
        var c = existing ?? new PanelConfig();
        c.InstallRoot = InstallRoot;
        c.InstalledAtUtc ??= DateTime.UtcNow;
        c.App.Name = GymName;
        c.App.NodeRole = NodeRole;
        c.App.NodeId = NodeRole == "local" ? NodeId : "standalone";
        c.App.LiveApiUrl = NodeRole == "local" ? LiveApiUrl.TrimEnd('/') : "";
        c.App.SuperAdminEmail = SuperAdminEmail;
        c.Web.Port = WebPort;
        c.Web.LanAccess = LanAccess;
        c.Services.Nginx.Enabled = true;
        c.Services.Php.Enabled = true;
        c.Services.Scheduler.Enabled = true;
        c.Services.HikVision.Enabled = InstallHelper;
        c.Services.Cloudflared.Enabled = InstallHelper && InstallTunnel;
        c.Biometric.Enabled = InstallHelper;
        c.Biometric.DeviceUsername = DeviceUsername;
        c.Biometric.Debug = HelperDebug;
        c.Tunnel.Enabled = InstallHelper && InstallTunnel;
        c.Tunnel.PublicHostname = PublicHostname;
        foreach (var (id, p) in Payloads)
        {
            switch (id)
            {
                case "php": c.Versions.Php = p.Version; break;
                case "nginx": c.Versions.Nginx = p.Version; break;
                case "cloudflared": c.Versions.Cloudflared = p.Version; break;
                case "hikvision": c.Versions.HikVision = p.Version; break;
                case "app": c.Versions.App = p.Version; break;
            }
        }
        c.Versions.Panel = typeof(WizardState).Assembly.GetName().Version?.ToString(3) ?? "";
        return c;
    }

    /// <summary>Pre-fill from an existing install (Re-run setup).</summary>
    public static WizardState FromExisting(PanelConfig c, Runtime.Templates.EnvFile? env, Runtime.Templates.HikAppSettings? hik)
    {
        var s = new WizardState
        {
            InstallRoot = c.InstallRoot,
            IsRepair = true,
            InstallHelper = c.Services.HikVision.Enabled,
            InstallTunnel = c.Services.Cloudflared.Enabled,
            GymName = c.App.Name,
            WebPort = c.Web.Port,
            LanAccess = c.Web.LanAccess,
            SuperAdminEmail = string.IsNullOrEmpty(c.App.SuperAdminEmail) ? "admin@jprimefitness.ph" : c.App.SuperAdminEmail,
            NodeRole = c.App.NodeRole,
            NodeId = c.App.NodeId,
            LiveApiUrl = c.App.LiveApiUrl,
            DeviceUsername = c.Biometric.DeviceUsername,
            HelperDebug = c.Biometric.Debug,
            PublicHostname = c.Tunnel.PublicHostname,
        };
        if (env is not null)
        {
            s.SuperAdminName = env.Get("PRODUCTION_SUPER_ADMIN_NAME") is { Length: > 0 } n ? n : s.SuperAdminName;
            s.KioskToken = env.Get("KIOSK_TOKEN") is { Length: > 0 } k ? k : s.KioskToken;
            s.BiometricToken = env.Get("BIOMETRIC_TOKEN") is { Length: > 0 } b ? b : s.BiometricToken;
            s.LiveSyncToken = env.Get("LIVE_SYNC_TOKEN") ?? "";
            s.MailEnabled = env.Get("MAIL_MAILER") == "smtp";
            s.MailHost = env.Get("MAIL_HOST") ?? "";
            s.MailPort = int.TryParse(env.Get("MAIL_PORT"), out var mp) ? mp : 587;
            s.MailUsername = env.Get("MAIL_USERNAME") ?? "";
            s.MailPassword = env.Get("MAIL_PASSWORD") ?? "";
            s.MailFrom = env.Get("MAIL_FROM_ADDRESS") ?? "";
            s.MailEncryption = env.Get("MAIL_SCHEME") ?? env.Get("MAIL_ENCRYPTION") ?? "tls";
            s.PaymongoPublicKey = env.Get("PAYMONGO_PUBLIC_KEY") ?? "";
            s.PaymongoSecretKey = env.Get("PAYMONGO_SECRET_KEY") ?? "";
            s.PaymongoWebhookSecret = env.Get("PAYMONGO_WEBHOOK_SECRET") ?? "";
            s.RecaptchaSiteKey = env.Get("RECAPTCHA_SITE_KEY") ?? "";
            s.RecaptchaSecretKey = env.Get("RECAPTCHA_SECRET_KEY") ?? "";
        }
        if (hik is not null)
        {
            s.DevicePassword = hik.Password;
            if (!string.IsNullOrEmpty(hik.ForwardToken)) s.BiometricToken = hik.ForwardToken;
        }
        return s;
    }

    public IReadOnlyDictionary<string, string?> AdvancedEnv()
    {
        var d = new Dictionary<string, string?>();
        if (MailEnabled && MailHost.Length > 0)
        {
            d["MAIL_MAILER"] = "smtp";
            d["MAIL_HOST"] = MailHost;
            d["MAIL_PORT"] = MailPort.ToString();
            d["MAIL_USERNAME"] = MailUsername;
            d["MAIL_PASSWORD"] = MailPassword;
            d["MAIL_SCHEME"] = MailEncryption == "none" ? "smtp" : "smtps";
            d["MAIL_ENCRYPTION"] = MailEncryption == "none" ? "null" : MailEncryption;
            d["MAIL_FROM_ADDRESS"] = MailFrom.Length > 0 ? MailFrom : SuperAdminEmail;
        }
        else
        {
            d["MAIL_MAILER"] = "log";
        }
        d["PAYMONGO_PUBLIC_KEY"] = PaymongoPublicKey;
        d["PAYMONGO_SECRET_KEY"] = PaymongoSecretKey;
        d["PAYMONGO_WEBHOOK_SECRET"] = PaymongoWebhookSecret;
        d["RECAPTCHA_SITE_KEY"] = RecaptchaSiteKey;
        d["RECAPTCHA_SECRET_KEY"] = RecaptchaSecretKey;
        return d;
    }
}
