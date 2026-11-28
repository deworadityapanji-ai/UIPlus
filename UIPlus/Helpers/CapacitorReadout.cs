using System;

namespace UIPlus.Helpers;

internal static class CapacitorReadout
{
    // The game's "0" format on .NET Framework rounds half a kJ upwards.
    internal static int WholeKj(float energy) => (int)Math.Round(energy, MidpointRounding.AwayFromZero);

    internal static float SmoothRate(float previous, float change, float seconds) =>
        seconds > 0f ? previous + (change / seconds - previous) * (1f - (float)Math.Exp(-seconds / 0.5f)) : previous;

    internal static int Direction(float rate) => Math.Abs(rate) < 1f ? 0 : Math.Sign(rate);
}
