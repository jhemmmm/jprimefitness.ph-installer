using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime;

namespace JPrime.Panel.Setup;

/// <summary>Hidden developer mode: <c>JPrimePanel.exe --dev-update &lt;root&gt; &lt;bundle.zip&gt;</c>.
/// Applies an app bundle, a hikvision-*.zip, or swaps a JPrimePanel.exe like the Updates dialog, without the UI;
/// <c>--dev-update &lt;root&gt; check</c> runs the GitHub release check. Exit code 0 on success.</summary>
public static class DevUpdate
{
    public static int Run(string root, string zip)
    {
        var paths = new AppPaths(root);
        var store = new ConfigStore(paths.PanelConfigFile);
        var ctx = AppServices.Initialize(paths, store, store.Load());
        void L(string s) { Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}"); Log.Info("[dev-update] " + s); }
        try
        {
            var name = Path.GetFileName(zip);
            if (zip == "check")
            {
                foreach (var u in new Runtime.Updates.UpdateChecker(ctx).CheckAsync(CancellationToken.None).GetAwaiter().GetResult())
                    L($"{u.Title}: installed {u.Installed}, latest {u.Latest?.Tag ?? "-"} ({u.Latest?.AssetName}), available={u.Available}{(u.Error is null ? "" : ", error " + u.Error)}");
                L("CHECK_OK");
                return 0;
            }
            if (zip.StartsWith("install:", StringComparison.OrdinalIgnoreCase))
            {
                // install:app | install:helper — download from GitHub and apply through the same runner as the dialog.
                var target = Enum.Parse<Runtime.Updates.UpdateTarget>(zip[8..], ignoreCase: true);
                var info = new Runtime.Updates.UpdateChecker(ctx).CheckAsync(CancellationToken.None).GetAwaiter().GetResult().First(u => u.Target == target);
                if (!info.Available) { L($"{info.Title}: nothing to install ({info.Installed} vs {info.Latest?.Tag})"); return 0; }
                new Runtime.Updates.UpdateRunner(ctx).InstallAsync(info, L, CancellationToken.None).GetAwaiter().GetResult();
                L($"UPDATE_OK {target} {info.Latest!.Tag}");
                return 0;
            }
            if (name.StartsWith("hikvision", StringComparison.OrdinalIgnoreCase))
            {
                var v = Path.GetFileNameWithoutExtension(zip).Replace("hikvision-", "");
                new Runtime.Updates.UpdateRunner(ctx).ApplyHelperAsync(zip, v, L, CancellationToken.None).GetAwaiter().GetResult();
                L("UPDATE_OK helper " + v);
                return 0;
            }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                L("Installed at " + Runtime.Updates.PanelUpdater.Swap(paths, zip, L));
                L("UPDATE_OK panel (not relaunched)");
                return 0;
            }
            var version = Path.GetFileNameWithoutExtension(zip).Replace("jprimefitness.ph-", "");
            new AppUpdater(ctx).ApplyAsync(zip, version, L, CancellationToken.None).GetAwaiter().GetResult();
            L("UPDATE_OK " + version);
            return 0;
        }
        catch (Exception ex)
        {
            L("UPDATE_FAILED " + ex.Message);
            return 1;
        }
    }
}
