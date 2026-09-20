using System.Diagnostics;

namespace JPrime.Panel.Processes;

public static class ProcessTree
{
    /// <summary>Kill a process and all descendants. Order: managed tree kill, then taskkill /T /F.</summary>
    public static void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
            return;
        }
        catch (ArgumentException)
        {
            return; // already gone
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Kill(tree) pid {pid} failed ({ex.Message}); falling back to taskkill");
        }

        try
        {
            var psi = new ProcessStartInfo("taskkill.exe", $"/T /F /PID {pid}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var tk = Process.Start(psi);
            tk?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            App.Log.Warn($"taskkill pid {pid} failed: {ex.Message}");
        }
    }

    /// <summary>Kill leftover runtime processes whose image lives under the install root (manual launches, or a
    /// crash of a panel build that predates job-object protection).</summary>
    public static int SweepOrphans(string installRoot, IEnumerable<string> imageNames)
    {
        var killed = 0;
        var root = Path.GetFullPath(installRoot).TrimEnd('\\') + "\\";
        foreach (var name in imageNames)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); } catch { continue; }
            foreach (var p in procs)
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        App.Log.Warn($"Sweeping orphan {name} pid {p.Id} ({path})");
                        KillTree(p.Id);
                        killed++;
                    }
                }
                catch
                {
                    // Access denied (another user's process) or already exited.
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        return killed;
    }
}
