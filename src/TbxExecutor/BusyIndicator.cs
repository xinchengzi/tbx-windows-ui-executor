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
        bool wasNotBusy;
        lock (_lock)
        {
            wasNotBusy = !IsBusyInternal();
            _macroCount++;
        }
        if (wasNotBusy) NotifyStateChanged(true);
    }

    public void ExitMacro()
    {
        bool isNowNotBusy;
        lock (_lock)
        {
            _macroCount = Math.Max(0, _macroCount - 1);
            isNowNotBusy = !IsBusyInternal();
        }
        if (isNowNotBusy) NotifyStateChanged(false);
    }

    public void FlashBusy(int durationMs = 500)
    {
        bool wasNotBusy;
        lock (_lock)
        {
            wasNotBusy = !IsBusyInternal();
            _isFlashing = true;
            _flashTimer.Change(durationMs, Timeout.Infinite);
        }
        if (wasNotBusy) NotifyStateChanged(true);
    }

    private void OnFlashTimerElapsed(object? state)
    {
        bool isNowNotBusy;
        lock (_lock)
        {
            _isFlashing = false;
            isNowNotBusy = !IsBusyInternal();
        }
        if (isNowNotBusy) NotifyStateChanged(false);
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
