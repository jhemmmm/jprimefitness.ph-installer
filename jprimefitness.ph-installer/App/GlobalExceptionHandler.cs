namespace JPrime.Panel.App;

/// <summary>A panel crash kills every supervised service (job objects), so unexpected exceptions are logged and
/// surfaced instead of terminating the process.</summary>
public static class GlobalExceptionHandler
{
    public static void Install()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            try
            {
                MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails were written to panel.log.",
                    "JPrime Control Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception (AppDomain)", e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }
}
