using System;
using System.Collections.Generic;

namespace TbxExecutor;

/// <summary>
/// Display/monitor information in virtual screen coordinates (physical pixels).
/// </summary>
public sealed record DisplayInfo(
    int Index,
    string DeviceName,
    bool IsPrimary,
    RectPx BoundsRectPx,
    RectPx WorkAreaRectPx,
    int DpiX,
    int DpiY,
    double ScaleX,
    double ScaleY);

public interface IDisplayEnvironmentProvider
{
    /// <summary>
    /// Returns all connected displays/monitors.
    /// Coordinates are in physical pixels in the virtual screen coordinate space.
    /// </summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>
    /// Returns the virtual screen rectangle (union of all monitors).
    /// </summary>
    RectPx GetVirtualScreenRectPx();
}

public sealed class NullDisplayEnvironmentProvider : IDisplayEnvironmentProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays() => Array.Empty<DisplayInfo>();

    public RectPx GetVirtualScreenRectPx() => new RectPx(0, 0, 0, 0);
}
