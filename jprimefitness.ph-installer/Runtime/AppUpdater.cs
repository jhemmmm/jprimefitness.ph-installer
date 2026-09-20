using System.IO.Compression;
using System.Text.Json;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Setup;

namespace JPrime.Panel.Runtime;

public sealed record ReleaseInfo(string Tag, string ZipUrl, string? Sha256Url, long Size, DateTime PublishedAt);

/// <summary>Replaces <c>app\</c> with a newer release bundle while keeping .env, storage and the database.
/// Rolls back to the previous folder when migrations fail.</summary>
public sealed class AppUpdater
{
    public const string Repo = "jhemmmm/jprimefitness.ph";

    private readonly AppServices _ctx;

    public AppUpdater(AppServices ctx)
    {
        _ctx = ctx;
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var resp = await _ctx.Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTime() : DateTime.MinValue;
        string? zip = null, sha = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.StartsWith("jprimefitness.ph", StringComparison.OrdinalIgnoreCase))
            {
                zip = url;
                size = asset.GetProperty("size").GetInt64();
            }
            else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                sha = url;
            }
        }
        return zip is null ? null : new ReleaseInfo(tag, zip, sha, size, published);
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
        if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true);
        try
        {
            MoveContents(paths.AppDir, oldDir);
        }
        catch (Exception ex)
        {
            log("Could not move the current app aside, restoring: " + ex.Message);
            MoveContents(oldDir, paths.AppDir);
            throw new InvalidOperationException(InUseMessage(ex), ex);
        }
        try
        {
            MoveContents(newDir, paths.AppDir);
            Directory.Delete(newDir);
        }
        catch (Exception ex)
        {
            log("Could not move the new release in, restoring: " + ex.Message);
            MoveContents(paths.AppDir, newDir);
            MoveContents(oldDir, paths.AppDir);
            throw new InvalidOperationException(InUseMessage(ex), ex);
        }

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
                MoveContents(paths.AppDir, newDir + ".failed");
                MoveContents(oldDir, paths.AppDir);
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

    private static string InUseMessage(Exception ex) =>
        "A file or folder in the app folder is in use by another program (an Explorer window, editor or terminal open in that folder is enough). "
        + "Close it and run the update again; the current app was left unchanged. Detail: " + ex.Message;

    /// <summary>Moves every entry of <paramref name="from"/> into <paramref name="to"/> (created if missing). Entries are
    /// moved one by one: renaming the folder itself fails while any program holds it open, moving its children does not.
    /// Each move is retried briefly because antivirus scanners hold freshly extracted files for a moment.</summary>
    private static void MoveContents(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(entry));
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest, overwrite: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 8)
                {
                    Thread.Sleep(250 * attempt);
                }
            }
        }
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
