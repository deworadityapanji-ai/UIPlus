using UnityEngine;
using UIPlus.Patches;

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
        bool attach = Plugin.IsActive && HudViews.CameraAttached(cam) && !DynamicMap.mapMaximized;
        bool hudView = attach && HudViews.IsHudView(cam);
        ChaseHud.UpdateHudViewOverrides(hudView);
        if (attach)
            HudElements.AttachToCamera(cam, hudView);
        else
            HudElements.RestoreAttachments();
        HudElementPositions.Tick(cam);
        // Before the custom HUD, which centres the minimap using the zoomed scale.
        bool stockHud = HudViews.HudViewSuppressed(cam);
        MinimapZoom.Tick(stockHud);
        ArcadeHud.Tick(cam);
        RwrCompat.Tick(HudViews.Showing(cam) && !DynamicMap.mapMaximized, ArcadeHud.RwrOnMap, ArcadeHud.HudColour);
        // The custom styles show speed, altitude and heading themselves.
        HudElements.FadeHelmet(ArcadeHud.NativeInstrumentsHidden && !DynamicMap.mapMaximized);
        HudDiagnostics.Tick();
        TargetingScreenOverlay.Tick(cam);
        MissileIndicatorsCompat.Tick(HudElementPositions.Style.Value, !stockHud && Plugin.MatchMissileIndicators.Value);
        HudHide.Apply(stockHud);   // last: everything else has had its say this frame
    }

    private void OnDestroy()
    {
        ChaseHud.UpdateHudViewOverrides(false);
        HudElementPositions.Restore();
        ArcadeHud.Dispose();
        NoAutopilotCompat.SetHidden(false);
        RwrCompat.Dispose();
        MissileIndicatorsCompat.Dispose();
        HudElements.FadeHelmet(false);
        HudElements.Dispose();
        TargetingScreenOverlay.Dispose();
    }
}
