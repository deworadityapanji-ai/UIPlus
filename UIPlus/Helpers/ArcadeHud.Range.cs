using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// Weapon range, read from the game's own missile HUD state (CombatHUD.weaponState when it is a
// HUDMissileState), so the numbers and the SHOOT call are exactly the game's:
//   OUT OF RANGE beyond max range, TOO CLOSE inside min range, OUT OF ARC, TOO SLOW, and SHOOT
//   once the target is inside the no-escape zone (NEZ). The NEZ is only marked when it is
//   meaningfully short of max range (under 90%), as in the game.
// Shown as: Arcade, a range scale on the aircraft side of the throttle tape; Wingman, on the
// divider between the weapon text and the star; Thunder, a RNG line in the data block;
// Dashboard, beside the weapon name.
internal static partial class ArcadeHud
{
    private struct WeaponRange
    {
        internal bool HasTarget, NezShown, Shoot;
        internal float Min, Max, Nez, Target;
        internal string Hint;
    }

    private static FieldInfo _weaponStateField, _rsHidden, _rsMin, _rsMax, _rsNez, _rsTarget, _rsHint;

    private static bool ReadRange(Aircraft aircraft, out WeaponRange range)
    {
        range = default;
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        if (hud == null) return false;
        _weaponStateField ??= AccessTools.Field(typeof(CombatHUD), "weaponState");
        object state = _weaponStateField?.GetValue(hud);
        if (state == null || state.GetType().Name != "HUDMissileState") return false;
        if (_rsMax == null)
        {
            System.Type t = state.GetType();
            _rsHidden = AccessTools.Field(t, "hidden");
            _rsMin = AccessTools.Field(t, "minRange");
            _rsMax = AccessTools.Field(t, "maxRange");
            _rsNez = AccessTools.Field(t, "noEscapeRange");
            _rsTarget = AccessTools.Field(t, "maxTargetDist");
            _rsHint = AccessTools.Field(t, "hint");
            if (_rsMax == null) return false;
        }
        if (_rsHidden?.GetValue(state) is bool hidden && hidden) return false;
        range.Min = _rsMin?.GetValue(state) is float min ? min : 0f;
        range.Max = _rsMax.GetValue(state) is float max ? max : 0f;
        range.Nez = _rsNez?.GetValue(state) is float nez ? nez : 0f;
        if (range.Max <= 1f) return false;
        range.NezShown = range.Nez > 0f && range.Nez < range.Max * 0.9f;
        var targets = aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
        range.HasTarget = targets != null && targets.Count > 0;
        range.Target = range.HasTarget && _rsTarget?.GetValue(state) is float d ? d : 0f;
        if (range.HasTarget && _rsHint?.GetValue(state) is TMP_Text hint && hint.enabled && !string.IsNullOrEmpty(hint.text))
        {
            range.Hint = hint.text;
            range.Shoot = hint.text == "SHOOT";
        }
        return true;
    }

    // ---- the game's own range ladder: hidden where a style shows range itself ------------------

    // The ladder, its MAX / MIN / NEZ marks and texts, the target-distance markers and the hint
    // (SHOOT, OUT OF RANGE...). The "no shoot" mark is left alone; aiming cues live elsewhere.
    private static readonly string[] NativeRangeParts =
    {
        "ladderImages", "maxDistImage", "minDistImage", "distSpanImage", "noEscapeMark",
        "maxRangeText", "minRangeText", "noEscapeRangeText", "targetText", "hint",
        "rMaxTransform", "rMinTransform", "rNETransform", "maxTargetDistTransform",
        "minTargetDistTransform", "targetDistSpan", "avgDistTransform", "outRangeTransform",
    };
    private static readonly List<(CanvasGroup Group, bool Owned)> HiddenRange = new();
    private static Object _hiddenRangeState;

    // Faded out with a transparency group: the game keeps updating the parts (we read its
    // numbers), they are just not drawn. Undone when leaving the style.
    private static void HideNativeRange(bool hide)
    {
        Object state = null;
        if (hide && SceneSingleton<CombatHUD>.i is CombatHUD hud)
        {
            _weaponStateField ??= AccessTools.Field(typeof(CombatHUD), "weaponState");
            state = _weaponStateField?.GetValue(hud) as Object;
            if (state != null && state.GetType().Name != "HUDMissileState") state = null;
        }
        if (state == _hiddenRangeState && (state == null || HiddenRange.Count > 0)) return;
        foreach (var (group, owned) in HiddenRange)
            if (group != null) { if (owned) Object.Destroy(group); else group.alpha = 1f; }
        HiddenRange.Clear();
        _hiddenRangeState = state;
        if (state == null) return;
        var seen = new HashSet<GameObject>();
        void Hide(object part)
        {
            GameObject go = part is Component c ? c.gameObject : part as GameObject;
            if (go == null || !seen.Add(go)) return;
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            bool owned = group == null;
            if (owned) group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            HiddenRange.Add((group, owned));
        }
        foreach (string name in NativeRangeParts)
        {
            object value = AccessTools.Field(state.GetType(), name)?.GetValue(state);
            if (value is System.Collections.IEnumerable many && !(value is Component)) { foreach (object item in many) Hide(item); }
            else Hide(value);
        }
    }

    private static Color AlertColour()
    {
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        return theme != null ? theme.Alert : new Color(1f, 0.25f, 0.2f);
    }

    private static string Km(float metres) => UnitConverter.DistanceReading(metres);

    // ---- range scale on a line (Arcade's throttle tape, Wingman's divider) ---------------------

    private static RectTransform _rangeScale, _rangeMax, _rangeNez, _rangeMin, _rangeNezBand, _rangeCaret;
    private static TextMeshProUGUI _rangeMaxText, _rangeNezText, _rangeCaretText, _rangeHint;
    private static float _rangeX, _rangeDir, _rangeH;

    /// <param name="spine">The line to put the scale on; its bottom is zero range.</param>
    /// <param name="direction">-1: ticks and labels to the left of the line; +1: to the right.</param>
    /// <param name="tickX">Where the ticks start, from the line's centre.</param>
    /// <param name="bandX">Where the NEZ band runs, from the line's centre (0 = on the line).</param>
    private static void BuildRangeScale(RectTransform spine, float height, float direction, float tickX, float bandX)
    {
        _rangeScale = null;
        if (spine == null) return;
        _rangeDir = direction; _rangeX = tickX; _rangeH = height;
        _rangeScale = Rect("RangeScale", spine, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(2f, height));
        _rangeNezBand = Rect("NezBand", _rangeScale, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(bandX, 0f), new Vector2(3f, 0f));
        Fill(_rangeNezBand, Color.white);
        _rangeMax = Tick("Max", 10f, out _rangeMaxText, 11);
        _rangeNez = Tick("Nez", 10f, out _rangeNezText, 11);
        _rangeNezText.text = "NEZ";
        _rangeMin = Tick("Min", 6f, out _, 11);
        _rangeCaret = Tick("Target", 14f, out _rangeCaretText, 14);
        _rangeCaret.sizeDelta = new Vector2(14f, 4f);
        _rangeHint = Text("Hint", _rangeCaret, 16, direction < 0f ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
        _rangeHint.characterSpacing = 4f;
        _rangeHint.enableWordWrapping = false;
        PinBeside(_rangeHint.rectTransform, 140f, 20f, -18f);
        _rangeScale.gameObject.SetActive(false);
    }

    private static RectTransform Tick(string name, float length, out TextMeshProUGUI label, float size)
    {
        RectTransform tick = Rect(name, _rangeScale, new Vector2(0.5f, 0f), new Vector2(_rangeDir < 0f ? 1f : 0f, 0.5f),
            new Vector2(_rangeX, 0f), new Vector2(length, 2f));
        Fill(tick, Color.white);
        label = Text(name + "Label", tick, size, _rangeDir < 0f ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
        label.characterSpacing = 2f;
        label.enableWordWrapping = false;
        PinBeside(label.rectTransform, 110f, size + 4f, 0f);
        return tick;
    }

    // Text on the far side of a tick, away from the line.
    private static void PinBeside(RectTransform text, float width, float height, float y)
    {
        bool left = _rangeDir < 0f;
        text.anchorMin = text.anchorMax = new Vector2(left ? 0f : 1f, 0.5f);
        text.pivot = new Vector2(left ? 1f : 0f, 0.5f);
        text.sizeDelta = new Vector2(width, height);
        text.anchoredPosition = new Vector2(left ? -4f : 4f, y);
    }

    private static void UpdateRangeScale(Aircraft aircraft)
    {
        if (_rangeScale == null) return;
        bool show = ReadRange(aircraft, out WeaponRange r);
        if (_rangeScale.gameObject.activeSelf != show) _rangeScale.gameObject.SetActive(show);
        if (!show) return;
        float h = _rangeH;
        float Y(float metres) => Mathf.Clamp01(metres / r.Max) * h;
        Color ink = Faded(0.8f), alert = AlertColour();
        SetTick(_rangeMax, h, ink);
        _rangeMaxText.text = "MAX " + Km(r.Max);
        _rangeMaxText.color = ink;
        _rangeNez.gameObject.SetActive(r.NezShown);
        _rangeNezBand.gameObject.SetActive(r.NezShown);
        if (r.NezShown)
        {
            SetTick(_rangeNez, Y(r.Nez), alert);
            _rangeNezText.color = alert;
            _rangeNezBand.sizeDelta = new Vector2(3f, Y(r.Nez));
            _rangeNezBand.GetComponent<Image>().color = new Color(alert.r, alert.g, alert.b, 0.55f);
        }
        _rangeMin.gameObject.SetActive(r.Min > r.Max * 0.02f);
        SetTick(_rangeMin, Y(r.Min), Faded(0.5f));

        _rangeCaret.gameObject.SetActive(r.HasTarget);
        if (!r.HasTarget) return;
        bool inNez = r.NezShown ? r.Target < r.Nez : r.Target <= r.Max;
        bool outOfRange = r.Target > r.Max;
        Color caret = outOfRange ? Faded(0.45f) : inNez ? alert : Green;
        SetTick(_rangeCaret, Y(r.Target), caret);
        _rangeCaretText.text = Km(r.Target);
        _rangeCaretText.color = caret;
        bool flash = Mathf.Sin(Time.timeSinceLevelLoad * 12f) > -0.3f;
        _rangeHint.text = r.Hint ?? "";
        _rangeHint.color = r.Shoot ? (flash ? alert : new Color(alert.r, alert.g, alert.b, 0.35f)) : Faded(0.6f);
    }

    private static void SetTick(RectTransform tick, float y, Color colour)
    {
        tick.anchoredPosition = new Vector2(_rangeX, y);
        tick.GetComponent<Image>().color = colour;
    }

    // ---- Thunder and Dashboard: one compact text -------------------------------------------------

    /// <summary>
    /// "RNG 12.4km / 18km   NEZ 6km   SHOOT" (Thunder) or the short form for Dashboard. Null
    /// when the selected weapon has no range display.
    /// </summary>
    private static string RangeText(Aircraft aircraft, bool compact)
    {
        if (!ReadRange(aircraft, out WeaponRange r)) return null;
        string alert = Hex(AlertColour());
        string Dimmed(string s) => $"<alpha=#80>{s}<alpha=#FF>";
        if (!r.HasTarget)
            return compact ? Dimmed("MAX " + Km(r.Max))
                : $"{Dimmed("RNG")} {Dimmed("MAX")} {Km(r.Max)}" + (r.NezShown ? $"   {Dimmed("NEZ")} {Km(r.Nez)}" : "");
        bool inNez = r.NezShown ? r.Target < r.Nez : r.Target <= r.Max;
        bool outOfRange = r.Target > r.Max;
        string distance = outOfRange ? Dimmed(Km(r.Target)) : inNez ? $"<color={alert}>{Km(r.Target)}</color>" : Km(r.Target);
        string hint = string.IsNullOrEmpty(r.Hint) ? "" : r.Shoot ? $"<color={alert}>SHOOT</color>" : Dimmed(r.Hint);
        if (compact) return r.Shoot ? hint : distance;
        return $"{Dimmed("RNG")} {distance} {Dimmed("/ " + Km(r.Max))}"
               + (r.NezShown ? $"   {Dimmed("NEZ")} {Km(r.Nez)}" : "") + (hint.Length > 0 ? "   " + hint : "");
    }
}
