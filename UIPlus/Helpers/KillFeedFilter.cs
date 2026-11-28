namespace UIPlus.Helpers;

public enum KillFeedMode { Off, All, Mine }

internal enum KillFeedEvent { Kill, Intercept, Player }

internal static class KillFeedFilter
{
    internal static bool Shows(KillFeedMode mode, KillFeedEvent kind, bool mine, bool intercepts, bool players) =>
        mode != KillFeedMode.Off && (kind == KillFeedEvent.Player
            ? players
            : (kind != KillFeedEvent.Intercept || intercepts) && (mode == KillFeedMode.All || mine));
}
