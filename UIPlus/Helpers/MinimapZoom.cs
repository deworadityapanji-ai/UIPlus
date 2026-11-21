using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.Globalization;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace UIPlus.Helpers;

// Step zoom for the small map, in every HUD style. Each press of the "UI Plus Minimap Zoom"
// action goes one step wider (and wraps back to the closest), easing smoothly between steps.
//
// The game's map icons already counter-scale themselves against the map image's scale every frame
// (that is how its full-screen map zoom keeps icons a constant size), so zooming is only a matter of
// scaling the map image. Steps are absolute spans, so opening and closing the full-screen map,
// which reparents and rescales the image, cannot make the zoom drift. The full-screen map is left
// entirely to the game.
[HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Maximize))]
internal static class MinimapZoomMaximizePatch
{
    private static void Prefix(DynamicMap __instance)
    {
        MinimapZoom.BeforeMaximize(__instance);
        // The game moves the map to the full-screen view keeping its on-screen turn. Hand it the
        // map as the game laid it out (no lean, no rotating clip), or each opening bakes Wingman's
        // lean into the map: it turned a little more and shrank every time.
        HudElementPositions.Restore();
    }
}

[HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.Minimize))]
internal static class MapMinimizeUnturnPatch
{
    // Back on the HUD: the map itself is never turned (its panel is); clear a turn left from before.
    private static void Postfix(DynamicMap __instance)
    {
        if (__instance != null && __instance.transform.localRotation != Quaternion.identity)
            __instance.transform.localRotation = Quaternion.identity;
    }
}

internal static class MinimapZoom
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<string> StepsKm;

    private static DynamicMap _map;
    private static float _nativeSpan = -1f;      // metres across the minimap at the game's own zoom
    private static int _step = -1;
    private static bool _applied;              // the map image currently carries our zoom
    private static float _scale = -1f;
    private static float _lastChange = -100f;
    private static TextMeshProUGUI _label;
    private static string _stepsText;
    private static readonly List<float> Steps = new();

    internal static void Bind(ConfigFile config)
    {
        Enabled = config.Bind("Minimap", "Zoom", true,
            "Allow zooming the small map with the \"UI Plus Minimap Zoom\" action (bind it in Flight Controls).");
        StepsKm = config.Bind("Minimap", "ZoomStepsKm", "2.5, 5, 10, 20, 30",
            "Minimap widths in kilometres, closest first. Each press of the zoom action goes one step wider, then wraps back to the closest.");
    }

    private static List<float> ParsedSteps()
    {
        if (_stepsText == StepsKm.Value && Steps.Count > 0) return Steps;
        _stepsText = StepsKm.Value;
        Steps.Clear();
        foreach (string part in StepsKm.Value.Split(','))
            if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float km) && km > 0.1f)
                Steps.Add(km * 1000f);
        Steps.Sort();
        if (Steps.Count == 0) Steps.AddRange(new[] { 2500f, 5000f, 10000f, 20000f, 30000f });
        return Steps;
    }

    // Map-image scale that shows `span` metres across the minimap. The game places the image with
    // position = -worldPosition * mapDisplayFactor * imageScale * backgroundScale, in the
    // background's units, and the background's width is what the minimap shows.
    private static float ScaleForSpan(DynamicMap map, float span)
    {
        float width = map.mapBackground.rectTransform.rect.width;
        float bg = Mathf.Max(0.0001f, map.mapBackground.transform.localScale.x);
        return width / Mathf.Max(0.0001f, map.mapDisplayFactor * bg * span);
    }

    private static float SpanForScale(DynamicMap map, float scale) =>
        map.mapBackground.rectTransform.rect.width
        / Mathf.Max(0.0001f, map.mapDisplayFactor * map.mapBackground.transform.localScale.x * scale);

    internal static void Tick()
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map != _map)
        {
            // New scene or map: measure the game's own zoom again before touching anything.
            _map = map;
            _nativeSpan = -1f;
            _step = -1;
            _scale = -1f;
            _label = null;
        }
        if (map == null || map.mapImage == null || map.mapBackground == null || !Enabled.Value || !Plugin.IsActive)
        {
            if (_label != null) _label.gameObject.SetActive(false);
            return;
        }
        // NuclearMinimap has its own minimap zoom: hand the map back and stay out of its way.
        if (HudElementPositions.NuclearMinimapPresent)
        {
            HandBack(map);
            if (_label != null) _label.gameObject.SetActive(false);
            return;
        }
        if (DynamicMap.mapMaximized)
        {
            if (_label != null) _label.gameObject.SetActive(false);
            return;
        }
        // Only while flying: before spawning (and after being shot down) the map belongs to the
        // game's spawn and airbase screens, so it is handed back at the game's own scale.
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        if (hud == null || hud.aircraft == null)
        {
            HandBack(map);
            if (_label != null) _label.gameObject.SetActive(false);
            return;
        }

        Transform image = map.mapImage.transform;
        List<float> steps = ParsedSteps();
        if (_nativeSpan < 0f)
        {
            _nativeSpan = SpanForScale(map, image.localScale.x);
            _step = 0;
            for (int i = 1; i < steps.Count; i++)
                if (Mathf.Abs(Mathf.Log(steps[i] / _nativeSpan)) < Mathf.Abs(Mathf.Log(steps[_step] / _nativeSpan))) _step = i;
            _scale = ScaleForSpan(map, steps[_step]);
            Plugin.Logger.LogInfo($"Minimap zoom: the game's own minimap shows {_nativeSpan / 1000f:F1} km; starting at the {steps[_step] / 1000f:0.#} km step.");
        }
        _step = Mathf.Clamp(_step, 0, steps.Count - 1);

        var player = GameManager.playerInput;
        int action = Plugin.MapZoomActionId;
        if (player != null && action >= 0 && player.GetButtonDown(action))
        {
            _step = (_step + 1) % steps.Count;
            _lastChange = Time.unscaledTime;
        }

        // Ease towards the chosen step.
        float target = ScaleForSpan(map, steps[_step]);
        if (_scale <= 0f) _scale = target;
        _scale = Mathf.Lerp(_scale, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        float previous = image.localScale.x;
        if (Mathf.Abs(previous - _scale) > 0.00001f)
        {
            image.localScale = new Vector3(_scale, _scale, image.localScale.z);
            _applied = true;
            // The game re-sizes map icons a fifth at a time, one batch per frame; after a zoom
            // change that leaves some icons briefly at the wrong size. Re-size them all now.
            if (Mathf.Abs(_scale - previous) > previous * 0.0005f) RefreshIcons(map, _scale);
        }
        // The game centres the small map a fixed 4 km ahead of the aircraft; zoomed in, that puts
        // the aircraft off the map. Keep the same look-ahead, scaled with the zoom, so the aircraft
        // stays at the same spot. (Arcade and Wingman then re-centre on the aircraft itself.)
        CentreAhead(map, steps[_step]);

        UpdateLabel(map, steps[_step]);
    }

    // Put the map image back at the game's own minimap scale. The game opens the full-screen map
    // from whatever scale the image has, so this runs just before it does (see the Maximize patch)
    // and whenever there is no aircraft. The next minimised frame snaps back to the chosen step.
    private static void HandBack(DynamicMap map)
    {
        if (_applied && map != null && map.mapImage != null && _nativeSpan > 0f && !DynamicMap.mapMaximized)
        {
            float native = ScaleForSpan(map, _nativeSpan);
            Transform image = map.mapImage.transform;
            image.localScale = new Vector3(native, native, image.localScale.z);
            RefreshIcons(map, native);
        }
        _applied = false;
        _scale = -1f;
    }

    internal static void BeforeMaximize(DynamicMap map)
    {
        if (map == _map) HandBack(map);
    }

    private static FieldInfo _airbaseIcons;

    private static void RefreshIcons(DynamicMap map, float scale)
    {
        float inverse = 1f / Mathf.Max(0.0001f, scale);
        Transform image = map.mapImage.transform;
        foreach (MapIcon icon in map.mapIcons)
            if (icon != null) Update(icon);
        _airbaseIcons ??= AccessTools.Field(typeof(DynamicMap), "airbaseIconLookup");
        if (_airbaseIcons?.GetValue(map) is IDictionary airbases)
            foreach (object value in airbases.Values)
                if (value is MapIcon icon && icon != null) Update(icon);

        // An icon whose unit has just been destroyed can throw inside the game's own update; skip it
        // (the game removes it itself) rather than lose the rest of this frame's HUD work.
        void Update(MapIcon icon)
        {
            try { icon.UpdateIcon(map.mapDisplayFactor, inverse, image, false); }
            catch (System.NullReferenceException) { }
        }
    }

    // Same as the game's own minimap centring, with the 4 km look-ahead scaled by the zoom.
    private static void CentreAhead(DynamicMap map, float span)
    {
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        if (hud == null || hud.aircraft == null || cam == null) return;
        float ahead = 4000f * (_nativeSpan > 0f ? span / _nativeSpan : 1f);
        Vector3 here = cam.transform.GlobalPosition().AsVector3() * map.mapDisplayFactor;
        Vector3 forward = hud.aircraft.transform.forward;
        forward.y = 0f;
        Vector3 point = here + forward.normalized * map.mapDisplayFactor * ahead;
        Transform image = map.mapImage.transform;
        // Directions in the parent's own frame, so a rotated map panel (Wingman's lean) stays centred.
        Quaternion toLocal = image.parent != null ? Quaternion.Inverse(image.parent.rotation) : Quaternion.identity;
        image.localPosition = (toLocal * image.right * -point.x + toLocal * image.up * -point.z)
                              * image.localScale.x * map.mapBackground.transform.localScale.x;
    }

    // "ZOOM 10 km" in the minimap's top-left corner: bright just after a change, dim otherwise.
    private static void UpdateLabel(DynamicMap map, float span)
    {
        if (_label == null)
        {
            var go = new GameObject("UI Plus Zoom Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(map.transform, false);
            _label = go.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI sample = SceneSingleton<FlightHud>.i != null
                ? SceneSingleton<FlightHud>.i.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (sample != null) { _label.font = sample.font; _label.fontSharedMaterial = sample.fontSharedMaterial; }
            _label.fontSize = 13;
            _label.enableWordWrapping = false;
            _label.raycastTarget = false;
            _label.alignment = TextAlignmentOptions.TopLeft;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8f, -6f);
            rt.sizeDelta = new Vector2(160f, 20f);
        }
        if (!_label.gameObject.activeSelf) _label.gameObject.SetActive(true);
        _label.text = "ZOOM  " + UnitConverter.DistanceReading(span);
        float since = Time.unscaledTime - _lastChange;
        float alpha = since < 2.5f ? 1f : Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((since - 2.5f) / 0.6f));
        Color hud = ArcadeHud.HudColour;
        _label.color = new Color(hud.r, hud.g, hud.b, alpha);
    }
}
