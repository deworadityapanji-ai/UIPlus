using System;
using UnityEngine;

namespace UIPlus.Helpers;

internal static class HudTrayLayout
{
    internal static bool SameRect(Rect a, Rect b) =>
        Math.Abs(a.x - b.x) < 0.01f && Math.Abs(a.y - b.y) < 0.01f &&
        Math.Abs(a.width - b.width) < 0.01f && Math.Abs(a.height - b.height) < 0.01f;

    internal static Rect AuthoredFallback(Rect authored)
    {
        // Some mod gauges have a transform-only root. Give them a stable nominal slot.
        Vector2 size = new(authored.width > 0f ? authored.width : 100f, authored.height > 0f ? authored.height : 100f);
        return new Rect(authored.center - size * 0.5f, size);
    }

    internal static Rect Union(Rect a, Rect b)
    {
        Vector2 lo = Vector2.Min(a.min, b.min), hi = Vector2.Max(a.max, b.max);
        return new Rect(lo, hi - lo);
    }

    internal static float Slot(bool strip, bool thunder, bool stacked)
    {
        // Dashboard and Analog share the strip height. Plus stacks gear and autopilot.
        float slot = strip ? 100f : thunder ? 130f : 150f;
        return stacked ? slot * 0.45f : slot;
    }

    internal static (Rect Bounds, float Scale) Fit(Rect measured, float slot)
    {
        float scale = Math.Max(0.5f, Math.Min(1.6f, slot / Math.Max(measured.width, measured.height)));
        return (new Rect(measured.position * scale, measured.size * scale), scale);
    }
}
