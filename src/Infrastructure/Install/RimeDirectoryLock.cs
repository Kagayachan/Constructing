// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Install;

/// <summary>
/// A cross-process exclusive lock scoped to a canonical Rime user directory, held
/// across the whole read-merge-backup-write-deploy-rollback sequence so two
/// concurrent installs or restores cannot interleave and erase each other's
/// committed state (code review M-01).
/// </summary>
public sealed class RimeDirectoryLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _acquired;

    private RimeDirectoryLock(Mutex mutex, bool acquired)
    {
        _mutex = mutex;
        _acquired = acquired;
    }

    /// <summary>
    /// Acquires the lock for <paramref name="rimeDirectory"/>, waiting up to
    /// <paramref name="timeoutMs"/>. Returns null when the lock could not be taken.
    /// </summary>
    public static RimeDirectoryLock? TryAcquire(string rimeDirectory, int timeoutMs = 30_000)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rimeDirectory))
            .ToLowerInvariant();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32];
        var mutex = CreateMutex($"Global\\ssf2weasel-rime-{hash}")
            ?? CreateMutex($"Local\\ssf2weasel-rime-{hash}");
        if (mutex is null)
        {
            return null;
        }

        try
        {
            var acquired = mutex.WaitOne(timeoutMs, exitContext: false);
            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new RimeDirectoryLock(mutex, acquired: true);
        }
        catch (AbandonedMutexException)
        {
            // A previous holder crashed without releasing; we now own the mutex.
            return new RimeDirectoryLock(mutex, acquired: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            mutex.Dispose();
            return null;
        }
    }

    private static Mutex? CreateMutex(string name)
    {
        try
        {
            return new Mutex(initiallyOwned: false, name);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_acquired)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _acquired = false;
        }

        _mutex.Dispose();
    }
}
