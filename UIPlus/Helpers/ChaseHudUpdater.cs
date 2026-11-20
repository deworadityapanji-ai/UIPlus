using UnityEngine;

namespace UIPlus.Helpers;

// CameraStateManager runs at order 2. Finish after it, CombatHUD and other camera/HUD LateUpdates
// (including mods which defer target selection there), and after ArcadeCam's own updater (10000),
// which prepares the weapon camera the targeting screen checks.
[DefaultExecutionOrder(10001)]
internal sealed class ChaseHudUpdater : MonoBehaviour
{
    private void LateUpdate()
    {
        YappinatorCompat.TryInitialize();
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        if (cam != null && cam.currentState == cam.chaseState
            && Plugin.FlightHud.Value && !DynamicMap.mapMaximized)
            HudElements.AttachToCamera(cam);
        else
            HudElements.RestoreAttachments();
        HudElementPositions.Tick(cam);
        // Before the custom HUD, which centres the minimap using the zoomed scale.
        MinimapZoom.Tick();
        ArcadeHud.Tick(cam);
        RwrCompat.Tick(HudViews.Showing(cam) && !DynamicMap.mapMaximized, ArcadeHud.RwrOnMap, ArcadeHud.HudColour);
        // The custom styles show speed, altitude and heading themselves.
        HudElements.FadeHelmet(ArcadeHud.NativeInstrumentsHidden && !DynamicMap.mapMaximized);
        HudDiagnostics.Tick();
        TargetingScreenOverlay.Tick(cam);
    }

    private void OnDestroy()
    {
        HudElementPositions.Restore();
        ArcadeHud.Dispose();
        NoAutopilotCompat.SetHidden(false);
        RwrCompat.Dispose();
        HudElements.FadeHelmet(false);
        HudElements.Dispose();
        TargetingScreenOverlay.Dispose();
    }
}
