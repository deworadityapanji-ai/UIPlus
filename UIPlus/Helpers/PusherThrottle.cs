using System;

namespace UIPlus.Helpers;

internal enum PusherGauge { Missing, SingleSided, Reversible }
internal enum PusherSource { None, Gauge, Engine }

internal static class PusherThrottle
{
    internal const float DefaultReverse = 0.47f, DefaultForward = 0.53f;

    internal static bool ShouldRefreshGauge(bool hudChanged, PusherGauge gauge, bool pollDue, bool inRetryWindow) =>
        hudChanged || (gauge != PusherGauge.Reversible && pollDue && inRetryWindow);

    internal static PusherGauge Gauge(bool present, bool twoBars, float? split, float? idle = null)
    {
        if (!present) return PusherGauge.Missing;
        // The UFO has bar references too. Its split is at the end of the axis, so
        // its anti-gravity gauge has no negative side even with both images assigned.
        if (!twoBars) return PusherGauge.SingleSided;
        if (split is float centre && (!(centre > 0f && centre < 1f) ||
            idle is float deadZone && deadZone >= 0f && !ValidBand(centre - deadZone, centre + deadZone)))
            return PusherGauge.SingleSided;
        return PusherGauge.Reversible;
    }

    internal static PusherSource Source(PusherGauge gauge, bool engine) => gauge switch
    {
        PusherGauge.Reversible => PusherSource.Gauge,
        PusherGauge.Missing when engine => PusherSource.Engine,
        _ => PusherSource.None,
    };

    internal static bool ValidBand(float reverse, float forward) => reverse > 0f && forward >= reverse && forward < 1f;

    internal static float Signed(float command, float reverse = DefaultReverse, float forward = DefaultForward)
    {
        if (command < reverse) return Math.Max(-1f, (command - reverse) / reverse);
        if (command > forward) return Math.Min(1f, (command - forward) / (1f - forward));
        return 0f;
    }
}
