using BepInEx.Configuration;

namespace UIPlus.Helpers;

/// <summary>
/// Which camera views UI Plus draws its HUD in. Chase follows the Chase HUD → FlightHud setting
/// (whichever mod drives the chase camera); cockpit and orbit have their own switches. Other
/// views (free / TV / cinematic cameras, the map, menus) never show it.
/// </summary>
internal static class HudViews
{
    internal const string Section = "HUD Views";
    internal static ConfigEntry<bool> Cockpit, Orbit;

    internal static void Bind(ConfigFile config)
    {
        Cockpit = config.Bind(Section, "ShowInCockpit", true,
            "Use the selected HUD style in the cockpit view too. The game's flight HUD is already on there; the custom styles replace its readouts as in chase. Off leaves the cockpit HUD exactly as the game draws it.");
        Orbit = config.Bind(Section, "ShowInOrbit", true,
            "Show the HUD in the game's orbit camera, which normally switches it off. Uses the same HUD style and Chase HUD choices (map, pitch ladder, waterline, instrument groups) as chase view.");
    }

    internal static bool IsCockpit(CameraStateManager cam) => cam != null && cam.currentState == cam.cockpitState;
    internal static bool IsOrbit(CameraStateManager cam) => cam != null && cam.currentState == cam.orbitState;

    /// <summary>True when the camera is in a view where UI Plus should draw.</summary>
    internal static bool Showing(CameraStateManager cam)
    {
        if (cam == null || cam.currentState == null) return false;
        if (cam.currentState == cam.chaseState) return Plugin.FlightHud.Value;
        if (cam.currentState == cam.orbitState) return Orbit != null && Orbit.Value;
        if (cam.currentState == cam.cockpitState) return Cockpit != null && Cockpit.Value;
        return false;
    }

    internal static bool ShowingNow => Showing(SceneSingleton<CameraStateManager>.i);

    /// <summary>
    /// The targeting-camera overlay: every HUD view except the cockpit with the Native style,
    /// where the cockpit's own screens already show that feed.
    /// </summary>
    internal static bool TargetingAllowed(CameraStateManager cam) =>
        Showing(cam) && !HudHide.Hidden && ChaseInput.IsAllowed(cam)
        && !(IsCockpit(cam) && !ArcadeHud.CustomHudActive);
}
