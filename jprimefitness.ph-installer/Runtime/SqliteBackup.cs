using JPrime.Panel.App;
using JPrime.Panel.Config;

namespace JPrime.Panel.Runtime;

/// <summary>Online SQLite backup through PHP's SQLite3::backup() (safe while nginx/php-cgi are writing), then
/// integrity check and retention pruning. Files: <c>data\backups\jprime-YYYYMMDD-HHmmss.sqlite</c>.</summary>
public sealed class SqliteBackup
{
    private readonly AppPaths _paths;
    private readonly PanelConfig _config;

    public SqliteBackup(AppPaths paths, PanelConfig config)
    {
        _paths = paths;
        _config = config;
    }

    public async Task<string> RunAsync(CancellationToken ct, Action<string>? log = null)
    {
        Directory.CreateDirectory(_paths.BackupsDir);
        var target = Path.Combine(_paths.BackupsDir, $"jprime-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite");
        var php = new PhpRunner(_paths, _config.Web.MaxRequests);
        var script = BackupScript(_paths.SqliteDb, target);
        var r = await php.RunAsync(new[] { "-n", "-d", "extension_dir=" + _paths.PhpExtDir, "-d", "extension=sqlite3", "-r", script }, ct, TimeSpan.FromMinutes(10), log).ConfigureAwait(false);
        if (!r.Ok || !r.StdOut.Contains("BACKUP_OK"))
        {
            try { if (File.Exists(target)) File.Delete(target); } catch { }
            throw new InvalidOperationException("Backup failed: " + r.Combined);
        }
        log?.Invoke($"Backup written: {target} ({new FileInfo(target).Length / 1024} KB)");
        Prune(log);
        _config.Backup.LastRunUtc = DateTime.UtcNow;
        return target;
    }

    private static string BackupScript(string source, string target)
    {
        static string P(string s) => s.Replace("\\", "/").Replace("'", "\\'");
        return
            "$src = new SQLite3('" + P(source) + "', SQLITE3_OPEN_READONLY);" +
            "$src->busyTimeout(10000);" +
            "$dst = new SQLite3('" + P(target) + "');" +
            "if (!$src->backup($dst)) { fwrite(STDERR, 'backup() returned false'); exit(2); }" +
            "$dst->close();" +
            "$chk = new SQLite3('" + P(target) + "', SQLITE3_OPEN_READONLY);" +
            "$res = $chk->querySingle('PRAGMA integrity_check');" +
            "if ($res !== 'ok') { fwrite(STDERR, 'integrity_check: ' . $res); exit(3); }" +
            "echo 'BACKUP_OK';";
    }

    public void Prune(Action<string>? log = null)
    {
        var keep = Math.Max(1, _config.Backup.RetentionCount);
        var files = new DirectoryInfo(_paths.BackupsDir).GetFiles("jprime-*.sqlite").OrderByDescending(f => f.Name).ToList();
        foreach (var old in files.Skip(keep))
        {
            try
            {
                old.Delete();
                log?.Invoke($"Pruned old backup {old.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not prune {old.FullName}: {ex.Message}");
            }
        }
    }

    public bool IsDue()
    {
        if (!_config.Backup.Enabled) return false;
        var last = _config.Backup.LastRunUtc;
        if (last is null) return true;
        return DateTime.UtcNow - last.Value >= TimeSpan.FromHours(Math.Max(1, _config.Backup.IntervalHours));
    }

    public IReadOnlyList<FileInfo> List() =>
        Directory.Exists(_paths.BackupsDir)
            ? new DirectoryInfo(_paths.BackupsDir).GetFiles("jprime-*.sqlite").OrderByDescending(f => f.Name).ToList()
            : Array.Empty<FileInfo>();
}
