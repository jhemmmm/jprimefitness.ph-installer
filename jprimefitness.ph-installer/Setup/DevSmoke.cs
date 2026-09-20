using System.Diagnostics;
using System.Net.Http;
using System.Text;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Services;

namespace JPrime.Panel.Setup;

/// <summary>Hidden developer mode: <c>JPrimePanel.exe --dev-smoke &lt;root&gt; [seconds]</c>.
/// Starts every enabled service headlessly, polls health, hits the app over HTTP, kills a php-cgi worker to
/// prove respawn, then stops everything. Writes <c>logs\dev-smoke.log</c>; exit code 0 = all checks passed.</summary>
public static class DevSmoke
{
    public static int Run(string root, int holdSeconds)
    {
        var paths = new AppPaths(root);
        var store = new ConfigStore(paths.PanelConfigFile);
        var config = store.Load();
        var ctx = AppServices.Initialize(paths, store, config);
        var report = new StringBuilder();
        var failures = 0;
        void L(string s)
        {
            report.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {s}");
            Log.Info("[dev-smoke] " + s);
        }
        void Check(bool ok, string what)
        {
            L((ok ? "PASS " : "FAIL ") + what);
            if (!ok) failures++;
        }

        try
        {
            var svc = ctx.Services;
            svc.SweepOrphans();
            svc.StartPolling();

            L("Starting all enabled services");
            svc.StartAllAsync().GetAwaiter().GetResult();
            foreach (var s in svc.Enabled) L($"  {s.Id}: {s.Status.State} {s.Status.Message} pids=[{string.Join(',', s.Status.Pids)}]");

            // Wait for nginx + pool to report Running (poll loop runs every 2 s).
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && !(svc.Nginx.Status.State == ServiceState.Running && svc.Php.Status.State == ServiceState.Running))
            {
                Thread.Sleep(500);
            }
            Check(svc.Php.Status.State == ServiceState.Running, $"php pool Running ({svc.Php.Status.Message}) pids=[{string.Join(',', svc.Php.Status.Pids)}]");
            Check(svc.Nginx.Status.State == ServiceState.Running, $"nginx Running ({svc.Nginx.Status.Message}) pids=[{string.Join(',', svc.Nginx.Status.Pids)}]");
            Check(svc.Scheduler.Status.State == ServiceState.Running, $"scheduler Running pid=[{string.Join(',', svc.Scheduler.Status.Pids)}]");

            var http = ctx.Http;
            var port = config.Web.Port;
            var up = Get(http, $"http://127.0.0.1:{port}/up");
            Check(up.Status == 200, $"GET /up -> {up.Status}");
            var discover = Get(http, $"http://127.0.0.1:{port}/api/kiosk/discover");
            Check(discover.Status == 200 && discover.Body.Contains("jprimefitness-kiosk-api"), $"GET /api/kiosk/discover -> {discover.Status} {Trunc(discover.Body)}");
            var login = Get(http, $"http://127.0.0.1:{port}/login");
            Check(login.Status == 200 && login.Body.Contains("<html", StringComparison.OrdinalIgnoreCase), $"GET /login -> {login.Status} ({login.Body.Length} bytes)");
            var asset = Get(http, $"http://127.0.0.1:{port}/build/manifest.json");
            Check(asset.Status is 200 or 403 or 404, $"GET /build/manifest.json -> {asset.Status}");

            // Kill one php-cgi externally; supervisor must respawn it.
            var victim = svc.Php.Status.Pids.FirstOrDefault();
            if (victim != 0)
            {
                L($"Killing php-cgi pid {victim} externally");
                try { Process.GetProcessById(victim).Kill(); } catch (Exception ex) { L("kill failed: " + ex.Message); }
                var respawnDeadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < respawnDeadline && (svc.Php.Status.Pids.Count < config.Web.PoolSize || svc.Php.Status.Pids.Contains(victim)))
                {
                    Thread.Sleep(250);
                }
                Check(svc.Php.Status.Pids.Count == config.Web.PoolSize && !svc.Php.Status.Pids.Contains(victim),
                    $"php-cgi respawned: pids=[{string.Join(',', svc.Php.Status.Pids)}]");
                Thread.Sleep(2500);
                Check(svc.Php.Status.State == ServiceState.Running, $"php pool back to Running ({svc.Php.Status.Message})");
            }

            // Burst of requests across the pool: no 502s expected.
            var codes = new Dictionary<int, int>();
            for (var i = 0; i < 40; i++)
            {
                var r = Get(http, $"http://127.0.0.1:{port}/up");
                codes[r.Status] = codes.GetValueOrDefault(r.Status) + 1;
            }
            Check(codes.GetValueOrDefault(200) == 40, "40x GET /up -> " + string.Join(", ", codes.Select(kv => $"{kv.Key}:{kv.Value}")));

            // Online backup while services are running.
            try
            {
                var backup = new Runtime.SqliteBackup(paths, config);
                var file = backup.RunAsync(CancellationToken.None, L).GetAwaiter().GetResult();
                Check(File.Exists(file) && new FileInfo(file).Length > 0, $"online backup written {Path.GetFileName(file)} ({new FileInfo(file).Length / 1024} KB)");
            }
            catch (Exception ex)
            {
                Check(false, "online backup: " + ex.Message);
            }

            // Download cache reuse + sha check on a cached payload.
            try
            {
                var dm = new DownloadManager(ctx.Http, paths.DownloadsDir);
                var cached = dm.IsCached(PayloadCatalog.Nginx);
                var path = dm.FetchAsync(PayloadCatalog.Nginx, null, CancellationToken.None).GetAwaiter().GetResult();
                Check(File.Exists(path), $"download manager reuse (cached before={cached}) -> {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Check(false, "download manager: " + ex.Message);
            }

            if (holdSeconds > 0)
            {
                L($"Holding services up for {holdSeconds}s (browse http://localhost:{port}/)");
                Thread.Sleep(TimeSpan.FromSeconds(holdSeconds));
            }

            L("Stopping all");
            svc.StopAllAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
            foreach (var s in svc.Enabled) L($"  {s.Id}: {s.Status.State}");
            var leftovers = new[] { "nginx", "php-cgi" }.SelectMany(n => Process.GetProcessesByName(n)).Where(p =>
            {
                try { return p.MainModule?.FileName?.StartsWith(paths.InstallRoot, StringComparison.OrdinalIgnoreCase) == true; }
                catch { return false; }
            }).Select(p => $"{p.ProcessName}:{p.Id}").ToList();
            Check(leftovers.Count == 0, "no leftover nginx/php-cgi after StopAll" + (leftovers.Count > 0 ? " -> " + string.Join(", ", leftovers) : ""));
        }
        catch (Exception ex)
        {
            L("EXCEPTION " + ex);
            failures++;
        }
        finally
        {
            try { ctx.Services.Dispose(); } catch { }
            report.AppendLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
            File.WriteAllText(Path.Combine(paths.LogsDir, "dev-smoke.log"), report.ToString());
        }
        return failures == 0 ? 0 : 1;
    }

    private static (int Status, string Body) Get(HttpClient http, string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var resp = http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            return ((int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            return (0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Trunc(string s) => s.Length > 120 ? s[..120] + "..." : s.Replace("\n", " ");
}
