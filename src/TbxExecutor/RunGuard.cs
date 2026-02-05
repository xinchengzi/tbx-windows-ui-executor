using System;
using System.Threading;
using System.Threading.Tasks;

namespace TbxExecutor;

/// <summary>
/// Global single-run concurrency guard.
/// Only one macro or input sequence can run at a time.
/// </summary>
public sealed class RunGuard
{
    private static readonly RunGuard _instance = new();
    public static RunGuard Instance => _instance;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private RunGuard() { }

    /// <summary>
    /// Try to acquire the run lock immediately without blocking.
    /// </summary>
    /// <returns>True if lock was acquired; false if another run is in progress.</returns>
    public bool TryAcquire()
    {
        return _semaphore.Wait(0);
    }

    /// <summary>
    /// Try to acquire the run lock asynchronously without blocking.
    /// </summary>
    /// <returns>True if lock was acquired; false if another run is in progress.</returns>
    public async Task<bool> TryAcquireAsync(CancellationToken ct = default)
    {
        return await _semaphore.WaitAsync(0, ct);
    }

    /// <summary>
    /// Release the run lock. Must be called after TryAcquire/TryAcquireAsync returns true.
    /// </summary>
    public void Release()
    {
        _semaphore.Release();
    }

    /// <summary>
    /// Check if a run is currently in progress (lock is held).
    /// </summary>
    public bool IsBusy => _semaphore.CurrentCount == 0;
}
