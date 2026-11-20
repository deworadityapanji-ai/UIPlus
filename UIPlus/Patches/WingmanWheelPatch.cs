using System.Collections.Generic;
using UIPlus.Helpers;
using HarmonyLib;

namespace UIPlus.Patches;

// The game's radial-menu control step (stick or buttons). In the Wingman style, while the weapon
// wheel is open, ArcadeHud.WingmanWheel takes the frame so the stick picks the star's pointers.
[HarmonyPatch(typeof(RadialMenuMain), "CheckAction")]
internal static class WingmanWheelPatch
{
    private static bool Prefix(RadialMenuMain __instance, List<RadialMenuAction> allowedActions, string buttonName) =>
        ArcadeHud.WingmanWheel(__instance, allowedActions, buttonName);
}
