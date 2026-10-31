// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
namespace Corvid;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private volatile bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    public static SingleInstance? Claim(string app, string dataDir)
    {
        var key = dataDir.Replace('\\', '/').ToLowerInvariant();
        var showName = $"{app}.Show.{key}";
        var mutex = new Mutex(true, $"{app}.SingleInstance.{key}", out var created);
        if (!created)
        {
            mutex.Dispose();
            try
            {
                using var first = EventWaitHandle.OpenExisting(showName);
                first.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, showName));
    }

    public void Listen(Action onSecondStart)
    {
        var show = _show;
        new Thread(() =>
        {
            try
            {
                while (!_disposed && show.WaitOne())
                {
                    if (!_disposed) onSecondStart();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }) { IsBackground = true }.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _show.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
