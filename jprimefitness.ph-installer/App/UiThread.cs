namespace JPrime.Panel.App;

/// <summary>Captured WinForms synchronization context so background code can marshal to the UI thread.</summary>
public static class UiThread
{
    private static SynchronizationContext? _context;

    public static void Capture()
    {
        _context = SynchronizationContext.Current;
    }

    public static bool IsCaptured => _context is not null;

    public static void Post(Action action)
    {
        var ctx = _context;
        if (ctx is null)
        {
            action();
            return;
        }
        ctx.Post(_ =>
        {
            try { action(); }
            catch (Exception ex) { Log.Error("UI callback failed", ex); }
        }, null);
    }
}
