using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// Nuclear Option Plus: the game's own HUD left exactly as it is (map, weapon panel, instruments),
// with additions around it: the RWR on the map, the faction / escalation bar, flight warnings,
// combat callouts, a weapon ring under the weapon panel and an always-visible damage silhouette.
// Each addition can be switched off, down to plain vanilla with the RWR on the map.
internal static partial class ArcadeHud
{
    internal const int RingItem = 22;
    internal static ConfigEntry<bool> PlusRwrOnMap, PlusFactionBar, PlusWarnings, PlusWeaponRing, PlusSubtitles, PlusDamageAlways;

    private static bool _plus;
    private static Vector2 RingBase = new(0f, -150f);               // box's top-right corner; follows the weapon panel
    private const float RingSize = 160f, RingInner = 0.6f;          // diameter; inner radius as a share of the outer
    private const float BoxHeight = 196f;
    private static RectTransform _wheel;                            // the ring itself, inside the box (_ring)
    private static Image _ringBox;
    private static float _nextColourCheck;
    private const float SliceGap = 3f;                              // degrees between slices

    private sealed class Slice
    {
        internal Image Back, Reload;
        internal RectTransform Label;
        internal Image Icon;
        internal TextMeshProUGUI Name, Count;
        internal RectTransform Pips;                       // one pip per pylon, between icon and count
        internal readonly List<Image> PipFills = new();
        internal readonly List<Image> PipBacks = new();
    }

    private const float PipSize = 5f, PipGap = 2f;

    // One pip per pylon: loaded ones solid, reloading ones filling up from the bottom.
    private static void UpdatePips(Slice s, bool pylons, List<float> fills, Color ink)
    {
        int count = pylons && fills.Count >= 2 ? Mathf.Min(fills.Count, 8) : 0;
        if (s.PipFills.Count != count)
        {
            foreach (Transform child in s.Pips) Object.Destroy(child.gameObject);
            s.PipFills.Clear(); s.PipBacks.Clear();
            float width = count * PipSize + (count - 1) * PipGap;
            for (int i = 0; i < count; i++)
            {
                Vector2 at = new(-width * 0.5f + PipSize * 0.5f + i * (PipSize + PipGap), 0f);
                RectTransform back = Rect("Pip", s.Pips, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(PipSize, PipSize));
                Fill(back, Color.white);
                RectTransform fill = Rect("Fill", back, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PipSize, PipSize));
                Fill(fill, Color.white);
                Image fillImage = fill.GetComponent<Image>();
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Vertical;
                fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                s.PipBacks.Add(back.GetComponent<Image>());
                s.PipFills.Add(fillImage);
            }
        }
        for (int i = 0; i < count; i++)
        {
            s.PipBacks[i].color = new Color(ink.r, ink.g, ink.b, 0.22f);
            s.PipFills[i].color = ink;
            s.PipFills[i].fillAmount = fills[i];
        }
    }

    private static RectTransform _ring;
    private static Image _ringBorder;
    private static readonly List<Slice> Slices = new();
    private static int _ringCount = -1;
    private static Sprite _weaponRingSprite, _weaponRingBorderSprite;

    /// <summary>The RWR scope goes on the minimap (and the map is centred on the aircraft for it).</summary>
    internal static bool RwrOnMap => NativeInstrumentsHidden
        || (HudElementPositions.Style != null && HudElementPositions.Style.Value == HudStyles.Plus
            && HudViews.ShowingNow && PlusRwrOnMap != null && PlusRwrOnMap.Value);

    private static void BuildPlusLayout()
    {
        if (PlusFactionBar == null || PlusFactionBar.Value) BuildTopBar();
        if (PlusWeaponRing == null || PlusWeaponRing.Value) BuildRing();
    }

    // Per frame, instead of the replacing styles' refresh: the game's HUD stays in charge.
    private static void TickPlus(Aircraft aircraft)
    {
        HideNativeWeaponPanel(false);
        SyncNeutralQolFuel(false);
        // The game applies the player's HUD colour after spawning: follow it (rebuild on change).
        if (_colorSample != null && Time.unscaledTime >= _nextColourCheck)
        {
            _nextColourCheck = Time.unscaledTime + 0.25f;
            Color now = CurrentHudColour();
            if (Mathf.Abs(now.r - _sampledColour.r) + Mathf.Abs(now.g - _sampledColour.g) + Mathf.Abs(now.b - _sampledColour.b) > 0.02f)
                _aircraft = null;
        }
        CheckBorrowedOwners();
        if (BorrowedGauges.Count == 0 && _vector != null && TrayNodes().Count > 0) BorrowVectorGauges();
        if (PlusRwrOnMap == null || PlusRwrOnMap.Value) CentreMinimap(aircraft);
        if (_topBar != null) RefreshTopBar();
        if (_ring != null) RefreshRing(aircraft);
        if (PlusWarnings == null || PlusWarnings.Value) UpdateWarnings(aircraft);
        else { SetWarning(_caution, null, Color.clear, false); SetWarning(_pullUp, null, Color.clear, false); }
        if (PlusDamageAlways == null || PlusDamageAlways.Value) KeepDamageVisible(aircraft);
        else ReleaseDamageVisible();
        UpdateMessages();
        UpdateWeaponPopup(aircraft);
        UpdateKillFeed();
        ApplyOffsets();
    }

    // ---- weapon ring ------------------------------------------------------------------------

    // A hollow ring under the weapon panel, split evenly between the weapons carried (one weapon is
    // the whole ring, two are top and bottom halves, and so on), like the game's own weapon wheel.
    // The selected weapon's slice is lit, the next one the cycle button picks is softly lit, empty
    // ones are dimmed, and a reload fills its slice. The middle stays empty: the weapon panel above
    // already shows the selected weapon and its ammo.
    private static void BuildRing()
    {
        // A box joined to the bottom of the game's weapon panel, in that panel's own background
        // colour, so the ring reads as part of the same corner block.
        _ring = Rect("WeaponRing", _root.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), RingBase, new Vector2(380f, BoxHeight));
        _ringBox = _ring.gameObject.AddComponent<Image>();
        _ringBox.raycastTarget = false;
        _wheel = Rect("Wheel", _ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RingSize, RingSize));
        _weaponRingSprite ??= RingSprite(RingInner, 1f);
        _weaponRingBorderSprite ??= RingSprite(0.97f, 1f);
        Slices.Clear();
        _ringCount = -1;
        var border = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        border.transform.SetParent(_wheel, false);
        var rt = (RectTransform)border.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-6f, -6f); rt.offsetMax = new Vector2(6f, 6f);
        _ringBorder = border.GetComponent<Image>();
        _ringBorder.sprite = _weaponRingBorderSprite;
        _ringBorder.raycastTarget = false;
    }

    private static void LayoutRing(int count)
    {
        foreach (Slice s in Slices)
        {
            if (s.Back != null) Object.Destroy(s.Back.gameObject);
            if (s.Reload != null) Object.Destroy(s.Reload.gameObject);
            if (s.Label != null) Object.Destroy(s.Label.gameObject);
        }
        Slices.Clear();
        _ringCount = count;
        if (count <= 0) return;
        float step = 360f / count;
        float gap = count > 1 ? SliceGap : 0f;
        float radius = RingSize * 0.5f * (1f + RingInner) * 0.5f;   // middle of the rim
        for (int i = 0; i < count; i++)
        {
            // Slice i is centred on i × step, clockwise from the top.
            float start = i * step - step * 0.5f + gap * 0.5f;
            var slice = new Slice
            {
                Back = RingSlice("Slice" + i, start, (step - gap) / 360f),
                Reload = RingSlice("Reload" + i, start, 0f),
            };
            float mid = i * step * Mathf.Deg2Rad;
            slice.Label = Rect("Label" + i, _wheel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(Mathf.Sin(mid), Mathf.Cos(mid)) * radius, new Vector2(60f, 34f));
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(slice.Label, false);
            slice.Icon = icon.GetComponent<Image>();
            slice.Icon.preserveAspect = true;
            slice.Icon.raycastTarget = false;
            Pin(slice.Icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(40f, 16f), new Vector2(0f, 0f));
            slice.Pips = Rect("Pips", slice.Label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -1f), new Vector2(60f, PipSize));
            slice.Name = Text("Name", slice.Label, 11, TextAlignmentOptions.Center);
            Pin(slice.Name.rectTransform, new Vector2(0.5f, 1f), new Vector2(64f, 16f), new Vector2(0f, -2f));
            slice.Count = Text("Count", slice.Label, 12, TextAlignmentOptions.Center);
            Pin(slice.Count.rectTransform, new Vector2(0.5f, 0f), new Vector2(64f, 14f), new Vector2(0f, 0f));
            // The HUD's font material is additive (it only brightens), which makes dark text on the
            // lit slice invisible; these labels use the font's normal material instead.
            if (_font != null) slice.Name.fontSharedMaterial = slice.Count.fontSharedMaterial = _font.material;
            Slices.Add(slice);
        }
        _ringBorder.transform.SetAsFirstSibling();
    }

    private static Image RingSlice(string name, float startDegrees, float fill)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_wheel, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localRotation = Quaternion.Euler(0f, 0f, -startDegrees);   // clockwise from the top
        var image = go.GetComponent<Image>();
        image.sprite = _weaponRingSprite;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Top;
        image.fillClockwise = true;
        image.fillAmount = fill;
        image.raycastTarget = false;
        return image;
    }

    private static void RefreshRing(Aircraft aircraft)
    {
        List<WeaponStation> stations = aircraft.weaponStations;
        int count = stations != null ? stations.Count : 0;
        if (count != _ringCount) LayoutRing(count);
        WeaponStation current = aircraft.weaponManager != null ? aircraft.weaponManager.currentWeaponStation : null;
        int selected = current != null && stations != null ? stations.IndexOf(current) : -1;
        int next = NextStation(stations, selected);
        FollowWeaponPanel(out Color panelColour);
        _ringBox.color = panelColour;
        // Unselected slices: a shade darker than the box; the selected one in the HUD colour.
        Color panel = new(0f, 0f, 0f, 0.45f);
        _ringBorder.color = Faded(0.45f);
        float step = count > 0 ? 360f / count : 360f;
        float gap = count > 1 ? SliceGap : 0f;
        for (int i = 0; i < Slices.Count && i < count; i++)
        {
            Slice s = Slices[i];
            WeaponStation st = stations[i];
            bool empty = Empty(st);
            bool isSelected = i == selected, isNext = i == next;
            s.Back.color = isSelected ? Faded(0.85f) : isNext ? Faded(0.12f) : panel;
            float progress = ReloadsCompat.Progress(i);
            s.Reload.fillAmount = progress >= 0f ? (step - gap) / 360f * Mathf.Clamp01(progress) : 0f;
            s.Reload.color = Faded(0.3f);
            // Dark ink on the lit (selected) slice, HUD colour on the dark ones, grey when empty.
            Color ink = empty ? new Color(0.55f, 0.55f, 0.55f, 0.7f)
                : isSelected ? new Color(0.02f, 0.03f, 0.03f, 1f) : Faded(isNext ? 1f : 0.7f);
            Sprite icon = st.WeaponInfo != null ? Cutout(st.WeaponInfo.weaponIcon) : null;
            s.Icon.sprite = icon;
            s.Icon.enabled = icon != null;
            s.Icon.color = ink;
            s.Name.text = icon == null ? ShortName(st) : "";   // the rim only fits an icon and its count
            s.Name.color = ink;
            s.Name.rectTransform.anchoredPosition = new Vector2(0f, icon != null ? -20f : -6f);
            s.Count.text = st.GetAmmoReadout();
            s.Count.color = ink;
            UpdatePips(s, PylonFills(st, i, PylonScratch), PylonScratch, ink);
        }
    }

    // Sits the box right under the game's weapon panel (its capacitor bar included), as wide as
    // the panel, and borrows the panel's background colour. Follows the panel if it is moved.
    private static FieldInfo _topRightPanel;
    private static float _nextPanelCheck;
    // The weapon panel's dark comes from its artwork (its tint is white), so match it by value.
    private static readonly Color _panelColour = new(0.05f, 0.06f, 0.07f, 0.78f);
    private static Color? _panelColourNow;

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static void FollowWeaponPanel(out Color colour)
    {
        colour = _panelColourNow ?? _panelColour;
        if (Time.unscaledTime < _nextPanelCheck && !HudLayoutEditor.IsOpen) return;
        _nextPanelCheck = Time.unscaledTime + 0.25f;
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        _topRightPanel ??= AccessTools.Field(typeof(CombatHUD), "topRightPanel");
        if (hud == null || !(_topRightPanel?.GetValue(hud) is GameObject panel) || !panel.activeInHierarchy) return;
        // Continue straight down from the capacitor bar: same width, same background graphic.
        float bottom = float.MaxValue, left = float.MaxValue, right = float.MinValue;
        var c = new Vector3[4];
        RectTransform power = FindChild(panel.transform, "PowerPanel") as RectTransform;
        if (power != null && power.gameObject.activeInHierarchy)
        {
            power.GetWorldCorners(c);
            bottom = c[0].y; left = c[0].x; right = c[2].x;
            Image back = power.GetComponent<Image>();
            if (back != null && back.sprite != null && _ringBox.sprite != back.sprite)
            {
                _ringBox.sprite = back.sprite;
                _ringBox.type = back.type;
                _ringBox.pixelsPerUnitMultiplier = back.pixelsPerUnitMultiplier;
            }
            if (back != null) { _panelColourNow = back.color; colour = back.color; }
        }
        else
            foreach (Image image in panel.GetComponentsInChildren<Image>(false))
            {
                if (!image.enabled || image.color.a < 0.05f) continue;
                image.rectTransform.GetWorldCorners(c);
                bottom = Mathf.Min(bottom, c[0].y);
                left = Mathf.Min(left, c[0].x);
                right = Mathf.Max(right, c[2].x);
            }
        if (bottom == float.MaxValue || !(_root.transform is RectTransform root)) return;
        float scale = root.lossyScale.x > 0f ? root.lossyScale.x : 1f;   // screen pixels per reference pixel
        float width = Mathf.Clamp((right - left) / scale, 160f, 600f);
        _ring.sizeDelta = new Vector2(width, BoxHeight);
        RingBase = new Vector2(-(Screen.width - right) / scale, -(Screen.height - bottom) / scale);
    }

    private static bool Empty(WeaponStation st) => st == null || (st.GetAmmoLoaded() <= 0 && st.Ammo <= 0);

    // The station the cycle button selects next: the following one that still has ammo.
    private static int NextStation(List<WeaponStation> stations, int selected)
    {
        if (stations == null || stations.Count < 2) return -1;
        for (int k = 1; k < stations.Count; k++)
        {
            int i = ((selected < 0 ? -1 : selected) + k + stations.Count) % stations.Count;
            if (i != selected && !Empty(stations[i])) return i;
        }
        return -1;
    }

    // A white annulus (inner..outer, as fractions of the radius) with soft edges.
    private static Sprite RingSprite(float inner, float outer)
    {
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float half = size * 0.5f, edge = 1.5f / half;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((outer - r) / edge) * Mathf.Clamp01((r - inner) / edge);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ---- damage silhouette, always faintly visible ------------------------------------------

    // The game fades its damage silhouette out a few seconds after a hit, and undamaged parts are
    // fully transparent. Plus keeps the outline faintly visible and each damaged part tinted by how
    // badly it is hit, so the aircraft's state can be read at a glance. Colours are the game's own.
    private static FieldInfo _sdBackground, _sdParts, _sdTimer, _partImage, _partCondition;
    private static object _keptDisplay;

    private static void KeepDamageVisible(Aircraft aircraft)
    {
        object display = AccessTools.Field(typeof(Aircraft), "statusDisplay")?.GetValue(aircraft);
        if (display == null) return;
        _sdBackground ??= AccessTools.Field(display.GetType(), "aircraftBackground");
        _sdParts ??= AccessTools.Field(display.GetType(), "statusDisplays");
        _sdTimer ??= AccessTools.Field(display.GetType(), "displayTimer");
        _keptDisplay = display;
        if (_sdBackground?.GetValue(display) is Image background && background.color.a < 0.3f)
            background.color = new Color(background.color.r, background.color.g, background.color.b, 0.3f);
        if (!(_sdParts?.GetValue(display) is System.Collections.IList parts)) return;
        foreach (object part in parts)
        {
            if (part == null) continue;
            _partImage ??= AccessTools.Field(part.GetType(), "partImage");
            _partCondition ??= AccessTools.Field(part.GetType(), "displayCondition");
            if (!(_partImage?.GetValue(part) is Image image) || !(_partCondition?.GetValue(part) is float condition)) continue;
            float wanted = (1f - condition) * 0.8f;
            if (image.color.a < wanted) image.color = new Color(image.color.r, image.color.g, image.color.b, wanted);
        }
    }

    // Leaving Plus: if the game's own fade has finished, put the silhouette back to hidden.
    private static void ReleaseDamageVisible()
    {
        object display = _keptDisplay;
        _keptDisplay = null;
        if (display == null || display is Object o && o == null) return;
        if (_sdTimer?.GetValue(display) is float timer && timer > 0f) return;   // the game is showing it anyway
        if (_sdBackground?.GetValue(display) is Image background)
            background.color = new Color(background.color.r, background.color.g, background.color.b, 0f);
        if (!(_sdParts?.GetValue(display) is System.Collections.IList parts)) return;
        foreach (object part in parts)
            if (part != null && _partImage?.GetValue(part) is Image image)
                image.color = new Color(image.color.r, image.color.g, image.color.b, 0f);
    }
}
