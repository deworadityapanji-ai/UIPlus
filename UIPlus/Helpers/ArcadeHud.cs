using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// Dashboard was called Minimal; saved configs saying "Minimal" are carried over on load.
public enum HudStyle { Native, Arcade, Wingman, Dashboard, Thunder, Analog }

// Plus (the game's own HUD with additions) is kept in the code but left out of the style list
// until it is ready; its value follows the listed ones. A saved "Plus" loads as Native.
internal static class HudStyles
{
    internal const HudStyle Plus = (HudStyle)50;
}

// "Arcade" chase HUD: a flat, Ace Combat style layout drawn on our own canvas. It reads the same
// aircraft data as the game's gauges. Native widgets it replaces are hidden, not destroyed, so
// switching back to Native restores everything. All sizes are in 1920x1080 reference pixels.
internal static partial class ArcadeHud
{
    // Custom element ids (continuing HudElementPositions' numbering).
    internal const int SpeedBlock = 8, AltBlock = 9, WeaponList = 10, StatusRow = 11, VectorGauges = 12, Caution = 13, PullUp = 14;
    // Degrees to turn each weapon icon so it points up. The game draws them sideways; -90 assumes
    // the nose points left. The left icon is the mirror image of the right one.
    internal static ConfigEntry<float> IconRotation;

    // Layout (reference pixels). Blocks hang off their inner edge, beside the aircraft.
    // Per-style values are set by ApplyStyleBases() when the HUD is built.
    private static Vector2 SpeedBase = new(-370f, -20f), AltBase = new(370f, -20f);
    private static Vector2 ListBase = new(-40f, 200f);       // bottom-right of the weapon list
    private static Vector2 StatusBase = new(0f, -230f);      // centre of the status tag row
    private static readonly Vector2 CautionBase = new(0f, 120f), PullUpBase = new(0f, -70f);
    private static Vector2 VectorBase = new(-315f, 105f);    // right edge of the gauge tray, left of the silhouette
    private static Vector2 DamageBase = new(-165f, 105f);    // centre of the silhouette
    private static float DamageScale = 0.5f;
    private const float ListWidth = 250f, RowHeight = 26f;
    private const float GaugeHeight = 200f, GaugeGap = 18f;
    private const float TapeFillOffset = 5f, TapeFillWidth = 4f;
    private const float IconLong = 112f, IconShort = 52f, IconGap = 92f;
    private static float TraySlot = 150f;   // largest width or height of one gauge in the tray

    // The HUD colour, sampled from a native readout when the HUD is built (falls back to pure green).
    private static Color Green = new(0f, 1f, 0f, 1f);
    private static TextMeshProUGUI _colorSample;
    private static Color _sampledColour = new(0f, 1f, 0f, 1f);   // the game's HUD colour (Green may be overridden)
    private static TMP_FontAsset _font;
    private static Material _fontMaterial;

    private static GameObject _root;
    private static RectTransform _speed, _alt, _list, _damage;
    private static TextMeshProUGUI _speedValue, _speedExtra, _altValue, _altExtra;
    private static TextMeshProUGUI _divider, _flare;
    private static Gauge _fuel, _throttle;
    private static Image _iconLeft, _iconRight, _iconLeftFill, _iconRightFill;
    // Extra HUD gauges (by component type name, comma separated) to move into the Arcade gauge tray.
    internal static ConfigEntry<string> ExtraGauges;
    internal static Color HudColour => Green;
    private static readonly List<Row> Rows = new();
    private static readonly Dictionary<Sprite, Sprite> Cutouts = new();
    private static float _nextSlow;
    private static Aircraft _aircraft;

    private sealed class Row
    {
        internal RectTransform Rect;
        internal TextMeshProUGUI Marker, Name, Ammo;
    }

    private sealed class Gauge
    {
        internal RectTransform Fill, Level;
        internal Image FillImage;
        internal TextMeshProUGUI Percent;
        internal bool Horizontal;   // Wingman's thin bars fill left to right
        internal float Length, Thickness, LevelStart = 56f;   // 0 = the style's defaults
    }

    private static Color Faded(float alpha) => new(Green.r, Green.g, Green.b, alpha);

    // True while the Arcade style is selected; the native left/right instrument groups are then
    // replaced by this HUD's own speed and altitude blocks.
    internal static bool NativeInstrumentsHidden =>
        HudElementPositions.Style != null && HudElementPositions.Style.Value != HudStyle.Native
        && HudElementPositions.Style.Value != HudStyles.Plus && HudViews.ShowingNow;

    // True for every style that draws this HUD: the replacing styles and Plus, which keeps the
    // game's own HUD and only adds to it.
    internal static bool CustomHudActive =>
        HudElementPositions.Style != null && HudElementPositions.Style.Value != HudStyle.Native
        && HudViews.ShowingNow;

    internal static void Tick(CameraStateManager cam)
    {
        FlightHud hud = SceneSingleton<FlightHud>.i;
        bool want = Plugin.IsActive && !HudHide.Hidden && HudViews.Showing(cam)
            && CustomHudActive && hud != null && !DynamicMap.mapMaximized
            && cam.followingUnit is Aircraft;
        Canvas flightCanvas = want ? FlightCanvas(hud) : null;
        if (!want || flightCanvas == null || !flightCanvas.gameObject.activeInHierarchy)
        {
            // Includes weapon view and "Hide UI", when the game switches its own HUD canvas off.
            if (_root != null) _root.SetActive(false);
            _capSupply = null;
            MapDock = Vector2.zero;
            HideNativeRange(false);
            PlaceWeaponWheel(false);
            SyncNeutralQolFuel(false);
            HideNativeWeaponPanel(false);
            HideExtras(false);
            PusherStockHud.Restore();
            ReturnVectorGauges();
            ReleaseSilhouette();
            ReleaseDamageVisible();
            HideNativeLog(false);
            return;
        }
        var aircraft = (Aircraft)cam.followingUnit;
        if (_root == null || _aircraft != aircraft || _builtStyle != HudElementPositions.Style.Value
            || (!_plus && _hasPusher != PusherAircraft.HasPusher(aircraft))
            || (!_plus && _builtCapacitorBar != WantCapacitorBar)
            || (_wingman && _builtSlant != (WingmanSlant != null && WingmanSlant.Value))
            || (!_plus && _builtBannerSkin != ElementLooks.SkinOf(BannerItem))
            || ((_wingman || _builtStyle == HudStyle.Arcade)
                && (_builtSpeedSkin != ElementLooks.SkinOf(SpeedBlock) || _builtAltSkin != ElementLooks.SkinOf(AltBlock)))
            || (!_plus && (_builtFeedSkin != ElementLooks.SkinOf(KillFeedItem) || _builtDamageSkin != ElementLooks.SkinOf(HudElementPositions.Damage)))
            || (_minimal && _builtRwrAway != RwrAway)
            || (_analog && (_builtRwrActive != RwrCompat.Active || _builtPanelSkin != ElementLooks.SkinOf(StripItem)))
            || (Plugin.TextContrast != null && _builtContrast != Plugin.TextContrast.Value)
            || (!_plus && !_minimal && !_thunder && _builtListSkin != ElementLooks.SkinOf(WeaponList))
            || (!_plus && _builtStarWheel != (!_wingCluster && !ElementLooks.Get(WeaponWheelItem).Hidden && ElementLooks.SkinOf(WeaponWheelItem) == 1)))
            Build(flightCanvas, hud, aircraft);
        if (_root == null) return;
        _root.SetActive(true);
        _root.GetComponent<Canvas>().sortingOrder = flightCanvas.sortingOrder;
        if (_plus) PusherStockHud.Restore();
        else PusherStockHud.Sync(aircraft, _hasPusher, hud.GetHUDCenter(), flightCanvas, cam.mainCamera);
        if (_plus) { MapDock = Vector2.zero; HideNativeRange(false); PlaceWeaponWheel(false); TickPlus(aircraft); return; }
        SyncNeutralQolFuel(_thunder);
        HideNativeWeaponPanel(true);
        if (HiddenExtras.Count == 0) HideExtras(true);
        CheckBorrowedOwners();
        // Aircraft without tray gauges: look again once a second, not every frame.
        if (BorrowedGauges.Count == 0 && _vector != null && Time.unscaledTime >= _nextTrayLook)
        {
            _nextTrayLook = Time.unscaledTime + 1f;
            if (TrayNodes().Count > 0) BorrowVectorGauges();
        }
        UpdateSilhouette(aircraft);
        // Arcade and Wingman lay the RWR over the map, so it is centred on the aircraft there.
        // NuclearMinimap centres the map on the aircraft itself (and owns its orientation).
        if (!_minimal) { if (!HudElementPositions.NuclearMinimapPresent) CentreMinimap(aircraft); MapDock = Vector2.zero; }
        else { if (_analog) UpdateAnalog(aircraft); else UpdateBall(aircraft); }
        SampleCapacitor(aircraft);
        Refresh(aircraft);
        UpdatePusher(aircraft);
        UpdateIcons(aircraft);
        UpdateWarnings(aircraft);
        UpdateMissileArcs(aircraft);
        UpdateMessages();
        UpdateWeaponPopup(aircraft);
        ApplyOffsets();
        if (_minimal) DockMap();
        UpdateTargetFrame(aircraft);
        UpdateRangeScale(aircraft);
        HideNativeRange(true);
        UpdateKillFeed();
        PlaceWeaponWheel(true);
        UpdateStarAim();   // every non-Plus style shows range its own way
    }

    private static FieldInfo _canvasField;
    private static Canvas FlightCanvas(FlightHud hud)
    {
        _canvasField ??= AccessTools.Field(typeof(FlightHud), "canvas");
        return _canvasField?.GetValue(hud) as Canvas;
    }

    // The HUD's main colour as the active theme styles it ("HUD_TextMainColor" in the theme's HUD
    // section, e.g. white in a custom theme). The readouts' own text colour can stay at the default
    // green while the theme decides what is drawn, so the theme is the truth.
    internal static bool GameHudColour(out Color colour)
    {
        colour = default;
        var theme = ThemeManager.Active != null ? ThemeManager.Active.HudTheme : null;
        if (theme == null || theme.TextStyles == null) return false;
        foreach (var item in theme.TextStyles)
            if (item != null && item.labelName == "HUD_TextMainColor" && item.Style != null)
            {
                Color c = item.Style.Color;
                colour = new Color(c.r, c.g, c.b, 1f);
                return true;
            }
        return false;
    }

    private static Color CurrentHudColour() =>
        GameHudColour(out Color chosen) ? chosen : _colorSample != null ? _colorSample.color : Green;

    internal static void Dispose()
    {
        HideNativeWeaponPanel(false);
        HideExtras(false);
        PusherStockHud.Restore();
        ReturnVectorGauges();
        ReleaseSilhouette();
        ReleaseDamageVisible();
        HideNativeRange(false);
        RestoreWeaponWheel();
        WatchRadar(null);
        HideNativeLog(false);
        if (_root != null) Object.Destroy(_root);
        _root = null;
        Rows.Clear();
        _aircraft = null;
        _capSupply = null;
        foreach (Sprite s in Cutouts.Values) if (s != null) { Object.Destroy(s.texture); Object.Destroy(s); }
        Cutouts.Clear();
    }

    // ---- building -------------------------------------------------------------------------

    private static void Build(Canvas flightCanvas, FlightHud hud, Aircraft aircraft)
    {
        ReleaseSilhouette();
        ReleaseDamageVisible();
        HideExtras(false);
        ReturnVectorGauges();
        if (_root != null) Object.Destroy(_root);
        Rows.Clear();
        _aircraft = aircraft;
        _builtContrast = Plugin.TextContrast != null ? Plugin.TextContrast.Value : TextHalo.Strength.Off;

        // Borrow the game's HUD colour, font and font material from the altitude readout, which the
        // game styles with its main HUD text colour and never recolours for warnings.
        Altitude altitude = hud.GetComponentInChildren<Altitude>(true);
        if (altitude == null) altitude = Object.FindObjectOfType<Altitude>();
        TextMeshProUGUI sample = altitude != null ? altitude.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        sample ??= hud.GetComponentInChildren<TextMeshProUGUI>(true);
        _colorSample = sample;
        _font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;
        _fontMaterial = sample != null ? sample.fontSharedMaterial : null;
        if (sample != null) Green = new Color(sample.color.r, sample.color.g, sample.color.b, 1f);
        // The player's HUD colour setting is the truth: the readout's own text colour can stay at
        // the default green while the game draws it in the chosen colour.
        if (GameHudColour(out Color chosen)) Green = chosen;
        _sampledColour = Green;
        Plugin.Logger.LogInfo($"Arcade HUD: colour {Green} from '{(sample != null ? sample.name : "none")}', font material '{(_fontMaterial != null ? _fontMaterial.name : "default")}'");

        LookBase.Clear();
        _root = new GameObject("UI Plus HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerID = flightCanvas.sortingLayerID;
        canvas.sortingOrder = flightCanvas.sortingOrder;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        _builtStyle = HudElementPositions.Style.Value;
        _wingman = _builtStyle == HudStyle.Wingman;
        // Analog shares Dashboard's strip (RWR slot, docked map, tray), with dials for a layout.
        _analog = _builtStyle == HudStyle.Analog;
        _minimal = _builtStyle == HudStyle.Dashboard || _analog;
        _thunder = _builtStyle == HudStyle.Thunder;
        _plus = _builtStyle == HudStyles.Plus;
        _hasPusher = PusherAircraft.HasPusher(aircraft);
        _pusherFill = _pusherNeedle = null;
        _pusherInk = null;
        _pusherReverse = _pusherIdle = null;
        _builtCapacitorBar = !_plus && WantCapacitorBar;
        _ring = null;
        _starMarker = null;
        _tgtPanel = null;
        _killFeed = null;
        _rangeScale = null;
        _wingDivider = null;
        _dataBlock = _topBar = null;
        _weaponCap = _dataCap = null;
        _capLine = null;
        _damageReport = null;
        _speed = _alt = _list = _strip = _rwrSlot = _horizon = null;
        _fuelTape = _throttleTape = null;
        _fuel = _throttle = null;
        _wingCluster = false;
        _starWheel = null;
        Slots.Clear();
        Petals.Clear();
        ApplyStyleBases();
        Rows.Clear();
        _bars = null;
        _reloadParent = null; ReloadTracks.Clear(); _reloadCount = -1;
        if (_plus)
        {
            // The game's own HUD stays; only the additions are built.
            _damage = _status = null;
            BuildPlusLayout();
            // The game's nozzle / wing-sweep / tilt gauges, gathered beside its damage silhouette.
            _vector = Rect("GaugeTray", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0.5f),
                VectorBase, new Vector2(140f, 110f));
            BorrowVectorGauges();
            FindNative(hud);
            _caution = MakeWarning("Caution", CautionBase);
            _pullUp = MakeWarning("PullUp", PullUpBase);
            BuildMessages();
            BuildCallout();
            BuildWeaponPopup();
            return;
        }
        if (_wingman)
        {
            BuildWingmanLayout();
        }
        else if (_minimal)
        {
            if (_analog) BuildAnalogLayout(hud, aircraft); else BuildMinimalLayout();
        }
        else if (_thunder)
        {
            BuildThunderLayout();
        }
        else
        {
            _speed = ReadoutBlock(true, arcadeStyle: true);
            _alt = ReadoutBlock(false, arcadeStyle: true);

            // The weapon display in its chosen skin: Arcade's list, or Wingman's cluster.
            if (WingmanClusterSkin) BuildWingmanCluster(); else BuildArcadeList();
        }

        // Wingman's HULL bar (throttle + fuel) and Thunder's data block and match bar, as extras in
        // the other styles (built hidden; the editor adds them). Not slanted outside Wingman.
        ExtraFuel.Clear();
        ExtraThrottle.Clear();
        if (!_wingman)
        {
            Gauge styleFuel = _fuel, styleThrottle = _throttle;
            _bars = Rect("Bars", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), BarsBase, new Vector2(ClusterWidth, 36f));
            BuildHull(_bars);
            ExtraFuel.Add(_fuel);
            ExtraThrottle.Add(_throttle);
            _fuel = styleFuel;
            _throttle = styleThrottle;
        }
        if (!_thunder)
        {
            BuildDataBlock();
            BuildTopBar();
        }

        // The Wingman star on its own (WEAPON WHEEL element), when added with the star skin.
        _builtListSkin = ElementLooks.SkinOf(WeaponList);
        _builtStarWheel = StarWheelWanted;
        if (_builtStarWheel) BuildStandaloneStar();

        // Arcade's fuel and throttle tapes, beside the speed and altitude blocks: part of Arcade, an
        // extra in the other styles (built there too, hidden until added in the editor). Outside
        // Arcade they are fed alongside the style's own fuel and throttle readouts.
        _fuelTape = Tape(true, "FUEL", SpeedBase, out Gauge fuelTape, out _fuelTapeBase);
        _throttleTape = Tape(false, "THR", AltBase, out Gauge throttleTape, out _throttleTapeBase);
        if (_fuel == null) _fuel = fuelTape; else ExtraFuel.Add(fuelTape);
        if (_throttle == null) _throttle = throttleTape; else ExtraThrottle.Add(throttleTape);
        if (_hasPusher && _builtStyle == HudStyle.Arcade)
            BuildPusherTape((RectTransform)throttleTape.Fill.parent);

        // The damage silhouette (a copy of the game's). Arcade: bottom right, with the selected
        // weapon on both sides. Wingman: small in the top-right corner, icons by the counter.
        _damage = Rect("Damage", _minimal ? _strip : _root.transform,
            _wingman ? new Vector2(1f, 1f) : _minimal ? new Vector2(0f, 1f) : _thunder ? new Vector2(0f, 0f) : new Vector2(1f, 0f),
            new Vector2(0.5f, 0.5f), DamageBase, new Vector2(IconGap * 2f + IconShort, 150f));
        _iconLeft = MakeIcon("IconLeft", out _iconLeftFill);
        _iconRight = MakeIcon("IconRight", out _iconRightFill);
        // Damage skin: 1 just the aircraft, 2 with the weapon icons (Arcade), 3 the game's own.
        _builtDamageSkin = ElementLooks.SkinOf(HudElementPositions.Damage);
        _damageIcons = _builtDamageSkin == 2 || (_builtDamageSkin == 0 && !(_wingman || _minimal || _thunder));
        if (!_damageIcons)
        {
            // Wingman and Minimal show reloads as plain bars by the counter, not as weapon icons;
            // Thunder lists weapons as text.
            _iconLeft.gameObject.SetActive(false);
            _iconRight.gameObject.SetActive(false);
        }

        // Boxed status tags (gear, brake, auto-hover, nozzle) in a row under the aircraft.
        _status = Rect("Status", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), StatusBase,
            new Vector2(400f, 26f));
        Tags.Clear();
        for (int i = 0; i < 4; i++) Tags.Add(MakeTag(_status));
        // Thunder writes the tags into its text block instead.
        if (_thunder) _status.gameObject.SetActive(false);
        if (_thunder) BuildDamageReport();
        FindNative(hud);
        _caution = MakeWarning("Caution", CautionBase);
        _pullUp = MakeWarning("PullUp", PullUpBase);
        BuildMessages();
        BuildCallout();
        BuildWeaponPopup();
        BuildTargetFrame();
        BuildKillFeed();
        BuildMissileArcs();
        // Weapon range: on Wingman's divider (to its right, on the line), on Arcade's throttle tape
        // (the aircraft side, beyond the tape's own ticks).
        if (_wingDivider != null) BuildRangeScale(_wingDivider, 160f, 1f, 3f, 0f);
        else if (_builtStyle == HudStyle.Arcade)
        {
            float clearance = _hasPusher ? TapeFillWidth : 0f;
            BuildRangeScale((RectTransform)throttleTape.Fill.parent, GaugeHeight, -1f, -16f - clearance,
                -13f - clearance, targetFill: true);
        }

        // The game's own nozzle / wing-sweep / tilt gauges, moved into the Arcade layout.
        // Minimal keeps the tray inside its strip, at the right end; elsewhere it floats on its own.
        _vector = _minimal
            ? Rect("GaugeTray", _strip, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(140f, 110f))
            : Rect("GaugeTray", _root.transform,
                _wingman ? new Vector2(1f, 1f) : _thunder ? new Vector2(0f, 0f) : new Vector2(1f, 0f),
                // Thunder's tray grows rightward from the silhouette; the others grow leftward.
                _thunder ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f),
                VectorBase, new Vector2(140f, 110f));
        BorrowVectorGauges();
        if (_wingman) ApplySlant();
    }

    // ---- minimap ----------------------------------------------------------------------------

    // The game centres the small map on a point 4 km ahead of the aircraft, so the aircraft sits low
    // on it. Arcade redoes that one step (after the game's own Update) centred on the aircraft, so
    // the RWR scope can sit on top of it. Heading-up rotation, zoom and icons are the game's own.
    private static void CentreMinimap(Aircraft aircraft)
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null || DynamicMap.mapMaximized || map.mapImage == null || map.mapBackground == null) return;
        Transform image = map.mapImage.transform;
        Vector3 p = aircraft.GlobalPosition().AsVector3() * map.mapDisplayFactor;
        // Directions in the parent's own frame, so this stays right when the map panel is rotated
        // (Wingman's lean); the game's formula assumes an unrotated panel.
        Quaternion toLocal = image.parent != null ? Quaternion.Inverse(image.parent.rotation) : Quaternion.identity;
        image.localPosition = (toLocal * image.right * -p.x + toLocal * image.up * -p.z)
                              * image.localScale.x * map.mapBackground.transform.localScale.x;
    }

    // ---- warnings -------------------------------------------------------------------------

    // Boxed red warnings around the HUD centre, as in Ace Combat:
    //   above: MISSILE while a missile is tracking you (the game's own known-missile list), else
    //          CAUTION while an enemy radar has you targeted (locked);
    //   below: PULL UP when about to hit the ground, else STALL while stalling.
    // The game's own missile distances stay above the map (they belong to the native map panel).
    private static Tag _caution, _pullUp;
    private static Aircraft _radarAircraft;
    private static float _lockedUntil;

    // The game raises a radar warning on every enemy radar sweep. "isTarget" covers ground and
    // ship radars; for aircraft the emitter's own target list is checked, as the game does elsewhere.
    private static void WatchRadar(Aircraft aircraft)
    {
        if (_radarAircraft == aircraft) return;
        if (_radarAircraft != null) _radarAircraft.onRadarWarning -= OnRadarWarning;
        _radarAircraft = aircraft;
        _lockedUntil = 0f;
        if (aircraft != null) aircraft.onRadarWarning += OnRadarWarning;
    }

    private static void OnRadarWarning(Aircraft.OnRadarWarning e)
    {
        bool locked = e.isTarget;
        if (!locked && e.detected && e.emitter != null && _radarAircraft != null)
        {
            try { locked = e.emitter.CheckIsTarget(_radarAircraft); }
            catch { locked = false; }
        }
        if (locked) _lockedUntil = Time.timeSinceLevelLoad + 2.5f;
    }

    private static Tag MakeWarning(string name, Vector2 position)
    {
        Tag tag = MakeTag(_root.transform as RectTransform);
        tag.Rect.name = name;
        tag.Rect.anchoredPosition = position;
        tag.Text.fontSize = 18;
        tag.Text.characterSpacing = 6f;
        return tag;
    }

    private static void UpdateWarnings(Aircraft aircraft)
    {
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        Color alert = theme != null ? theme.Alert : new Color(1f, 0.25f, 0.2f);
        bool flash = Mathf.Sin(Time.timeSinceLevelLoad * 12f) > -0.3f;

        // Stall: the same test as the game's stall horn.
        AoADisplay aoaApp = App<AoADisplay>();
        bool stalling = false;
        if (aoaApp != null && aircraft.cockpit != null && aircraft.cockpit.rb != null)
        {
            Vector3 v = aircraft.cockpit.transform.InverseTransformDirection(aircraft.cockpit.rb.velocity);
            float aoa = Mathf.Atan2(v.y, v.z) * -57.29578f;
            stalling = aircraft.speed > GetFloat(aoaApp, "velocityThreshold", 10f)
                       && aoa > GetFloat(aoaApp, "hornThreshold", float.MaxValue);
        }
        // Ground: descending with under 5 s to impact (gear up, so landings stay quiet).
        float climb = Vector3.Dot(aircraft.CockpitRB().velocity, Vector3.up);
        bool ground = climb < -5f && aircraft.radarAlt < 1500f
                      && aircraft.radarAlt / -climb < 5f
                      && aircraft.gearState == LandingGear.GearState.LockedRetracted;
        // Missile: any missile the aircraft's warning system currently knows about.
        bool missile = false;
        MissileWarning warning = aircraft.GetMissileWarningSystem();
        if (warning != null && warning.knownMissiles != null) missile = warning.knownMissiles.Count > 0;

        WatchRadar(aircraft);
        bool locked = Time.timeSinceLevelLoad < _lockedUntil;
        string top = missile ? "MISSILE" : locked ? "CAUTION" : null;
        string bottom = ground ? "PULL UP" : stalling ? "STALL" : null;
        if (HudLayoutEditor.IsOpen)
        {
            // Preview both so they can be placed in the editor.
            top ??= "CAUTION";
            bottom ??= "PULL UP";
            flash = true;
        }
        SetWarning(_caution, top, alert, flash);
        SetWarning(_pullUp, bottom, alert, flash);
    }

    private static void SetWarning(Tag tag, string text, Color color, bool on)
    {
        if (tag == null) return;
        bool show = text != null;
        if (tag.Rect.gameObject.activeSelf != show) tag.Rect.gameObject.SetActive(show);
        if (!show) return;
        tag.Text.text = text;
        float width = tag.Text.GetPreferredValues(text).x + 28f;
        tag.Rect.sizeDelta = new Vector2(width, 28f);
        Color c = on ? color : new Color(color.r, color.g, color.b, 0.35f);
        tag.Text.color = c;
        // Thunder: War Thunder style warnings are bare red text, no box.
        // Plus: the same bare text, in the game's own HUD font.
        foreach (Image edge in tag.Edges) if (edge != null) edge.color = _thunder || _plus ? Color.clear : c;
        if (_thunder) tag.Text.fontSize = 24;
        else if (_plus) tag.Text.fontSize = 22;
    }

    // ---- nozzle / wing / tilt gauges ------------------------------------------------------

    // These are animated pictures (an icon that swings with the nozzle, wing or rotor angle), which
    // say more than a number. They already use the HUD green, so they are moved, not redrawn. The
    // game keeps driving them (including their 5 s fade) while they sit on our canvas.
    private sealed class Borrowed
    {
        internal RectTransform Node;
        internal Transform Parent;
        internal int Sibling;
        internal Vector3 LocalPosition, LocalScale;
        internal Quaternion LocalRotation;
        internal Vector2 AnchorMin, AnchorMax, Pivot, SizeDelta, AnchoredPosition;
    }

    private static RectTransform _vector;
    private static readonly List<Borrowed> BorrowedGauges = new();

    // What goes in the tray, right to left: nozzle, wing, the IR-signature ring of the game's
    // StatusGauges (some aircraft, including modded ones, put it in their HUD extras), then any
    // extra gauge types the user lists in ArcadeExtraGauges. Aim cues are never moved.
    private static List<RectTransform> TrayNodes()
    {
        var nodes = new List<RectTransform>();
        void Add(Transform t) { if (t is RectTransform r && !nodes.Contains(r)) nodes.Add(r); }
        if (App<NozzleGauge>() != null) Add(App<NozzleGauge>().transform);
        // Plus keeps the game's gear / brake and autopilot indicators, gathered here with the other
        // aircraft-state gauges so they stay on screen instead of riding on the aircraft.
        if (_plus && App<GearIndicator>() != null) Add(App<GearIndicator>().transform);
        if (_plus && App<AutopilotIndicator>() != null) Add(App<AutopilotIndicator>().transform);
        if (App<WingAngleGauge>() != null) Add(App<WingAngleGauge>().transform);
        Transform ir = IrRing(App<StatusGauges>());
        if (ir != null) Add(ir);
        string extra = ExtraGauges != null ? ExtraGauges.Value : "";
        if (!string.IsNullOrWhiteSpace(extra))
            foreach (string raw in extra.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                foreach (HUDApp app in Native.Values)
                    if (app != null && (app.GetType().Name == name || app.GetType().FullName == name)) Add(app.transform);
            }
        return nodes;
    }

    // The IR ring is one of several parts of StatusGauges; take the smallest node that holds it.
    private static Transform IrRing(StatusGauges gauges)
    {
        if (gauges == null) return null;
        object ring = AccessTools.Field(typeof(StatusGauges), "irLevelGauge")?.GetValue(gauges);
        if (ring == null) return null;
        Transform common = null;
        foreach (string field in new[] { "title", "image", "circle", "reading", "needle" })
        {
            object part = AccessTools.Field(ring.GetType(), field)?.GetValue(ring);
            Transform t = part is Component c ? c.transform : part as Transform;
            if (t == null) continue;
            common = common == null ? t : CommonParent(common, t);
        }
        // Never take the whole app (or anything above it): its other parts duplicate Arcade gauges.
        return common != null && common != gauges.transform && common.IsChildOf(gauges.transform) ? common : null;
    }

    private static Transform CommonParent(Transform a, Transform b)
    {
        for (Transform p = a; p != null; p = p.parent)
            if (b == p || b.IsChildOf(p)) return p;
        return null;
    }

    private static void BorrowVectorGauges()
    {
        const float gap = 18f;
        // Thunder lines gauges up left-to-right from the tray's left edge (beside the silhouette);
        // the other styles go right-to-left from its right edge.
        bool leftToRight = _thunder;
        float side = leftToRight ? 1f : -1f;
        float cursor = 0f, tallest = 0f;   // x of the next gauge's leading edge, from the tray's anchored edge
        // Plus: the gear and brake/autopilot indicators share one column (brake above gear), so the
        // wing / nozzle gauges sit closer to the silhouette.
        Transform gearNode = _plus ? App<GearIndicator>()?.transform : null;
        Transform apNode = _plus ? App<AutopilotIndicator>()?.transform : null;
        RectTransform stackBase = null;
        float stackWidth = 0f, stackHeight = 0f, stackCentre = 0f, stackCentreX = 0f, stackCentreY = 0f;
        foreach (RectTransform node in TrayNodes())
        {
            bool isGear = gearNode != null && node == gearNode, isAp = apNode != null && node == apNode;
            var b = new Borrowed
            {
                Node = node, Parent = node.parent, Sibling = node.GetSiblingIndex(),
                LocalPosition = node.localPosition, LocalScale = node.localScale, LocalRotation = node.localRotation,
                AnchorMin = node.anchorMin, AnchorMax = node.anchorMax, Pivot = node.pivot,
                SizeDelta = node.sizeDelta, AnchoredPosition = node.anchoredPosition,
            };
            Vector2 size = node.rect.size;
            float ratio = Mathf.Clamp(node.lossyScale.x / Mathf.Max(0.0001f, _vector.lossyScale.x), 0.1f, 10f);
            node.SetParent(_vector, false);
            node.anchorMin = node.anchorMax = new Vector2(leftToRight ? 0f : 1f, 0.5f);
            node.sizeDelta = size;
            node.localRotation = Quaternion.identity;
            node.localScale = Vector3.one * ratio;
            node.anchoredPosition = Vector2.zero;
            // Measure what it actually draws, then line it up right-to-left, vertically centred.
            DrawnBounds(node, out Vector2 lo, out Vector2 hi);
            float width = Mathf.Abs(hi.x - lo.x), height = Mathf.Abs(hi.y - lo.y);
            // Aircraft draw these gauges at very different sizes (a swing-wing gauge can be three
            // times a nozzle gauge). Shrink anything larger than one tray slot, keeping its shape,
            // so the tray has the same footprint on every aircraft; smaller gauges stay as they are.
            // Never below half size, so a mismeasured gauge stays readable.
            // Small gauges are enlarged (up to 1.6x) so a lone nozzle gauge reads clearly.
            float slot = isGear || isAp ? TraySlot * 0.45f : TraySlot;   // stacked icons get half a slot each
            float fit = Mathf.Clamp(slot / Mathf.Max(1f, Mathf.Max(width, height)), 0.5f, 1.6f);
            Plugin.Logger.LogInfo($"Arcade HUD tray: '{node.name}' draws {width:F0}x{height:F0} (largest piece '{_largestName}'), scale {fit:F2}");
            if (Mathf.Abs(fit - 1f) > 0.001f)
            {
                node.localScale *= fit;
                DrawnBounds(node, out lo, out hi);
                width = Mathf.Abs(hi.x - lo.x);
                height = Mathf.Abs(hi.y - lo.y);
            }
            float centreX = (lo.x + hi.x) * 0.5f, centreY = (lo.y + hi.y) * 0.5f;
            // Vertically, centre what the gauge can reach as its icons swing, not just its pose now.
            DrawnBounds(node, out Vector2 sweepLo, out Vector2 sweepHi, includeSweep: true);
            centreY = (sweepLo.y + sweepHi.y) * 0.5f;
            if (isAp && stackBase != null)
            {
                // Above the gear indicator, the pair centred vertically as one column.
                float columnWidth = Mathf.Max(stackWidth, width);
                float column = stackCentre + side * (columnWidth - stackWidth) * 0.5f;
                cursor += side * (columnWidth - stackWidth);
                float total = stackHeight + 6f + height;
                stackBase.anchoredPosition = new Vector2(column - stackCentreX, -total * 0.5f + stackHeight * 0.5f - stackCentreY);
                node.anchoredPosition = new Vector2(column - centreX, total * 0.5f - height * 0.5f - centreY);
                tallest = Mathf.Max(tallest, total);
                stackBase = null;
                BorrowedGauges.Add(b);
                continue;
            }
            node.anchoredPosition = new Vector2(cursor + side * width * 0.5f - centreX, -centreY);
            if (isGear)
            {
                stackBase = node; stackWidth = width; stackHeight = height;
                stackCentre = cursor + side * width * 0.5f; stackCentreX = centreX; stackCentreY = centreY;
            }
            cursor += side * (width + gap);
            tallest = Mathf.Max(tallest, height);
            BorrowedGauges.Add(b);
        }
        if (BorrowedGauges.Count > 0) _vector.sizeDelta = new Vector2(Mathf.Abs(cursor) - gap, Mathf.Max(tallest, 40f));
        _trayExtentValid = false;
        foreach (Borrowed b in BorrowedGauges)
        {
            DrawnBounds(b.Node, out Vector2 lo, out Vector2 hi, includeSweep: true);
            _trayLo = _trayExtentValid ? Vector2.Min(_trayLo, lo) : lo;
            _trayHi = _trayExtentValid ? Vector2.Max(_trayHi, hi) : hi;
            _trayExtentValid = true;
        }
        FitStripToTray();
    }

    // The area a gauge really draws, in the tray's space: its own rect plus every visible graphic
    // under it (the IR ring's parts, for example, reach well outside their parent's rect).
    private static float _largestArea;
    private static string _largestName = "";
    // Everything the tray can draw (gauges, labels, swinging icons), in the tray's own space.
    private static Vector2 _trayLo, _trayHi;
    private static bool _trayExtentValid;

    // Slides the tray back inside the screen when any part of it would hang off an edge, e.g. a
    // gauge label that sits above the gauge when the tray is near the top of the screen.
    private static void KeepTrayOnScreen()
    {
        if (!_trayExtentValid || _vector == null || _minimal || !(_root.transform is RectTransform root)) return;
        const float margin = 6f;
        Vector2 a = root.InverseTransformPoint(_vector.TransformPoint(_trayLo));
        Vector2 b = root.InverseTransformPoint(_vector.TransformPoint(_trayHi));
        Vector2 lo = Vector2.Min(a, b), hi = Vector2.Max(a, b);
        Rect area = root.rect;
        Vector2 shift = Vector2.zero;
        if (hi.x > area.xMax - margin) shift.x = area.xMax - margin - hi.x;
        if (lo.x + shift.x < area.xMin + margin) shift.x = area.xMin + margin - lo.x;
        if (hi.y > area.yMax - margin) shift.y = area.yMax - margin - hi.y;
        if (lo.y + shift.y < area.yMin + margin) shift.y = area.yMin + margin - lo.y;
        if (shift != Vector2.zero) _vector.anchoredPosition += shift;
    }

    // Reused every frame by DrawnBounds (it runs per frame while the tray is kept on screen).
    private static readonly List<Graphic> BoundsGraphics = new();
    private static readonly Vector3[] BoundsCorners = new Vector3[4];
    private static float _nextTrayLook;

    private static void DrawnBounds(RectTransform node, out Vector2 lo, out Vector2 hi, bool includeSweep = false)
    {
        _largestArea = 0f;
        _largestName = "";
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        Vector3[] c = BoundsCorners;
        void Add(RectTransform r)
        {
            r.GetWorldCorners(c);
            foreach (Vector3 p in c)
            {
                Vector2 local = _vector.InverseTransformPoint(p);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
        }
        // Only pieces that actually draw something: visible colour, and a sprite or real text.
        // Partly drawn ("filled") images are skipped: a swing-wing gauge's arc is a 430px ring of
        // which only a slice shows, so its frame says nothing about the visible size.
        // Gauges carry invisible helpers (transparent images, large hit areas) that would make
        // them measure far bigger than they look. The node's own rect is used only as a fallback.
        bool any = false;
        node.GetComponentsInChildren(false, BoundsGraphics);
        foreach (Graphic g in BoundsGraphics)
        {
            if (!g.enabled || g.color.a < 0.05f) continue;
            if (g is Image image && (image.sprite == null || image.type == Image.Type.Filled)) continue;
            if (g is TMP_Text text && string.IsNullOrWhiteSpace(text.text)) continue;
            Vector2 size = g.rectTransform.rect.size;
            if (size.x > 600f || size.y > 600f) continue;
            Add(g.rectTransform);
            if (g is TMP_Text drawn && drawn.textInfo != null && drawn.textInfo.characterCount > 0)
            {
                // Text can overflow its box (a long "NOZZLE 90°" label); include what it renders.
                Bounds tb = drawn.textBounds;
                foreach (Vector3 corner in new[] { tb.min, tb.max, new Vector3(tb.min.x, tb.max.y), new Vector3(tb.max.x, tb.min.y) })
                {
                    Vector2 local = _vector.InverseTransformPoint(drawn.rectTransform.TransformPoint(corner));
                    min = Vector2.Min(min, local);
                    max = Vector2.Max(max, local);
                }
            }
            any = true;
            float area = size.x * size.y;
            if (area > _largestArea) { _largestArea = area; _largestName = g.name + $" {size.x:F0}x{size.y:F0}"; }
        }
        if (!any) Add(node);
        // Optionally include the whole sweep of icons that rotate (nozzle and wing icons, the input
        // pivot): a square around each pivot, as wide as its furthest visible point. Used only to
        // place a gauge vertically, so a nozzle turned to 90 degrees still stays in its slot.
        HUDApp app = includeSweep ? node.GetComponent<HUDApp>() : null;
        if (app != null)
            foreach (string field in new[] { "nozzleIcon", "wingIcon", "inputPivot" })
            {
                if (!(AccessTools.Field(app.GetType(), field)?.GetValue(app) is Transform pivot)) continue;
                float reach = 0f;
                foreach (Graphic g in pivot.GetComponentsInChildren<Graphic>(false))
                {
                    if (!g.enabled || g.color.a < 0.05f) continue;
                    if (g is Image image && (image.sprite == null || image.type == Image.Type.Filled)) continue;
                    g.rectTransform.GetWorldCorners(c);
                    foreach (Vector3 p in c) reach = Mathf.Max(reach, Vector3.Distance(p, pivot.position));
                }
                if (reach <= 0f) continue;
                Vector2 centre = _vector.InverseTransformPoint(pivot.position);
                float r = reach / Mathf.Max(0.0001f, _vector.lossyScale.x);
                min = Vector2.Min(min, centre - new Vector2(r, r));
                max = Vector2.Max(max, centre + new Vector2(r, r));
            }
        lo = min;
        hi = max;
    }

    // Put the gauges back where the game had them. If the aircraft's HUD extras are gone (the
    // aircraft was destroyed), the gauge would otherwise outlive it on our canvas, so destroy it.
    private static void ReturnVectorGauges()
    {
        foreach (Borrowed b in BorrowedGauges)
        {
            if (b.Node == null) continue;
            if (b.Parent == null)
            {
                Object.Destroy(b.Node.gameObject);
                continue;
            }
            b.Node.SetParent(b.Parent, false);
            b.Node.anchorMin = b.AnchorMin;
            b.Node.anchorMax = b.AnchorMax;
            b.Node.pivot = b.Pivot;
            b.Node.sizeDelta = b.SizeDelta;
            b.Node.anchoredPosition = b.AnchoredPosition;
            b.Node.localPosition = b.LocalPosition;
            b.Node.localRotation = b.LocalRotation;
            b.Node.localScale = b.LocalScale;
            b.Node.SetSiblingIndex(Mathf.Min(b.Sibling, b.Parent.childCount - 1));
        }
        BorrowedGauges.Clear();
        FitStripToTray();
    }

    private static void CheckBorrowedOwners()
    {
        for (int i = BorrowedGauges.Count - 1; i >= 0; i--)
        {
            Borrowed b = BorrowedGauges[i];
            if (b.Parent != null && b.Node != null) continue;
            if (b.Node != null) Object.Destroy(b.Node.gameObject);
            BorrowedGauges.RemoveAt(i);
        }
    }

    // ---- native readouts mirrored into Arcade ---------------------------------------------

    // The game keeps calling Refresh on its gauges even while they are hidden, so their text and
    // colours stay current. Arcade reads them instead of re-deriving each formula.
    private static readonly Dictionary<System.Type, HUDApp> Native = new();
    private static readonly List<(CanvasGroup group, bool owned)> HiddenExtras = new();

    private static void FindNative(FlightHud hud)
    {
        Native.Clear();
        Transform centre = hud.GetHUDCenter();
        if (centre == null) return;
        // Only the flight HUD's own apps (helmet duplicates live elsewhere, under HMDCenter).
        foreach (HUDApp app in centre.GetComponentsInChildren<HUDApp>(true))
            if (app != null && !Native.ContainsKey(app.GetType())) Native[app.GetType()] = app;
    }

    private static T App<T>() where T : HUDApp =>
        Native.TryGetValue(typeof(T), out HUDApp app) && app != null ? (T)app : null;

    private static TF Get<TF>(HUDApp app, string field) where TF : class =>
        app == null ? null : AccessTools.Field(app.GetType(), field)?.GetValue(app) as TF;

    private static float GetFloat(HUDApp app, string field, float fallback)
    {
        object value = app == null ? null : AccessTools.Field(app.GetType(), field)?.GetValue(app);
        return value is float f ? f : fallback;
    }

    // Gear, auto-hover and nozzle are shown as Arcade tags, so their native icons are made invisible
    // (they keep running, including their voice warnings).
    private static void HideExtras(bool hide)
    {
        foreach (var (group, owned) in HiddenExtras)
            if (group != null) { if (owned) Object.Destroy(group); else group.alpha = 1f; }
        HiddenExtras.Clear();
        if (!hide) return;
        var toHide = new List<HUDApp> { App<GearIndicator>(), App<AutopilotIndicator>() };
        if (IrRing(App<StatusGauges>()) != null) toHide.Add(App<StatusGauges>());
        foreach (HUDApp app in toHide)
        {
            if (app == null) continue;
            CanvasGroup group = app.GetComponent<CanvasGroup>();
            bool owned = group == null;
            if (owned) group = app.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            HiddenExtras.Add((group, owned));
        }
    }

    private sealed class Tag
    {
        internal RectTransform Rect;
        internal TextMeshProUGUI Text;
        internal readonly List<Image> Edges = new();
    }

    private static RectTransform _status;
    private static readonly List<Tag> Tags = new();

    private static Tag MakeTag(RectTransform parent)
    {
        var tag = new Tag { Rect = Rect("Tag", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 24f)) };
        int before = tag.Rect.childCount;
        Frame(tag.Rect, Green, 1.5f);
        for (int i = before; i < tag.Rect.childCount; i++) tag.Edges.Add(tag.Rect.GetChild(i).GetComponent<Image>());
        tag.Text = Text("Text", tag.Rect, 15, TextAlignmentOptions.Center);
        tag.Text.characterSpacing = 4f;
        RectTransform rt = tag.Text.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        tag.Rect.gameObject.SetActive(false);
        return tag;
    }

    private static readonly List<(string text, Color color)> TagValues = new();

    private static void UpdateTags(Aircraft aircraft)
    {
        TagValues.Clear();
        bool flash = Mathf.Sin(Time.timeSinceLevelLoad * 16f) > 0f;
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        Color warning = theme != null ? theme.Warning : new Color(1f, 0.8f, 0.2f);

        // Gear: green when down; flashing in the game's alert colour when down above its speed
        // limit; flashing amber while moving.
        GearIndicator gear = App<GearIndicator>();
        Image gearIcon = Get<Image>(gear, "gearIcon");
        bool moving = aircraft.gearState != LandingGear.GearState.LockedRetracted
                      && aircraft.gearState != LandingGear.GearState.LockedExtended;
        if (moving)
            TagValues.Add(("GEAR", flash ? warning : Faded(0.35f)));
        else if (gearIcon != null && gearIcon.enabled)
        {
            bool alert = theme != null && ColorsClose(gearIcon.color, theme.Alert);
            TagValues.Add(("GEAR", alert ? (flash ? theme.Alert : Faded(0.35f)) : Green));
        }
        Image brake = Get<Image>(gear, "brakeIcon");
        if (brake != null && brake.enabled) TagValues.Add(("BRAKE", Green));

        Image auto = Get<Image>(App<AutopilotIndicator>(), "icon");
        if (auto != null && auto.enabled)
            TagValues.Add(("AUTO HOVER", auto.color.r > 0.5f ? warning : Green));


        // Lay the visible tags out as one centred row.
        const float gap = 10f, pad = 14f;
        float total = 0f;
        for (int i = 0; i < Tags.Count; i++)
        {
            bool used = i < TagValues.Count;
            Tags[i].Rect.gameObject.SetActive(used);
            if (!used) continue;
            Tags[i].Text.text = TagValues[i].text;
            float width = Tags[i].Text.GetPreferredValues(TagValues[i].text).x + pad * 2f;
            Tags[i].Rect.sizeDelta = new Vector2(width, 24f);
            Tags[i].Text.color = TagValues[i].color;
            foreach (Image edge in Tags[i].Edges) if (edge != null) edge.color = TagValues[i].color;
            total += width + (i > 0 ? gap : 0f);
        }
        float x = -total / 2f;
        for (int i = 0; i < TagValues.Count && i < Tags.Count; i++)
        {
            float w = Tags[i].Rect.sizeDelta.x;
            Tags[i].Rect.anchoredPosition = new Vector2(x + w / 2f, 0f);
            x += w + gap;
        }
        _status.sizeDelta = new Vector2(Mathf.Max(total, 120f), 26f);
    }

    private static bool ColorsClose(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.15f;

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    private static TextMeshProUGUI ListLine(string name)
    {
        TextMeshProUGUI t = Text(name, _list, 20, TextAlignmentOptions.Right);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(1f, 1f);
        t.rectTransform.sizeDelta = new Vector2(ListWidth, RowHeight);
        return t;
    }

    // Each weapon icon is a faint "ghost" of the full icon with a filled copy on top. The fill
    // shows a round being ready (full) or reloading (revealed top to bottom as it reloads).
    private static Image MakeIcon(string name, out Image fill)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_damage, false);
        var image = go.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // Sized in the icon's own (sideways) orientation; the rotation then stands it upright.
        rt.sizeDelta = new Vector2(IconLong, IconShort);

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(go.transform, false);
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        fill = fillGo.GetComponent<Image>();
        fill.preserveAspect = true;
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        Thicken(fill);
        return image;
    }

    private static readonly List<float> PendingScratch = new();
    private static readonly List<float> PylonScratch = new();

    /// <summary>
    /// Fill (0..1) per pylon of a station: loaded pylons full, reloading ones each at their own
    /// progress (soonest first, next to the loaded ones), the rest empty. True for pylon weapons
    /// (one round per mount: missiles, bombs); false for guns and pods, which get one value, the
    /// share of a full load that is loaded.
    /// </summary>
    private static bool PylonFills(WeaponStation st, int index, List<float> fills)
    {
        fills.Clear();
        if (st == null || st.Weapons == null || st.Weapons.Count == 0) return false;
        bool pylons = st.Weapons.Count <= 12, lostMount = false;
        int full = 0;
        foreach (Weapon w in st.Weapons)
        {
            if (w == null) { pylons = false; continue; }
            int f = w.GetFullAmmo();
            full += f;
            if (f != 1) pylons = false;
            // Same rule as Ace Combat Reloads: a station that lost a mount is no longer reloaded.
            if (!w.IsAttached() && w.GetAmmoTotal() < f) lostMount = true;
        }
        int loaded = st.GetAmmoLoaded();
        if (!pylons)
        {
            fills.Add(full > 0 ? Mathf.Clamp01((float)loaded / full) : 0f);
            return false;
        }
        if (lostMount) PendingScratch.Clear();
        else ReloadsCompat.PendingProgress(index, PendingScratch);
        int p = 0;
        for (int i = 0; i < st.Weapons.Count; i++)
            fills.Add(i < loaded ? 1f : p < PendingScratch.Count ? PendingScratch[p++] : 0f);
        return true;
    }

    // Two icons = the next two rounds, as in Ace Combat (two, for Arcade's symmetry). With the
    // Reloads mod, each empty icon fills from the top with its own round's reload.
    private static void UpdateIcons(Aircraft aircraft)
    {
        WeaponStation current = aircraft.weaponManager != null ? aircraft.weaponManager.currentWeaponStation : null;
        int index = current != null && aircraft.weaponStations != null ? aircraft.weaponStations.IndexOf(current) : -1;
        if (_wingCluster || _minimal)
        {
            UpdateReloadBars(current, index);
            if (!_damageIcons) return;
        }
        if (_iconLeftFill == null || _thunder) return;
        int loaded = current != null ? current.GetAmmoLoaded() : 0;
        float first, second;
        if (PylonFills(current, index, PylonScratch))
        {
            first = PylonScratch.Count > 0 ? PylonScratch[0] : 0f;
            second = PylonScratch.Count > 1 ? PylonScratch[1] : 0f;
        }
        else
        {
            float progress = index >= 0 ? Mathf.Max(0f, ReloadsCompat.Progress(index)) : 0f;
            first = loaded >= 1 ? 1f : progress;
            second = loaded >= 2 ? 1f : loaded == 1 ? progress : 0f;
        }
        float turn = IconRotation != null ? IconRotation.Value : -90f;
        SetFill(_iconLeftFill, first, turn);
        SetFill(_iconRightFill, second, turn);
    }

    // "Top of the screen" depends on how far the icon is turned to stand upright.
    private static void SetFill(Image image, float amount, float turn)
    {
        float a = Mathf.Abs(turn);
        if (a < 45f || a > 135f)
        {
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = a < 45f ? (int)Image.OriginVertical.Top : (int)Image.OriginVertical.Bottom;
        }
        else
        {
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = turn < 0f ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
        }
        image.fillAmount = Mathf.Clamp01(amount);
    }

    // Thin line art reads as a faint "ghost" at HUD sizes; a 1px same-colour outline thickens it.
    private static void Thicken(Graphic graphic)
    {
        var outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = Faded(0.55f);
        outline.effectDistance = new Vector2(0.8f, -0.8f);
        outline.useGraphicAlpha = true;
    }

    // Arcade's weapon list: pivot at the bottom, it sits on the silhouette cluster and grows upward.
    private static void BuildArcadeList()
    {
        _list = Rect("Weapons", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), ListBase,
            new Vector2(ListWidth, RowHeight));
        _divider = ListLine("Divider");
        _flare = ListLine("Flares");
        if (_builtCapacitorBar) _weaponCap = BuildCapacitorBar(_list, Vector2.one, Vector2.zero, 190f, RowHeight, 14f);
        else _capLine = ListLine("Capacitor");
    }

    // A readout block: spaced label, outlined value box, two small detail lines, and a slim vertical
    // gauge on the side facing the aircraft (like Ace Combat's tapes).
    internal const int FuelTapeItem = HudElementPositions.FuelTape, ThrottleTapeItem = HudElementPositions.ThrottleTape;
    private static RectTransform _fuelTape, _throttleTape;
    // Fuel and throttle readouts added from another style, updated alongside the style's own.
    private static readonly List<Gauge> ExtraFuel = new(), ExtraThrottle = new();
    private static Vector2 _fuelTapeBase, _throttleTapeBase;
    private static int _builtSpeedSkin, _builtAltSkin, _builtFeedSkin, _builtDamageSkin;
    private static TextHalo.Strength _builtContrast;
    private static bool _damageIcons, _builtRwrAway, _wingCluster, _builtStarWheel;
    private static int _builtListSkin;

    // The speed or altitude block in its chosen skin (0 = the style's own): every skin shows the
    // same reading and detail lines, only the look differs.
    private static RectTransform ReadoutBlock(bool speed, bool arcadeStyle)
    {
        int skin = ElementLooks.SkinOf(speed ? SpeedBlock : AltBlock);
        if (speed) _builtSpeedSkin = skin; else _builtAltSkin = skin;
        bool arcadeLook = skin == 1 || (skin == 0 && arcadeStyle);
        if (speed)
            return arcadeLook ? Block("Speed", true, "SPEED", out _speedValue, out _speedExtra)
                : WingmanBlock("Speed", true, "SPD", out _speedValue, out _speedExtra);
        return arcadeLook ? Block("Altitude", false, "ALT", out _altValue, out _altExtra)
            : WingmanBlock("Altitude", false, "ALT", out _altValue, out _altExtra);
    }

    // Arcade's slim tape on its own, where it used to sit beside its block (toward the aircraft).
    private static RectTransform Tape(bool leftOfAircraft, string label, Vector2 blockBase, out Gauge gauge, out Vector2 basePosition)
    {
        float side = leftOfAircraft ? 1f : -1f;
        basePosition = blockBase + new Vector2(side * GaugeGap, 0f);
        RectTransform holder = Rect(label == "FUEL" ? "FuelTape" : "ThrottleTape", _root.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), basePosition, new Vector2(40f, GaugeHeight + 48f));
        gauge = MakeGauge(holder, leftOfAircraft, label, centred: true);
        return holder;
    }

    private static RectTransform Block(string name, bool leftOfAircraft, string label,
        out TextMeshProUGUI value, out TextMeshProUGUI extra)
    {
        float inner = leftOfAircraft ? 1f : 0f;   // x of the edge nearest the aircraft
        float outer = 1f - inner;
        TextAlignmentOptions align = leftOfAircraft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        RectTransform rect = Rect(name, _root.transform, new Vector2(0.5f, 0.5f), new Vector2(inner, 0.5f),
            leftOfAircraft ? SpeedBase : AltBase, new Vector2(200f, 104f));

        TextMeshProUGUI title = Text("Label", rect, 15, align);
        title.text = label;
        title.characterSpacing = 8f;
        title.color = Faded(0.85f);
        Pin(title.rectTransform, new Vector2(inner, 1f), new Vector2(170f, 20f), new Vector2(0f, 0f));

        // Value box: thin outline, value text inset from the aircraft-side edge.
        RectTransform box = Rect("ValueBox", rect, new Vector2(inner, 1f), new Vector2(inner, 1f),
            new Vector2(0f, -22f), new Vector2(150f, 40f));
        Frame(box, Faded(0.9f), 1.5f);
        value = Text("Value", box, 30, align);
        Pin(value.rectTransform, new Vector2(inner, 0.5f), new Vector2(140f, 40f),
            new Vector2(leftOfAircraft ? -8f : 8f, 0f));

        extra = Text("Extra", rect, 15, align);
        extra.color = Faded(0.8f);
        extra.lineSpacing = -10f;
        Pin(extra.rectTransform, new Vector2(inner, 1f), new Vector2(170f, 40f), new Vector2(0f, -66f));
        return rect;
    }

    private static void Pin(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, anchor.y >= 1f ? 1f : anchor.y <= 0f ? 0f : 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    // Slim tape: a 2px spine with end caps, ticks every 10% (longer at 0/50/100) pointing at the
    // aircraft, a narrow fill on the text side, a level marker, the label above and the value below.
    private static Gauge MakeGauge(RectTransform block, bool leftOfAircraft, string label, bool centred = false)
    {
        float inner = leftOfAircraft ? 1f : 0f;
        float side = leftOfAircraft ? 1f : -1f;    // +1 means "towards the aircraft" is +x
        // Beside a block (at its aircraft-side edge), or centred in a holder of its own.
        RectTransform g = centred
            ? Rect("Gauge", block, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, GaugeHeight))
            : Rect("Gauge", block, new Vector2(inner, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(side * GaugeGap, 0f), new Vector2(2f, GaugeHeight));
        Color line = Faded(0.9f);
        Fill(g, line);                                            // spine
        Line(g, new Vector2(0.5f, 1f), new Vector2(side * 4f, -1f), new Vector2(10f, 2f), line);   // caps
        Line(g, new Vector2(0.5f, 0f), new Vector2(side * 4f, 1f), new Vector2(10f, 2f), line);
        for (int i = 1; i < 10; i++)
        {
            bool major = i == 5;
            float length = major ? 9f : 5f;
            Line(g, new Vector2(0.5f, i / 10f), new Vector2(side * (length / 2f + 1f), 0f),
                new Vector2(length, 1.5f), Faded(major ? 0.9f : 0.6f));
        }
        // Fill runs up the text side of the spine.
        RectTransform fill = Rect("Fill", g, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-side * TapeFillOffset, 0f), new Vector2(TapeFillWidth, 0f));
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.raycastTarget = false;
        // Level marker across the spine at the current value.
        RectTransform level = Rect("Level", g, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(16f, 2f));
        Image levelImage = level.gameObject.AddComponent<Image>();
        levelImage.color = Green;
        levelImage.raycastTarget = false;

        TextMeshProUGUI top = Text("GaugeLabel", g, 14, TextAlignmentOptions.Center);
        top.text = label;
        top.characterSpacing = 6f;
        top.color = Faded(0.85f);
        Pin(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(70f, 18f), new Vector2(0f, 22f));
        top.rectTransform.pivot = new Vector2(0.5f, 1f);
        TextMeshProUGUI percent = Text("GaugePercent", g, 15, TextAlignmentOptions.Center);
        Pin(percent.rectTransform, new Vector2(0.5f, 0f), new Vector2(160f, 18f), new Vector2(0f, -4f));
        percent.rectTransform.pivot = new Vector2(0.5f, 1f);
        return new Gauge { Fill = fill, FillImage = fillImage, Level = level, Percent = percent };
    }

    private static void Fill(RectTransform rt, Color color)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void Line(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        RectTransform rt = Rect("Line", parent, anchor, new Vector2(0.5f, 0.5f), pos, size);
        Fill(rt, color);
    }

    // A thin rectangular outline drawn with four edge strips.
    private static void Frame(RectTransform parent, Color color, float width)
    {
        Edge(parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, width), color);
        Edge(parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, width), color);
        Edge(parent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(width, 0f), color);
        Edge(parent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(width, 0f), color);
    }

    private static void Edge(RectTransform parent, Vector2 min, Vector2 max, Vector2 size, Color color)
    {
        RectTransform e = Rect("Edge", parent, min, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        e.anchorMin = min;
        e.anchorMax = max;
        e.sizeDelta = size;
        Fill(e, color);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.font = _font;
        if (_fontMaterial != null) t.fontSharedMaterial = _fontMaterial;
        t.fontSize = size;
        t.color = Green;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        // High contrast: a soft dark halo behind it (not in Dashboard, whose strip is already dark).
        if (_builtContrast != TextHalo.Strength.Off && !_minimal) TextHalo.Add(t, _builtContrast);
        return t;
    }

    private static Row MakeRow(int index)
    {
        RectTransform rect = Rect("Row", _list, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -index * RowHeight), new Vector2(ListWidth, RowHeight));
        var row = new Row { Rect = rect };
        row.Marker = Text("Marker", rect, 20, TextAlignmentOptions.Left);
        Pin(row.Marker.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, RowHeight), Vector2.zero);
        row.Name = Text("Name", rect, 20, TextAlignmentOptions.Left);
        Pin(row.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(160f, RowHeight), new Vector2(20f, 0f));
        row.Ammo = Text("Ammo", rect, 20, TextAlignmentOptions.Right);
        Pin(row.Ammo.rectTransform, new Vector2(1f, 0.5f), new Vector2(110f, RowHeight), Vector2.zero);
        return row;
    }

    // ---- damage silhouette ----------------------------------------------------------------

    // A copy of the game's damage display lives on our canvas. The original is made invisible; each
    // frame the copy takes the original's part colours (red/yellow damage) with the outline always
    // shown in the HUD colour. Nothing competes with the game's own fade any more.
    private static StatusDisplay _source;
    private static GameObject _clone;
    private static CanvasGroup _sourceGroup;
    private static bool _ownsSourceGroup;
    private static readonly List<Transform> FromNodes = new(), ToNodes = new();
    private static readonly List<(Image from, Image to, PartStatusDisplay part, bool outline)> Pairs = new();
    private static FieldInfo _silhouetteField, _partsField;
    private static readonly HashSet<PartStatusDisplay> LoggedParts = new();

    private static FieldInfo _aircraftStatusField;
    private static int _damageLogs;

    private static void UpdateSilhouette(Aircraft aircraft)
    {
        // Use the aircraft's own damage display. Searching under the HUD anchor can find a stale
        // one left from an earlier spawn, which never receives this aircraft's damage.
        _aircraftStatusField ??= AccessTools.Field(typeof(Aircraft), "statusDisplay");
        StatusDisplay display = _aircraftStatusField?.GetValue(aircraft) as StatusDisplay;
        if (display != _source) CloneSilhouette(display);
        if (_builtDamageSkin == 3)
        {
            // Native skin: the game's own display shows (and fades) as in the vanilla HUD.
            HideNativeDamageAnchor(false);
            if (_sourceGroup != null && _sourceGroup.alpha != 1f) _sourceGroup.alpha = 1f;
            if (_clone != null && _clone.activeSelf) _clone.SetActive(false);
            return;
        }
        if (_clone != null && !_clone.activeSelf) _clone.SetActive(true);
        // The game's damage anchor can also hold an older display from an earlier spawn, which the
        // game may still show; hide the whole anchor, not just this aircraft's display.
        HideNativeDamageAnchor(true);
        if (_clone == null || _source == null) return;
        // The game turns its damage display back up when hit; keep it hidden, our copy is the one.
        if (_sourceGroup != null && _sourceGroup.alpha != 0f) _sourceGroup.alpha = 0f;
        for (int i = 1; i < FromNodes.Count && i < ToNodes.Count; i++)
            if (FromNodes[i] != null && ToNodes[i] != null && ToNodes[i].gameObject.activeSelf != FromNodes[i].gameObject.activeSelf)
                ToNodes[i].gameObject.SetActive(FromNodes[i].gameObject.activeSelf);
        foreach (var (from, to, part, outline) in Pairs)
        {
            if (from == null || to == null) continue;
            Color c = from.color;
            if (outline) c = Green;
            else if (part != null)
            {
                // Undamaged parts stay invisible; damaged ones show at least as strongly as the game's.
                c.a = Mathf.Max(1f - part.displayCondition, from.color.a);
                if (part.displayCondition < 0.999f && _damageLogs < 6 && LoggedParts.Add(part))
                {
                    _damageLogs++;
                    Plugin.Logger.LogInfo($"Arcade HUD silhouette: '{from.name}' damaged, condition {part.displayCondition:F2}, colour {c}, copy active {to.gameObject.activeInHierarchy}");
                }
            }
            else c.a = 1f;
            to.color = c;
        }
    }

    private static void CloneSilhouette(StatusDisplay display)
    {
        ReleaseSilhouette();
        _source = display;
        if (display == null || _damage == null) return;
        _silhouetteField ??= AccessTools.Field(typeof(StatusDisplay), "aircraftBackground");
        _partsField ??= AccessTools.Field(typeof(StatusDisplay), "statusDisplays");
        Image sourceOutline = _silhouetteField?.GetValue(display) as Image;
        var partOf = new Dictionary<Image, PartStatusDisplay>();
        if (_partsField?.GetValue(display) is List<PartStatusDisplay> parts)
            foreach (PartStatusDisplay p in parts)
                if (p?.partImage != null) partOf[p.partImage] = p;

        _clone = Object.Instantiate(display.gameObject, _damage, false);
        _clone.name = "Silhouette";
        // The copy is only a picture: remove its game logic before it ever updates.
        foreach (StatusDisplay logic in _clone.GetComponentsInChildren<StatusDisplay>(true))
            Object.DestroyImmediate(logic);
        // Keep the original's size and proportions even if it used stretch anchors, and convert
        // between the game's HUD canvas scale and ours so DamageScale is relative to the native size.
        if (_clone.transform is RectTransform cloneRect && display.transform is RectTransform sourceRect)
        {
            cloneRect.anchorMin = cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRect.pivot = sourceRect.pivot;
            cloneRect.sizeDelta = sourceRect.rect.size;
        }
        float canvasRatio = Mathf.Clamp(display.transform.lossyScale.x / Mathf.Max(0.0001f, _damage.lossyScale.x), 0.1f, 10f);
        _clone.transform.localRotation = Quaternion.identity;
        _clone.transform.localScale = Vector3.one * (DamageScale * canvasRatio);
        _clone.transform.localPosition = Vector3.zero;
        _clone.transform.SetAsFirstSibling();

        display.GetComponentsInChildren(true, FromNodes);
        _clone.GetComponentsInChildren(true, ToNodes);
        var fromImages = display.GetComponentsInChildren<Image>(true);
        var toImages = _clone.GetComponentsInChildren<Image>(true);
        Image cloneOutline = null;
        for (int i = 0; i < fromImages.Length && i < toImages.Length; i++)
        {
            bool isOutline = fromImages[i] == sourceOutline;
            if (isOutline) cloneOutline = toImages[i];
            partOf.TryGetValue(fromImages[i], out PartStatusDisplay part);
            Pairs.Add((fromImages[i], toImages[i], part, isOutline));
        }
        int partCount = 0;
        foreach (var pair in Pairs) if (pair.part != null) partCount++;
        Plugin.Logger.LogInfo($"Arcade HUD silhouette: copied '{display.name}', {Pairs.Count} images, {partCount} damage parts, outline {(cloneOutline != null ? "found" : "missing")}");
        if (cloneOutline != null)
        {
            Thicken(cloneOutline);
            // Centre the picture on the outline, not on the game's anchor point.
            var c = new Vector3[4];
            cloneOutline.rectTransform.GetWorldCorners(c);
            Vector3 centre = _damage.InverseTransformPoint((c[0] + c[2]) * 0.5f);
            _clone.transform.localPosition -= new Vector3(centre.x, centre.y, 0f);
        }

        // Hide the game's own copy (it keeps running, so damage still updates it).
        _sourceGroup = display.GetComponent<CanvasGroup>();
        _ownsSourceGroup = _sourceGroup == null;
        if (_ownsSourceGroup) _sourceGroup = display.gameObject.AddComponent<CanvasGroup>();
        _sourceGroup.alpha = 0f;
    }

    private static CanvasGroup _anchorGroup;
    private static bool _ownsAnchorGroup;

    private static void HideNativeDamageAnchor(bool hide)
    {
        if (!hide)
        {
            if (_anchorGroup != null)
            {
                if (_ownsAnchorGroup) Object.Destroy(_anchorGroup);
                else _anchorGroup.alpha = 1f;
            }
            _anchorGroup = null;
            return;
        }
        Transform anchor = SceneSingleton<FlightHud>.i != null ? SceneSingleton<FlightHud>.i.statusAnchor : null;
        if (anchor == null) return;
        if (_anchorGroup == null || _anchorGroup.transform != anchor)
        {
            HideNativeDamageAnchor(false);
            _anchorGroup = anchor.GetComponent<CanvasGroup>();
            _ownsAnchorGroup = _anchorGroup == null;
            if (_ownsAnchorGroup) _anchorGroup = anchor.gameObject.AddComponent<CanvasGroup>();
        }
        if (_anchorGroup.alpha != 0f) _anchorGroup.alpha = 0f;
    }

    private static void ReleaseSilhouette()
    {
        HideNativeDamageAnchor(false);
        if (_sourceGroup != null)
        {
            if (_ownsSourceGroup) Object.Destroy(_sourceGroup);
            else _sourceGroup.alpha = 1f;
        }
        _sourceGroup = null;
        if (_clone != null) Object.Destroy(_clone);
        _clone = null;
        _source = null;
        Pairs.Clear();
        LoggedParts.Clear();
        FromNodes.Clear();
        ToNodes.Clear();
    }

    // ---- icons ----------------------------------------------------------------------------

    private static int _iconLogs;

    // The game's weapon icons appear to be light line art on an opaque dark background, which the
    // native panel hides. Make a copy whose transparency follows the brightness, once per sprite.
    private static Sprite Cutout(Sprite source)
    {
        if (source == null) return null;
        if (Cutouts.TryGetValue(source, out Sprite cached)) return cached;
        Sprite result = null;
        try
        {
            Texture2D texture = source.texture;
            Rect r = source.textureRect;
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] pixels = copy.GetPixels32();
            // Stretch brightness so the faintest sprite's brightest pixels become fully opaque.
            int peak = 1, opaque = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int bright = Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b)) * pixels[i].a / 255;
                peak = Mathf.Max(peak, bright);
                if (pixels[i].a > 250) opaque++;
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                int bright = Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b)) * pixels[i].a / 255;
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.Min(255, bright * 255 / peak));
            }
            if (_iconLogs++ < 4)
                Plugin.Logger.LogInfo($"Arcade HUD icon '{source.name}': {copy.width}x{copy.height}, peak brightness {peak}/255, {100 * opaque / pixels.Length}% opaque in the source");
            copy.SetPixels32(pixels);
            copy.Apply();
            result = Sprite.Create(copy, new Rect(0f, 0f, copy.width, copy.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (System.Exception e)
        {
            Plugin.Logger.LogWarning("Arcade HUD: could not convert weapon icon " + source.name + ": " + e.Message);
        }
        Cutouts[source] = result;
        return result;
    }

    // ---- per-frame data -------------------------------------------------------------------

    private static void Refresh(Aircraft aircraft)
    {
        // Speed and altitude change fast; the rest does not need to be rebuilt every frame.
        float speed = aircraft.speed;
        _speedValue.text = _minimal ? BigUnits(UnitConverter.SpeedReading(speed)) : UnitConverter.SpeedReading(speed);
        // Overspeed: flash in the game's alert colour, as the native speed readout does.
        bool over = speed > GetFloat(App<SpeedGauge>(), "overspeedThreshold", float.MaxValue);
        ColorTheme colours = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        _speedValue.color = over && Mathf.Sin(Time.timeSinceLevelLoad * 16f) > 0f
            ? (colours != null ? colours.Alert : Color.red) : Green;
        string altitude = UnitConverter.AltitudeReading(aircraft.transform.position.GlobalY());
        _altValue.text = _minimal ? BigUnits(altitude) : altitude;
        if (Time.unscaledTime < _nextSlow) return;
        _nextSlow = Time.unscaledTime + 0.1f;

        // If the game's theme changes the HUD colour, rebuild with the new colour next frame.
        if (_colorSample != null)
        {
            Color now = CurrentHudColour();
            if (Mathf.Abs(now.r - _sampledColour.r) + Mathf.Abs(now.g - _sampledColour.g) + Mathf.Abs(now.b - _sampledColour.b) > 0.02f)
                _aircraft = null;
        }

        float sound = LevelInfo.GetSpeedOfSound(aircraft.GlobalPosition().y);
        float g = Vector3.Dot(aircraft.pilots[0].GetAccel() + Vector3.up, aircraft.transform.up);
        // Mach, G with the game's own max-G readout, and AoA in the game's colour.
        string maxG = Get<TextMeshProUGUI>(App<GIndicators>(), "gMaxForceLabel")?.text ?? "";
        string line1 = $"M {speed / sound:F2}   G {g:F1} {maxG}";
        TextMeshProUGUI aoa = Get<TextMeshProUGUI>(App<AoADisplay>(), "AoAText");
        string line2 = "";
        if (aoa != null && aoa.enabled)
            line2 = $"<color={Hex(aoa.color)}>AOA {aoa.text}</color>";
        _speedExtra.text = line2.Length > 0 ? line1 + "\n" + line2 : line1;
        float climb = Vector3.Dot(aircraft.CockpitRB().velocity, Vector3.up);
        string altLine = "R " + UnitConverter.AltitudeReading(aircraft.radarAlt) + "   V " + UnitConverter.ClimbRateReading(climb);
        // Helicopters: rotor RPM from the game's gauge, in its own (warning) colour.
        TextMeshProUGUI rpm = Get<TextMeshProUGUI>(App<RPMGauge>(), "rpmText");
        if (rpm != null && !string.IsNullOrEmpty(rpm.text))
            altLine += $"\n<color={Hex(rpm.color)}>RPM {rpm.text}</color>";
        _altExtra.text = altLine;
        UpdateTags(aircraft);

        float fuel = aircraft.GetFuelLevel();
        Color fuelColour = fuel < 0.15f ? new Color(1f, 0.3f, 0.2f) : fuel < 0.3f ? new Color(1f, 0.85f, 0.2f) : Green;
        SetGauge(_fuel, fuel, fuelColour);
        SetGauge(_throttle, aircraft.GetInputs().throttle, Green);
        foreach (Gauge extra in ExtraFuel) SetGauge(extra, fuel, fuelColour);
        foreach (Gauge extra in ExtraThrottle) SetGauge(extra, aircraft.GetInputs().throttle, Green);
        // Use the game's own throttle wording: per-aircraft regions such as "MIL", plus
        // "AIRBRAKE" at 0 and "AFTERBURNER" at full, in its green-to-orange colours.
        TextMeshProUGUI reading = Get<TextMeshProUGUI>(App<ThrottleGauge>(), "throttleReading");
        if (reading != null && !string.IsNullOrEmpty(reading.text))
        {
            _throttle.Percent.text = _minimal || _wingman ? Abbreviate(reading.text) : reading.text;
            Color rc = new Color(reading.color.r, reading.color.g, reading.color.b, 1f);
            _throttle.Percent.color = rc;
            _throttle.FillImage.color = new Color(rc.r, rc.g, rc.b, 0.85f);
            foreach (Gauge extra in ExtraThrottle)
            {
                extra.Percent.text = reading.text;
                extra.Percent.color = rc;
                extra.FillImage.color = new Color(rc.r, rc.g, rc.b, 0.85f);
            }
        }

        List<WeaponStation> stations = aircraft.weaponStations;
        int count = stations != null ? stations.Count : 0;
        WeaponStation current = aircraft.weaponManager != null ? aircraft.weaponManager.currentWeaponStation : null;
        if (_wingCluster) RefreshStar(aircraft, stations, current);
        else if (_analog) RefreshAnalog(aircraft, stations, current);
        else if (_minimal) RefreshMinimal(aircraft, stations, current);
        else if (_thunder) RefreshThunder(aircraft, stations, current);
        else RefreshList(aircraft, stations, count, current);
        if (_starWheel != null) RefreshStarOnly(aircraft, stations, current);
        // Thunder's data block and match bar, when added to another style as extras.
        if (!_thunder && !_plus)
        {
            if (!ElementLooks.Get(DataBlockItem).Hidden) RefreshThunder(aircraft, stations, current);
            else if (!ElementLooks.Get(TopBarItem).Hidden) RefreshTopBar();
        }
        SetIconSprites(current);
    }

    private static void RefreshList(Aircraft aircraft, List<WeaponStation> stations, int count, WeaponStation current)
    {
        while (Rows.Count < count) Rows.Add(MakeRow(Rows.Count));
        for (int i = 0; i < Rows.Count; i++)
        {
            Row row = Rows[i];
            bool used = i < count;
            row.Rect.gameObject.SetActive(used);
            if (!used) continue;
            WeaponStation station = stations[i];
            bool selected = station == current;
            WeaponInfo info = station.WeaponInfo;
            string name = info == null ? "---" : (string.IsNullOrEmpty(info.shortName) ? info.weaponName : info.shortName);
            bool empty = station.GetAmmoLoaded() <= 0 && station.Ammo <= 0;
            float alpha = selected ? 1f : 0.72f;
            Color c = empty ? new Color(0.55f, 0.55f, 0.55f, alpha) : Faded(alpha);
            row.Marker.text = selected ? ">" : "";
            row.Name.text = (name ?? "").ToUpperInvariant();
            int reserve = ReloadsCompat.Reserve(i);
            row.Ammo.text = reserve > 0
                ? $"{station.GetAmmoReadout()} <size=75%><alpha=#B0>+{reserve}</size>"
                : station.GetAmmoReadout();
            row.Marker.color = row.Name.color = row.Ammo.color = c;
        }

        // Ace Combat style divider, then countermeasures and capacitor.
        float y = count * RowHeight;
        _divider.rectTransform.anchoredPosition = new Vector2(0f, -y);
        _divider.text = "-----     ---";
        _divider.color = Faded(0.6f);
        y += RowHeight;
        string flares = CountermeasureText(aircraft);
        _flare.rectTransform.anchoredPosition = new Vector2(0f, -y);
        _flare.text = flares;
        if (flares.Length > 0) y += RowHeight;
        if (ShowCapacitor(_weaponCap))
        {
            _weaponCap.Rect.anchoredPosition = new Vector2(0f, -y);
            y += RowHeight;
        }
        if (_capLine != null)
        {
            _capLine.rectTransform.anchoredPosition = new Vector2(0f, -y);
            _capLine.text = _capListText;
            if (_capListText.Length > 0) y += RowHeight;
        }
        _list.sizeDelta = new Vector2(ListWidth, y);
    }

    private static void SetIconSprites(WeaponStation current)
    {
        // Selected weapon icon (ghost + fill, see UpdateIcons).
        Sprite icon = current != null && current.WeaponInfo != null ? Cutout(current.WeaponInfo.weaponIcon) : null;
        foreach (Image image in new[] { _iconLeft, _iconRight })
        {
            image.sprite = icon;
            image.enabled = icon != null;
            image.color = Faded(0.18f);
        }
        foreach (Image image in new[] { _iconLeftFill, _iconRightFill })
        {
            image.sprite = icon;
            image.enabled = icon != null;
            image.color = Green;
        }
    }

    private static void SetGauge(Gauge gauge, float fraction, Color color)
    {
        fraction = Mathf.Clamp01(fraction);
        float length = gauge.Length > 0f ? gauge.Length : gauge.Horizontal ? BarLength : GaugeHeight;
        float thick = gauge.Thickness > 0f ? gauge.Thickness : gauge.Horizontal ? 6f : 4f;
        if (gauge.Horizontal)
        {
            float width = length * fraction;
            gauge.Fill.sizeDelta = new Vector2(width, thick);
            gauge.FillImage.color = new Color(color.r, color.g, color.b, 0.85f);
            gauge.Level.anchoredPosition = new Vector2(gauge.LevelStart + width, 0f);
            gauge.Percent.text = $"{fraction * 100f:F0}%";
            gauge.Percent.color = color;
            return;
        }
        float height = length * fraction;
        gauge.Fill.sizeDelta = new Vector2(thick, height);
        gauge.FillImage.color = new Color(color.r, color.g, color.b, 0.85f);
        gauge.Level.anchoredPosition = new Vector2(0f, height);
        gauge.Percent.text = $"{fraction * 100f:F0}%";
        gauge.Percent.color = color;
    }

    // The capacitor bar carries the energy reading, leaving the countermeasure line for its state.
    private static string CountermeasureText(Aircraft aircraft, bool wingmanCount = false)
    {
        Countermeasure active = aircraft.countermeasureManager != null
            ? aircraft.countermeasureManager.GetActiveCountermeasure() : null;
        if (active == null) return "";
        string tag = active is FlareEjector ? "FLR" : active is ChaffEjector ? "CHF" : active is RadarJammer ? "JAM" : ShortCmName(active.displayName);
        if (active.chargeable || active is RadarJammer)
        {
            PowerSupply supply = aircraft.GetPowerSupply();
            string state = aircraft.countermeasureTrigger ? "ACTIVE" :
                supply != null && supply.GetChargeKJ() <= 0f ? "EMPTY" : "READY";
            return $"{tag}   {state}";
        }
        int ammo = active is FlareEjector flare ? flare.GetAmmo() : active.ammo;
        int reserve = ReloadsCompat.CountermeasureReserve(active);
        string count = wingmanCount
            ? (ammo > 0 ? $"{tag}  READY  {ammo}" : $"{tag}  EMPTY")
            : $"{tag}   {ammo}";
        return count + (reserve > 0 ? $" <size=75%><alpha=#B0>+{reserve}</size>" : "");
    }

    // A custom countermeasure's display name, shortened to fit where "FLR" goes.
    private static string ShortCmName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "CM";
        name = name.Trim().ToUpperInvariant();
        if (name.Length <= 10) return name;
        string first = name.Split(' ')[0];   // "DECOY DRONE LAUNCHER" -> "DECOY"
        return first.Length >= 3 && first.Length <= 10 ? first : name.Substring(0, 10);
    }

    // The native capacitor bar exists only on aircraft that use stored energy.
    private static bool HasCapacitor()
    {
        Transform panel = NativePanel();
        Transform power = panel != null ? panel.Find("PowerPanel") : null;
        return power != null && power.gameObject.activeSelf;
    }

    // ---- placement ------------------------------------------------------------------------

    private static void ApplyOffsets()
    {
        float unit = 1080f / Mathf.Max(1, Screen.height); // screen pixels -> reference pixels
        Offset(_speed, SpeedBlock, SpeedBase, unit);
        Offset(_alt, AltBlock, AltBase, unit);
        Offset(_starWheel, WeaponWheelItem, StarWheelBase, unit);
        Offset(_arcs, MissileArcsItem, MissileArcsBase, unit);
        Offset(_fuelTape, FuelTapeItem, _fuelTapeBase, unit);
        Offset(_throttleTape, ThrottleTapeItem, _throttleTapeBase, unit);
        Offset(_list, WeaponList, ListBase, unit);
        Offset(_status, StatusRow, StatusBase, unit);
        Offset(_strip, StripItem, StripBase, unit);
        Offset(_vector, VectorGauges, _minimal ? new Vector2(-14f, 0f) : VectorBase, unit);
        if (!_minimal) KeepTrayOnScreen();
        if (_caution != null) Offset(_caution.Rect, Caution, CautionBase, unit);
        if (_pullUp != null) Offset(_pullUp.Rect, PullUp, PullUpBase, unit);
        Offset(_damage, HudElementPositions.Damage, DamageBase - new Vector2(_minimal ? _stripTrim : 0f, 0f), unit);
        Offset(_bars, BarsItem, BarsBase, unit);
        if (_analog)
        {
            for (int id = HudElementPositions.AnalogEngine; id < AnalogItems.Length; id++)
                Offset(AnalogItems[id], id, AnalogBases[id], unit);
            if (_rwrBezel != null && _strip.lossyScale.x != 0f)
                _rwrBezel.position = _rwrSlot.position + (Vector3)HudElementPositions.PixelOffset(HudElementPositions.Rwr);
        }
        Offset(_dataBlock, DataBlockItem, DataBlockBase, unit);
        Offset(_topBar, TopBarItem, TopBarBase, unit);
        Offset(_ring, RingItem, RingBase, unit);
        Offset(_killFeed, KillFeedItem, KillFeedBase, unit);
        Offset(_subtitle, SubtitleItem, SubtitleBase, unit);
        Offset(_banner, BannerItem, BannerBase, unit);
        if (_plus || _iconLeft == null || _iconRight == null) return;   // Plus has no weapon icons

        float turn = IconRotation != null ? IconRotation.Value : -90f;
        if (_wingman && !_damageIcons)
        {
            // Wingman: two upright icons side by side, left of the counter.
            var a = (RectTransform)_iconLeft.transform;
            var b = (RectTransform)_iconRight.transform;
            a.anchoredPosition = new Vector2(-196f, -8f);
            b.anchoredPosition = new Vector2(-170f, -8f);
            a.localRotation = b.localRotation = Quaternion.Euler(0f, 0f, turn);
            a.localScale = b.localScale = Vector3.one;
            return;
        }
        // Right icon is turned upright; the left one is its mirror image (mirror the sprite, then
        // turn the opposite way), so the same edge of the icon faces outward on both sides.
        var right = (RectTransform)_iconRight.transform;
        right.anchoredPosition = new Vector2(IconGap, 0f);
        right.localRotation = Quaternion.Euler(0f, 0f, turn);
        right.localScale = Vector3.one;
        var left = (RectTransform)_iconLeft.transform;
        left.anchoredPosition = new Vector2(-IconGap, 0f);
        left.localRotation = Quaternion.Euler(0f, 0f, -turn);
        left.localScale = new Vector3(-1f, 1f, 1f);
    }

    // Each block's own scale and rotation as built, so the editor's look is applied on top of it.
    private static readonly System.Collections.Generic.Dictionary<RectTransform, (Vector3 Scale, Quaternion Rotation)> LookBase = new();

    private static void Offset(RectTransform rect, int id, Vector2 basePosition, float unit)
    {
        if (rect == null) return;
        Vector2 px = HudElementPositions.PixelOffset(id) * unit;
        // Child offsets are screen distances even when the panel is scaled or turned.
        if (_minimal && rect.parent == _strip && _strip.lossyScale.x != 0f)
            px = _strip.InverseTransformVector(HudElementPositions.PixelOffset(id));
        if (!LookBase.TryGetValue(rect, out var built))
            LookBase[rect] = built = (rect.localScale, rect.localRotation);
        float k = ElementLooks.ScaleFactor(id);
        Vector3 size = built.Scale * k;
        if (rect.localScale != size) rect.localScale = size;
        Quaternion turn = built.Rotation * Quaternion.Euler(0f, 0f, ElementLooks.Get(id).Rotation);
        if (rect.localRotation != turn) rect.localRotation = turn;
        // Sized and turned about the block's centre: shift it so the centre stays put.
        Vector3 centre = Vector3.Scale(rect.rect.center, built.Scale);
        rect.anchoredPosition = basePosition + px + (Vector2)(built.Rotation * centre - turn * (centre * k));
    }

    internal static bool TryGetRect(int id, out Rect rect)
    {
        rect = default;
        RectTransform target = id == SpeedBlock ? _speed : id == AltBlock ? _alt : id == WeaponList ? _list
            : id == StatusRow ? _status
            : id == VectorGauges ? (BorrowedGauges.Count > 0 ? _vector : null)
            : id == Caution && _caution != null && _caution.Rect.gameObject.activeSelf ? _caution.Rect
            : id == PullUp && _pullUp != null && _pullUp.Rect.gameObject.activeSelf ? _pullUp.Rect
            : id == HudElementPositions.Damage ? _damage
            : id == BarsItem ? _bars
            : _analog && id >= HudElementPositions.AnalogEngine ? AnalogItems[id]
            : id == StripItem ? _strip
            : id == DataBlockItem ? _dataBlock
            : id == TopBarItem ? _topBar
            : id == RingItem ? _ring
            : id == TargetItem && _tgtPanel != null ? _tgtPanel
            : id == KillFeedItem ? _killFeed
            : id == FuelTapeItem ? _fuelTape
            : id == WeaponWheelItem ? _starWheel
            : id == MissileArcsItem ? _arcs
            : id == ThrottleTapeItem ? _throttleTape
            : id == SubtitleItem && _subtitleGroup != null && _subtitleGroup.alpha > 0.01f ? _subtitle
            : id == BannerItem && _bannerGroup != null && _bannerGroup.alpha > 0.01f ? _banner
            : null;
        if (target == null || _root == null || !target.gameObject.activeInHierarchy) return false;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        rect = HudElementPositions.ToGui(corners);
        return rect.width > 4f && rect.height > 4f;
    }

    // ---- hiding the native weapon panel ---------------------------------------------------

    private static bool _nativeHidden;
    private static CanvasGroup _hiddenGroup;
    private static bool _ownsGroup;

    private static Transform NativePanel()
    {
        CombatHUD combat = SceneSingleton<CombatHUD>.i;
        _topRightPanel ??= AccessTools.Field(typeof(CombatHUD), "topRightPanel");
        var panel = combat != null
            ? _topRightPanel?.GetValue(combat) as GameObject : null;
        return panel != null ? panel.transform : null;
    }

    // The native panel (weapon and countermeasure boxes, capacitor bar) stays alive so the game
    // keeps updating it; it is only made invisible. Its data is shown by this HUD instead.
    private static void HideNativeWeaponPanel(bool hide)
    {
        if (hide == _nativeHidden) return;
        if (hide)
        {
            Transform panel = NativePanel();
            if (panel == null) return;
            _hiddenGroup = panel.GetComponent<CanvasGroup>();
            _ownsGroup = _hiddenGroup == null;
            if (_ownsGroup) _hiddenGroup = panel.gameObject.AddComponent<CanvasGroup>();
            _hiddenGroup.alpha = 0f;
            _nativeHidden = true;
            return;
        }
        if (_hiddenGroup != null)
        {
            if (_ownsGroup) Object.Destroy(_hiddenGroup);
            else _hiddenGroup.alpha = 1f;
        }
        _hiddenGroup = null;
        _nativeHidden = false;
    }
}
