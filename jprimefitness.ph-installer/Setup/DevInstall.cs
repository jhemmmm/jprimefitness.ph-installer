using JPrime.Panel.App;

namespace JPrime.Panel.Setup;

/// <summary>Hidden developer mode: <c>JPrimePanel.exe --dev-install &lt;root&gt; [--with-hikvision &lt;devicePassword&gt;] [--payloads &lt;dir&gt;]</c>.
/// Runs the real InstallEngine headlessly with wizard defaults, resolving payloads from a cache directory
/// (default: &lt;root&gt;\downloads) without hitting GitHub for the app bundle. Exit 0 = installed.</summary>
public static class DevInstall
{
    public static int Run(string root, string? devicePassword, string? payloadDir)
    {
        var s = new WizardState
        {
            InstallRoot = root,
            InstallHelper = devicePassword is not null,
            InstallTunnel = false,
            GymName = "JPrime Fitness PH (dev)",
            WebPort = 8001,
            LanAccess = false, // no UAC prompt in the headless run unless runtimes are missing
            SuperAdminName = "JPrime Super Admin",
            SuperAdminEmail = "admin@jprimefitness.ph",
            SuperAdminPassword = "Admin#12345",
            DevicePassword = devicePassword ?? "",
        };
        var http = AppServices.CreateHttpClient();
        var lines = new List<string>();
        void L(string t)
        {
            lines.Add($"{DateTime.Now:HH:mm:ss} {t}");
            Console.WriteLine(t);
        }
        try
        {
            var cache = payloadDir ?? Path.Combine(root, "downloads");
            var dm = new DownloadManager(http, cache);
            var php = PayloadCatalog.ResolvePhpAsync(http, CancellationToken.None).GetAwaiter().GetResult();
            var list = new List<Payload> { php, PayloadCatalog.Nginx, PayloadCatalog.CaBundle };
            var local = Directory.GetFiles(cache, "jprimefitness.ph-*.zip").OrderByDescending(f => f).FirstOrDefault()
                        ?? throw new FileNotFoundException("No jprimefitness.ph-*.zip in " + cache);
            var ver = Path.GetFileNameWithoutExtension(local).Replace("jprimefitness.ph-", "");
            list.Add(new Payload("app", $"JPrime app {ver}", "file://" + local, Path.GetFileName(local), PayloadKind.Zip, ver));
            if (s.InstallHelper)
            {
                var hikLocal = Directory.GetFiles(cache, "hikvision-*.zip").OrderByDescending(f => f).FirstOrDefault();
                list.Add(hikLocal is not null
                    ? new Payload("hikvision", "Biometric helper (local)", "file://" + hikLocal, Path.GetFileName(hikLocal), PayloadKind.Zip, Path.GetFileNameWithoutExtension(hikLocal).Replace("hikvision-", ""))
                    : PayloadCatalog.ResolveHikVisionAsync(http, CancellationToken.None).GetAwaiter().GetResult());
            }
            s.NeedVcRedist = !PrereqChecker.IsVcRedist2015PlusInstalled();
            s.NeedAspNetRuntime = s.InstallHelper && !PrereqChecker.IsAspNetRuntime8Installed();
            if (s.NeedVcRedist) list.Add(PayloadCatalog.VcRedist);
            if (s.NeedAspNetRuntime) list.Add(PayloadCatalog.AspNetRuntime);

            foreach (var p in list)
            {
                s.Payloads[p.Id] = p;
                if (p.Url.StartsWith("file://"))
                {
                    s.Downloaded[p.Id] = p.Url["file://".Length..];
                    L($"payload {p.Id}: local {s.Downloaded[p.Id]}");
                }
                else
                {
                    s.Downloaded[p.Id] = dm.FetchAsync(p, null, CancellationToken.None).GetAwaiter().GetResult();
                    L($"payload {p.Id}: {s.Downloaded[p.Id]}");
                }
            }
            if (s.InstallHelper) s.HelperIsSelfContained = ZipExtractor.ContainsFile(s.Downloaded["hikvision"], "hostfxr.dll");

            var progress = new Progress<StepProgress>(p => L($"[{p.Fraction:P0}] {p.Step} {p.Message}"));
            var engine = new InstallEngine(s, http, progress, L);
            engine.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            L("INSTALL OK");
            return 0;
        }
        catch (Exception ex)
        {
            L("INSTALL FAILED: " + ex);
            return 1;
        }
        finally
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "logs"));
                File.WriteAllLines(Path.Combine(root, "logs", "dev-install.log"), lines);
            }
            catch { }
        }
    }
}
