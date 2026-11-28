namespace UIPlus.Helpers;

internal enum HudCameraView { Other, Chase, Orbit, Cockpit, HudView }

internal static class HudViewPolicy
{
    internal static HudCameraView Resolve(HudCameraView stockView, bool hudViewShowing) =>
        hudViewShowing ? HudCameraView.HudView : stockView;

    internal static bool Shows(HudCameraView view, bool chase, bool orbit, bool cockpit, bool hudView) => view switch
    {
        HudCameraView.Chase => chase,
        HudCameraView.Orbit => orbit,
        HudCameraView.Cockpit => cockpit,
        HudCameraView.HudView => hudView,
        _ => false,
    };

    internal static bool AttachesToCamera(HudCameraView view, bool chase, bool hudView) => view switch
    {
        HudCameraView.Chase => chase,
        HudCameraView.HudView => hudView,
        _ => false,
    };
}
