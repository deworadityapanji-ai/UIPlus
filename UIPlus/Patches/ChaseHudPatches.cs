using System;
using System.Collections.Generic;
using System.Reflection;
using UIPlus.Helpers;
using HarmonyLib;
using UnityEngine;

namespace UIPlus.Patches;

// Keeps the flight HUD showing in chase view, with the per-element chase choices (map, pitch
// ladder, waterline, instrument groups). Works with the game's own chase camera and with
// ArcadeCam's; each mod patches the chase state for its own part only.
internal static class ChaseHud
{
    /// <summary>
    /// Vanilla hides the flight HUD in chase view. <c>EnterState</c> clears <c>showHUD</c>, and
    /// <c>CheckHUD</c> short-circuits on it before ever reaching the branch that would turn the HUD
    /// on; the only thing that sets the flag back is <c>ToggleHUD</c>, reachable solely through a
    /// debug key gated on <c>GameState.Editor</c>. So in an ordinary match third person means no
    /// instruments and no virtual joystick indicator.
    ///
    /// Setting the flag before the method runs hands the decision back to the game's own logic
    /// rather than forcing the canvas on: the HUD appears in the positions it was written for
    /// (Back, Tail, wing roots, Belly) and stays off in the ones where an overlay would be nonsense.
    /// </summary>
    [HarmonyPatch(typeof(CameraChaseState), nameof(CameraChaseState.CheckHUD))]
    static class ChaseCheckHud
    {
        static void Prefix(ref bool ___showHUD)
        {
            if (Plugin.FlightHud.Value && !ArcadeCamBridge.HudViewShowing)
                ___showHUD = true;
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), nameof(CameraChaseState.EnterState))]
    static class ChaseEnter
    {
        static void Postfix() => TargetingScreenOverlay.Hide();
    }

    // A finalizer so it also runs when vanilla LeaveState throws (see ArcadeCam's note on the
    // followingUnit null reference). It returns nothing, so the exception is left as it was.
    [HarmonyPatch(typeof(CameraChaseState), nameof(CameraChaseState.LeaveState))]
    static class ChaseLeave
    {
        static void Finalizer()
        {
            TargetingScreenOverlay.Hide();
            ShowMapWidgets(show: true);
            ApplyElementOverrides(inChase: false);
        }
    }

    // Orbit: vanilla switches the HUD off on entering it. Treated like chase when ShowInOrbit is on.
    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.UpdateState))]
    static class OrbitUpdate
    {
        static void Postfix(CameraStateManager cam)
        {
            if (HudViews.Current(cam) != HudCameraView.Orbit) return;
            if (HudViews.Orbit.Value) { EnforceHudVisibility(true); _orbitShown = true; }
            else if (_orbitShown) { EnforceHudVisibility(false); _orbitShown = false; }
        }
    }

    private static bool _orbitShown;

    [HarmonyPatch(typeof(CameraOrbitState), nameof(CameraOrbitState.LeaveState))]
    static class OrbitLeave
    {
        static void Finalizer()
        {
            if (!_orbitShown) return;
            _orbitShown = false;
            ShowMapWidgets(show: true);
            ApplyElementOverrides(inChase: false);
        }
    }

    // Cockpit: the game's HUD is already on. With a custom style selected, hide the native
    // left/right instrument groups the style replaces, as in chase; nothing else is changed.
    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.UpdateState))]
    static class CockpitUpdate
    {
        static void Postfix(CameraStateManager cam)
        {
            if (HudViews.Current(cam) != HudCameraView.Cockpit) return;
            bool hide = ArcadeHud.NativeInstrumentsHidden && !DynamicMap.mapMaximized;
            if (hide)
            {
                HudElements.Apply(HudAttachment.Hidden, HudAttachment.Hidden, HudAttachment.Aircraft);
                _cockpitHidden = true;
            }
            else if (_cockpitHidden)
            {
                HudElements.ShowAll();
                _cockpitHidden = false;
            }
        }
    }

    private static bool _cockpitHidden;

    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.LeaveState))]
    static class CockpitLeave
    {
        static void Finalizer()
        {
            if (!_cockpitHidden) return;
            _cockpitHidden = false;
            HudElements.ShowAll();
        }
    }

    [HarmonyPatch(typeof(CameraChaseState), nameof(CameraChaseState.UpdateState))]
    static class ChaseUpdate
    {
        static void Postfix(CameraStateManager cam)
        {
            if (HudViews.Current(cam) != HudCameraView.Chase) return;
            EnforceHudVisibility(Plugin.FlightHud.Value);
        }
    }

    [HarmonyPatch(typeof(CameraStateManager), nameof(CameraStateManager.SwitchState))]
    static class RestoreHudViewBeforeSwitch
    {
        // Restore the previous view before the next camera applies its own HUD state.
        static void Prefix() => UpdateHudViewOverrides(false);
    }

    private static readonly Dictionary<GameObject, bool> HudViewObjects = new();
    private static readonly Dictionary<Behaviour, bool> HudViewBehaviours = new();
    private static bool _hudViewOverrides;

    internal static void UpdateHudViewOverrides(bool active)
    {
        if (active)
        {
            _hudViewOverrides = true;
            // ArcadeCam owns the canvas and its map/pause lifecycle in this state.
            ShowMapWidgets(Plugin.Map.Value, hudView: true);
            ApplyElementOverrides(inChase: true, hudView: true);
        }
        else if (_hudViewOverrides)
        {
            _hudViewOverrides = false;
            HudElements.RestoreHudViewVisibility();
            foreach (var saved in HudViewObjects)
                if (saved.Key != null && saved.Key.activeSelf != saved.Value) saved.Key.SetActive(saved.Value);
            foreach (var saved in HudViewBehaviours)
                if (saved.Key != null && saved.Key.enabled != saved.Value) saved.Key.enabled = saved.Value;
            HudViewObjects.Clear();
            HudViewBehaviours.Clear();
        }
    }

    private static void SetActive(GameObject node, bool active, bool hudView)
    {
        if (node == null || node.activeSelf == active) return;
        if (hudView && !HudViewObjects.ContainsKey(node)) HudViewObjects.Add(node, node.activeSelf);
        node.SetActive(active);
    }

    private static void SetEnabled(Behaviour node, bool enabled, bool hudView)
    {
        if (node == null || node.enabled == enabled) return;
        if (hudView && !HudViewBehaviours.ContainsKey(node)) HudViewBehaviours.Add(node, node.enabled);
        node.enabled = enabled;
    }

    /// <summary>
    /// Keeps the HUD in the state the config asks for, checked every frame instead of once on
    /// entry.
    ///
    /// Setting <c>showHUD</c> in <c>CheckHUD</c> is not enough on its own, because that method
    /// only runs when the chase state is entered, while plenty of other code turns the HUD off
    /// afterwards and turns it back on under a cockpit-only condition. Pausing runs
    /// <c>FlightHud.EnableCanvas(false)</c> unconditionally, and the resume path only restores
    /// it <c>if (currentState == cockpitState)</c>. Worse, <c>SwitchState</c> calls
    /// <c>DynamicMap.Minimize()</c> immediately *after* <c>EnterState</c>, and that method ends
    /// with <c>EnableCanvas(aircraft != null &amp;&amp; cameraMode == cockpit)</c> — so the map is
    /// switched off a moment after entering chase had switched it on.
    ///
    /// Rather than chase every one of those paths, and every new one a game update might add,
    /// this repairs the state whenever it finds it wrong. <c>EnableCanvas</c> drags a
    /// <c>RefreshSettings()</c> across five singletons behind it, so it is only called when
    /// something actually needs changing, never once per frame.
    /// </summary>
    internal static void EnforceHudVisibility(bool wanted)
    {
        FlightHud hud = SceneSingleton<FlightHud>.i;
        bool hudActive = hud != null && FlightHudCanvasActive(hud);

        // One set of settings covers both chase cameras. They differ in where they point, not
        // in what the HUD is drawn on, and someone who wants instruments in third person wants
        // them in third person.
        if (!wanted)
        {
            // Vanilla already leaves the HUD off in chase, so there is normally nothing to do —
            // but the mode can be switched on with the HUD already up, and then it has to come
            // down. Only the canvas is touched: the minimap furniture lives under it and goes
            // with it, and its own state is left alone so it returns as the config asks.
            if (hudActive)
                FlightHud.EnableCanvas(enable: false);

            return;
        }

        if (DynamicMap.mapMaximized)
        {
            // Opening the map full screen hides the flight HUD so it is not painted over the
            // map — but `Maximize()` only does that `if (currentState == cockpitState)`. In
            // chase that branch never runs, so the HUD stays up: a translucent grey overlay with
            // the green velocity vector sitting on top of the map. Do what the cockpit does.
            if (hudActive)
                FlightHud.EnableCanvas(enable: false);

            // The player asked for the map, so put its widgets back whatever MapInChase says —
            // that flag is about the minimap in chase, not about a map opened deliberately.
            ShowMapWidgets(true);
            return;
        }

        if (hud != null && !hudActive)
            FlightHud.EnableCanvas(enable: true);

        ShowMapWidgets(Plugin.Map.Value);
        ApplyElementOverrides(inChase: true);
    }

    /// <summary>
    /// Shows or hides the minimap and the chrome drawn around it.
    ///
    /// Switching off the <c>DynamicMap</c> object alone leaves the widget's furniture behind: a
    /// dark plate in the lower left with a small forward-reference marker on it, both of which
    /// live outside that object. The plate is the <c>Image</c> on the panel two levels up, and
    /// the marker is a sibling of the map under the anchor between them — so hiding the anchor
    /// takes the marker and the map together, and disabling just that one Image takes the plate.
    ///
    /// The panel itself has to stay active: it also carries the threat list, and switching it
    /// off would cost the player their missile warnings to tidy up an empty frame.
    ///
    /// Objects are reached by walking up from the map rather than by name, so this survives a
    /// renamed hierarchy as long as the shape of it holds.
    /// </summary>
    internal static void ShowMapWidgets(bool show, bool hudView = false)
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null)
            return;

        Transform anchor = map.transform.parent;
        Transform panel = anchor != null ? anchor.parent : null;

        if (anchor != null) SetActive(anchor.gameObject, show, hudView);

        if (!hudView && show && !map.gameObject.activeSelf)
            DynamicMap.EnableCanvas(enable: true);

        SetPlateVisible(panel, show, hudView);
    }

    /// <summary>
    /// Toggles the panel's own Image without touching its children. Matched on type name and
    /// handled as a <see cref="Behaviour"/> so this does not need a reference to the UI assembly
    /// for a single <c>enabled</c> flag.
    /// </summary>
    private static void SetPlateVisible(Transform panel, bool show, bool hudView)
    {
        if (panel == null)
            return;

        foreach (Component component in panel.GetComponents<Component>())
        {
            if (component is Behaviour behaviour
                && component.GetType().Name == "Image"
                && behaviour.enabled != show)
            {
                SetEnabled(behaviour, show, hudView);
            }
        }
    }

    /// <summary>
    /// Applies the per-element chase overrides, and clears them again when leaving chase so the
    /// cockpit never inherits a setting that was only ever about third person.
    ///
    /// Note what these two cannot do: the pitch ladder's rungs and its horizon line are one
    /// scrolling texture — the game moves a single <c>RawImage</c>'s uvRect with pitch — so
    /// hiding the ladder takes the horizon line with it. Separating them would mean replacing
    /// the texture, not toggling an object.
    /// </summary>
    internal static void ApplyElementOverrides(bool inChase, bool hudView = false)
    {
        FlightHud hud = SceneSingleton<FlightHud>.i;
        if (hud == null)
            return;

        bool showLadder = !inChase || Plugin.PitchLadder.Value;
        if (Field(ref _pitchLadderField, "pitchCompassCenter")?.GetValue(hud) is GameObject ladder
            && ladder.activeSelf != showLadder)
        {
            SetActive(ladder, showLadder, hudView);
        }

        // The weapon HUD states switch the waterline back on whenever the selected weapon
        // changes, so this is re-applied every frame rather than set once.
        bool showWaterline = !inChase
            || (Plugin.Waterline.Value != HudAttachment.Hidden && !ElementLooks.Get(HudElementPositions.Waterline).Hidden);
        if (Field(ref _waterlineField, "waterline")?.GetValue(hud) is Behaviour waterline
            && waterline.enabled != showWaterline)
        {
            SetEnabled(waterline, showWaterline, hudView);
        }

        // Only the Image is switched off, never the GameObject. PlayerAxisControls keeps the
        // virtual joystick's deflection in virtualJoystickPos.transform.localPosition and reads
        // pitch, roll and yaw straight back out of it, so deactivating the object would take the
        // stick away along with its indicator. The game drives the same object through SetActive
        // and its colour and never touches enabled, so there is nothing here to fight over.
        //
        // Reached by reflection and held as Behaviour rather than Image, like the waterline
        // above: the mod does not reference UnityEngine.UI, and enabled is all it needs.
        bool showStick = !inChase || Plugin.VirtualJoystickIndicator.Value;
        if (Field(ref _joystickPosField, "virtualJoystickPos")?.GetValue(hud) is Behaviour knob
            && knob.enabled != showStick)
        {
            SetEnabled(knob, showStick, hudView);
        }

        if (Field(ref _joystickVectorField, "virtualJoystickVector")?.GetValue(hud) is Behaviour vector
            && vector.enabled != showStick)
        {
            SetEnabled(vector, showStick, hudView);
        }

        // The instrument groups are whole objects rather than fields on the HUD, and which ones
        // exist depends on the airframe, so they are resolved by component type rather than named
        // here. See HudElements.
        if (hudView) return; // AttachToCamera applies and records the groups in this view.
        if (inChase)
        {
            HudElements.Apply(
                HudElements.LeftMode,
                HudElements.RightMode,
                Plugin.Compass.Value);
        }
        else
        {
            HudElements.ShowAll();
        }
    }

    private static FieldInfo _pitchLadderField;
    private static FieldInfo _waterlineField;
    private static FieldInfo _joystickPosField;
    private static FieldInfo _joystickVectorField;

    private static FieldInfo Field(ref FieldInfo cache, string name) =>
        cache ??= AccessTools.Field(typeof(FlightHud), name);

    // FlightHud.canvas is private, and there is no public way to ask whether the HUD is showing.
    private static FieldInfo _canvasField;

    private static bool FlightHudCanvasActive(FlightHud hud)
    {
        _canvasField ??= AccessTools.Field(typeof(FlightHud), "canvas");

        // Typed as Component rather than Canvas: only the GameObject is needed, and naming
        // Canvas would drag in a reference to UnityEngine.UIModule for nothing.
        if (_canvasField?.GetValue(hud) is Component canvas)
            return canvas.gameObject.activeSelf;

        // Cannot tell — assume it is fine rather than hammer EnableCanvas every frame.
        return true;
    }

}
