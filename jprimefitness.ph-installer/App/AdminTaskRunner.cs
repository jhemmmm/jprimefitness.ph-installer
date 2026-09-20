using System.Diagnostics;
using System.Text;

namespace JPrime.Panel.App;

/// <summary>Executes <c>--admin-task</c> requests inside the elevated instance. Tasks:
/// <list type="bullet">
/// <item><c>firewall &lt;port&gt; &lt;scope&gt;</c> — inbound TCP rule "JPrime Web (port)". scope = LocalSubnet|Any</item>
/// <item><c>firewall-helper</c> — inbound TCP 5077 + UDP 37020, LocalSubnet</item>
/// <item><c>firewall-remove</c> — delete all "JPrime *" rules</item>
/// <item><c>run-installer &lt;exe&gt; &lt;args...&gt;</c> — run a silent installer (vcredist / ASP.NET runtime)</item>
/// </list>
/// Exit code = first failing task's code, else 0. Logs to <c>&lt;--log-dir&gt;\admin-task.log</c>.</summary>
public static class AdminTaskRunner
{
    public static int Run(CommandLine cli)
    {
        string? logDir = null;
        // --log-dir is parsed here because CommandLine treats unknown switches loosely.
        var raw = Environment.GetCommandLineArgs();
        for (var i = 0; i < raw.Length - 1; i++)
        {
            if (string.Equals(raw[i], "--log-dir", StringComparison.OrdinalIgnoreCase)) logDir = raw[i + 1];
        }
        var sb = new StringBuilder();
        void L(string s)
        {
            sb.AppendLine($"{DateTime.Now:HH:mm:ss} {s}");
        }

        var exit = 0;
        try
        {
            if (!Elevation.IsElevated())
            {
                L("Not elevated; refusing to run admin tasks.");
                exit = 5;
            }
            else
            {
                foreach (var task in cli.AdminTasks)
                {
                    L($"== {task.Name} {string.Join(' ', task.Args)}");
                    var code = RunOne(task, L);
                    L($"   exit {code}");
                    if (code != 0 && exit == 0) exit = code;
                }
            }
        }
        catch (Exception ex)
        {
            L("FATAL " + ex);
            if (exit == 0) exit = 1;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(logDir))
                {
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, "admin-task.log"), sb.ToString());
                }
            }
            catch { }
        }
        return exit;
    }

    private static int RunOne(AdminTaskRequest task, Action<string> log)
    {
        switch (task.Name.ToLowerInvariant())
        {
            case "firewall":
                {
                    var port = task.Args.Count > 0 ? task.Args[0] : "8001";
                    var scope = task.Args.Count > 1 ? task.Args[1] : "LocalSubnet";
                    var remote = scope.Equals("Any", StringComparison.OrdinalIgnoreCase) ? "any" : "LocalSubnet";
                    Netsh(log, $"advfirewall firewall delete rule name=\"JPrime Web ({port})\"");
                    return Netsh(log, $"advfirewall firewall add rule name=\"JPrime Web ({port})\" dir=in action=allow protocol=TCP localport={port} remoteip={remote} profile=private,domain");
                }
            case "firewall-helper":
                {
                    Netsh(log, "advfirewall firewall delete rule name=\"JPrime Biometric Helper (TCP 5077)\"");
                    Netsh(log, "advfirewall firewall delete rule name=\"JPrime Biometric Helper SADP (UDP 37020)\"");
                    var a = Netsh(log, "advfirewall firewall add rule name=\"JPrime Biometric Helper (TCP 5077)\" dir=in action=allow protocol=TCP localport=5077 remoteip=LocalSubnet profile=private,domain");
                    var b = Netsh(log, "advfirewall firewall add rule name=\"JPrime Biometric Helper SADP (UDP 37020)\" dir=in action=allow protocol=UDP localport=37020 remoteip=LocalSubnet profile=private,domain");
                    return a != 0 ? a : b;
                }
            case "firewall-remove":
                {
                    // netsh cannot wildcard; delete the known names.
                    foreach (var name in new[] { "JPrime Biometric Helper (TCP 5077)", "JPrime Biometric Helper SADP (UDP 37020)" })
                        Netsh(log, $"advfirewall firewall delete rule name=\"{name}\"");
                    if (task.Args.Count > 0) Netsh(log, $"advfirewall firewall delete rule name=\"JPrime Web ({task.Args[0]})\"");
                    return 0;
                }
            case "run-installer":
                {
                    if (task.Args.Count == 0) return 2;
                    var psi = new ProcessStartInfo(task.Args[0]) { UseShellExecute = false, CreateNoWindow = true };
                    for (var i = 1; i < task.Args.Count; i++) psi.ArgumentList.Add(task.Args[i]);
                    log($"   run {psi.FileName} {string.Join(' ', psi.ArgumentList)}");
                    using var p = Process.Start(psi);
                    if (p is null) return 3;
                    p.WaitForExit();
                    // 3010 = success, reboot required; 1638 = newer version already installed (vcredist)
                    return p.ExitCode is 0 or 3010 or 1638 ? 0 : p.ExitCode;
                }
            default:
                log($"   unknown task {task.Name}");
                return 4;
        }
    }

    private static int Netsh(Action<string> log, string arguments)
    {
        var psi = new ProcessStartInfo("netsh.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        log($"   netsh {arguments} -> {p.ExitCode} {output.Trim()}");
        return p.ExitCode;
    }
}
