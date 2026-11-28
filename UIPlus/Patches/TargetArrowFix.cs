using HarmonyLib;
using UIPlus.Helpers;
using UnityEngine;

namespace UIPlus.Patches;

// Vanilla bug fix: the HUD's target arrow (and any off-screen marker pinned to the screen edge)
// pointed to the wrong side for a target behind the camera (more than 90 degrees off the nose).
// The game takes the direction from WorldToScreenPoint, and a point behind the camera projects
// mirrored through the screen centre. This is the game's own HUDFunctions.PinToScreenEdge with that
// one correction: behind the camera, the direction is flipped back. Plugin.FixTargetArrow turns it off.
[HarmonyPatch(typeof(HUDFunctions), nameof(HUDFunctions.PinToScreenEdge))]
internal static class TargetArrowFix
{
    private static bool Prefix(Vector3 coords, ref Vector3 rayToScreen, ref float arrowAngle, ref bool __result)
    {
        if (Plugin.FixTargetArrow == null || !Plugin.FixTargetArrow.Value) return true;
        CameraStateManager manager = SceneSingleton<CameraStateManager>.i;
        if (HudViews.HudViewSuppressed(manager)) return true;
        Camera camera = manager != null ? manager.mainCamera : null;
        if (camera == null) return true;

        bool result = false;
        Vector3 to = coords - camera.transform.position;
        rayToScreen = camera.WorldToScreenPoint(coords);
        Vector3 half = new(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        rayToScreen -= half;
        rayToScreen.z = 0f;
        bool behind = Vector3.Angle(manager.transform.forward, to) > 90f;
        if (behind)
        {
            // The correction: undo the mirror of a point behind the camera.
            rayToScreen = -rayToScreen;
            if (rayToScreen.sqrMagnitude < 1f) rayToScreen = Vector3.down;   // dead astern: point down
        }
        arrowAngle = Mathf.Atan2(rayToScreen.y, rayToScreen.x);
        float slope = Mathf.Tan(arrowAngle);
        if (behind || Mathf.Abs(rayToScreen.x) > half.x || Mathf.Abs(rayToScreen.y) > half.y)
        {
            result = true;
            rayToScreen = rayToScreen.x > 0f
                ? new Vector3(half.x, half.x * slope, 0f)
                : new Vector3(-half.x, -half.x * slope, 0f);
            if (rayToScreen.y > half.y) rayToScreen = new Vector3(half.y / slope, half.y, 0f);
            else if (rayToScreen.y < -half.y) rayToScreen = new Vector3(-half.y / slope, -half.y, 0f);
        }
        rayToScreen += half;
        __result = result;
        return false;
    }
}
