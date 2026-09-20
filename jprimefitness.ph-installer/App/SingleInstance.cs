using System.Threading;

namespace JPrime.Panel.App;

/// <summary>Global mutex so only one panel runs per machine session, plus an event the second launch can pulse
/// to make the first one show its window.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Global\JPrimePanel.SingleInstance";
    private const string ShowEventName = @"Global\JPrimePanel.Show";

    private readonly Mutex _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registration;
    private bool _released;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>Tries for ~5 s (Run-key vs shortcut race, previous instance still shutting down).</summary>
    public static SingleInstance? TryAcquire(int timeoutMs = 5000)
    {
        var mutex = new Mutex(false, MutexName, out _);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            try
            {
                if (mutex.WaitOne(250))
                {
                    return new SingleInstance(mutex);
                }
            }
            catch (AbandonedMutexException)
            {
                // Previous owner died without releasing: we now own it.
                return new SingleInstance(mutex);
            }

            if (Environment.TickCount64 >= deadline)
            {
                mutex.Dispose();
                return null;
            }
        }
    }

    /// <summary>Called by a second launch to front the running instance.</summary>
    public static void SignalShow()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(ShowEventName);
            evt.Set();
        }
        catch
        {
            // No listener yet; nothing to do.
        }
    }

    /// <summary>Registers a callback fired (on a thread-pool thread) whenever another launch asks us to show.</summary>
    public void ListenForShow(Action onShow)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out _);
        _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShow(), null, -1, false);
    }

    /// <summary>Release before launching the installed copy from the wizard so it can acquire immediately.</summary>
    public void Release()
    {
        if (_released) return;
        _released = true;
        _registration?.Unregister(null);
        _showEvent?.Dispose();
        try { _mutex.ReleaseMutex(); } catch { /* not owner */ }
    }

    public void Dispose()
    {
        Release();
        _mutex.Dispose();
    }
}
