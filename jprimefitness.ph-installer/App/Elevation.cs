using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace JPrime.Panel.App;

/// <summary>Runs admin-only work by relaunching this exe elevated in <c>--admin-task</c> mode and waiting.</summary>
public static class Elevation
{
    public const int ExitCancelled = 1223; // ERROR_CANCELLED: user declined UAC

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Runs one or more admin tasks in a single elevated process (one UAC prompt).
    /// Returns the elevated process exit code, or <see cref="ExitCancelled"/> when the user declined.</summary>
    public static async Task<int> RunAdminTasksAsync(IEnumerable<AdminTaskRequest> tasks, string logsDir, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
        var args = new List<string>();
        foreach (var t in tasks)
        {
            args.Add("--admin-task");
            args.Add(t.Name);
            args.AddRange(t.Args);
        }
        args.Add("--log-dir");
        args.Add(logsDir);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Elevated process did not start.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ExitCancelled)
        {
            return ExitCancelled;
        }

        using (process)
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
