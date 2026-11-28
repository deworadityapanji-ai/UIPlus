using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UIPlus.Helpers;

/// <summary>
/// The "UI Plus Hide HUD" binding: one press hides the whole flight HUD (UI Plus's own and the
/// game's flight HUD, combat markers, helmet readouts and minimap), the next brings it back. Unlike
/// the pause menu's HIDE UI it works while flying, for screenshots and clean views. Canvases are
/// only switched off for drawing (Canvas.enabled), so the HUD keeps updating underneath and
/// returns exactly as it was.
/// </summary>
internal static class HudHide
{
    internal static bool Hidden { get; private set; }

    private static readonly FieldInfo HudCanvas = AccessTools.Field(typeof(FlightHud), "canvas");
    private static readonly Dictionary<Canvas, bool> Saved = new();

    // Per frame (UI Plus's Update): read the binding.
    internal static void Poll()
    {
        var player = GameManager.playerInput;
        int action = Plugin.HideHudActionId;
        if (player == null || action < 0 || !player.GetButtonDown(action)) return;
        if (HudLayoutEditor.IsOpen) return;   // the editor needs the HUD
        Hidden = !Hidden;
        if (!Hidden) Restore();
        Plugin.Logger.LogInfo(Hidden ? "HUD hidden (UI Plus Hide HUD)." : "HUD shown again.");
    }

    // Late in the frame (after the game and UI Plus turned things on): keep everything off.
    internal static void Apply(bool stockHud)
    {
        if (stockHud)
        {
            if (Saved.Count != 0) Restore();
            return;
        }
        if (!Hidden) return;
        FlightHud hud = SceneSingleton<FlightHud>.i;
        Hide(hud != null ? HudCanvas?.GetValue(hud) as Canvas : null);
        Hide(ParentCanvas(SceneSingleton<CombatHUD>.i));
        Hide(ParentCanvas(SceneSingleton<HeadMountedDisplay>.i));
        Hide(ParentCanvas(SceneSingleton<DynamicMap>.i));
        Hide(RwrCompat.ScopeCanvas);
    }

    private static Canvas ParentCanvas(Component component)
    {
        for (Transform node = component != null ? component.transform : null; node != null; node = node.parent)
            if (node.GetComponent<Canvas>() is Canvas canvas) return canvas;
        return null;
    }

    private static void Hide(Canvas canvas)
    {
        if (canvas == null) return;
        if (!Saved.ContainsKey(canvas)) Saved[canvas] = canvas.enabled;
        if (canvas.enabled) canvas.enabled = false;
    }

    private static void Restore()
    {
        foreach (var pair in Saved)
            if (pair.Key != null && pair.Key.enabled != pair.Value) pair.Key.enabled = pair.Value;
        Saved.Clear();
    }

    internal static void Reset()
    {
        if (Hidden) Restore();
        Hidden = false;
    }
}
