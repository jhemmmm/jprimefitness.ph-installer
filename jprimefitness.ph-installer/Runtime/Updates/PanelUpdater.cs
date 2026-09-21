using JPrime.Panel.App;

namespace JPrime.Panel.Runtime.Updates;

/// <summary>Swaps the installed JPrimePanel.exe for a downloaded one. A running exe cannot be overwritten but can be
/// renamed, so the current file becomes JPrimePanel.exe.old and the caller relaunches the new file.</summary>
public static class PanelUpdater
{
    public static string InstalledVersion => typeof(PanelUpdater).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Puts <paramref name="newExe"/> in place of the installed panel. Returns the installed path to launch.</summary>
    public static string Swap(AppPaths paths, string newExe, Action<string> log)
    {
        var installed = paths.PanelExe;
        var old = installed + ".old";
        Directory.CreateDirectory(paths.PanelDir);
        if (File.Exists(old)) File.Delete(old);
        if (File.Exists(installed)) File.Move(installed, old);
        try
        {
            File.Copy(newExe, installed, overwrite: true);
        }
        catch
        {
            if (File.Exists(old) && !File.Exists(installed)) File.Move(old, installed);
            throw;
        }
        log($"Installed new panel at {installed} (previous kept as JPrimePanel.exe.old)");
        return installed;
    }
}
