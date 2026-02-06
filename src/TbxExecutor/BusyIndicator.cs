using System;
using System.Threading;

namespace TbxExecutor;

public sealed class BusyIndicator : IDisposable
{
    private readonly object _lock = new();
    private readonly Timer _flashTimer;
    private int _macroCount;
    private int _flashCount;
    private bool _disposed;

    public event Action<bool>? BusyStateChanged;

    public BusyIndicator()
    {
        _flashTimer = new Timer(OnFlashTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _macroCount > 0 || _flashCount > 0;
            }
        }
    }

    public void EnterMacro()
    {
        bool wasNotBusy;
        lock (_lock)
        {
            wasNotBusy = !IsBusy;
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
            isNowNotBusy = !IsBusy;
        }
        if (isNowNotBusy) NotifyStateChanged(false);
    }

    public void FlashBusy(int durationMs = 500)
    {
        bool wasNotBusy;
        lock (_lock)
        {
            wasNotBusy = !IsBusy;
            _flashCount++;
            _flashTimer.Change(durationMs, Timeout.Infinite);
        }
        if (wasNotBusy) NotifyStateChanged(true);
    }

    private void OnFlashTimerElapsed(object? state)
    {
        bool isNowNotBusy;
        lock (_lock)
        {
            _flashCount = Math.Max(0, _flashCount - 1);
            isNowNotBusy = !IsBusy;
        }
        if (isNowNotBusy) NotifyStateChanged(false);
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
