using System.Runtime.InteropServices;

namespace JPrime.Panel.Processes;

/// <summary>Sends Ctrl+C to a hidden-console child (HikVision helper, cloudflared) from this GUI process.
/// All process starts and Ctrl+C sequences share <see cref="Lock"/> so a child never inherits a temporarily attached console.</summary>
public static class ConsoleCtrl
{
    public static readonly object Lock = new();

    // Kept alive in a static so the delegate is never collected while registered.
    private static readonly ConsoleCtrlDelegate SwallowHandler = _ => true;

    /// <summary>Returns true when the event was delivered (not whether the child exited).</summary>
    public static bool SendCtrlC(int pid)
    {
        lock (Lock)
        {
            FreeConsole();
            if (!AttachConsole((uint)pid))
            {
                // ERROR_INVALID_HANDLE: child has no console; ERROR_ACCESS_DENIED: we still own one.
                App.Log.Warn($"AttachConsole({pid}) failed: {Marshal.GetLastWin32Error()}");
                return false;
            }
            try
            {
                SetConsoleCtrlHandler(SwallowHandler, true);
                var ok = GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
                if (!ok) App.Log.Warn($"GenerateConsoleCtrlEvent({pid}) failed: {Marshal.GetLastWin32Error()}");
                // Give the event a moment to be dispatched before we detach.
                Thread.Sleep(100);
                return ok;
            }
            finally
            {
                SetConsoleCtrlHandler(SwallowHandler, false);
                FreeConsole();
            }
        }
    }

    private const uint CTRL_C_EVENT = 0;

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
}
