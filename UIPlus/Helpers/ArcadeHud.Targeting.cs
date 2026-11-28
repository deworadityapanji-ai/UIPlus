using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// Where the targeting-camera feed goes in each custom style, and the frame around it:
//   Arcade    top right, Ace Combat lock-on window: corner brackets, TGT / name / range header
//   Wingman   top left, the same window leaning like the map
//   Dashboard the strip's right end, as one more section of the strip
//   Thunder   directly under the map, as wide as the map
// The feed itself is drawn by TargetingScreenOverlay into the rectangle given here. Plus and
// Native keep the overlay's own placement (Targeting Screen settings).
internal static partial class ArcadeHud
{
    internal const int TargetItem = 23;
    internal static ConfigEntry<bool> ArcadeTargetingScreen, WingmanTargetingScreen;

    private const float TgtPad = 8f, TgtHeader = 22f;
    private static RectTransform _tgtPanel;
    private static CanvasGroup _tgtGroup;
    private static TextMeshProUGUI _tgtHeader;
    private static Image _tgtBack, _tgtPreview;
    private static readonly System.Collections.Generic.List<GameObject> _tgtBrackets = new();
    private static float _tgtAlpha;
    // The feed's rectangle inside the panel, from the panel's bottom-left corner (reference px).
    private static Rect _tgtFeed;

    private enum TargetSlot { Legacy, Hidden, Slotted }

    private static bool TargetingInStyle =>
        !_plus && !ElementLooks.Get(TargetItem).Hidden && (!(_builtStyle == HudStyle.Arcade) || ArcadeTargetingScreen == null || ArcadeTargetingScreen.Value)
        && (!_wingman || WingmanTargetingScreen == null || WingmanTargetingScreen.Value);

    private static void BuildTargetFrame()
    {
        _tgtPanel = null;
        if (_plus) return;
        Transform parent = _minimal && _strip != null ? _strip : _root.transform;
        _tgtPanel = Rect("TargetingScreen", parent, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(300f, 200f));
        _tgtGroup = _tgtPanel.gameObject.AddComponent<CanvasGroup>();
        _tgtGroup.alpha = 0f;
        _tgtAlpha = 0f;
        _tgtBack = _tgtPanel.gameObject.AddComponent<Image>();
        _tgtBack.raycastTarget = false;
        // Dashboard / Thunder: the strip's or map panel's own backing; Arcade / Wingman: no box.
        _tgtBack.color = _minimal || _thunder ? MapPanelColour() : Color.clear;
        int before = _tgtPanel.childCount;
        Corners(_tgtPanel, 14f, Faded(0.8f));
        _tgtBrackets.Clear();
        for (int i = before; i < _tgtPanel.childCount; i++) _tgtBrackets.Add(_tgtPanel.GetChild(i).gameObject);
        _tgtHeader = Text("Header", _tgtPanel, 13, TextAlignmentOptions.Left);
        _tgtHeader.characterSpacing = 2f;
        _tgtHeader.enableWordWrapping = false;
        // Editor preview of the feed area.
        var preview = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        preview.transform.SetParent(_tgtPanel, false);
        _tgtPreview = preview.GetComponent<Image>();
        _tgtPreview.raycastTarget = false;
        _tgtPreview.color = new Color(0f, 0f, 0f, 0.5f);
    }

    // Size and place the panel for this style and the feed's aspect, then fade it with the feed.
    private static void UpdateTargetFrame(Aircraft aircraft)
    {
        if (_tgtPanel == null) return;
        bool editing = HudLayoutEditor.IsOpen;
        bool showing = TargetingInStyle && Plugin.TargetingScreenEnabled.Value && TargetingScreenOverlay.Showing;
        _tgtAlpha = Mathf.MoveTowards(_tgtAlpha, showing || editing ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        _tgtGroup.alpha = _tgtAlpha;
        _tgtPreview.enabled = editing && !showing;
        if (_tgtAlpha <= 0f && !editing) return;

        float aspect = Mathf.Clamp(TargetingScreenOverlay.LastFeedAspect, 0.8f, 2.4f);
        ElementLooks.Look look = ElementLooks.Get(TargetItem);
        float size = look.Scale;   // the feed is drawn into this rectangle, so it is sized, not scaled
        float unit = 1080f / Mathf.Max(1, Screen.height);
        Vector2 offset = HudElementPositions.PixelOffset(TargetItem) * unit;
        float fw, fh, w, h;
        bool header = !_minimal;
        if (_minimal)
        {
            // A section on the strip's right end, the strip's height.
            h = StripHeight * size;
            fh = h - TgtPad * 2f - 14f;
            fw = fh * aspect;
            w = fw + TgtPad * 2f;
            _tgtPanel.anchorMin = _tgtPanel.anchorMax = new Vector2(1f, 0f);
            _tgtPanel.pivot = Vector2.zero;
            _tgtPanel.anchoredPosition = new Vector2(8f, 0f) + offset;
            if (_analog)
            {
                // Analog's panel is wide: sit on top of its right end instead of beyond it.
                _tgtPanel.anchorMin = _tgtPanel.anchorMax = Vector2.one;
                _tgtPanel.anchoredPosition = new Vector2(-w, 8f) + offset;
            }
        }
        else if (_thunder && MapScreenRect(out Rect map))
        {
            // Under the map, as wide as the map.
            float scale = Mathf.Max(0.0001f, _root.transform.lossyScale.x);
            w = map.width / scale * size;
            fw = w - TgtPad * 2f;
            fh = fw / aspect;
            h = fh + TgtPad * 2f + TgtHeader;
            _tgtPanel.anchorMin = _tgtPanel.anchorMax = Vector2.zero;
            _tgtPanel.pivot = Vector2.zero;
            _tgtPanel.anchoredPosition = new Vector2(map.xMin / scale, map.yMin / scale - 6f - h) + offset;
        }
        else
        {
            // Arcade: top right; Wingman: top left, leaning like the map.
            fw = 340f * size;
            fh = fw / aspect;
            w = fw + TgtPad * 2f;
            h = fh + TgtPad * 2f + TgtHeader;
            bool left = _wingman;
            _tgtPanel.anchorMin = _tgtPanel.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            _tgtPanel.pivot = Vector2.zero;
            _tgtPanel.anchoredPosition = new Vector2(left ? 40f : -30f - w, -40f - h) + offset;
        }
        _tgtPanel.sizeDelta = new Vector2(w, h);
        float leanDegrees = _wingman ? MapLeanDegrees : 0f;
        _tgtPanel.localRotation = Quaternion.Euler(0f, 0f, leanDegrees + look.Rotation);
        // Sized and turned about the centre of where the unchanged panel would be.
        Vector3 before = new(w / Mathf.Max(0.05f, size) * 0.5f, h / Mathf.Max(0.05f, size) * 0.5f, 0f);
        Vector3 after = new(w * 0.5f, h * 0.5f, 0f);
        _tgtPanel.anchoredPosition += (Vector2)(Quaternion.Euler(0f, 0f, leanDegrees) * before - _tgtPanel.localRotation * after);
        _tgtFeed = new Rect(TgtPad, TgtPad, fw, fh);
        // Skin: 0 the style's own, 1 corner brackets, 2 box, 3 feed only.
        bool boxStyle = _minimal || _thunder;
        int skin = look.Skin;
        bool brackets = skin == 0 || skin == 1;
        bool box = skin == 2 || (skin == 0 && boxStyle);
        foreach (GameObject bracket in _tgtBrackets)
            if (bracket != null && bracket.activeSelf != brackets) bracket.SetActive(brackets);
        _tgtBack.color = box ? MapPanelColour() : Color.clear;
        if (_tgtHeader.gameObject.activeSelf != (skin != 3)) _tgtHeader.gameObject.SetActive(skin != 3);
        var previewRect = _tgtPreview.rectTransform;
        previewRect.anchorMin = previewRect.anchorMax = Vector2.zero;
        previewRect.pivot = Vector2.zero;
        previewRect.anchoredPosition = _tgtFeed.position;
        previewRect.sizeDelta = _tgtFeed.size;

        // Header: TGT, target name and range (Dashboard has a small label instead).
        RectTransform hr = _tgtHeader.rectTransform;
        hr.anchorMin = hr.anchorMax = Vector2.zero;
        hr.pivot = Vector2.zero;
        hr.sizeDelta = new Vector2(w - TgtPad * 2f, header ? TgtHeader : 14f);
        hr.anchoredPosition = new Vector2(TgtPad, TgtPad + fh + 2f);
        _tgtHeader.fontSize = header ? 13 : 11;
        _tgtHeader.color = header ? Green : Faded(0.55f);
        _tgtHeader.text = header ? TargetCaption(aircraft) : "TGT";
    }

    private static string TargetCaption(Aircraft aircraft)
    {
        if (HudLayoutEditor.IsOpen && !TargetingScreenOverlay.Showing) return "TGT  ·  TARGETING SCREEN";
        var list = aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
        Unit target = list != null && list.Count > 0 ? list[0] as Unit : null;
        if (target == null) return "TGT";
        string range = UnitConverter.DistanceReading(Vector3.Distance(target.transform.position, aircraft.transform.position));
        return $"TGT  ·  {(target.unitName ?? "").ToUpperInvariant()}  ·  {range}";
    }

    private static bool MapScreenRect(out Rect rect)
    {
        rect = default;
        // The map picture as shown: the map's own box, less the padding its clip trims off.
        // (The panel around it is larger: it also holds the threat tabs and spare margin.)
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null || !map.gameObject.activeInHierarchy) return false;
        var c = new Vector3[4];
        ((RectTransform)map.transform).GetWorldCorners(c);
        float x0 = Mathf.Min(c[0].x, c[2].x), x1 = Mathf.Max(c[0].x, c[2].x);
        float y0 = Mathf.Min(c[0].y, c[2].y), y1 = Mathf.Max(c[0].y, c[2].y);
        RectMask2D clip = map.GetComponent<RectMask2D>();
        if (clip != null && clip.enabled)
        {
            Vector4 pad = clip.padding * map.transform.lossyScale.x;   // left, bottom, right, top
            x0 += pad.x; y0 += pad.y; x1 -= pad.z; y1 -= pad.w;
        }
        rect = new Rect(x0, y0, x1 - x0, y1 - y0);
        return rect.width > 10f;
    }

    /// <summary>
    /// Where the targeting overlay should draw the feed this frame, in GUI coordinates (top-left
    /// origin), with the rotation to apply about <paramref name="pivot"/> (Wingman's lean).
    /// </summary>
    internal static int TargetingSlot(out Rect gui, out Vector2 pivot, out float angle)
    {
        gui = default; pivot = default; angle = 0f;
        if (!CustomHudActive || HudElementPositions.Style.Value == HudStyles.Plus) return (int)TargetSlot.Legacy;
        if (_root == null || !_root.activeInHierarchy || _plus) return (int)TargetSlot.Hidden;   // being rebuilt / hidden
        if (!TargetingInStyle || _tgtPanel == null) return (int)TargetSlot.Hidden;
        float scale = _root.transform.lossyScale.x;
        Vector3 origin = _tgtPanel.position;   // the panel's bottom-left corner (its pivot), screen px
        pivot = new Vector2(origin.x, Screen.height - origin.y);
        gui = new Rect(pivot.x + _tgtFeed.x * scale, pivot.y - (_tgtFeed.y + _tgtFeed.height) * scale,
            _tgtFeed.width * scale, _tgtFeed.height * scale);
        angle = -_tgtPanel.localEulerAngles.z;
        return (int)TargetSlot.Slotted;
    }
}
