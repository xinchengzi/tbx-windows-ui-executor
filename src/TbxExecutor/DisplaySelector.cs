using System;

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

        return null;
    }

    public static DisplayInfo? ByRectCenter(IReadOnlyList<DisplayInfo> displays, RectPx rect)
    {
        var cx = rect.X + rect.W / 2;
        var cy = rect.Y + rect.H / 2;
        return ByPoint(displays, cx, cy);
    }

    private static bool ContainsPoint(RectPx r, int x, int y)
    {
        // Inclusive-exclusive to match typical pixel rect conventions.
        return x >= r.X && y >= r.Y && x < (r.X + r.W) && y < (r.Y + r.H);
    }
}
