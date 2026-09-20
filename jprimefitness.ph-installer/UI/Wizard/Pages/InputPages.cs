using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Setup;

namespace JPrime.Panel.UI.Wizard.Pages;

public sealed class WelcomePage : WizardPage
{
    public override string Title => "Welcome";
    public override string Subtitle => "Set up JPrime Fitness PH on this PC";
    public override bool ShowBack => false;

    public WelcomePage()
    {
        var stack = Stack();
        stack.Controls.Add(Note("""
            This wizard installs everything the gym system needs on this computer:

              •  The JPrime web app (members, POS, attendance, payroll) with its own database
              •  The web server that phones, laptops and kiosk tablets on the gym Wi-Fi connect to
              •  Optionally, the Hikvision fingerprint helper and a Cloudflare Tunnel for the live server

            Afterwards a small control panel lives in the system tray to start, stop and monitor everything, like XAMPP. It starts automatically when you sign in to Windows.

            You will need: an internet connection (about 150 MB is downloaded), a few minutes, and one administrator approval for the firewall and runtime components.

            Click Next to begin.
            """));
        Controls.Add(stack);
    }
}

public sealed class FolderPage : WizardPage
{
    private readonly TextBox _root = Input(width: 480);
    private readonly CheckBox _helper = new() { Text = "Hikvision fingerprint helper (biometric attendance and enrollment)", AutoSize = true };
    private readonly CheckBox _tunnel = new() { Text = $"Cloudflare Tunnel so the live server can reach the helper ({PanelConfig.TunnelSettings.DefaultPublicHostname})", AutoSize = true, Margin = new Padding(24, 0, 0, 0) };
    private readonly Label _repair = Note("");

    public override string Title => "Install folder";
    public override string Subtitle => "Where to install and which components to include";

    public FolderPage()
    {
        _repair.ForeColor = Color.DarkOrange;
        _repair.Visible = false;

        var stack = Stack();
        stack.Controls.Add(_repair);
        stack.Controls.Add(Heading("Install folder"));
        stack.Controls.Add(Note("Use a short folder without special characters. The database, logs and backups also live here."));
        stack.Controls.Add(Row(_root, Btn("Browse...", Browse)));

        stack.Controls.Add(Heading("Components"));
        var always = new CheckBox { Text = "JPrime web app + PHP + nginx web server (required)", Checked = true, Enabled = false, AutoSize = true };
        stack.Controls.Add(always);
        stack.Controls.Add(_helper);
        stack.Controls.Add(_tunnel);
        _helper.CheckedChanged += (_, _) => { _tunnel.Enabled = _helper.Checked; if (!_helper.Checked) _tunnel.Checked = false; };
        stack.Controls.Add(Note("The helper needs the Hikvision terminal on the same network as this PC. The tunnel is only useful when a separate live (cloud) server runs the public website and must enroll fingerprints remotely."));
        Controls.Add(stack);
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = _root.Text, ShowNewFolderButton = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) _root.Text = dlg.SelectedPath;
    }

    public override void OnEnter(WizardState s)
    {
        _root.Text = s.InstallRoot;
        _helper.Checked = s.InstallHelper;
        _tunnel.Checked = s.InstallTunnel;
        _tunnel.Enabled = s.InstallHelper;
        _repair.Visible = s.IsRepair;
        _repair.Text = s.IsRepair ? "An existing installation was found here. Setup will repair it and keep your database, .env and backups." : "";
    }

    public override ValidationResult Validate(WizardState s)
    {
        var root = _root.Text.Trim().TrimEnd('\\');
        if (root.Length < 3 || !Path.IsPathRooted(root)) return ValidationResult.Error("Enter a full folder path such as C:\\JPrime.");
        if (root.Any(c => c > 127)) return ValidationResult.Error("The folder path must not contain accented or non-English characters (nginx cannot read them).");
        if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)) return ValidationResult.Error("Choose a folder, not a drive root.");
        s.InstallRoot = root;
        s.InstallHelper = _helper.Checked;
        s.InstallTunnel = _helper.Checked && _tunnel.Checked;
        return ValidationResult.Valid;
    }
}

public sealed class AppSettingsPage : WizardPage
{
    private readonly TextBox _gym = Input();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 8001, Width = 100 };
    private readonly CheckBox _lan = new() { Text = "Allow phones and kiosk tablets on the gym Wi-Fi to open the app (Windows Firewall rule)", AutoSize = true, Checked = true };
    private readonly TextBox _name = Input();
    private readonly TextBox _email = Input();
    private readonly TextBox _pwd = Input(password: true);
    private readonly TextBox _pwd2 = Input(password: true);
    private readonly Label _pwdNote = new() { AutoSize = true, ForeColor = Color.DimGray };

    public override string Title => "App settings";
    public override string Subtitle => "Gym name, web port and the first administrator account";

    public AppSettingsPage()
    {
        var stack = Stack();
        stack.Controls.Add(Heading("Gym"));
        var g1 = FormGrid();
        AddRow(g1, "Gym name", _gym, "Shown in the app title and emails (APP_NAME).");
        AddRow(g1, "Web port", _port, "Browsers connect to http://localhost:PORT. 8001 is the default; use 8080 if 8001 is taken.");
        AddSpan(g1, _lan);
        stack.Controls.Add(g1);

        stack.Controls.Add(Heading("Super administrator"));
        stack.Controls.Add(Note("This account manages everything. You can add staff accounts inside the app later."));
        var g2 = FormGrid();
        AddRow(g2, "Full name", _name);
        AddRow(g2, "Email (login)", _email);
        AddRow(g2, "Password", _pwd);
        AddRow(g2, "Confirm password", _pwd2);
        AddSpan(g2, _pwdNote);
        stack.Controls.Add(g2);
        Controls.Add(stack);
    }

    public override void OnEnter(WizardState s)
    {
        _gym.Text = s.GymName;
        _port.Value = s.WebPort;
        _lan.Checked = s.LanAccess;
        _name.Text = s.SuperAdminName;
        _email.Text = s.SuperAdminEmail;
        _pwd.Text = s.SuperAdminPassword;
        _pwd2.Text = s.SuperAdminPassword;
        _pwdNote.Text = s.IsRepair
            ? "Leave the password empty to keep the existing administrator account unchanged."
            : "At least 8 characters with upper and lower case letters and a symbol.";
    }

    public override ValidationResult Validate(WizardState s)
    {
        if (_gym.Text.Trim().Length == 0) return ValidationResult.Error("Enter the gym name.");
        if (_name.Text.Trim().Length == 0) return ValidationResult.Error("Enter the administrator's name.");
        var email = _email.Text.Trim();
        if (!email.Contains('@') || email.Contains(' ')) return ValidationResult.Error("Enter a valid email address for the administrator.");
        var pwd = _pwd.Text;
        if (pwd.Length > 0 || !s.IsRepair)
        {
            var err = PasswordPolicy.Validate(pwd);
            if (err is not null) return ValidationResult.Error(err);
            if (pwd != _pwd2.Text) return ValidationResult.Error("The two passwords do not match.");
        }
        s.GymName = _gym.Text.Trim();
        s.WebPort = (int)_port.Value;
        s.LanAccess = _lan.Checked;
        s.SuperAdminName = _name.Text.Trim();
        s.SuperAdminEmail = email;
        s.SuperAdminPassword = pwd;
        return ValidationResult.Valid;
    }
}

public sealed class DeploymentPage : WizardPage
{
    private readonly WizardForm _host;
    private readonly RadioButton _standalone = new() { Text = "Standalone: this PC is the only JPrime server", AutoSize = true, Checked = true };
    private readonly RadioButton _local = new() { Text = "Local node: this PC syncs with a live server on the internet", AutoSize = true };
    private readonly TextBox _liveUrl = Input();
    private readonly TextBox _token = Input(password: true);
    private readonly TextBox _nodeId = Input(width: 200);
    private readonly Button _test = new() { Text = "Test connection", AutoSize = true };
    private readonly Label _testResult = new() { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TableLayoutPanel _localGrid;

    public override string Title => "Deployment mode";
    public override string Subtitle => "Single server, or a local node paired with the live website";

    public DeploymentPage(WizardForm host)
    {
        _host = host;
        var stack = Stack();
        stack.Controls.Add(_standalone);
        stack.Controls.Add(Note("Everything runs here. Members register at the counter or on this PC's website over the gym Wi-Fi."));
        stack.Controls.Add(_local);
        stack.Controls.Add(Note("The public website (online registration, PayMongo) runs on a cloud server. This PC keeps working when the internet drops and syncs both ways every 30 seconds."));
        _localGrid = FormGrid();
        AddRow(_localGrid, "Live server URL", _liveUrl, "https://your-public-domain");
        AddRow(_localGrid, "Sync token", _token, "Generated on the live server with: php artisan sync:issue-token");
        AddRow(_localGrid, "Node ID", _nodeId, "Short name for this PC in the audit log.");
        AddSpan(_localGrid, Row(_test, _testResult));
        AddSpan(_localGrid, Note("Setup first copies roles, members, rate plans and staff from the live server, then applies the administrator from the previous page. Use the same email as the live administrator; passwords never sync, so the password you entered is for this PC only."));
        _localGrid.Margin = new Padding(24, 4, 0, 0);
        stack.Controls.Add(_localGrid);
        Controls.Add(stack);

        _standalone.CheckedChanged += (_, _) => _localGrid.Enabled = _local.Checked;
        _local.CheckedChanged += (_, _) => _localGrid.Enabled = _local.Checked;
        _test.Click += async (_, _) => await TestAsync();
    }

    public override void OnEnter(WizardState s)
    {
        _local.Checked = s.NodeRole == "local";
        _standalone.Checked = !_local.Checked;
        _liveUrl.Text = s.LiveApiUrl;
        _token.Text = s.LiveSyncToken;
        _nodeId.Text = s.NodeId;
        _localGrid.Enabled = _local.Checked;
        _testResult.Text = "";
    }

    private async Task TestAsync()
    {
        _testResult.ForeColor = Color.DimGray;
        _testResult.Text = "Testing...";
        try
        {
            var url = _liveUrl.Text.Trim().TrimEnd('/') + "/api/sync/heartbeat";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("{\"node_id\":\"setup-test\"}", Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.Text.Trim());
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await _host.Http.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (resp.IsSuccessStatusCode && body.Contains("\"ok\":true"))
            {
                _testResult.ForeColor = Color.Green;
                _testResult.Text = "Connected. The live server accepted the token.";
            }
            else
            {
                _testResult.ForeColor = Color.Crimson;
                _testResult.Text = (int)resp.StatusCode switch
                {
                    401 => "The live server rejected the token (401).",
                    404 => "Not a live node, or wrong URL (404). Check APP_NODE_ROLE=live on the server.",
                    403 => "This PC's IP is not in SYNC_ALLOWED_IPS on the live server (403).",
                    _ => $"Unexpected response {(int)resp.StatusCode}: {body[..Math.Min(body.Length, 120)]}",
                };
            }
        }
        catch (Exception ex)
        {
            _testResult.ForeColor = Color.Crimson;
            _testResult.Text = "Could not reach the live server: " + ex.Message;
        }
    }

    public override ValidationResult Validate(WizardState s)
    {
        if (_local.Checked)
        {
            var url = _liveUrl.Text.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return ValidationResult.Error("Enter the live server URL, e.g. https://jprimefitness.ph");
            if (TokenPolicy.Validate(_token.Text.Trim(), "The sync token") is { } err) return ValidationResult.Error(err + " Paste the token generated on the live server.");
            if (_nodeId.Text.Trim().Length == 0) return ValidationResult.Error("Enter a node ID.");
            s.NodeRole = "local";
            s.LiveApiUrl = url.TrimEnd('/');
            s.LiveSyncToken = _token.Text.Trim();
            s.NodeId = _nodeId.Text.Trim();
        }
        else
        {
            s.NodeRole = "standalone";
        }
        return ValidationResult.Valid;
    }
}

public sealed class BiometricPage : WizardPage
{
    private readonly TextBox _user = Input(width: 200);
    private readonly TextBox _pwd = Input(password: true);
    private readonly TextBox _token = Input();
    private readonly CheckBox _debug = new() { Text = "Verbose helper logging (troubleshooting)", AutoSize = true };

    public override string Title => "Biometric helper";
    public override string Subtitle => "Connect the Hikvision fingerprint terminal";
    public override bool AppliesTo(WizardState s) => s.InstallHelper;

    public BiometricPage()
    {
        var stack = Stack();
        stack.Controls.Add(Heading("Terminal login"));
        stack.Controls.Add(Note("The same admin credentials you use in the Hikvision device menu or iVMS. The helper finds the terminal on the network automatically."));
        var g = FormGrid();
        AddRow(g, "Device username", _user);
        AddRow(g, "Device password", _pwd);
        stack.Controls.Add(g);

        stack.Controls.Add(Heading("Shared token"));
        stack.Controls.Add(Note("The helper signs every attendance event with this token and the app checks it. It is generated for you. If a live server also uses this helper, put the same value in its BIOMETRIC_TOKEN."));
        var g2 = FormGrid();
        AddRow(g2, "BIOMETRIC_TOKEN", Row(_token, CopyButton(() => _token.Text), Btn("Generate new", () => _token.Text = SecretGenerator.Hex())));
        AddSpan(g2, _debug);
        stack.Controls.Add(g2);

        stack.Controls.Add(Heading("Before you continue"));
        stack.Controls.Add(Note("""
            •  Plug the terminal into the same router/switch as this PC.
            •  Close the official Hikvision SADP tool if it is open (it blocks discovery).
            •  Give this PC a fixed IP address (DHCP reservation on the router) so the terminal can always reach it.
            """));
        Controls.Add(stack);
    }

    public override void OnEnter(WizardState s)
    {
        _user.Text = s.DeviceUsername;
        _pwd.Text = s.DevicePassword;
        _token.Text = s.BiometricToken;
        _debug.Checked = s.HelperDebug;
    }

    public override ValidationResult Validate(WizardState s)
    {
        if (_user.Text.Trim().Length == 0) return ValidationResult.Error("Enter the terminal username (usually admin).");
        if (_pwd.Text.Length == 0) return ValidationResult.Error("Enter the terminal password.");
        if (TokenPolicy.Validate(_token.Text.Trim(), "The token") is { } err) return ValidationResult.Error(err);
        s.DeviceUsername = _user.Text.Trim();
        s.DevicePassword = _pwd.Text;
        s.BiometricToken = _token.Text.Trim();
        s.HelperDebug = _debug.Checked;
        return ValidationResult.Valid;
    }
}

public sealed class TunnelPage : WizardPage
{
    private readonly TextBox _token = Input(width: 400, password: true);
    private readonly TextBox _host = Input(width: 320);
    private readonly Label _route = Note("");
    private readonly Label _check = Note("");
    private readonly TextBox _env = new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Horizontal,
        Font = new Font("Consolas", 9f),
        BackColor = Color.White,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
    };
    private string _biometricToken = "";

    public override string Title => "Cloudflare Tunnel";
    public override string Subtitle => "Publish the biometric helper for the live server";
    public override bool AppliesTo(WizardState s) => s.InstallHelper && s.InstallTunnel;

    public TunnelPage()
    {
        var stack = Stack();
        stack.Controls.Add(Heading("Step 1: Create the tunnel and copy its token"));
        stack.Controls.Add(Note("""
            Open https://one.dash.cloudflare.com → Networks → Tunnels → Create a tunnel → choose Cloudflared → name it (e.g. jprime-gym-pc) → Save tunnel.
            On the "Install and run a connector" screen pick Windows. The command shown ends with a very long value starting with eyJ. That is the token. Paste it below (pasting the whole command also works).
            """));
        var g = FormGrid();
        AddRow(g, "Tunnel token", _token, "Stored encrypted on this PC and never written to .env.");
        stack.Controls.Add(g);

        stack.Controls.Add(Heading("Step 2: Route the public hostname to the helper"));
        stack.Controls.Add(_route);
        var g2 = FormGrid();
        AddRow(g2, "Public hostname", _host, "Must match the hostname you add in the tunnel.");
        stack.Controls.Add(g2);

        stack.Controls.Add(Heading("Step 3: Tell the LIVE server how to reach the helper"));
        stack.Controls.Add(Note("Add these lines to the cloud server's .env, then run  php artisan optimize:clear  and restart PHP."));
        stack.Controls.Add(Row(_env, CopyButton(() => _env.Text.ReplaceLineEndings("\n"), "Copy")));  // LF: the target is a Linux .env
        stack.Controls.Add(_check);
        Controls.Add(stack);

        _host.TextChanged += (_, _) => RenderHostText();
    }

    /// <summary>Steps 2-3 quote the hostname, so they follow what is typed instead of a fixed example.</summary>
    private void RenderHostText()
    {
        var host = _host.Text.Trim();
        if (host.Length == 0) host = PanelConfig.TunnelSettings.DefaultPublicHostname;
        var dot = host.IndexOf('.');
        var (sub, domain) = dot > 0 ? (host[..dot], host[(dot + 1)..]) : (host, "<your domain>");
        _route.Text = $"""
            Still in the tunnel, open the Public Hostname tab → Add a public hostname:
                Subdomain:  {sub}
                Domain:  {domain}
                Type:  HTTP
                URL:  localhost:{PanelConfig.BiometricSettings.DefaultHelperPort}
            Save. Cloudflare creates the DNS record for you.
            """;
        _env.Text = WizardState.LiveServerEnvSnippet(host, _biometricToken).ReplaceLineEndings();  // the edit control needs CRLF to break lines
        _env.Height = _env.Font.Height * _env.Lines.Length + SystemInformation.HorizontalScrollBarHeight + 8;  // no AutoScaleMode: size from the DPI-scaled font
        _check.Text = $"Quick test from any PC: https://{host}/health should return the helper's JSON. The helper itself has no login, so anyone who knows the address can reach it; to restrict it later add a Cloudflare WAF rule (Security → WAF → Custom rules) that allows only the live server's IP.";
    }

    public override void OnEnter(WizardState s)
    {
        _token.Text = s.TunnelToken;
        _token.PlaceholderText = s.HasStoredTunnelToken ? "(leave empty to keep the stored token)" : "";
        _biometricToken = s.BiometricToken;
        // Setting _host.Text re-renders through TextChanged; render explicitly only when it does not fire.
        if (_host.Text == s.PublicHostname) RenderHostText();
        else _host.Text = s.PublicHostname;
    }

    public override ValidationResult Validate(WizardState s)
    {
        var keepStored = s.HasStoredTunnelToken && _token.Text.Trim().Length == 0;
        var token = "";
        if (!keepStored && !TunnelToken.TryExtract(_token.Text, out token)) return ValidationResult.Error("Paste the tunnel token (a long value starting with eyJ...). You can also add it later from the panel.");
        var host = _host.Text.Trim();
        if (host.Length == 0 || host.Contains('/') || host.Contains(' ')) return ValidationResult.Error($"Enter the public hostname, e.g. {PanelConfig.TunnelSettings.DefaultPublicHostname}");
        _token.Text = token;
        s.TunnelToken = token;
        s.PublicHostname = host;
        return ValidationResult.Valid;
    }
}

public sealed class AdvancedPage : WizardPage
{
    private readonly TextBox _kiosk = Input();
    private readonly CheckBox _mail = new() { Text = "Send emails through SMTP (otherwise emails are only written to the log)", AutoSize = true };
    private readonly TextBox _mailHost = Input(width: 260);
    private readonly NumericUpDown _mailPort = new() { Minimum = 1, Maximum = 65535, Value = 587, Width = 90 };
    private readonly ComboBox _mailEnc = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox _mailUser = Input(width: 260);
    private readonly TextBox _mailPwd = Input(width: 260, password: true);
    private readonly TextBox _mailFrom = Input(width: 260);
    private readonly TextBox _pmPub = Input();
    private readonly TextBox _pmSec = Input(password: true);
    private readonly TextBox _pmHook = Input(password: true);
    private readonly TextBox _rcSite = Input();
    private readonly TextBox _rcSec = Input(password: true);
    private readonly TableLayoutPanel _mailGrid;

    public override string Title => "Advanced (optional)";
    public override string Subtitle => "Kiosk token, email, online payments and reCAPTCHA. Defaults are fine for a gym counter.";

    public AdvancedPage()
    {
        var stack = Stack();
        stack.Controls.Add(Heading("Kiosk"));
        stack.Controls.Add(Note("Kiosk tablets authenticate with this token (X-Kiosk-Token). Generated for you; copy it into the kiosk app."));
        var g0 = FormGrid();
        AddRow(g0, "KIOSK_TOKEN", Row(_kiosk, CopyButton(() => _kiosk.Text)));
        stack.Controls.Add(g0);

        stack.Controls.Add(Heading("Email"));
        stack.Controls.Add(_mail);
        _mailGrid = FormGrid();
        AddRow(_mailGrid, "SMTP host", _mailHost);
        AddRow(_mailGrid, "Port", _mailPort);
        _mailEnc.Items.AddRange(new object[] { "tls", "ssl", "none" });
        _mailEnc.SelectedIndex = 0;
        AddRow(_mailGrid, "Encryption", _mailEnc);
        AddRow(_mailGrid, "Username", _mailUser);
        AddRow(_mailGrid, "Password", _mailPwd);
        AddRow(_mailGrid, "From address", _mailFrom);
        _mailGrid.Margin = new Padding(24, 0, 0, 0);
        stack.Controls.Add(_mailGrid);
        _mail.CheckedChanged += (_, _) => _mailGrid.Enabled = _mail.Checked;

        stack.Controls.Add(Heading("PayMongo (online membership payments)"));
        stack.Controls.Add(Note("Leave empty on a gym PC; online payments are handled by the live server."));
        var g2 = FormGrid();
        AddRow(g2, "Public key", _pmPub);
        AddRow(g2, "Secret key", _pmSec);
        AddRow(g2, "Webhook secret", _pmHook);
        stack.Controls.Add(g2);

        stack.Controls.Add(Heading("Google reCAPTCHA (public registration form)"));
        var g3 = FormGrid();
        AddRow(g3, "Site key", _rcSite);
        AddRow(g3, "Secret key", _rcSec);
        stack.Controls.Add(g3);
        Controls.Add(stack);
    }

    public override void OnEnter(WizardState s)
    {
        _kiosk.Text = s.KioskToken;
        _mail.Checked = s.MailEnabled;
        _mailGrid.Enabled = s.MailEnabled;
        _mailHost.Text = s.MailHost;
        _mailPort.Value = Math.Clamp(s.MailPort, 1, 65535);
        _mailEnc.SelectedItem = _mailEnc.Items.Contains(s.MailEncryption) ? s.MailEncryption : "tls";
        _mailUser.Text = s.MailUsername;
        _mailPwd.Text = s.MailPassword;
        _mailFrom.Text = s.MailFrom;
        _pmPub.Text = s.PaymongoPublicKey;
        _pmSec.Text = s.PaymongoSecretKey;
        _pmHook.Text = s.PaymongoWebhookSecret;
        _rcSite.Text = s.RecaptchaSiteKey;
        _rcSec.Text = s.RecaptchaSecretKey;
    }

    public override ValidationResult Validate(WizardState s)
    {
        if (TokenPolicy.Validate(_kiosk.Text.Trim(), "The kiosk token") is { } err) return ValidationResult.Error(err);
        if (_mail.Checked && _mailHost.Text.Trim().Length == 0) return ValidationResult.Error("Enter the SMTP host or untick SMTP.");
        s.KioskToken = _kiosk.Text.Trim();
        s.MailEnabled = _mail.Checked;
        s.MailHost = _mailHost.Text.Trim();
        s.MailPort = (int)_mailPort.Value;
        s.MailEncryption = (string)_mailEnc.SelectedItem!;
        s.MailUsername = _mailUser.Text.Trim();
        s.MailPassword = _mailPwd.Text;
        s.MailFrom = _mailFrom.Text.Trim();
        s.PaymongoPublicKey = _pmPub.Text.Trim();
        s.PaymongoSecretKey = _pmSec.Text.Trim();
        s.PaymongoWebhookSecret = _pmHook.Text.Trim();
        s.RecaptchaSiteKey = _rcSite.Text.Trim();
        s.RecaptchaSecretKey = _rcSec.Text.Trim();
        return ValidationResult.Valid;
    }
}

public sealed class ReviewPage : WizardPage
{
    private readonly TextBox _summary = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5f),
        BackColor = Color.White,
    };

    public override string Title => "Review";
    public override string Subtitle => "Check the summary, then click Install";
    public override string NextText => "Install";

    public ReviewPage()
    {
        Controls.Add(_summary);
    }

    public override void OnEnter(WizardState s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Install folder      {s.InstallRoot}{(s.IsRepair ? "   (repair existing install)" : "")}");
        sb.AppendLine($"Gym name            {s.GymName}");
        sb.AppendLine($"Web address         http://localhost:{s.WebPort}   (LAN access: {(s.LanAccess ? "yes" : "no")})");
        sb.AppendLine($"Administrator       {s.SuperAdminName} <{s.SuperAdminEmail}>{(s.SuperAdminPassword.Length == 0 ? "   (unchanged)" : "")}");
        sb.AppendLine($"Mode                {(s.NodeRole == "local" ? $"local node → {s.LiveApiUrl} (node id {s.NodeId})" : "standalone")}");
        sb.AppendLine($"Database            SQLite at {s.InstallRoot}\\data\\jprime.sqlite, daily backups");
        sb.AppendLine();
        sb.AppendLine("Components");
        sb.AppendLine("  [x] PHP 8.4 + nginx + JPrime app + scheduler");
        sb.AppendLine($"  [{(s.InstallHelper ? "x" : " ")}] Hikvision biometric helper" + (s.InstallHelper ? $"   (device user {s.DeviceUsername}, port {PanelConfig.BiometricSettings.DefaultHelperPort})" : ""));
        sb.AppendLine($"  [{(s.InstallHelper && s.InstallTunnel ? "x" : " ")}] Cloudflare Tunnel" + (s.InstallHelper && s.InstallTunnel ? $"   ({s.PublicHostname})" : ""));
        sb.AppendLine();
        sb.AppendLine("Tokens (also shown on the last page)");
        sb.AppendLine($"  KIOSK_TOKEN       {s.KioskToken}");
        if (s.InstallHelper) sb.AppendLine($"  BIOMETRIC_TOKEN   {s.BiometricToken}");
        sb.AppendLine();
        sb.AppendLine($"Email               {(s.MailEnabled ? $"SMTP {s.MailHost}:{s.MailPort}" : "log only")}");
        sb.AppendLine($"PayMongo            {(s.PaymongoSecretKey.Length > 0 ? "configured" : "not configured")}");
        sb.AppendLine($"reCAPTCHA           {(s.RecaptchaSecretKey.Length > 0 ? "configured" : "not configured")}");
        sb.AppendLine();
        sb.AppendLine("Next: downloads (~150 MB), extraction, one administrator prompt (firewall + runtimes), database setup.");
        _summary.Text = sb.ToString();
        _summary.SelectionStart = 0;
        _summary.SelectionLength = 0;
    }
}
