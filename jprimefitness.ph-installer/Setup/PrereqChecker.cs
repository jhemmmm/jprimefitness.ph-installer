using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using JPrime.Panel.Health;
using Microsoft.Win32;

namespace JPrime.Panel.Setup;

public enum CheckLevel
{
    Ok,
    Warning,
    Blocker,
}

public sealed record CheckResult(string Name, CheckLevel Level, string Detail);

/// <summary>Pre-flight checks shown on the wizard's second page.</summary>
public static class PrereqChecker
{
    public static bool IsAspNetRuntime8Installed()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.AspNetCore.App");
        if (!Directory.Exists(shared)) return false;
        return Directory.GetDirectories(shared).Select(Path.GetFileName).Any(v => v is not null && v.StartsWith("8.", StringComparison.Ordinal));
    }

    public static bool IsVcRedist2015PlusInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            if (key is null) return false;
            var installed = key.GetValue("Installed") as int? ?? 0;
            var major = key.GetValue("Major") as int? ?? 0;
            return installed == 1 && major >= 14;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> HasInternetAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(6000);
            using var r = await http.GetAsync("https://api.github.com/", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return (int)r.StatusCode < 500;
        }
        catch
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
    }

    public static IReadOnlyList<CheckResult> CheckSystem(int webPort, int helperPort, int poolBasePort, int poolSize, int metricsPort, bool helperSelected, bool tunnelSelected, string installRoot)
    {
        var list = new List<CheckResult>();

        list.Add(Environment.Is64BitOperatingSystem && RuntimeInformation.OSArchitecture == Architecture.X64
            ? new("64-bit Windows", CheckLevel.Ok, RuntimeInformation.OSDescription)
            : new("64-bit Windows", CheckLevel.Blocker, "PHP, nginx and the biometric SDK need 64-bit Windows on x64."));

        list.Add(Environment.OSVersion.Version >= new Version(10, 0)
            ? new("Windows 10 or newer", CheckLevel.Ok, Environment.OSVersion.VersionString)
            : new("Windows 10 or newer", CheckLevel.Blocker, "Windows 10 or 11 is required."));

        list.Add(PortCheck("Web port", webPort, blocker: true));
        for (var i = 0; i < poolSize; i++) list.Add(PortCheck($"PHP worker port", poolBasePort + i, blocker: true));
        if (helperSelected) list.Add(PortCheck("Biometric helper port", helperPort, blocker: true));
        if (tunnelSelected) list.Add(PortCheck("Tunnel metrics port", metricsPort, blocker: false));

        if (helperSelected)
        {
            list.Add(IsAspNetRuntime8Installed()
                ? new("ASP.NET Core Runtime 8 (x64)", CheckLevel.Ok, "installed")
                : new("ASP.NET Core Runtime 8 (x64)", CheckLevel.Warning, "Will be installed (needs administrator approval)."));
        }
        list.Add(IsVcRedist2015PlusInstalled()
            ? new("Visual C++ Redistributable (x64)", CheckLevel.Ok, "installed")
            : new("Visual C++ Redistributable (x64)", CheckLevel.Warning, "Will be installed (needs administrator approval)."));

        if (installRoot.Any(c => c > 127))
        {
            list.Add(new("Install folder", CheckLevel.Blocker, "nginx cannot handle non-ASCII characters in its path. Choose a plain folder like C:\\JPrime."));
        }
        else if (installRoot.Contains(' '))
        {
            list.Add(new("Install folder", CheckLevel.Warning, "Spaces in the path work but a short path like C:\\JPrime is safer."));
        }
        else
        {
            list.Add(new("Install folder", CheckLevel.Ok, installRoot));
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(installRoot)!);
            var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
            list.Add(freeGb >= 2
                ? new("Free disk space", CheckLevel.Ok, $"{freeGb:F1} GB free on {drive.Name}")
                : new("Free disk space", CheckLevel.Blocker, $"Only {freeGb:F1} GB free on {drive.Name}; at least 2 GB is needed."));
        }
        catch
        {
            list.Add(new("Free disk space", CheckLevel.Warning, "Could not determine free space."));
        }

        var lan = LanAddresses.List();
        list.Add(lan.Count > 0
            ? new("Network", CheckLevel.Ok, string.Join(", ", lan.Select(a => $"{a.Ip} ({a.Adapter}{(a.IsDhcp ? ", DHCP" : "")})")))
            : new("Network", CheckLevel.Warning, "No LAN address found. Phones and kiosk tablets will not reach this PC until it is on the gym network."));

        return list;
    }

    private static CheckResult PortCheck(string name, int port, bool blocker)
    {
        if (PortScanner.IsFree(port)) return new($"{name} {port}", CheckLevel.Ok, "free");
        var owner = PortScanner.WhoHolds(port);
        var who = owner is null ? "another program" : $"{owner.ProcessName} (pid {owner.Pid})";
        var detail = $"in use by {who}." + (owner?.Hint is { } h ? " " + h : "");
        return new($"{name} {port}", blocker ? CheckLevel.Blocker : CheckLevel.Warning, detail);
    }
}
