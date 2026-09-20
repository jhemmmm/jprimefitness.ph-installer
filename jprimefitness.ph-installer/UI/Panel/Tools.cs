using System.Diagnostics;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime;
using JPrime.Panel.Services;
using JPrime.Panel.Setup;

namespace JPrime.Panel.UI.Panel;

/// <summary>Toolbar actions. Heavier dialogs live in their own forms; this is the glue.</summary>
public static class Tools
{
    public static void ConfigureService(IWin32Window owner, ServiceBase service)
    {
        var ctx = AppServices.Current;
        switch (service.Id)
        {
            case "nginx":
            case "php":
                using (var f = new SettingsForm(ctx)) f.ShowDialog(owner);
                break;
            case "scheduler":
                using (var f = new EnvEditorForm(ctx)) f.ShowDialog(owner);
                break;
            case "hikvision":
                ConfigureHikVision(owner, ctx);
                break;
            case "cloudflared":
                {
                    var token = PromptDialog.Show(owner, "Cloudflare Tunnel token",
                        "Paste the tunnel token from Cloudflare Zero Trust (Networks > Tunnels > your tunnel > Install connector).\nLeave empty to keep the current token.",
                        password: true);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        if (!TunnelToken.TryExtract(token, out var extracted))
                        {
                            MessageBox.Show(owner, "That does not look like a tunnel token (a long value starting with eyJ...). Nothing was changed.", "JPrime", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            break;
                        }
                        ctx.Secrets.Set(SecretStore.TunnelToken, extracted);
                        if (service.Status.IsActive) _ = service.RestartAsync();
                        else MessageBox.Show(owner, "Token saved. Start the tunnel row to connect.", "JPrime", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    break;
                }
        }
    }

    private static void ConfigureHikVision(IWin32Window owner, AppServices ctx)
    {
        var current = Runtime.Templates.HikAppSettings.TryLoad(ctx.Paths.HikVisionAppSettings);
        var pwd = PromptDialog.Show(owner, "Hikvision terminal password",
            $"Admin password of the fingerprint terminal (user '{ctx.Config.Biometric.DeviceUsername}').\nLeave empty to keep the current password.",
            password: true);
        if (string.IsNullOrEmpty(pwd)) return;
        var env = Runtime.Templates.EnvFile.Load(ctx.Paths.AppEnvFile);
        var token = env.Get("BIOMETRIC_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            token = SecretGenerator.Hex();
            env.Set("BIOMETRIC_TOKEN", token);
            env.Save(ctx.Paths.AppEnvFile);
        }
        var settings = new Runtime.Templates.HikAppSettings(
            ctx.Config.Biometric.DeviceUsername, pwd,
            $"http://127.0.0.1:{ctx.Config.Web.Port}/api/biometric/hikvision/callback",
            token, ctx.Config.Biometric.Debug);
        ConfigWriter.WriteHikAppSettings(settings, ctx.Paths);
        if (ctx.Services.HikVision.Status.IsActive) _ = ctx.Services.HikVision.RestartAsync();
        else MessageBox.Show(owner, "Saved. Start the biometric helper row to connect to the terminal.", "JPrime", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static void OpenInEditor(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show($"File not found:\n{path}", "JPrime", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"notepad failed: {ex.Message}");
        }
    }

    public static void BackupNow(IWin32Window owner)
    {
        var ctx = AppServices.Current;
        var ok = TaskDialog.Run(owner, "Database backup", async (log, ct) =>
        {
            var backup = new SqliteBackup(ctx.Paths, ctx.Config);
            log($"Backing up {ctx.Paths.SqliteDb}");
            await backup.RunAsync(ct, log);
            ctx.SaveConfig();
        });
        if (ok) PanelAppContext.OpenUrl(ctx.Paths.BackupsDir);
    }

    public static void UpdateApp(IWin32Window owner)
    {
        var ctx = AppServices.Current;
        using var dlg = new UpdateForm(ctx);
        dlg.ShowDialog(owner);
    }

    public static void ArtisanConsole(IWin32Window owner)
    {
        using var f = new ArtisanConsoleForm(AppServices.Current);
        f.ShowDialog(owner);
    }

    public static void Settings(IWin32Window owner)
    {
        using var f = new SettingsForm(AppServices.Current);
        f.ShowDialog(owner);
    }

    public static void EditEnv(IWin32Window owner)
    {
        using var f = new EnvEditorForm(AppServices.Current);
        f.ShowDialog(owner);
    }

    public static void RerunSetup(IWin32Window owner)
    {
        var ctx = AppServices.Current;
        var r = MessageBox.Show(owner,
            "This stops all services and opens the setup wizard again with your current settings pre-filled.\nYour database and .env are kept.\n\nContinue?",
            "Re-run setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return;
        _ = Task.Run(async () =>
        {
            await ctx.Services.StopAllAsync();
            UiThread.Post(() =>
            {
                var exe = Environment.ProcessPath!;
                Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "--wizard", "--install-root", ctx.Paths.InstallRoot }, UseShellExecute = false });
                Application.Exit();
            });
        });
    }
}

/// <summary>Minimal single-value input dialog.</summary>
public static class PromptDialog
{
    public static string? Show(IWin32Window owner, string title, string label, bool password = false, string initial = "")
    {
        using var form = new Form
        {
            Text = title,
            Width = 560,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var lbl = new Label { Text = label, Left = 12, Top = 12, Width = 520, Height = 50 };
        var box = new TextBox { Left = 12, Top = 70, Width = 520, UseSystemPasswordChar = password, Text = initial };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 372, Top = 110, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 457, Top = 110, Width = 75 };
        form.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
