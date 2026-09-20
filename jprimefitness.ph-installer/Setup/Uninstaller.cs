using System.Diagnostics;
using JPrime.Panel.App;
using JPrime.Panel.UI.Panel;

namespace JPrime.Panel.Setup;

/// <summary><c>JPrimePanel.exe --uninstall</c>: stops services, removes autostart/shortcut/firewall rules and the
/// runtime folders. The data folder (database + backups) is kept unless the user opts to delete it.</summary>
public static class Uninstaller
{
    public static int Run(AppServices ctx)
    {
        var root = ctx.Paths.InstallRoot;
        var answer = MessageBox.Show(
            $"Remove JPrime from this PC?\n\nInstall folder: {root}\n\nYes = remove programs but KEEP the database and backups (data folder)\nNo = remove EVERYTHING including the database\nCancel = do nothing",
            "Uninstall JPrime", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
        if (answer == DialogResult.Cancel) return 0;
        var keepData = answer == DialogResult.Yes;
        if (!keepData)
        {
            var confirm = MessageBox.Show("This deletes the member database permanently. Are you sure?", "Uninstall JPrime", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return 0;
        }

        try
        {
            ctx.Services.StopAllAsync().GetAwaiter().GetResult();
            ctx.Services.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Stop during uninstall: " + ex.Message);
        }

        StartupRegistration.Apply(false, ctx.Paths);
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "JPrime Control Panel.lnk")); } catch { }
        InstallLocator.DeletePointer();

        var tasks = new List<AdminTaskRequest> { new("firewall-remove", new[] { ctx.Config.Web.Port.ToString() }) };
        var code = Elevation.RunAdminTasksAsync(tasks, ctx.Paths.LogsDir, CancellationToken.None).GetAwaiter().GetResult();
        if (code == Elevation.ExitCancelled) Log.Warn("Firewall rules were left in place (UAC declined).");

        // Delete everything except data\ (and the panel exe, which is running: schedule its removal).
        foreach (var dir in new[] { ctx.Paths.PhpDir, ctx.Paths.NginxDir, ctx.Paths.CloudflaredDir, ctx.Paths.HikVisionDir, ctx.Paths.AppDir, ctx.Paths.AppOldDir, ctx.Paths.AppNewDir, ctx.Paths.DownloadsDir, ctx.Paths.ConfigDir, ctx.Paths.LogsDir })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) { Log.Warn($"Could not delete {dir}: {ex.Message}"); }
        }
        if (!keepData)
        {
            try { if (Directory.Exists(ctx.Paths.DataDir)) Directory.Delete(ctx.Paths.DataDir, true); } catch (Exception ex) { Log.Warn($"Could not delete data: {ex.Message}"); }
        }

        // The running exe cannot delete itself: hand off to cmd after we exit.
        var panelDir = ctx.Paths.PanelDir;
        var script = keepData
            ? $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{panelDir}\""
            : $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{root}\"";
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", script) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }

        MessageBox.Show(keepData
                ? $"JPrime was removed. Your database and backups remain in {ctx.Paths.DataDir}."
                : "JPrime was removed completely.",
            "Uninstall JPrime", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
