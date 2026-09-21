using JPrime.Panel.App;
using JPrime.Panel.Setup;

namespace JPrime.Panel.Runtime.Updates;

/// <summary>Replaces <c>hikvision\</c> with a newer helper release, keeping appsettings.json (device credentials written
/// by setup) and the logs. The helper service must be stopped by the caller.</summary>
public sealed class HelperUpdater
{
    private readonly AppServices _ctx;

    public HelperUpdater(AppServices ctx)
    {
        _ctx = ctx;
    }

    public void Apply(string zip, string newVersion, Action<string> log)
    {
        var paths = _ctx.Paths;
        var current = paths.HikVisionDir;
        var incoming = current + ".new";
        var stash = current + ".old";

        log("Extracting new helper");
        if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
        ZipExtractor.Extract(zip, incoming, stripTopLevel: true);
        if (!File.Exists(Path.Combine(incoming, "HikVision.exe"))) throw new InvalidOperationException("The zip does not contain HikVision.exe.");

        log("Carrying over appsettings.json and logs");
        // The release ships a placeholder appsettings.json; ours has the real device credentials and token.
        if (File.Exists(paths.HikVisionAppSettings)) File.Copy(paths.HikVisionAppSettings, Path.Combine(incoming, "appsettings.json"), overwrite: true);
        if (Directory.Exists(paths.HikVisionLogsDir)) DirectorySwap.MoveContents(paths.HikVisionLogsDir, Path.Combine(incoming, "logs"));
        foreach (var old in Directory.GetFiles(incoming, ".installed-*")) File.Delete(old);
        File.WriteAllText(Path.Combine(incoming, ".installed-" + newVersion), DateTime.UtcNow.ToString("O"));

        log("Swapping folders");
        DirectorySwap.Replace(current, incoming, stash, "hikvision");

        _ctx.Config.Versions.HikVision = newVersion;
        _ctx.SaveConfig();
        log($"Updated biometric helper to {newVersion}. Previous version kept in hikvision.old until the next update.");
    }
}
