namespace TaskbarCat;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\TaskbarCat.SingleInstance.v1";
    private const string ReplaceEventName = "Local\\TaskbarCat.ReplaceInstance.v1";
    private readonly Mutex mutex;
    private readonly EventWaitHandle replaceEvent;
    private RegisteredWaitHandle? registeredWait;
    private bool ownsMutex;

    public SingleInstanceCoordinator()
    {
        mutex = new Mutex(true, MutexName, out var createdNew);
        ownsMutex = createdNew;
        replaceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReplaceEventName);
    }

    public bool ReplaceExisting(TimeSpan timeout)
    {
        if (ownsMutex) return true;
        replaceEvent.Set();
        try { ownsMutex = mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        return ownsMutex;
    }

    public void WatchForReplacement(Action closeCurrentInstance)
    {
        registeredWait = ThreadPool.RegisterWaitForSingleObject(replaceEvent, (_, timedOut) =>
        {
            if (!timedOut) closeCurrentInstance();
        }, null, Timeout.Infinite, true);
    }

    public void Dispose()
    {
        registeredWait?.Unregister(null);
        if (ownsMutex)
        {
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            ownsMutex = false;
        }
        replaceEvent.Dispose();
        mutex.Dispose();
    }
}
