using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime;

namespace JPrime.Panel.Setup;

/// <summary>Hidden developer mode: <c>JPrimePanel.exe --dev-update &lt;root&gt; &lt;bundle.zip&gt;</c>.
/// Applies a bundle exactly like the panel's Update dialog, without the UI. Exit code 0 on success.</summary>
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
