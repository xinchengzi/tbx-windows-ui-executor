using System;
using System.Collections.Generic;

namespace TbxExecutor;

public static class DisplaySelector
{
    public static DisplayInfo? ByPoint(IReadOnlyList<DisplayInfo> displays, int x, int y)
    {
        if (displays is null || displays.Count == 0) return null;

        foreach (var d in displays)
        {
            if (ContainsPoint(d.BoundsRectPx, x, y)) return d;
        }

        return FindNearest(displays, x, y);
    }

    public static DisplayInfo? ByRectCenter(IReadOnlyList<DisplayInfo> displays, RectPx rect)
    {
        var cx = rect.X + rect.W / 2;
        var cy = rect.Y + rect.H / 2;
        return ByPoint(displays, cx, cy);
    }

    private static bool ContainsPoint(RectPx r, int x, int y)
    {
        return x >= r.X && y >= r.Y && x < (r.X + r.W) && y < (r.Y + r.H);
    }

    private static DisplayInfo? FindNearest(IReadOnlyList<DisplayInfo> displays, int x, int y)
    {
        DisplayInfo? nearest = null;
        var minDist = double.MaxValue;

        foreach (var d in displays)
        {
            var cx = d.BoundsRectPx.X + d.BoundsRectPx.W / 2;
            var cy = d.BoundsRectPx.Y + d.BoundsRectPx.H / 2;
            var dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (dist < minDist)
            {
                minDist = dist;
                nearest = d;
            }
        }

        return nearest;
    }
}
