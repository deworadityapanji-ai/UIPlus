using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

/// <summary>
/// Steady HUD, built in from the SteadyHudMarkers mod by musket golfer (with their permission). Three parts,
/// each with its own switch:
///   Steady markers   The game positions target markers (and the rest of the combat HUD) before the
///                    camera has finished moving for the frame, so in third person they trail the
///                    camera by a frame and sway in fast turns. Their update is held back until the
///                    camera has moved, then run with its final pose.
///   Upright centre   Outside the cockpit, the HUD-centre group (weapon symbology, flight-path marker,
///                    pitch ladder) stays upright instead of rolling with the aircraft.
///   Camera compass   Outside the cockpit, the heading ribbon and its readout show where the camera is
///                    looking, not where the nose points.
/// If the standalone SteadyHudMarkers is installed, it is left to do this and none of it is patched.
/// </summary>
internal static class SteadyHud
{
    internal const string StandaloneGuid = "steadyhudmarkers";
    internal static ConfigEntry<bool> Markers, Upright, CameraCompass;
    internal static bool Installed { get; private set; }

    internal static void Bind(ConfigFile config)
    {
        Markers = config.Bind("Steady HUD", "SteadyMarkers", true,
            "Target markers and the rest of the combat HUD are placed with the camera's final position for the frame, so they no longer sway in third person. From SteadyHudMarkers.");
        Upright = config.Bind("Steady HUD", "UprightHudCentre", true,
            "Outside the cockpit, keep the game's HUD-centre group (weapon symbology, flight-path marker, pitch ladder) upright instead of rolling it with the aircraft. From SteadyHudMarkers.");
        CameraCompass = config.Bind("Steady HUD", "CompassFollowsCamera", true,
            "Outside the cockpit, the heading ribbon and its readout show the direction the camera looks instead of the aircraft's nose. From SteadyHudMarkers.");
    }

    /// <summary>Patches the game; called once all mods have loaded (so the standalone can be seen).</summary>
    internal static void Install(Harmony harmony)
    {
        if (Chainloader.PluginInfos.ContainsKey(StandaloneGuid))
        {
            Plugin.Logger.LogInfo("SteadyHudMarkers is installed: it handles the steady HUD, so UI Plus's built-in copy stays off.");
            return;
        }
        int ok = 0;
        ok += InstallSync(harmony) ? 1 : 0;
        ok += Patch(harmony, typeof(FlightHud), "Update", nameof(FlightHudPostfix), nameof(RestoreSlingPose)) ? 1 : 0;
        ok += Patch(harmony, typeof(FuelGauge), "UpdateFuelGauge", nameof(FuelPostfix)) ? 1 : 0;
        ok += Patch(harmony, typeof(ThrottleGauge), "UpdateThrottleDisplay", nameof(ThrottlePostfix)) ? 1 : 0;
        ok += Patch(harmony, typeof(ThreatItem), "AlignNotchIndicator", nameof(NotchPostfix)) ? 1 : 0;
        ok += Patch(harmony, typeof(Bearing), "Refresh", nameof(BearingPostfix)) ? 1 : 0;
        Installed = ok > 0;
        Plugin.Logger.LogInfo($"Steady HUD: {ok}/6 hooks applied.");
    }

    private static bool Patch(Harmony harmony, Type type, string method, string postfix, string prefix = null)
    {
        try
        {
            MethodInfo target = AccessTools.Method(type, method);
            if (target == null) { Plugin.Logger.LogWarning($"Steady HUD: {type.Name}.{method} not found; that part keeps the game's behaviour."); return false; }
            harmony.Patch(target, prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(SteadyHud), prefix)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(SteadyHud), postfix)));
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Steady HUD: could not patch {type.Name}.{method}: {e.Message}");
            return false;
        }
    }

    // Outside the cockpit only.
    private static bool ThirdPerson
    {
        get
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            return HudViews.Current(cam) switch
            {
                HudCameraView.Cockpit => false,
                HudCameraView.HudView => HudViews.HudView?.Value == true,
                _ => cam != null && cam.currentState != null,
            };
        }
    }

    // ---- steady markers: hold the combat HUD's update until the camera has moved ----

    private struct Held { internal MonoBehaviour Target; internal FastInvokeHandler Run; }
    private static readonly List<Held> HeldBack = new(8);
    private static readonly object[] NoArgs = new object[0];
    private static FastInvokeHandler _combatHud, _objectives, _airbase, _radarWarning, _flightHud;
    private static int _heldFrame = -1, _lastFlush = -1000;
    private static bool _inCamera, _replaying;

    private static bool InstallSync(Harmony harmony)
    {
        MethodInfo cameraLate = AccessTools.Method(typeof(CameraStateManager), "LateUpdate");
        MethodInfo originShift = AccessTools.Method(typeof(CameraStateManager), "CheckOriginShift");
        if (cameraLate == null || originShift == null) { Plugin.Logger.LogWarning("Steady HUD: the camera update was not found; markers keep the game's timing."); return false; }
        try
        {
            harmony.Patch(cameraLate, prefix: Hook(nameof(CameraLatePrefix), 800), finalizer: Hook(nameof(CameraLateFinalizer), 0));
            harmony.Patch(originShift, prefix: Hook(nameof(BeforeOriginShift), 800));
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Steady HUD: could not hook the camera update: " + e.Message); return false; }
        _combatHud = HoldBack(harmony, typeof(CombatHUD), "LateUpdate", nameof(CombatHudPrefix));
        _objectives = HoldBack(harmony, typeof(ObjectiveOverlayManager), "Update", nameof(ObjectivesPrefix));
        _airbase = HoldBack(harmony, typeof(AirbaseOverlay), "LateUpdate", nameof(AirbasePrefix));
        _radarWarning = HoldBack(harmony, typeof(RadarWarning), "Update", nameof(RadarWarningPrefix));
        MethodInfo flightHud = AccessTools.Method(typeof(FlightHud), "Update");
        if (flightHud != null) _flightHud = MethodInvoker.GetHandler(flightHud);
        return _combatHud != null;
    }

    private static HarmonyMethod Hook(string name, int priority) =>
        new(AccessTools.Method(typeof(SteadyHud), name)) { priority = priority };

    private static FastInvokeHandler HoldBack(Harmony harmony, Type type, string method, string prefix)
    {
        try
        {
            MethodInfo target = AccessTools.Method(type, method);
            if (target == null) return null;
            harmony.Patch(target, prefix: Hook(prefix, 800));
            return MethodInvoker.GetHandler(target);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Steady HUD: {type.Name}.{method} keeps the game's timing: {e.Message}"); return null; }
    }

    private static bool CombatHudPrefix(CombatHUD __instance) => RunNow(__instance, _combatHud);
    private static bool ObjectivesPrefix(ObjectiveOverlayManager __instance) => RunNow(__instance, _objectives);
    private static bool AirbasePrefix(AirbaseOverlay __instance) => RunNow(__instance, _airbase);
    private static bool RadarWarningPrefix(RadarWarning __instance) => RunNow(__instance, _radarWarning);

    // True runs it now (the game's own timing); false holds it for after the camera.
    private static bool RunNow(MonoBehaviour target, FastInvokeHandler run)
    {
        if (_replaying || run == null || !Markers.Value) return true;
        int frame = Time.frameCount;
        // Only while the camera update ran last frame too (so the held update surely runs this frame).
        if (frame - _lastFlush != 1) return true;
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        if (cam == null || !cam.isActiveAndEnabled || HudViews.HudViewSuppressed(cam)) return true;
        if (_heldFrame != frame) { HeldBack.Clear(); _heldFrame = frame; }
        HeldBack.Add(new Held { Target = target, Run = run });
        return false;
    }

    private static void CameraLatePrefix() => _inCamera = true;

    // A floating-origin shift moves everything: run the held updates first, against the pose they belong to.
    private static void BeforeOriginShift() { if (_inCamera) Flush(); }

    private static void CameraLateFinalizer()
    {
        _inCamera = false;
        Flush();
    }

    private static void Flush()
    {
        int frame = Time.frameCount;
        if (_lastFlush == frame) return;
        _lastFlush = frame;
        _replaying = true;
        try
        {
            // The flight-path marker and HUD centre lag the same way: place them again too.
            if (Markers.Value && _flightHud != null && SceneSingleton<FlightHud>.i is FlightHud hud && hud.isActiveAndEnabled
                && !HudViews.HudViewSuppressed(SceneSingleton<CameraStateManager>.i))
                Run(hud, _flightHud);
            if (_heldFrame != frame) return;
            foreach (Held held in HeldBack)
                if (held.Target != null && held.Target.isActiveAndEnabled) Run(held.Target, held.Run);
        }
        finally
        {
            HeldBack.Clear();
            _replaying = false;
        }
    }

    private static void Run(MonoBehaviour target, FastInvokeHandler run)
    {
        try { run(target, NoArgs); }
        catch (Exception e) { Debug.LogException(e, target); }
    }

    // ---- upright HUD centre and camera compass ----

    private static readonly FieldInfo PitchLadder = AccessTools.Field(typeof(FlightHud), "pitchCompassCenter");
    private static readonly FieldInfo Ribbon = AccessTools.Field(typeof(FlightHud), "compass");
    private static readonly FieldInfo FuelReading = AccessTools.Field(typeof(FuelGauge), "fuelReading");
    private static readonly FieldInfo ThrottleReading = AccessTools.Field(typeof(ThrottleGauge), "throttleReading");
    private static readonly FieldInfo Notch = AccessTools.Field(typeof(ThreatItem), "notchIndicator");
    private static readonly FieldInfo BearingText = AccessTools.Field(typeof(Bearing), "bearing");
    private static readonly FieldInfo WeaponState = AccessTools.Field(typeof(CombatHUD), "weaponState");
    private static Transform _adjustedSling;
    private static Vector3 _slingLocalPosition;
    private static Quaternion _slingLocalRotation;
    private static Transform _stockSling;
    private static int _stockSlingFrame = -1;
    private static Vector3 _stockSlingPosition;
    private static Quaternion _stockSlingRotation;

    // Restore the prefab pose before the next native update (also when Steady HUD replays it).
    private static void RestoreSlingPose()
    {
        if (_adjustedSling != null)
        {
            _adjustedSling.localPosition = _slingLocalPosition;
            _adjustedSling.localRotation = _slingLocalRotation;
        }
        _adjustedSling = null;
    }

    private static void FlightHudPostfix(FlightHud __instance)
    {
        if (!ThirdPerson) return;
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        Transform centre = __instance.GetHUDCenter();
        bool upright = Upright.Value && centre != null;
        Transform sling = SlingUnder(centre);
        Vector3 slingPosition = default;
        Quaternion slingRotation = default;
        if (sling != null)
        {
            sling.GetPositionAndRotation(out slingPosition, out slingRotation);
            if (!_replaying)
            {
                _stockSling = sling;
                _stockSlingFrame = Time.frameCount;
                _stockSlingPosition = slingPosition;
                _stockSlingRotation = slingRotation;
            }

            bool keepStockFrame = _replaying && _stockSlingFrame == Time.frameCount && _stockSling == sling;
            if (upright || keepStockFrame)
            {
                // The sling layout represents real positions and angles. Preserve the pose
                // from the game's normal update while the rest of the centre is steadied.
                _adjustedSling = sling;
                _slingLocalPosition = sling.localPosition;
                _slingLocalRotation = sling.localRotation;
                if (keepStockFrame)
                {
                    slingPosition = _stockSlingPosition;
                    slingRotation = _stockSlingRotation;
                }
            }
        }
        if (upright)
        {
            // Turn the centre upright, keeping the flight-path marker where the game put it.
            Transform marker = __instance.velocityVector != null ? __instance.velocityVector.transform : null;
            bool keep = marker != null && marker != centre && marker.IsChildOf(centre);
            Vector3 at = keep ? marker.position : Vector3.zero;
            centre.rotation = Quaternion.identity;
            if (keep) marker.position = at;
            if (PitchLadder?.GetValue(__instance) is GameObject ladder && cam != null && cam.mainCamera != null
                && ladder.transform.IsChildOf(centre))
                ladder.transform.eulerAngles = new Vector3(0f, 0f, -cam.mainCamera.transform.eulerAngles.z);
        }
        if (_adjustedSling != null) _adjustedSling.SetPositionAndRotation(slingPosition, slingRotation);
        if (CameraCompass.Value && Ribbon?.GetValue(__instance) is RawImage ribbon && CameraHeading(out float heading))
            ribbon.uvRect = new Rect((heading + 135f) / 360f, 0f, 0.25f, 1f);
    }

    private static Transform SlingUnder(Transform centre)
    {
        CombatHUD combat = SceneSingleton<CombatHUD>.i;
        return centre != null && combat != null && WeaponState?.GetValue(combat) is HUDSlingState sling &&
            sling != null && sling.transform.IsChildOf(centre) ? sling.transform : null;
    }

    private static void FuelPostfix(FuelGauge __instance) => AlignReadout(__instance, FuelReading);
    private static void ThrottlePostfix(ThrottleGauge __instance) => AlignReadout(__instance, ThrottleReading);

    // Readouts the game turns to match their gauge: keep them in line with the (upright) centre.
    private static void AlignReadout(Component gauge, FieldInfo field)
    {
        if (!Upright.Value || !ThirdPerson || field?.GetValue(gauge) is not Component readout) return;
        Transform centre = SceneSingleton<FlightHud>.i != null ? SceneSingleton<FlightHud>.i.GetHUDCenter() : null;
        readout.transform.rotation = centre != null && gauge.transform.IsChildOf(centre) ? centre.rotation : gauge.transform.rotation;
    }

    // The missile notch indicator: turned by the camera's roll, not the aircraft's.
    private static void NotchPostfix(ThreatItem __instance)
    {
        if (!Upright.Value || !ThirdPerson || Notch?.GetValue(__instance) is not GameObject notch) return;
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        if (hud == null || hud.aircraft == null || cam == null || cam.mainCamera == null) return;
        Vector3 angles = notch.transform.eulerAngles;
        angles.z += hud.aircraft.transform.eulerAngles.z - cam.mainCamera.transform.eulerAngles.z;
        notch.transform.eulerAngles = angles;
    }

    private static void BearingPostfix(Bearing __instance)
    {
        if (!CameraCompass.Value || !ThirdPerson || BearingText?.GetValue(__instance) is not TMP_Text text) return;
        if (CameraHeading(out float heading)) text.text = heading.ToString("F0", CultureInfo.CurrentCulture) + "°";
    }

    // The camera's compass heading (looking straight up or down: from its up vector instead).
    private static bool CameraHeading(out float heading)
    {
        heading = 0f;
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        if (cam == null || cam.mainCamera == null) return false;
        Transform view = cam.mainCamera.transform;
        Vector2 flat = new(view.forward.x, view.forward.z);
        if (flat.sqrMagnitude < 0.0001f)
        {
            Vector3 up = view.forward.y > 0f ? -view.up : view.up;
            flat = new Vector2(up.x, up.z);
            if (flat.sqrMagnitude < 0.000001f) return false;
        }
        heading = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
        if (heading < 0f) heading += 360f;
        return true;
    }
}
