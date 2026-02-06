using System;
using System.Threading;

namespace TbxExecutor;

public sealed class BusyIndicator : IDisposable
{
    private readonly object _lock = new();
    private readonly System.Threading.Timer _flashTimer;
    private int _macroCount;
    private bool _isFlashing;
    private bool _disposed;

    public event Action<bool>? BusyStateChanged;

    public BusyIndicator()
    {
        _flashTimer = new System.Threading.Timer(OnFlashTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _macroCount > 0 || _isFlashing;
            }
        }
    }

    public object GetCounters()
    {
        lock (_lock)
        {
            return new { macroCount = _macroCount, isFlashing = _isFlashing };
        }
    }

    public void EnterMacro()
    {
        bool shouldNotify;
        lock (_lock)
        {
            var wasNotBusy = !IsBusyInternal();
            _macroCount++;
            shouldNotify = wasNotBusy;
        }
        if (shouldNotify) NotifyStateChanged(true);
    }

    public void ExitMacro()
    {
        bool shouldNotify;
        lock (_lock)
        {
            _macroCount = Math.Max(0, _macroCount - 1);
            shouldNotify = !IsBusyInternal();
        }
        if (shouldNotify) NotifyStateChanged(false);
    }

    public void FlashBusy(int durationMs = 500)
    {
        bool shouldNotify;
        lock (_lock)
        {
            var wasNotBusy = !IsBusyInternal();
            _isFlashing = true;
            _flashTimer.Change(durationMs, Timeout.Infinite);
            shouldNotify = wasNotBusy;
        }
        if (shouldNotify) NotifyStateChanged(true);
    }

    private void OnFlashTimerElapsed(object? state)
    {
        bool shouldNotify;
        lock (_lock)
        {
            _isFlashing = false;
            shouldNotify = !IsBusyInternal();
        }
        if (shouldNotify) NotifyStateChanged(false);
    }

    private bool IsBusyInternal()
    {
        return _macroCount > 0 || _isFlashing;
    }

    private void NotifyStateChanged(bool isBusy)
    {
        try
        {
            BusyStateChanged?.Invoke(isBusy);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flashTimer.Dispose();
    }
}
