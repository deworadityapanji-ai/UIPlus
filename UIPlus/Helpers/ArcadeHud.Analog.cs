using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// "Analog" style: a panel of round cockpit instruments along the bottom, light-aircraft style,
// with the combat readouts on engraved plates beside them. It shares Dashboard's strip machinery
// (the RWR slot, the map docked to its left end, the gauge tray, the damage silhouette), so the
// _minimal flag is on for both; _analog picks this layout. Left to right:
//   RWR scope | ENGINE (fuel + throttle) | AIRSPEED | ATTITUDE (screen centre) | ALTIMETER |
//   VERTICAL SPEED | HEADING | G METER | AOA | STORES plate | damage silhouette | extra gauges
// Dial art is painted once into textures (DialArt) and cached; only needles and cards move.
internal static partial class ArcadeHud
{
    private static bool _analog, _builtRwrActive;

    private static int _builtPanelSkin;
    private static RectTransform _rwrBezel;
    private static readonly RectTransform[] AnalogItems = new RectTransform[HudElementPositions.Count];
    private static readonly Vector2[] AnalogBases = new Vector2[HudElementPositions.Count];

    private const float AnalogHeight = 150f, Dial = 118f, DialStep = 126f;
    private const float AnalogWidth = 1410f, AnalogCentreX = 513f, AnalogRwrSection = 192f;
    private const float AttDegPx = Dial * 0.4175f / 30f;   // the attitude ball shows +-30 degrees of pitch

    // Instrument colours: white markings on black faces, grey bezels; not the HUD colour.
    private static readonly Color PanelColour = new(0.12f, 0.125f, 0.13f, 0.92f);
    private static readonly Color FaceColour = new(0.055f, 0.055f, 0.06f, 1f);
    private static readonly Color BezelTop = new(0.46f, 0.46f, 0.48f, 1f), BezelBottom = new(0.11f, 0.11f, 0.12f, 1f);
    private static readonly Color Marking = new(0.94f, 0.94f, 0.91f, 1f), MarkingDim = new(0.94f, 0.94f, 0.91f, 0.6f);
    private static readonly Color ArcGreen = new(0.2f, 0.78f, 0.28f, 1f), ArcYellow = new(0.98f, 0.8f, 0.15f, 1f);
    private static readonly Color ArcRed = new(0.92f, 0.16f, 0.12f, 1f), ArcBlue = new(0.3f, 0.6f, 1f, 1f);
    private static readonly Color Pointer = new(1f, 0.58f, 0.1f, 1f);   // orange: aircraft symbols, lubber line, max-G
    private static readonly Color Sky = new(0.2f, 0.47f, 0.8f, 1f), Ground = new(0.42f, 0.27f, 0.15f, 1f);

    private static readonly Dictionary<string, Sprite> DialSprites = new();

    private static RectTransform _asiNeedle, _altLong, _altShort, _vsiNeedle, _gNeedle, _gPeakNeedle,
        _fuelNeedle, _thrNeedle, _hdgCard, _attHorizon, _attRoll, _attAircraft, _aoaMark;
    private static TextMeshProUGUI _machWindow, _altWindow, _aglWindow, _gWindow, _engineWindow, _aoaValue,
        _storeName, _storeAmmo, _storeRange, _storeNext, _storeCm;
    private static Image _shootLamp, _aoaMarkImage;

    // Scales worked out per aircraft and unit system when the panel is built.
    private static float _asiFactor, _asiTop, _climbFactor, _climbTop, _gLimit;
    private const float AsiFrom = 30f, AsiSweep = 300f, GLow = -3f, GHigh = 10f, GFrom = 225f, GSweep = 270f;
    // Needle positions, eased so the needles swing rather than jump.
    private static float _easeSpeed, _easeAlt, _easeClimb, _easeG, _gPeak, _easeFuel, _easeThrottle;

    // ---- small builders ---------------------------------------------------------------------

    private static Sprite DialSprite(string key, Func<Sprite> make)
    {
        if (DialSprites.TryGetValue(key, out Sprite s) && s != null) return s;
        s = make();
        DialSprites[key] = s;
        return s;
    }

    /// <summary>Bezel, black inner rim and (optionally) the black face.</summary>
    private static DialArt FaceArt(bool face = true)
    {
        var art = new DialArt(256);
        art.Ring(0.865f, 1f, BezelTop, BezelBottom);
        art.Ring(0.845f, 0.87f, new Color(0.02f, 0.02f, 0.02f, 1f), new Color(0.02f, 0.02f, 0.02f, 1f));
        if (face) art.Disc(0.85f, FaceColour);
        return art;
    }

    private static Vector2 Polar(float angle, float radius)
    {
        float a = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius * Dial * 0.5f;
    }

    private static RectTransform DialAt(string name, float x, Sprite face)
    {
        RectTransform dial = Rect(name, _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(x, -AnalogHeight * 0.5f), new Vector2(Dial, Dial));
        if (face != null) Picture(dial, face);
        return dial;
    }

    private static Image Picture(RectTransform rect, Sprite sprite)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    // A layer covering the whole dial (a card, an overlay, the glass).
    private static RectTransform Layer(string name, RectTransform dial, Sprite sprite)
    {
        RectTransform layer = Rect(name, dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Dial, Dial));
        if (sprite != null) Picture(layer, sprite);
        return layer;
    }

    private static TextMeshProUGUI DialText(RectTransform parent, string text, float size, Vector2 at, Color colour, float width = 60f)
    {
        TextMeshProUGUI t = Text("Mark", parent, size, TextAlignmentOptions.Center);
        t.text = text;
        t.color = colour;
        Pin(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(width, size + 4f), at);
        return t;
    }

    // A small black window with a value in it, like a drum counter.
    private static TextMeshProUGUI Window(RectTransform parent, Vector2 at, Vector2 size, float fontSize)
    {
        RectTransform box = Rect("Window", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, size);
        Fill(box, new Color(0f, 0f, 0f, 0.95f));
        Outline(box, new Color(0.35f, 0.35f, 0.37f, 1f));
        TextMeshProUGUI t = Text("Value", box, fontSize, TextAlignmentOptions.Center);
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.sizeDelta = Vector2.zero;
        t.color = Marking;
        return t;
    }

    private static void Outline(RectTransform box, Color colour)
    {
        // Stretched along each edge, so the frame follows the box when it is resized.
        Edge("Top", box, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), colour);
        Edge("Bottom", box, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), colour);
        Edge("Left", box, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), colour);
        Edge("Right", box, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), colour);
    }

    private static void Edge(string name, RectTransform box, Vector2 from, Vector2 to, Vector2 thickness, Color colour)
    {
        RectTransform edge = Rect(name, box, from, new Vector2(0.5f, 0.5f), Vector2.zero, thickness);
        edge.anchorMin = from;
        edge.anchorMax = to;
        edge.pivot = new Vector2(from.x == to.x ? from.x : 0.5f, from.y == to.y ? from.y : 0.5f);
        edge.sizeDelta = thickness;
        Fill(edge, colour);
    }

    // A needle turning about the dial's centre; lengths in reference pixels (drawn at twice that).
    private static RectTransform NeedleOn(RectTransform dial, string key, float length, float tail, float halfWidth, Color colour)
    {
        Sprite sprite = DialSprite($"needle:{key}:{length}:{tail}:{halfWidth}:{colour}", () =>
            DialArt.Needle("Needle " + key, length * 2f, tail * 2f, halfWidth * 2f, colour, new Color(0.22f, 0.22f, 0.23f, 1f)));
        Vector2 size = sprite.rect.size * 0.5f;
        RectTransform needle = Rect("Needle", dial, new Vector2(0.5f, 0.5f),
            new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height), Vector2.zero, size);
        Picture(needle, sprite);
        return needle;
    }

    private static void Hub(RectTransform dial, float size = 9f)
    {
        RectTransform hub = Rect("Hub", dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        Image image = Picture(hub, CircleSprite(true));
        image.color = new Color(0.18f, 0.18f, 0.19f, 1f);
    }

    // A faint reflection across the top of the glass.
    private static void Glass(RectTransform dial)
    {
        Sprite glass = DialSprite("glass", () => new DialArt(256)
            .Arc(295f, 65f, 0.55f, 0.8f, new Color(1f, 1f, 1f, 0.05f))
            .Arc(305f, 55f, 0.62f, 0.76f, new Color(1f, 1f, 1f, 0.04f))
            .ToSprite("Glass"));
        Layer("Glass", dial, glass);
    }

    private static Sprite Triangle() => DialSprite("triangle", () =>
        new DialArt(32).Polygon(Color.white, new Vector2(3f, 3f), new Vector2(29f, 3f), new Vector2(16f, 29f)).ToSprite("Triangle"));

    private static void Rotate(RectTransform needle, float angle)
    {
        if (needle != null) needle.localRotation = Quaternion.Euler(0f, 0f, -angle);
    }

    // ---- layout ----------------------------------------------------------------------------

    private static void BuildAnalogLayout(FlightHud hud, Aircraft aircraft)
    {
        _strip = Rect("Strip", _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), StripBase,
            new Vector2(AnalogWidth, AnalogHeight));
        _builtPanelSkin = ElementLooks.SkinOf(StripItem);
        if (_builtPanelSkin == 0) BuildPanelFrame(_strip);
        _easeSpeed = _easeAlt = _easeClimb = _easeFuel = _easeThrottle = 0f;
        _easeG = 1f;
        _gPeak = 1f;

        // RWR: a bezel the scope sits in, the alert tabs in a column beside it.
        int rwrFrom = _strip.childCount;
        _rwrBezel = DialAt("RwrBezel", 69f, DialSprite("rwr", () => FaceArt().ToSprite("RWR")));
        DialText(_rwrBezel, "RWR", 8f, new Vector2(0f, -Dial * 0.36f), MarkingDim);
        _rwrSlot = Rect("RwrSlot", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(69f, -AnalogHeight * 0.5f),
            new Vector2(Dial * 0.8f, Dial * 0.8f));
        _alertSlot = Rect("RwrAlerts", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(164f, -AnalogHeight * 0.5f),
            new Vector2(60f, Dial));
        int rwrTo = _strip.childCount;

        AnalogItems[HudElementPositions.AnalogEngine] = BuildEngineDial(261f);
        AnalogItems[HudElementPositions.AnalogAirspeed] = BuildAirspeedDial(387f, hud, aircraft);
        AnalogItems[HudElementPositions.AnalogAttitude] = BuildAttitudeDial(AnalogCentreX);
        AnalogItems[HudElementPositions.AnalogAltimeter] = BuildAltimeter(639f);
        AnalogItems[HudElementPositions.AnalogClimb] = BuildClimbDial(765f);
        AnalogItems[HudElementPositions.AnalogHeading] = BuildHeadingDial(891f);
        AnalogItems[HudElementPositions.AnalogGMeter] = BuildGMeter(1017f, aircraft);
        AnalogItems[HudElementPositions.AnalogAoa] = BuildAoaGauge(1098f);
        AnalogItems[HudElementPositions.AnalogStores] = BuildStoresPlate(1120f);

        // The style's shared readouts are drawn by the dials; these stay hidden but kept fed.
        _speedValue = Hidden("Speed");
        _altValue = Hidden("Altitude");
        _speedExtra = Hidden("FlightDetail");
        _altExtra = Hidden("AltDetail");
        _fuel = HiddenGauge("Fuel");
        _throttle = HiddenGauge("Throttle");

        // Failed systems under the damage silhouette, as in Dashboard.
        _damageReport = StripText("Failures", 10, new Vector2(DamageBase.x - 50f, -133f), 100f, "", TextAlignmentOptions.Center);
        _damageReport.enableWordWrapping = true;

        _builtRwrActive = RwrCompat.Active;
        TrimRwrSection(rwrFrom, rwrTo, AnalogRwrSection, !_builtRwrActive);
        // Trimming changes the anchors' positions, so save each instrument's base afterwards.
        for (int id = HudElementPositions.AnalogEngine; id < AnalogItems.Length; id++)
            AnalogBases[id] = AnalogItems[id].anchoredPosition;
    }

    internal const float ScreenFrameWidth = 12f;

    internal static void BuildInstrumentScreen(RectTransform frame)
    {
        BuildPanelFrame(frame);
        Inset(frame, "Rim", ScreenFrameWidth - 2f, BezelTop);
        Inset(frame, "Display", ScreenFrameWidth, Color.black);
    }

    private static void Inset(RectTransform frame, string name, float margin, Color colour)
    {
        RectTransform layer = Rect(name, frame, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        layer.anchorMax = Vector2.one;
        layer.offsetMin = new Vector2(margin, margin);
        layer.offsetMax = -layer.offsetMin;
        Fill(layer, colour);
    }

    private static void BuildPanelFrame(RectTransform panel)
    {
        Fill(panel, PanelColour);
        Outline(panel, new Color(0.3f, 0.3f, 0.32f, 1f));
        // Panel screws in the corners.
        foreach (Vector2 c in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
        {
            RectTransform screw = Rect("Screw", panel, c, new Vector2(0.5f, 0.5f),
                new Vector2(c.x > 0f ? -7f : 7f, c.y > 0f ? -7f : 7f), new Vector2(6f, 6f));
            Picture(screw, CircleSprite(true)).color = new Color(0.32f, 0.32f, 0.34f, 1f);
        }
    }

    // The shared fuel and throttle gauges, built but never shown.
    private static Gauge HiddenGauge(string name)
    {
        Gauge g = VBar(Vector2.zero, 10f, name);
        g.Fill.parent.gameObject.SetActive(false);
        g.Percent.gameObject.SetActive(false);
        return g;
    }

    // ENGINE: a twin gauge. Fuel on the left half (E at the bottom, F at the top), throttle on the
    // right half (0 at the bottom, 100 at the top); the game's throttle wording in the window.
    private const float FuelFrom = 210f, ThrFrom = 150f, HalfSweep = 120f;

    private static RectTransform BuildEngineDial(float x)
    {
        Sprite face = DialSprite("engine", () =>
        {
            DialArt art = FaceArt();
            art.Arc(FuelFrom, FuelFrom + HalfSweep * 0.15f, 0.76f, 0.84f, ArcRed);
            for (int i = 0; i <= 8; i++)
            {
                bool major = i % 4 == 0;
                art.Tick(FuelFrom + HalfSweep * i / 8f, major ? 0.66f : 0.73f, 0.84f, major ? 4f : 2.5f, Marking);
            }
            for (int i = 0; i <= 10; i++)
            {
                bool major = i % 5 == 0;
                art.Tick(ThrFrom - HalfSweep * i / 10f, major ? 0.66f : 0.73f, 0.84f, major ? 4f : 2.5f, Marking);
            }
            return art.ToSprite("Engine");
        });
        RectTransform dial = DialAt("Engine", x, face);
        DialText(dial, "E", 11f, Polar(FuelFrom, 0.5f), Marking);
        DialText(dial, "1/2", 10f, Polar(FuelFrom + HalfSweep * 0.5f, 0.5f), Marking);
        DialText(dial, "F", 11f, Polar(FuelFrom + HalfSweep, 0.5f), Marking);
        DialText(dial, "0", 10f, Polar(ThrFrom, 0.5f), Marking);
        DialText(dial, "50", 10f, Polar(ThrFrom - HalfSweep * 0.5f, 0.48f), Marking);
        DialText(dial, "100", 10f, Polar(ThrFrom - HalfSweep, 0.48f), Marking);
        DialText(dial, "FUEL", 8f, new Vector2(-Dial * 0.15f, Dial * 0.1f), MarkingDim);
        DialText(dial, "THR", 8f, new Vector2(Dial * 0.15f, Dial * 0.1f), MarkingDim);
        _engineWindow = Window(dial, new Vector2(0f, -Dial * 0.2f), new Vector2(40f, 14f), 10f);
        _fuelNeedle = NeedleOn(dial, "engine", Dial * 0.36f, 6f, 2.2f, Marking);
        _thrNeedle = NeedleOn(dial, "engine", Dial * 0.36f, 6f, 2.2f, Marking);
        Hub(dial);
        if (_hasPusher) BuildPusherDial(dial);
        Glass(dial);
        return dial;
    }

    // AIRSPEED: green from take-off speed to 90% of the never-exceed speed, yellow to it, a red
    // line at it; a white arc for the landing range, and a blue line at the corner (best turn) speed.
    private static RectTransform BuildAirspeedDial(float x, FlightHud hud, Aircraft aircraft)
    {
        bool metric = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;
        _asiFactor = metric ? 3.6f : 1.94384f;
        AircraftParameters p = aircraft.GetAircraftParameters();
        SpeedGauge gauge = hud != null ? hud.GetComponentInChildren<SpeedGauge>(true) : null;
        float limit = GetFloat(gauge, "overspeedThreshold", float.MaxValue);
        if (limit > 5000f) limit = p != null && p.maxSpeed > 1f ? p.maxSpeed : 300f;
        float raw = limit * _asiFactor * 1.12f;
        float step = NiceStep(raw, 8);
        _asiTop = Mathf.Ceil(raw / step) * step;
        bool hundreds = _asiTop >= 1000f;
        float takeoff = p != null ? p.takeoffSpeed * _asiFactor : 0f;
        float landing = p != null ? p.landingSpeed * _asiFactor : 0f;
        float corner = p != null ? p.cornerSpeed * _asiFactor : 0f;
        float vne = limit * _asiFactor;
        string key = $"asi:{_asiTop}:{step}:{vne:F0}:{takeoff:F0}:{landing:F0}:{corner:F0}";
        Sprite face = DialSprite(key, () =>
        {
            DialArt art = FaceArt();
            float green = takeoff > 0f && takeoff < vne * 0.9f ? takeoff : landing > 0f ? landing : 0f;
            if (landing > 0f && takeoff > landing) art.Arc(AsiAngle(landing), AsiAngle(takeoff), 0.7f, 0.75f, Marking);
            art.Arc(AsiAngle(green), AsiAngle(vne * 0.9f), 0.76f, 0.84f, ArcGreen);
            art.Arc(AsiAngle(vne * 0.9f), AsiAngle(vne), 0.76f, 0.84f, ArcYellow);
            for (float v = 0f; v <= _asiTop + 0.01f; v += step * 0.5f)
            {
                bool major = Mathf.Abs(Mathf.Repeat(v, step)) < 0.01f || Mathf.Abs(Mathf.Repeat(v, step) - step) < 0.01f;
                art.Tick(AsiAngle(v), major ? 0.66f : 0.74f, 0.84f, major ? 4f : 2.5f, Marking);
            }
            art.Tick(AsiAngle(vne), 0.62f, 0.85f, 5f, ArcRed);
            if (corner > 0f && corner < _asiTop) art.Tick(AsiAngle(corner), 0.66f, 0.85f, 4f, ArcBlue);
            return art.ToSprite("Airspeed");
        });
        RectTransform dial = DialAt("Airspeed", x, face);
        for (float v = step; v <= _asiTop + 0.01f; v += step)
        {
            string label = hundreds ? (v / 100f).ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);
            DialText(dial, label, label.Length > 2 ? 9f : 10f, Polar(AsiAngle(v), 0.52f), Marking);
        }
        DialText(dial, (hundreds ? "x100 " : "") + (metric ? "KM/H" : "KNOTS"), 7f, new Vector2(0f, Dial * 0.12f), MarkingDim);
        _machWindow = Window(dial, new Vector2(0f, -Dial * 0.13f), new Vector2(34f, 13f), 9f);
        _asiNeedle = NeedleOn(dial, "long", Dial * 0.38f, 7f, 2.2f, Marking);
        Hub(dial);
        Glass(dial);
        return dial;
    }

    private static float AsiAngle(float value) => AsiFrom + AsiSweep * Mathf.Clamp01(value / Mathf.Max(1f, _asiTop));

    // A round step that gives at most `count` labels up to `top`.
    private static float NiceStep(float top, int count)
    {
        foreach (float s in new[] { 10f, 20f, 25f, 50f, 100f, 200f, 250f, 500f, 1000f })
            if (top / s <= count) return s;
        return 2000f;
    }

    // ATTITUDE: separate layers let either the horizon or the aircraft bank against the roll scale.
    private static RectTransform BuildAttitudeDial(float x)
    {
        RectTransform dial = DialAt("Attitude", x, null);
        RectTransform clip = Rect("Clip", dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(Dial * 0.86f, Dial * 0.86f));
        Picture(clip, CircleSprite(true));
        clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        _attHorizon = Rect("Horizon", clip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        float big = Dial * 3f;
        Fill(Rect("Sky", _attHorizon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(big, big)), Sky);
        Fill(Rect("Ground", _attHorizon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(big, big)), Ground);
        Fill(Rect("Line", _attHorizon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(big, 2f)), Marking);
        for (int deg = -80; deg <= 80; deg += 5)
        {
            if (deg == 0 || (Mathf.Abs(deg) > 30 && deg % 10 != 0)) continue;
            bool major = deg % 10 == 0;
            float w = major ? Dial * 0.26f : Dial * 0.12f;
            Fill(Rect("Pitch", _attHorizon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, deg * AttDegPx),
                new Vector2(w, 1.5f)), major ? Marking : MarkingDim);
            if (!major) continue;
            string n = Mathf.Abs(deg).ToString(CultureInfo.InvariantCulture);
            DialText(_attHorizon, n, 7f, new Vector2(-w * 0.5f - 7f, deg * AttDegPx), Marking, 16f);
            DialText(_attHorizon, n, 7f, new Vector2(w * 0.5f + 7f, deg * AttDegPx), Marking, 16f);
        }

        // The bezel keeps the roll marks fixed while the pointer moves across them.
        Sprite overlay = DialSprite("attitude", () =>
        {
            DialArt art = FaceArt(face: false);
            foreach (int deg in new[] { -60, -45, -30, -20, -10, 10, 20, 30, 45, 60 })
            {
                bool major = deg % 30 == 0;
                art.Tick(deg, major ? 0.7f : 0.76f, 0.86f, major ? 4f : 2.5f, Marking);
            }
            art.Pointer(0f, 0.74f, 0.11f, 7f, Marking);
            return art.ToSprite("Attitude");
        });
        Layer("Bezel", dial, overlay);
        _attRoll = Rect("Roll", dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Dial, Dial));
        RectTransform skyPointer = Rect("SkyPointer", _attRoll, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
            new Vector2(0f, Dial * 0.355f), new Vector2(11f, 9f));
        Picture(skyPointer, Triangle()).color = Pointer;

        // Put all the aircraft marks on one pivot at the centre of the dial.
        _attAircraft = Layer("Aircraft", dial, null);
        foreach (float side in new[] { -1f, 1f })
        {
            Fill(Rect("Wing", _attAircraft, new Vector2(0.5f, 0.5f), new Vector2(side < 0f ? 1f : 0f, 0.5f),
                new Vector2(side * Dial * 0.07f, 0f), new Vector2(Dial * 0.17f, 3f)), Pointer);
            Fill(Rect("Tip", _attAircraft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
                new Vector2(side * Dial * 0.07f, 1.5f), new Vector2(3f, 7f)), Pointer);
        }
        Fill(Rect("Dot", _attAircraft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f)), Pointer);
        Glass(dial);
        return dial;
    }

    // ALTIMETER: the long needle turns once per 1000 (feet or metres), the short one once per
    // 10 000; the full altitude in a window, and height above ground (radar) above the centre.
    private static RectTransform BuildAltimeter(float x)
    {
        bool metric = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;
        Sprite face = DialSprite("altimeter", () =>
        {
            DialArt art = FaceArt();
            for (int i = 0; i < 50; i++)
            {
                bool major = i % 5 == 0;
                art.Tick(i * 7.2f, major ? 0.66f : 0.75f, 0.84f, major ? 4f : 2.2f, Marking);
            }
            return art.ToSprite("Altimeter");
        });
        RectTransform dial = DialAt("Altimeter", x, face);
        for (int i = 0; i < 10; i++) DialText(dial, i.ToString(CultureInfo.InvariantCulture), 11f, Polar(i * 36f, 0.56f), Marking);
        _aglWindow = DialText(dial, "", 7f, new Vector2(0f, Dial * 0.14f), MarkingDim, 50f);
        _altWindow = Window(dial, new Vector2(0f, -Dial * 0.08f), new Vector2(50f, 13f), 9f);
        _altShort = NeedleOn(dial, "short", Dial * 0.24f, 6f, 3.2f, Marking);
        _altLong = NeedleOn(dial, "long", Dial * 0.38f, 7f, 2.2f, Marking);
        Hub(dial);
        Glass(dial);
        return dial;
    }

    // VERTICAL SPEED: zero at nine o'clock, climb clockwise above it, descent below; the scale is
    // stretched near zero, as on the real instrument.
    private const float ClimbZero = 270f, ClimbSweep = 170f;

    private static RectTransform BuildClimbDial(float x)
    {
        bool metric = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;
        _climbFactor = metric ? 1f : 60f * 3.28084f;
        _climbTop = metric ? 30f : 6000f;
        float[] labels = metric ? new[] { 5f, 10f, 20f, 30f } : new[] { 1000f, 2000f, 4000f, 6000f };
        float[] minors = metric ? new[] { 2.5f, 7.5f, 15f, 25f } : new[] { 500f, 1500f, 3000f, 5000f };
        Sprite face = DialSprite("climb:" + metric, () =>
        {
            DialArt art = FaceArt();
            art.Tick(ClimbAngle(0f), 0.62f, 0.84f, 4.5f, Marking);
            foreach (float sign in new[] { 1f, -1f })
            {
                foreach (float v in labels) art.Tick(ClimbAngle(sign * v), 0.66f, 0.84f, 4f, Marking);
                foreach (float v in minors) art.Tick(ClimbAngle(sign * v), 0.74f, 0.84f, 2.5f, Marking);
            }
            return art.ToSprite("Climb");
        });
        RectTransform dial = DialAt("Climb", x, face);
        DialText(dial, "0", 11f, Polar(ClimbZero, 0.5f), Marking);
        foreach (float sign in new[] { 1f, -1f })
            foreach (float v in labels)
            {
                string label = metric ? v.ToString("0", CultureInfo.InvariantCulture) : (v / 1000f).ToString("0", CultureInfo.InvariantCulture);
                DialText(dial, label, 11f, Polar(ClimbAngle(sign * v), 0.5f), Marking);
            }
        DialText(dial, "UP", 7f, new Vector2(-Dial * 0.15f, Dial * 0.12f), MarkingDim);
        DialText(dial, "DN", 7f, new Vector2(-Dial * 0.15f, -Dial * 0.12f), MarkingDim);
        DialText(dial, "V/S", 8f, new Vector2(Dial * 0.06f, Dial * 0.1f), MarkingDim);
        DialText(dial, metric ? "M/S" : "x1000 FPM", 6f, new Vector2(Dial * 0.06f, -Dial * 0.1f), MarkingDim, 50f);
        _vsiNeedle = NeedleOn(dial, "long", Dial * 0.38f, 7f, 2.2f, Marking);
        Rotate(_vsiNeedle, ClimbZero);
        Hub(dial);
        Glass(dial);
        return dial;
    }

    private static float ClimbAngle(float rate)
    {
        float x = Mathf.Clamp01(Mathf.Abs(rate) / Mathf.Max(0.01f, _climbTop));
        float stretched = Mathf.Log10(1f + 9f * x);   // 1/6 of the range reaches 40% of the sweep
        return ClimbZero + Mathf.Sign(rate) * ClimbSweep * stretched;
    }

    // HEADING: a compass card turning behind a fixed aircraft and an orange lubber mark.
    private static RectTransform BuildHeadingDial(float x)
    {
        RectTransform dial = DialAt("Heading", x, DialSprite("face", () => FaceArt().ToSprite("Face")));
        Sprite card = DialSprite("compass", () =>
        {
            var art = new DialArt(256);
            for (int deg = 0; deg < 360; deg += 5)
            {
                bool major = deg % 10 == 0;
                art.Tick(deg, major ? 0.71f : 0.77f, 0.84f, major ? 3.2f : 2.2f, Marking);
            }
            return art.ToSprite("Compass card");
        });
        _hdgCard = Layer("Card", dial, card);
        for (int deg = 0; deg < 360; deg += 30)
        {
            string label = deg switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => (deg / 10).ToString(CultureInfo.InvariantCulture) };
            TextMeshProUGUI t = DialText(_hdgCard, label, deg % 90 == 0 ? 12f : 10f, Polar(deg, 0.55f), deg == 0 ? Pointer : Marking, 24f);
            t.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -deg);
        }
        Sprite overlay = DialSprite("heading", () =>
        {
            var art = new DialArt(256);
            for (int deg = 45; deg < 360; deg += 45) art.Tick(deg, 0.85f, 0.9f, 3f, Marking);
            art.Pointer(0f, 0.72f, 0.14f, 7f, Pointer);
            // The aircraft, seen from above, nose up.
            float u = art.Pixels(1f);
            Vector2 c = new(128f, 128f);
            Color plane = new(0.94f, 0.94f, 0.91f, 0.95f);
            art.Polygon(plane, c + new Vector2(-0.035f, -0.3f) * u, c + new Vector2(0.035f, -0.3f) * u,
                c + new Vector2(0.035f, 0.26f) * u, c + new Vector2(0f, 0.32f) * u, c + new Vector2(-0.035f, 0.26f) * u);
            art.Polygon(plane, c + new Vector2(-0.3f, -0.02f) * u, c + new Vector2(0.3f, -0.02f) * u,
                c + new Vector2(0.3f, 0.03f) * u, c + new Vector2(0.03f, 0.12f) * u, c + new Vector2(-0.03f, 0.12f) * u, c + new Vector2(-0.3f, 0.03f) * u);
            art.Polygon(plane, c + new Vector2(-0.12f, -0.3f) * u, c + new Vector2(0.12f, -0.3f) * u,
                c + new Vector2(0.12f, -0.26f) * u, c + new Vector2(-0.12f, -0.26f) * u);
            return art.ToSprite("Heading");
        });
        Layer("Lubber", dial, overlay);
        Glass(dial);
        return dial;
    }

    // G METER: -3 to +10 G with a red line at the aircraft's limit and an orange needle that stays
    // at the highest G pulled (since spawning).
    private static RectTransform BuildGMeter(float x, Aircraft aircraft)
    {
        AircraftParameters p = aircraft.GetAircraftParameters();
        _gLimit = p != null && p.aircraftGLimit > 1f ? p.aircraftGLimit : 9f;
        Sprite face = DialSprite($"g:{_gLimit:F1}", () =>
        {
            DialArt art = FaceArt();
            if (_gLimit < GHigh) art.Arc(GAngle(_gLimit), GAngle(GHigh), 0.76f, 0.84f, ArcRed);
            for (int g = (int)GLow; g <= (int)GHigh; g++)
            {
                bool major = g % 2 == 0;
                art.Tick(GAngle(g), major ? 0.66f : 0.74f, 0.84f, major ? 4f : 2.5f, Marking);
            }
            art.Tick(GAngle(_gLimit), 0.62f, 0.85f, 5f, ArcRed);
            return art.ToSprite("G meter");
        });
        RectTransform dial = DialAt("GMeter", x, face);
        for (int g = -2; g <= (int)GHigh; g += 2)
            DialText(dial, g.ToString(CultureInfo.InvariantCulture), 11f, Polar(GAngle(g), 0.5f), Marking);
        DialText(dial, "G", 10f, new Vector2(0f, -Dial * 0.12f), MarkingDim);
        _gWindow = Window(dial, new Vector2(0f, -Dial * 0.3f), new Vector2(30f, 13f), 9f);
        _gPeakNeedle = NeedleOn(dial, "peak", Dial * 0.36f, 4f, 1.4f, Pointer);
        _gNeedle = NeedleOn(dial, "long", Dial * 0.38f, 7f, 2.2f, Marking);
        Hub(dial);
        Glass(dial);
        return dial;
    }

    private static float GAngle(float g) => GFrom + GSweep * Mathf.Clamp01((g - GLow) / (GHigh - GLow));

    // AOA: a slim upright gauge, -5 to 25 degrees, its pointer in the game's AoA warning colour.
    private const float AoaGaugeHeight = 100f;

    private static RectTransform BuildAoaGauge(float x)
    {
        RectTransform box = Rect("Aoa", _strip, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, -AnalogHeight * 0.5f),
            new Vector2(28f, Dial));
        Fill(box, FaceColour);
        Outline(box, new Color(0.38f, 0.38f, 0.4f, 1f));
        RectTransform scale = Rect("Scale", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f),
            new Vector2(28f, AoaGaugeHeight));
        for (int deg = (int)AoaMin; deg <= (int)AoaMax; deg += 5)
        {
            bool major = deg % 10 == 0;
            float y = (deg - AoaMin) / (AoaMax - AoaMin) * AoaGaugeHeight;
            Fill(Rect("Tick", scale, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(3f, y), new Vector2(major ? 9f : 5f, 1.5f)), Marking);
        }
        // Zero: a longer mark across the gauge.
        Fill(Rect("Zero", scale, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(3f, -AoaMin / (AoaMax - AoaMin) * AoaGaugeHeight),
            new Vector2(22f, 1.5f)), MarkingDim);
        _aoaMark = Rect("Pointer", scale, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(10f, 9f));
        _aoaMark.localRotation = Quaternion.Euler(0f, 0f, 90f);
        _aoaMarkImage = Picture(_aoaMark, Triangle());
        _aoaMarkImage.color = Pointer;
        _aoaValue = DialText(box, "AOA", 7f, new Vector2(0f, -Dial * 0.5f + 7f), MarkingDim, 28f);
        return box;
    }

    // STORES: an engraved plate with the selected weapon, a drum counter for its rounds, a SHOOT
    // lamp, the reload bars, the next weapons and the countermeasures.
    private static RectTransform BuildStoresPlate(float x)
    {
        const float w = 180f;
        RectTransform plate = Rect("Stores", _strip, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x, -AnalogHeight * 0.5f),
            new Vector2(w, Dial));
        Fill(plate, new Color(0.075f, 0.078f, 0.082f, 1f));
        Outline(plate, new Color(0.38f, 0.38f, 0.4f, 1f));
        TextMeshProUGUI label = PlateText(plate, "STORES", 8f, new Vector2(8f, -5f), 80f);
        label.color = MarkingDim;
        label.characterSpacing = 3f;
        _storeRange = PlateText(plate, "", 10f, new Vector2(w - 88f, -4f), 80f, TextAlignmentOptions.Right);
        _storeName = PlateText(plate, "", 14f, new Vector2(8f, -18f), w - 16f);

        // Drum counter: the ammo count in a black window.
        RectTransform drum = Rect("Counter", plate, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -38f), new Vector2(104f, 30f));
        Fill(drum, new Color(0f, 0f, 0f, 1f));
        Outline(drum, new Color(0.35f, 0.35f, 0.37f, 1f));
        _storeAmmo = Text("Ammo", drum, 22f, TextAlignmentOptions.Center);
        _storeAmmo.rectTransform.anchorMin = Vector2.zero;
        _storeAmmo.rectTransform.anchorMax = Vector2.one;
        _storeAmmo.rectTransform.sizeDelta = Vector2.zero;
        _storeAmmo.color = Marking;

        // SHOOT lamp: lit inside the no-escape zone.
        RectTransform lamp = Rect("Shoot", plate, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(146f, -48f), new Vector2(16f, 16f));
        Picture(lamp, CircleSprite(true)).color = new Color(0.25f, 0.25f, 0.27f, 1f);
        RectTransform glow = Rect("Glow", lamp, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
        _shootLamp = Picture(glow, CircleSprite(true));
        PlateText(plate, "SHOOT", 7f, new Vector2(126f, -58f), 40f, TextAlignmentOptions.Center).color = MarkingDim;

        if (_builtCapacitorBar)
        {
            // Keep two next-store lines above the countermeasure and leave the lower right for reloads.
            _storeNext = PlateText(plate, "", 8f, new Vector2(8f, -68f), 118f, TextAlignmentOptions.TopLeft);
            _storeNext.lineSpacing = 0f;
            _storeCm = PlateText(plate, "", 9f, new Vector2(8f, -86f), 118f);
            _weaponCap = BuildCapacitorBar(plate, new Vector2(0f, 1f), new Vector2(8f, -101f), 118f, 15f, 9f);
        }
        else
        {
            _storeNext = PlateText(plate, "", 10f, new Vector2(8f, -74f), 118f);
            _storeNext.lineSpacing = 6f;
            _storeCm = PlateText(plate, "", 10f, new Vector2(8f, -102f), 118f);
        }
        _storeNext.color = MarkingDim;
        _storeCm.color = Marking;

        // Reload bars, one per pylon, at the plate's lower right.
        _reloadBarHeight = 38f;
        SetupReloadBars(plate, new Vector2(140f, -Dial + 6f), 34f, 7f);
        return plate;
    }

    private static TextMeshProUGUI PlateText(RectTransform plate, string text, float size, Vector2 at, float width,
        TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        TextMeshProUGUI t = Text("Text", plate, size, align);
        t.text = text;
        t.color = Marking;
        t.enableWordWrapping = false;
        Pin(t.rectTransform, new Vector2(0f, 1f), new Vector2(width, size + 6f), at);
        return t;
    }

    // ---- per frame -----------------------------------------------------------------------------

    private static void UpdateAnalog(Aircraft aircraft)
    {
        if (_asiNeedle == null) return;
        float ease = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
        Transform t = aircraft.transform;

        _easeSpeed = Mathf.Lerp(_easeSpeed, aircraft.speed * _asiFactor, ease);
        Rotate(_asiNeedle, AsiAngle(_easeSpeed));

        bool metric = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;
        float altitude = t.position.GlobalY() * (metric ? 1f : 3.28084f);
        _easeAlt = Mathf.Lerp(_easeAlt, altitude, ease);
        Rotate(_altLong, Mathf.Repeat(_easeAlt, 1000f) / 1000f * 360f);
        Rotate(_altShort, Mathf.Repeat(_easeAlt, 10000f) / 10000f * 360f);

        float climb = Vector3.Dot(aircraft.CockpitRB().velocity, Vector3.up) * _climbFactor;
        _easeClimb = Mathf.Lerp(_easeClimb, climb, ease * 0.6f);
        Rotate(_vsiNeedle, ClimbAngle(_easeClimb));

        if (aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null)
        {
            float g = Vector3.Dot(aircraft.pilots[0].GetAccel() + Vector3.up, t.up);
            _easeG = Mathf.Lerp(_easeG, g, ease * 0.7f);
            _gPeak = Mathf.Max(_gPeak, _easeG);
            Rotate(_gNeedle, GAngle(_easeG));
            Rotate(_gPeakNeedle, GAngle(_gPeak));
        }

        _easeFuel = Mathf.Lerp(_easeFuel, aircraft.GetFuelLevel(), ease);
        _easeThrottle = Mathf.Lerp(_easeThrottle, aircraft.GetInputs().throttle, ease);
        Rotate(_fuelNeedle, FuelFrom + HalfSweep * Mathf.Clamp01(_easeFuel));
        Rotate(_thrNeedle, ThrFrom - HalfSweep * Mathf.Clamp01(_easeThrottle));

        // Heading: the card turns so the aircraft's heading sits under the lubber mark.
        Vector3 f = t.forward;
        if (new Vector2(f.x, f.z).sqrMagnitude > 0.0001f && _hdgCard != null)
            _hdgCard.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);

        // Attitude: the same pitch and roll as Dashboard's ball.
        float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Atan2(-t.right.y, t.up.y) * Mathf.Rad2Deg;
        // Read the live skin so switching it in the editor reuses the same dial.
        bool insideOut = ElementLooks.SkinOf(HudElementPositions.AnalogAttitude) == 1;
        Quaternion horizonBank = insideOut ? Quaternion.Euler(0f, 0f, roll) : Quaternion.identity;
        Quaternion aircraftBank = insideOut ? Quaternion.identity : Quaternion.Euler(0f, 0f, -roll);
        _attHorizon.localRotation = horizonBank;
        _attHorizon.localPosition = horizonBank * new Vector3(0f, -Mathf.Clamp(pitch, -85f, 85f) * AttDegPx, 0f);
        _attAircraft.localRotation = aircraftBank;
        _attRoll.localRotation = insideOut ? horizonBank : aircraftBank;

        if (_aoaMark != null && aircraft.cockpit != null && aircraft.cockpit.rb != null)
        {
            Vector3 v = aircraft.cockpit.transform.InverseTransformDirection(aircraft.cockpit.rb.velocity);
            float aoa = aircraft.speed > 10f ? Mathf.Atan2(v.y, v.z) * -57.29578f : 0f;
            float y = (Mathf.Clamp(aoa, AoaMin, AoaMax) - AoaMin) / (AoaMax - AoaMin) * AoaGaugeHeight;
            _aoaMark.anchoredPosition = new Vector2(18f, y);
        }
    }

    // Ten times a second: the windows and the stores plate.
    private static void RefreshAnalog(Aircraft aircraft, List<WeaponStation> stations, WeaponStation current)
    {
        if (_machWindow == null) return;
        float sound = LevelInfo.GetSpeedOfSound(aircraft.GlobalPosition().y);
        _machWindow.text = "M " + (aircraft.speed / Mathf.Max(1f, sound)).ToString("0.00", CultureInfo.InvariantCulture);
        bool metric = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;
        float altitude = aircraft.transform.position.GlobalY() * (metric ? 1f : 3.28084f);
        _altWindow.text = altitude.ToString("#,0", CultureInfo.InvariantCulture) + (metric ? "<size=65%>M" : "<size=65%>FT");
        _aglWindow.text = "AGL " + UnitConverter.AltitudeReading(aircraft.radarAlt);
        _gWindow.text = _easeG.ToString("0.0", CultureInfo.InvariantCulture);
        _gWindow.color = _easeG > _gLimit ? ArcRed : Marking;

        // Engine window: the game's throttle wording (MIL, AB...) in its colour, or rotor RPM.
        TextMeshProUGUI rpm = Get<TextMeshProUGUI>(App<RPMGauge>(), "rpmText");
        TextMeshProUGUI reading = Get<TextMeshProUGUI>(App<ThrottleGauge>(), "throttleReading");
        if (rpm != null && !string.IsNullOrEmpty(rpm.text))
        {
            _engineWindow.text = rpm.text;
            _engineWindow.color = new Color(rpm.color.r, rpm.color.g, rpm.color.b, 1f);
        }
        else if (reading != null && !string.IsNullOrEmpty(reading.text))
        {
            _engineWindow.text = Abbreviate(reading.text);
            _engineWindow.color = new Color(reading.color.r, reading.color.g, reading.color.b, 1f);
        }
        else
        {
            _engineWindow.text = $"{100f * aircraft.GetInputs().throttle:F0}%";
            _engineWindow.color = Marking;
        }

        // AoA pointer in the game's AoA colour (it turns to a warning colour near the stall).
        TextMeshProUGUI aoa = Get<TextMeshProUGUI>(App<AoADisplay>(), "AoAText");
        if (_aoaMarkImage != null)
            _aoaMarkImage.color = aoa != null && aoa.enabled && aoa.color.g < 0.7f ? new Color(aoa.color.r, aoa.color.g, aoa.color.b, 1f) : Pointer;

        int count = stations != null ? stations.Count : 0;
        int index = current != null && stations != null ? stations.IndexOf(current) : -1;
        _storeName.text = current != null ? ShortName(current) : "---";
        _storeAmmo.text = current != null ? WithReserve(current, index) : "";
        _storeRange.text = RangeText(aircraft, true) ?? "";
        bool shoot = ReadRange(aircraft, out WeaponRange range) && range.Shoot;
        _shootLamp.color = shoot ? new Color(0.35f, 1f, 0.35f, 1f) : new Color(0.1f, 0.16f, 0.1f, 1f);
        string next = "";
        for (int step = 1; step <= 2 && step < count; step++)
        {
            WeaponStation st = stations[((index < 0 ? -1 : index) + step) % count];
            bool empty = st.GetAmmoLoaded() <= 0 && st.Ammo <= 0;
            string line = $"{ShortName(st)}  {st.GetAmmoReadout()}";
            next += (next.Length > 0 ? "\n" : "") + (empty ? $"<alpha=#60>{line}<alpha=#FF>" : line);
        }
        _storeNext.text = next;
        _storeCm.text = AppendCapacitorText(CountermeasureText(aircraft), "   ");
        ShowCapacitor(_weaponCap);
        RefreshDamageReport();
    }
}
