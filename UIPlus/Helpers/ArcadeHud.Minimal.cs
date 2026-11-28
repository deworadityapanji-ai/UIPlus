using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// "Dashboard" style: one instrument strip along the bottom centre (inspired by an unreleased FCS
// test HUD, rebuilt for play with its author's permission). Flight to the left of the attitude
// ball, combat to the right:
//   RWR | THR+FUEL bars | SPD, ALT (big) | attitude ball + AOA | weapon, ammo (big), reloads, CM |
//   NEXT | damage silhouette | the aircraft's extra gauges (nozzle, wing, IR), when it has any
// Three levels: big numbers to glance at (speed, altitude, ammo), medium details beside them, and
// small dim labels. Warnings, subtitles, banner, status tags and the map are shared.
internal static partial class ArcadeHud
{
    internal const int StripItem = 19;

    private static bool _minimal;
    private static RectTransform _strip, _rwrSlot, _alertSlot, _horizon, _aoaPointer;
    private static Vector2 StripBase = new(0f, 12f);
    private static TextMeshProUGUI _wpnName, _wpnCount, _cmLine, _nextList, _spdSub, _altSub, _aoaLine, _wpnRange;
    private const float StripWidth = 1070f, BallSize = 118f, PitchScale = 2.4f;
    private static float StripHeight => _analog ? AnalogHeight : 150f;
    private const float AoaTapeHeight = 96f, AoaMin = -5f, AoaMax = 25f;
    private const float BallCentreX = 600f;   // the attitude ball; the strip is pinned here at screen centre
    private const float BigText = 30f, MidText = 14f;
    private static float _reloadBarHeight = 64f;

    // Where the RWR scope goes in this style (screen pixels), for the RWR integration.
    internal static bool RwrSlot(out Vector2 centre, out float size, out float stripTop)
    {
        centre = default; size = 0f; stripTop = 0f;
        if (!_minimal || _rwrSlot == null || _strip == null || _root == null || !_root.activeInHierarchy) return false;
        var c = new Vector3[4];
        _rwrSlot.GetWorldCorners(c);
        Vector2 lo = RectTransformUtility.WorldToScreenPoint(null, c[0]);
        Vector2 hi = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        centre = (lo + hi) * 0.5f;
        size = _analog ? Vector3.Distance(c[0], c[3]) : Mathf.Abs(hi.x - lo.x);
        _strip.GetWorldCorners(c);
        stripTop = RectTransformUtility.WorldToScreenPoint(null, c[1]).y;
        return size > 10f;
    }

    // Where the RWR alert boxes go: a column beside the scope (screen pixels).
    internal static bool RwrAlertSlot(out Vector2 centre, out float height)
    {
        centre = default; height = 0f;
        if (!_minimal || _alertSlot == null || _root == null || !_root.activeInHierarchy) return false;
        var c = new Vector3[4];
        _alertSlot.GetWorldCorners(c);
        Vector2 lo = RectTransformUtility.WorldToScreenPoint(null, c[0]);
        Vector2 hi = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        centre = (lo + hi) * 0.5f;
        height = Mathf.Abs(hi.y - lo.y);
        return height > 10f;
    }

    // ---- small builders ---------------------------------------------------------------------

    private static TextMeshProUGUI StripText(string name, float size, Vector2 at, float width, string text = "",
        TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        TextMeshProUGUI t = Text(name, _strip, size, align);
        t.text = text;
        t.enableWordWrapping = false;
        Pin(t.rectTransform, new Vector2(0f, 1f), new Vector2(width, size + 6f), at);
        return t;
    }

    private static TextMeshProUGUI StripLabel(string text, float x, float y)
    {
        TextMeshProUGUI t = StripText("Label", 11, new Vector2(x, y), 60f, text);
        t.characterSpacing = 3f;
        t.color = Faded(0.55f);
        return t;
    }

    private static void Divider(float x)
    {
        Fill(Rect("Divider", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -14f), new Vector2(1f, StripHeight - 28f)),
            Faded(0.15f));
    }

    // Dummy text for readouts the other styles show, which Minimal lays out itself.
    private static TextMeshProUGUI Hidden(string name)
    {
        TextMeshProUGUI t = Text(name, _strip, 10, TextAlignmentOptions.Left);
        t.gameObject.SetActive(false);
        return t;
    }

    // ---- layout ----------------------------------------------------------------------------

    private static void BuildMinimalLayout()
    {
        _strip = Rect("Strip", _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), StripBase,
            new Vector2(StripWidth, StripHeight));
        Image back = _strip.gameObject.AddComponent<Image>();
        // Same darkness as the game's own map panel, so the map and the strip read as one set.
        back.color = MapPanelColour();
        back.raycastTarget = false;
        Corners(_strip, 16f, Faded(0.8f));

        // RWR: a compact scope (threats appear on it when you are painted), its alert tabs in a
        // column beside it with room for their full width.
        int rwrFrom = _strip.childCount;
        StripLabel("RWR", 14f, -8f);
        _rwrSlot = Rect("RwrSlot", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(62f, -78f),
            new Vector2(100f, 100f));
        _alertSlot = Rect("RwrAlerts", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(152f, -76f),
            new Vector2(64f, 116f));
        Divider(196f);
        int rwrTo = _strip.childCount;

        // Throttle and fuel as two vertical bars, values underneath.
        StripLabel("THR", 208f, -10f);
        StripLabel("FUEL", _hasPusher ? 260f : 252f, -10f);
        _throttle = VBar(new Vector2(224f, -28f), 86f, "Throttle");
        _fuel = VBar(new Vector2(_hasPusher ? 276f : 268f, -28f), 86f, "Fuel");
        if (_hasPusher)
        {
            BuildPusherBar(_strip, new Vector2(0f, 1f), new Vector2(248f, -28f - _throttle.Length * 0.5f),
                _throttle.Length, _throttle.Thickness, false, Faded(0.15f));
            TextMeshProUGUI prop = StripText("Pusher label", 9f, new Vector2(236f, -10f), 24f, "PROP",
                TextAlignmentOptions.Center);
            prop.color = Faded(0.75f);
        }
        Divider(298f);

        // Air data: speed and altitude big, each with its details underneath.
        StripLabel("SPD", 310f, -8f);
        _speedValue = StripText("Speed", BigText, new Vector2(310f, -18f), 218f);
        _spdSub = StripText("SpeedDetail", MidText, new Vector2(310f, -54f), 218f);
        StripLabel("ALT", 310f, -76f);
        _altValue = StripText("Altitude", BigText, new Vector2(310f, -86f), 218f);
        _altSub = StripText("AltDetail", MidText, new Vector2(310f, -122f), 218f);
        _speedExtra = Hidden("FlightDetail");
        _altExtra = Hidden("AltDetail");
        Divider(530f);

        // Attitude ball at the screen centre (see FitStripToTray), AOA tape hugging it, and AOA with
        // the turn rate underneath.
        BuildBall(new Vector2(BallCentreX, -66f));
        BuildAoaTape(new Vector2(672f, -18f));
        _aoaLine = StripText("AOA", 13, new Vector2(BallCentreX - 85f, -128f), 170f, "", TextAlignmentOptions.Center);
        Divider(692f);

        // Weapon: name, ammo big, reload bars (one per pylon) and countermeasures beside them.
        StripLabel("WPN", 712f, -8f);
        _wpnName = StripText("Weapon", 16, new Vector2(712f, -20f), 156f);
        _wpnCount = StripText("Ammo", BigText, new Vector2(712f, -42f), 156f);
        // Range on the name's row, right-aligned: target distance, SHOOT inside the NEZ.
        _wpnRange = StripText("Range", 13, new Vector2(712f, -22f), 156f, "", TextAlignmentOptions.Right);
        _reloadBarHeight = 42f;
        SetupReloadBars(_strip, new Vector2(720f, -132f), 36f, 8f);
        _cmLine = StripText("Countermeasures", 13, new Vector2(764f, -94f), 112f);
        if (_builtCapacitorBar)
            _weaponCap = BuildCapacitorBar(_strip, new Vector2(0f, 1f), new Vector2(764f, -114f), 112f, 26f, 11f);
        else _cmLine.lineSpacing = 8f;
        Divider(880f);

        // The next weapons in cycle order (below their label, not on it).
        StripLabel("NEXT", 892f, -8f);
        _nextList = StripText("Next", 13, new Vector2(892f, -34f), 98f);
        _nextList.lineSpacing = 16f;
        Divider(996f);

        // Failed systems (ENG FIRE...) under the strip's damage silhouette: the strip is the one
        // place to read the aircraft's state; the game's own silhouette stays hidden.
        _damageReport = StripText("Failures", 11, new Vector2(DamageBase.x - 64f, -133f), 128f, "", TextAlignmentOptions.Center);
        _damageReport.enableWordWrapping = true;

        TrimRwrSection(rwrFrom, rwrTo, RwrSection, false);
    }

    // RWR moved off the strip (in the editor), or (Analog) no RWR mod: drop its section and close
    // the gap. The scope's slot stays where it was (unseen), so the scope keeps its place plus your
    // offset.
    private static void TrimRwrSection(int rwrFrom, int rwrTo, float section, bool always)
    {
        _builtRwrAway = RwrAway;
        _stripTrim = 0f;
        if (_builtRwrAway || always)
        {
            _stripTrim = section;
            for (int i = 0; i < _strip.childCount; i++)
            {
                var child = (RectTransform)_strip.GetChild(i);
                if (i >= rwrFrom && i < rwrTo && child != _rwrSlot && child != _alertSlot && !(_analog && child == _rwrBezel && RwrCompat.Active))
                {
                    child.gameObject.SetActive(false);
                    continue;
                }
                // Everything after the section (and the unseen slots, which keep their screen
                // place) moves left; the strip's left edge moves right by the same amount.
                if (i >= rwrFrom) child.anchoredPosition -= new Vector2(_stripTrim, 0f);
            }
        }
    }

    private const float RwrSection = 196f;
    private static float _stripTrim;

    // The RWR scope was moved away from its place in the strip.
    private static bool RwrAway => RwrCompat.Active && HudElementPositions.Moved(HudElementPositions.Rwr);

    // ---- map docked to the strip's left end ----------------------------------------------------

    /// <summary>Extra screen-pixel shift for the minimap, applied by HudElementPositions.</summary>
    internal static Vector2 MapDock;

    // Dashboard: the minimap sits against the strip's left end, bottoms aligned, so the two read as
    // one unit. Only while the map is at its default spot (moving it in the editor turns this off).
    internal static bool MapDocked => _minimal && _strip != null && !DynamicMap.mapMaximized
        && (!_analog || ElementLooks.SkinOf(StripItem) == 0)
        && !ElementLooks.Get(StripItem).Hidden && !ElementLooks.Get(HudElementPositions.Map).Hidden
        && (DashboardDockMap == null || DashboardDockMap.Value)
        && Mathf.Abs(HudElementPositions.Offset(HudElementPositions.Map, true)) < 0.001f
        && Mathf.Abs(HudElementPositions.Offset(HudElementPositions.Map, false)) < 0.001f;

    internal static bool MovesWithStrip(int id) => _minimal &&
        (id >= HudElementPositions.AnalogEngine || id == HudElementPositions.Rwr || id == HudElementPositions.RwrAlerts
        || id == HudElementPositions.VectorGauges || id == HudElementPositions.Damage
        || (id == HudElementPositions.Map && MapDocked));

    private static void DockMap()
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        RectTransform panel = map != null && map.hudMapAnchor != null ? map.hudMapAnchor.parent as RectTransform : null;
        if (!MapDocked || panel == null) { MapDock = Vector2.zero; return; }
        var c = new Vector3[4];
        if (_analog && HudElementPositions.TryGetScreenRect(HudElementPositions.Map, out Rect mapRect))
        {
            _strip.GetWorldCorners(c);
            Rect strip = HudElementPositions.ToGui(c);
            MapDock += new Vector2(strip.xMin - mapRect.xMax, mapRect.yMax - strip.yMax);
            return;
        }
        panel.GetWorldCorners(c);
        float right = Mathf.Max(c[0].x, c[2].x);
        // Bottoms: the map picture's, plus the art's border, on the strip's (the panel itself
        // reaches further down than its art, which left the map floating above the strip).
        float unit = Screen.height / 1080f;
        ((RectTransform)map.transform).GetWorldCorners(c);
        float bottom = Mathf.Min(c[0].y, c[2].y) - 10f * unit;
        _strip.GetWorldCorners(c);
        float gap = 10f * unit;
        // Corrects from where the map is now (this frame's dock already applied): no build-up.
        MapDock += new Vector2(c[0].x - gap - right, c[0].y - bottom);
    }

    // "1148km/h" -> big number, small dim unit, so the digits carry the weight.
    private static string BigUnits(string reading)
    {
        if (string.IsNullOrEmpty(reading)) return reading;
        int i = reading.Length;
        while (i > 0 && !char.IsDigit(reading[i - 1])) i--;
        if (i <= 0 || i == reading.Length) return reading;
        return reading.Substring(0, i) + "<size=50%><alpha=#99> " + reading.Substring(i) + "<alpha=#FF></size>";
    }

    // The game's map panel art, sampled once: its colour times the panel's tint. Falls back to a
    // plain dark tone if the art cannot be read.
    private static Color MapPanelColour()
    {
        Color fallback = new(0f, 0f, 0f, 0.62f);
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        Image panel = map != null && map.hudMapAnchor != null ? map.hudMapAnchor.parent.GetComponent<Image>() : null;
        if (panel == null || panel.sprite == null) return fallback;
        try
        {
            Sprite sprite = panel.sprite;
            Texture2D texture = sprite.texture;
            Rect r = sprite.textureRect;
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            // Sample the middle of the panel art, away from its slanted edge.
            probe.ReadPixels(new Rect(r.x + r.width * 0.55f, r.y + r.height * 0.45f, 1f, 1f), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color art = probe.GetPixel(0, 0);
            Object.Destroy(probe);
            Color c = art * panel.color;
            Plugin.Logger.LogInfo($"Arcade HUD: map panel colour sampled as {c}");
            return c.a > 0.05f ? c : fallback;
        }
        catch (System.Exception e)
        {
            Plugin.Logger.LogWarning("Arcade HUD: could not sample the map panel colour: " + e.Message);
            return fallback;
        }
    }

    // Green corner brackets, as on the reference strip.
    private static void Corners(RectTransform r, float length, Color colour)
    {
        foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
        {
            Fill(Rect("CornerH", r, corner, new Vector2(corner.x, 0.5f), Vector2.zero, new Vector2(length, 2f)), colour);
            Fill(Rect("CornerV", r, corner, new Vector2(0.5f, corner.y), Vector2.zero, new Vector2(2f, length)), colour);
        }
    }

    // A vertical bar with its value centred underneath.
    private static Gauge VBar(Vector2 at, float length, string name)
    {
        RectTransform track = Rect(name + "Bar", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 1f), at, new Vector2(14f, length));
        Fill(track, Faded(0.15f));
        RectTransform fill = Rect("Fill", track, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(14f, 0f));
        Image image = fill.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        RectTransform level = Rect("Level", track, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 2f));
        Fill(level, Green);
        TextMeshProUGUI value = StripText(name + "Value", 12, new Vector2(at.x - 26f, at.y - length - 4f), 52f, "",
            TextAlignmentOptions.Center);
        return new Gauge { Fill = fill, FillImage = image, Level = level, Percent = value, Length = length, Thickness = 14f };
    }

    // ---- AOA tape --------------------------------------------------------------------------------

    private static void BuildAoaTape(Vector2 at)
    {
        RectTransform tape = Rect("AoaTape", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 1f), at, new Vector2(2f, AoaTapeHeight));
        Fill(tape, Faded(0.6f));
        for (int deg = (int)AoaMin; deg <= (int)AoaMax; deg += 5)
        {
            float y = (deg - AoaMin) / (AoaMax - AoaMin) * AoaTapeHeight;
            bool major = deg % 10 == 0;
            Fill(Rect("Tick", tape, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(1f, y),
                new Vector2(major ? 8f : 5f, 1.5f)), Faded(major ? 0.7f : 0.45f));
        }
        _aoaPointer = Rect("Pointer", tape, new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(10f, 3f));
        Fill(_aoaPointer, Green);
    }

    // ---- attitude ball --------------------------------------------------------------------------

    // A ring with roll ticks across the top; inside a circular clip, a horizon line and pitch ladder
    // that roll and pitch behind a fixed aircraft symbol.
    private static void BuildBall(Vector2 at)
    {
        RectTransform ball = Rect("Attitude", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), at, new Vector2(BallSize, BallSize));
        Image ring = ball.gameObject.AddComponent<Image>();
        ring.sprite = CircleSprite(false);
        ring.color = Faded(0.85f);
        ring.raycastTarget = false;
        foreach (int deg in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
        {
            bool major = deg % 30 == 0;
            RectTransform spoke = Rect("RollTick", ball, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            spoke.localRotation = Quaternion.Euler(0f, 0f, -deg);
            Fill(Rect("Tick", spoke, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, BallSize * 0.5f - 2f),
                new Vector2(1.5f, major ? 9f : 5f)), Faded(major ? 0.85f : 0.55f));
        }

        RectTransform clip = Rect("Clip", ball, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(BallSize - 14f, BallSize - 14f));
        Image disc = clip.gameObject.AddComponent<Image>();
        disc.sprite = CircleSprite(true);
        disc.raycastTarget = false;
        clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        _horizon = Rect("Horizon", clip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Fill(Rect("Line", _horizon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 2f)), Faded(0.9f));
        foreach (int deg in new[] { -20, -10, 10, 20 })
        {
            float w = deg % 20 == 0 ? 15f : 10f;
            Color c = Faded(deg > 0 ? 0.65f : 0.45f);
            Fill(Rect("PitchL", _horizon, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-5f, deg * PitchScale), new Vector2(w, 1.5f)), c);
            Fill(Rect("PitchR", _horizon, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, deg * PitchScale), new Vector2(w, 1.5f)), c);
        }

        // Fixed aircraft symbol: wings, centre dot and a short fin.
        Fill(Rect("WingL", ball, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-7f, 0f), new Vector2(18f, 2.5f)), Green);
        Fill(Rect("WingR", ball, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(18f, 2.5f)), Green);
        Fill(Rect("Dot", ball, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f)), Green);
        Fill(Rect("Fin", ball, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(2f, 6f)), Green);
    }

    // Generated once: an anti-aliased ring, or a filled disc (for the circular clip).
    private static Sprite _ringSprite, _discSprite;

    private static Sprite CircleSprite(bool filled)
    {
        Sprite cached = filled ? _discSprite : _ringSprite;
        if (cached != null) return cached;
        const int size = 256;
        float r = size * 0.5f - 1f, thickness = 3.5f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                float a = filled ? Mathf.Clamp01(r - d + 0.5f)
                                 : Mathf.Clamp01(thickness * 0.5f - Mathf.Abs(d - (r - thickness * 0.5f)) + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        if (filled) _discSprite = sprite; else _ringSprite = sprite;
        return sprite;
    }

    private static void UpdateBall(Aircraft aircraft)
    {
        if (_horizon == null) return;
        Transform t = aircraft.transform;
        float pitch = Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Atan2(-t.right.y, t.up.y) * Mathf.Rad2Deg;
        Quaternion turn = Quaternion.Euler(0f, 0f, roll);
        _horizon.localRotation = turn;
        _horizon.localPosition = turn * new Vector3(0f, -Mathf.Clamp(pitch, -60f, 60f) * PitchScale, 0f);

        // AOA pointer follows the same angle the game's AOA readout and stall horn use.
        if (_aoaPointer != null && aircraft.cockpit != null && aircraft.cockpit.rb != null)
        {
            Vector3 v = aircraft.cockpit.transform.InverseTransformDirection(aircraft.cockpit.rb.velocity);
            float aoa = aircraft.speed > 10f ? Mathf.Atan2(v.y, v.z) * -57.29578f : 0f;
            float y = (Mathf.Clamp(aoa, AoaMin, AoaMax) - AoaMin) / (AoaMax - AoaMin) * AoaTapeHeight;
            _aoaPointer.anchoredPosition = new Vector2(-1f, y);
        }
    }

    // ---- per-frame text ------------------------------------------------------------------------

    // Throttle wording shortened to fit under its bar.
    private static string Abbreviate(string text) =>
        text.Replace("AFTERBURNER", "AB").Replace("AIRBRAKE", "BRK");

    // TextMeshPro has no closing tag for transparency; a dim label is followed by a reset to opaque.
    private static string Dim(string label) => $"<alpha=#8C>{label}<alpha=#FF>";

    private static void RefreshMinimal(Aircraft aircraft, List<WeaponStation> stations, WeaponStation current)
    {
        float climb = Vector3.Dot(aircraft.CockpitRB().velocity, Vector3.up);
        float sound = LevelInfo.GetSpeedOfSound(aircraft.GlobalPosition().y);
        float g = Vector3.Dot(aircraft.pilots[0].GetAccel() + Vector3.up, aircraft.transform.up);
        string maxG = Get<TextMeshProUGUI>(App<GIndicators>(), "gMaxForceLabel")?.text ?? "";
        TextMeshProUGUI rpm = Get<TextMeshProUGUI>(App<RPMGauge>(), "rpmText");
        string rpmText = rpm != null && !string.IsNullOrEmpty(rpm.text)
            ? $"   {Dim("RPM")} <color={Hex(rpm.color)}>{rpm.text}</color>" : "";
        _spdSub.text = $"{Dim("M")} {aircraft.speed / sound:F2}   {Dim("G")} {g:F1} <size=80%><alpha=#B0>{maxG}<alpha=#FF></size>{rpmText}";
        _spdSub.color = Faded(0.9f);
        _altSub.text = $"{Dim("AGL")} {UnitConverter.AltitudeReading(aircraft.radarAlt)}   {Dim("V/S")} {UnitConverter.ClimbRateReading(climb)}";
        _altSub.color = Faded(0.9f);

        TextMeshProUGUI aoa = Get<TextMeshProUGUI>(App<AoADisplay>(), "AoAText");
        bool aoaShown = aoa != null && aoa.enabled;
        string aoaText = aoaShown ? $"<color={Hex(aoa.color)}>{aoa.text}</color>" : "--";
        float rate = aircraft.rb != null ? Mathf.Abs(Vector3.Dot(aircraft.rb.angularVelocity, Vector3.up)) * Mathf.Rad2Deg : 0f;
        _aoaLine.text = $"{Dim("AOA")} {aoaText}   {Dim("RATE")} {rate:F1}\u00b0/s";
        _aoaLine.color = Faded(0.9f);

        int count = stations != null ? stations.Count : 0;
        int index = current != null && stations != null ? stations.IndexOf(current) : -1;
        _wpnName.text = current != null ? ShortName(current) : "---";
        _wpnName.color = Faded(0.9f);
        _wpnRange.text = RangeText(aircraft, true) ?? "";
        _wpnRange.color = Green;
        _wpnCount.text = current != null ? WithReserve(current, index) : "";
        // The next three weapons the select button will cycle to.
        string next = "";
        for (int step = 1; step <= 3 && step < count; step++)
        {
            WeaponStation st = stations[((index < 0 ? -1 : index) + step) % count];
            bool empty = st.GetAmmoLoaded() <= 0 && st.Ammo <= 0;
            string line = $"{ShortName(st)}  {st.GetAmmoReadout()}";
            next += (next.Length > 0 ? "\n" : "") + (empty ? $"<color=#8C8C8C>{line}</color>" : line);
        }
        _nextList.text = next;
        _nextList.color = Faded(0.6f);
        _cmLine.text = AppendCapacitorText(CountermeasureText(aircraft), "\n");
        _cmLine.color = Faded(0.9f);
        ShowCapacitor(_weaponCap);
        RefreshDamageReport();
    }

    // The strip grows to the right to hold the aircraft's extra gauges, when it has any.
    private static void FitStripToTray()
    {
        if (!_minimal || _strip == null) return;
        float extra = BorrowedGauges.Count > 0 && _vector != null ? _vector.sizeDelta.x + 24f : 0f;
        float width = (_analog ? AnalogWidth : StripWidth) - _stripTrim + extra;
        _strip.sizeDelta = new Vector2(width, StripHeight);
        // Grow to the right only, keeping the attitude ball at the strip's anchor (screen centre).
        _strip.pivot = new Vector2(((_analog ? AnalogCentreX : BallCentreX) - _stripTrim) / width, 0f);
    }
}
