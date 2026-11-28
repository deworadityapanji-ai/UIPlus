using BepInEx.Configuration;

namespace UIPlus.Helpers;

/// <summary>
/// Which camera views UI Plus draws its HUD in. Chase follows the Chase HUD → FlightHud setting
/// (whichever mod drives the chase camera); cockpit, orbit and ArcadeCam's HUD view have their own switches. Other
/// views (free / TV / cinematic cameras, the map, menus) never show it.
/// </summary>
internal static class HudViews
{
    internal const string Section = "HUD Views";
    internal static ConfigEntry<bool> Cockpit, Orbit, HudView;

    internal static void Bind(ConfigFile config)
    {
        Cockpit = config.Bind(Section, "ShowInCockpit", true,
            "Use the selected HUD style in the cockpit view too. The game's flight HUD is already on there; the custom styles replace its readouts as in chase. Off leaves the cockpit HUD exactly as the game draws it.");
        Orbit = config.Bind(Section, "ShowInOrbit", true,
            "Show the HUD in the game's orbit camera, which normally switches it off. Uses the same HUD style and Chase HUD choices (map, pitch ladder, waterline, instrument groups) as chase view.");
        HudView = config.Bind(Section, "ShowInHudView", true,
            "Use UI Plus in the nose camera added by ArcadeCam. Requires ArcadeCam with HUD view support.");
    }

    internal static bool IsHudView(CameraStateManager cam) =>
        cam != null && cam.currentState != null && ArcadeCamBridge.HudViewShowing;

    internal static bool HudViewSuppressed(CameraStateManager cam) => IsHudView(cam) && HudView?.Value != true;

    internal static HudCameraView Current(CameraStateManager cam)
    {
        if (cam == null || cam.currentState == null) return HudCameraView.Other;
        HudCameraView stockView = cam.currentState == cam.chaseState ? HudCameraView.Chase
            : cam.currentState == cam.orbitState ? HudCameraView.Orbit
            : cam.currentState == cam.cockpitState ? HudCameraView.Cockpit : HudCameraView.Other;
        return HudViewPolicy.Resolve(stockView, ArcadeCamBridge.HudViewShowing);
    }

    internal static bool Showing(CameraStateManager cam) => HudViewPolicy.Shows(Current(cam),
        Plugin.FlightHud?.Value == true, Orbit?.Value == true, Cockpit?.Value == true, HudView?.Value == true);

    internal static bool CameraAttached(CameraStateManager cam) => HudViewPolicy.AttachesToCamera(Current(cam),
        Plugin.FlightHud?.Value == true, HudView?.Value == true);

    internal static bool ShowingNow => Showing(SceneSingleton<CameraStateManager>.i);

    /// <summary>
    /// The targeting-camera overlay: every HUD view except the cockpit with the Native style,
    /// where the cockpit's own screens already show that feed.
    /// </summary>
    internal static bool TargetingAllowed(CameraStateManager cam) =>
        Showing(cam) && !HudHide.Hidden && ChaseInput.IsAllowed(cam)
        && !(Current(cam) == HudCameraView.Cockpit && !ArcadeHud.CustomHudActive);
}
