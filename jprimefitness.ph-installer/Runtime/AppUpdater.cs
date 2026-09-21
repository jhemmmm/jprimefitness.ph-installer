using JPrime.Panel.App;
using JPrime.Panel.Runtime.Updates;
using JPrime.Panel.Setup;

namespace JPrime.Panel.Runtime;

/// <summary>Replaces <c>app\</c> with a newer release bundle while keeping .env, storage and the database.
/// Rolls back to the previous folder when migrations fail.</summary>
public sealed class AppUpdater
{
    private readonly AppServices _ctx;

    public AppUpdater(AppServices ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Full update from a local bundle zip (already downloaded/verified). Services must be stopped by the caller.</summary>
    public async Task ApplyAsync(string bundleZip, string newVersion, Action<string> log, CancellationToken ct)
    {
        var paths = _ctx.Paths;
        var newDir = paths.AppNewDir;
        var oldDir = paths.AppOldDir;

        log("Extracting new release");
        if (Directory.Exists(newDir)) Directory.Delete(newDir, true);
        ZipExtractor.Extract(bundleZip, newDir, stripTopLevel: true);
        if (!File.Exists(Path.Combine(newDir, "artisan"))) throw new InvalidOperationException("The bundle does not contain a Laravel app (artisan missing).");

        log("Carrying over .env and storage");
        if (File.Exists(paths.AppEnvFile)) File.Copy(paths.AppEnvFile, Path.Combine(newDir, ".env"), overwrite: true);
        var oldStorage = paths.AppStorageDir;
        var newStorage = Path.Combine(newDir, "storage");
        if (Directory.Exists(oldStorage)) CopyDirectory(Path.Combine(oldStorage, "app"), Path.Combine(newStorage, "app"));
        var hot = Path.Combine(newDir, "public", "hot");
        if (File.Exists(hot)) File.Delete(hot);
        foreach (var f in Directory.GetFiles(Path.Combine(newDir, "bootstrap", "cache"), "*.php")) File.Delete(f);

        log("Swapping folders");
        // A stopped php-cgi/php that has not fully exited still holds app\ as its working directory.
        _ctx.Services.SweepOrphans();
        DirectorySwap.Replace(paths.AppDir, newDir, oldDir, "app");

        try
        {
            var prov = new AppProvisioner(paths, _ctx.Config, log);
            prov.EnsureDirectories();
            await prov.BootstrapLaravelAsync(seedProduction: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log("Update failed, rolling back: " + ex.Message);
            try
            {
                DirectorySwap.MoveContents(paths.AppDir, newDir + ".failed");
                DirectorySwap.MoveContents(oldDir, paths.AppDir);
            }
            catch (Exception rb)
            {
                throw new InvalidOperationException($"Update failed AND rollback failed: {rb.Message}. Previous app is in {oldDir}.", ex);
            }
            throw;
        }

        _ctx.Config.Versions.App = newVersion;
        _ctx.SaveConfig();
        log($"Updated app to {newVersion}. Previous version kept in app.old until the next update.");
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            File.Copy(file, dest, overwrite: true);
        }
    }
}
