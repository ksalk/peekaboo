using System.Collections.Concurrent;

namespace Peekaboo.Api.Hikvision;

internal sealed record HikvisionConnectionKey(string NvrId, string Host, int Port);

internal sealed record HikvisionConnection(
    HikvisionConnectionKey Key,
    int UserId,
    HikvisionDeviceInfo Device);

internal sealed class HikvisionConnectionLease(
    HikvisionConnection connection,
    Action release) : IDisposable
{
    private int _disposed;

    public HikvisionConnection Connection { get; } = connection;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            release();
        }
    }
}

/// <summary>
/// Shares one SDK login per NVR while one or more streams use it. The login is
/// released as soon as the last lease is disposed, so no NVR session outlives the
/// viewer that needed it.
/// </summary>
internal sealed class HikvisionConnectionPool(
    IHikvisionLoginApi loginApi,
    ILogger<HikvisionConnectionPool> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<HikvisionConnectionKey, Entry> _entries = new();
    private int _disposed;

    public async Task<HikvisionConnectionLease> RentAsync(
        HikvisionConnectionKey key,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        while (true)
        {
            var entry = _entries.GetOrAdd(key, static entryKey => new Entry(entryKey));

            lock (entry.Sync)
            {
                if (entry.Removed)
                {
                    // The entry was invalidated or released while we looked it up.
                }
                else if (entry.Connection is not null)
                {
                    entry.RefCount++;
                    return CreateLease(entry, entry.Connection);
                }
            }

            if (entry.Removed)
            {
                continue;
            }

            await entry.LoginGate.WaitAsync(cancellationToken);
            try
            {
                HikvisionConnection? created = null;

                lock (entry.Sync)
                {
                    if (entry.Removed)
                    {
                        continue;
                    }

                    if (entry.Connection is not null)
                    {
                        entry.RefCount++;
                        return CreateLease(entry, entry.Connection);
                    }
                }

                HikvisionLoginResult result;
                try
                {
                    result = await Task.Run(
                        () => loginApi.Login(key.Host, key.Port),
                        cancellationToken);
                }
                catch
                {
                    lock (entry.Sync)
                    {
                        entry.Removed = true;
                    }

                    _entries.TryRemove(key, out _);
                    throw;
                }

                lock (entry.Sync)
                {
                    if (entry.Removed)
                    {
                        created = new HikvisionConnection(key, result.UserId, result.Device);
                    }
                    else
                    {
                        entry.Connection = new HikvisionConnection(
                            key,
                            result.UserId,
                            result.Device);
                        entry.RefCount++;
                        return CreateLease(entry, entry.Connection);
                    }
                }

                // The NVR was deleted or updated while the login was in flight.
                SafeLogout(created!);
            }
            finally
            {
                entry.LoginGate.Release();
            }
        }
    }

    public Task InvalidateAsync(string nvrId)
    {
        foreach (var pair in _entries.Where(pair => pair.Key.NvrId == nvrId).ToArray())
        {
            if (_entries.TryRemove(pair.Key, out var entry))
            {
                RemoveEntry(entry);
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        foreach (var pair in _entries.ToArray())
        {
            if (_entries.TryRemove(pair.Key, out var entry))
            {
                RemoveEntry(entry);
            }
        }

        return ValueTask.CompletedTask;
    }

    private HikvisionConnectionLease CreateLease(Entry entry, HikvisionConnection connection) =>
        new(connection, () => Release(entry));

    private void RemoveEntry(Entry entry)
    {
        HikvisionConnection? connection = null;

        lock (entry.Sync)
        {
            entry.Removed = true;

            if (entry.RefCount == 0)
            {
                connection = entry.Connection;
                entry.Connection = null;
            }
        }

        if (connection is not null)
        {
            SafeLogout(connection);
        }
    }

    private void Release(Entry entry)
    {
        HikvisionConnection? connection = null;

        lock (entry.Sync)
        {
            if (entry.RefCount <= 0)
            {
                return;
            }

            entry.RefCount--;

            if (entry.RefCount == 0)
            {
                entry.Removed = true;
                connection = entry.Connection;
                entry.Connection = null;
                _entries.TryRemove(entry.Key, out _);
            }
        }

        if (connection is not null)
        {
            SafeLogout(connection);
        }
    }

    private void SafeLogout(HikvisionConnection connection)
    {
        try
        {
            loginApi.Logout(connection.UserId);
            logger.LogDebug(
                "Hikvision SDK login for NVR {NvrId} released.",
                connection.Key.NvrId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Hikvision SDK logout for NVR {NvrId} failed.",
                connection.Key.NvrId);
        }
    }

    private sealed class Entry(HikvisionConnectionKey key)
    {
        public HikvisionConnectionKey Key { get; } = key;
        public SemaphoreSlim LoginGate { get; } = new(1, 1);
        public object Sync { get; } = new();
        public HikvisionConnection? Connection { get; set; }
        public int RefCount { get; set; }
        public bool Removed { get; set; }
    }
}
