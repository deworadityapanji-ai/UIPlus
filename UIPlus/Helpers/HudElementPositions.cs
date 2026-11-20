using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// User-adjustable chase-view positions for HUD items. Map, weapons and the damage silhouette are
// native panels moved here. The instrument groups are moved by HudElements. The Arcade style's own
// speed, altitude and weapon-list blocks are placed by ArcadeHud. Each style keeps its own saved layout.
internal static class HudElementPositions
{
    internal const int Map = 1, Weapon = 2, LeftInstruments = 3, RightInstruments = 4,
        Compass = 5, Damage = 6, Waterline = 7, SpeedBlock = 8, AltBlock = 9, WeaponList = 10, StatusRow = 11, VectorGauges = 12, Caution = 13, PullUp = 14, Rwr = 15, Subtitle = 16, Banner = 17, Bars = 18, Strip = 19, TopBar = 20, DataBlock = 21, Ring = 22, TargetScreen = 23, KillFeed = 24, RwrAlerts = 25, FuelTape = 26, ThrottleTape = 27, WeaponWheel = 28, Count = 29;
    private static readonly string[] Names = { "", "Map", "Weapon", "LeftInstruments", "RightInstruments",
        "Compass", "Damage", "Waterline", "SpeedBlock", "AltBlock", "WeaponList", "StatusRow", "VectorGauges", "Caution", "PullUp", "Rwr", "Subtitle", "Banner", "Bars", "Strip", "TopBar", "DataBlock", "WeaponRing", "TargetingScreen", "KillFeed", "RwrAlerts", "FuelTape", "ThrottleTape", "WeaponWheel" };

    internal static ConfigEntry<HudStyle> Style;
    internal static ConfigEntry<bool> PlainBackingWhenMoved;
    private static readonly ConfigEntry<float>[] NativeX = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] NativeY = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] ArcadeX = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] ArcadeY = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] WingmanX = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] WingmanY = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] MinimalX = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] MinimalY = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] ThunderX = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] ThunderY = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] PlusX = new ConfigEntry<float>[Count];
    private static readonly ConfigEntry<float>[] PlusY = new ConfigEntry<float>[Count];

    private static readonly Transform[] _target = new Transform[Count];
    private static readonly Vector3[] _base = new Vector3[Count];
    private static readonly Vector3[] _baseScale = new Vector3[Count];
    private static readonly Quaternion[] _baseRotation = new Quaternion[Count];
    private static readonly Backing[] _backing = new Backing[Count];
    private static readonly List<RectTransform> _scratch = new();
    private static HudStyle _lastStyle;

    // True for the styles that replace the native pieces (all custom styles except Plus).
    private static bool Arcade => Style != null && Style.Value != HudStyle.Native && Style.Value != HudStyles.Plus;
    private static bool Plus => Style != null && Style.Value == HudStyles.Plus;

    // Which items exist in the current style. Arcade replaces the left/right instrument groups.
    // Arcade also replaces the native weapon panel, so it has nothing to move there.
    internal static bool Valid(int id) => Plus
        // Plus: every native piece stays (and can be moved), plus its own additions.
        ? id <= Waterline || id == Caution || id == PullUp || id == Subtitle || id == Banner || id == VectorGauges
          || id == TopBar || id == Ring || id == KillFeed || ((id == Rwr || id == RwrAlerts) && RwrCompat.Present)
        : Arcade
        ? id != LeftInstruments && id != RightInstruments && id != Weapon && id != Ring && ((id != Rwr && id != RwrAlerts) || RwrCompat.Present)
          && (id != Strip || Style.Value == HudStyle.Dashboard)
          // The star wheel cannot be added while Wingman's cluster (which has its own star) shows.
          && (id != WeaponWheel || !ElementLooks.WingmanClusterIn(Style.Value))
        : id <= Waterline || ((id == Rwr || id == RwrAlerts) && RwrCompat.Present);

    // The game's panel art is slanted to fit one screen corner. When a panel is moved, a plain
    // rectangle sized to its real content replaces it; the original art is restored afterwards.
    private sealed class Backing
    {
        internal Image Panel;
        internal Color PanelColor;
        internal RectTransform Rect;
    }

    // Starting layout for the Arcade style, as offsets (percent of screen, +x right, +y down) from
    // the native positions measured at 1920x1080. They put the map bottom-left. Everything else
    // starts at 0 (the damage silhouette and the custom blocks have their own built-in spots).
    private static float ArcadeDefault(int id, bool xAxis) => id switch
    {
        Map => xAxis ? 2.5f : -6f,
        _ => 0f,
    };

    internal static void Bind(ConfigFile config)
    {
        // "Minimal" was renamed "Dashboard": read the saved word before binding (which would reject it).
        bool wasMinimal = SavedValue(config, "HUD Layout", "HudStyle") == "Minimal";
        Style = config.Bind("HUD Layout", "HudStyle", HudStyle.Native,
            "Native keeps the game's HUD, with your saved positions. Arcade is an Ace Combat style layout; Wingman is a Project Wingman style layout; Dashboard puts every instrument in one strip along the bottom, to glance at; Thunder is a War Thunder style, text-first layout. Each style keeps its own saved layout.");
        if (wasMinimal) Style.Value = HudStyle.Dashboard;
        ArcadeHud.ArcadeTargetingScreen = config.Bind("HUD Layout", "ArcadeTargetingScreen", true,
            "Arcade style: show the targeting-camera feed as an Ace Combat style lock-on window in the top-right corner while a target is selected.");
        ArcadeHud.WingmanTargetingScreen = config.Bind("HUD Layout", "WingmanTargetingScreen", true,
            "Wingman style: show the targeting-camera feed in the top-left corner, leaning like the map, while a target is selected.");
        ArcadeHud.KillFeed = config.Bind("HUD Layout", "KillFeed", KillFeedMode.All,
            "A compact kill feed (killer >> victim, newest on top, fading after a few seconds) in the custom styles. All shows every kill; Mine only yours and your own death; Off hides it. Shot-down missiles are not listed.");
        ArcadeHud.DashboardDockMap = config.Bind("HUD Layout", "DashboardDockMap", true,
            "Dashboard style: dock the minimap against the instrument strip's left end, bottoms aligned. Moving the map in the layout editor turns this off for that layout.");
        ArcadeHud.WeaponPopup = config.Bind("HUD Layout", "WeaponPopup", WeaponPopupMode.WingmanOnly,
            "Project Wingman style WEAPON SYSTEM pop-up naming the weapon you switch to. WingmanOnly shows it in the Wingman style; AllStyles in every custom style; Off never.");
        ArcadeHud.PlusRwrOnMap = config.Bind("HUD Layout", "PlusRwrOnMap", true,
            new ConfigDescription("Plus style: lay the KaceyTronic RWR scope over the minimap (the map is then centred on your aircraft). Off leaves the map exactly as the game draws it.", null, new ConfigurationManagerAttributes()));
        ArcadeHud.PlusFactionBar = config.Bind("HUD Layout", "PlusFactionBar", true,
            new ConfigDescription("Plus style: faction balance and nuclear escalation bar at the top of the screen (colours as set for Thunder).", null, new ConfigurationManagerAttributes()));
        ArcadeHud.PlusWarnings = config.Bind("HUD Layout", "PlusWarnings", true,
            new ConfigDescription("Plus style: MISSILE / CAUTION and PULL UP / STALL warnings, in the game's HUD font.", null, new ConfigurationManagerAttributes()));
        ArcadeHud.PlusWeaponRing = config.Bind("HUD Layout", "PlusWeaponRing", true,
            new ConfigDescription("Plus style: weapon ring under the weapon panel, split between your weapons: selected lit, next one softly lit, empty dimmed, reloads filling.", null, new ConfigurationManagerAttributes()));
        ArcadeHud.PlusSubtitles = config.Bind("HUD Layout", "PlusSubtitles", false,
            new ConfigDescription("Plus style: show chat and mission messages as subtitles with a MISSION UPDATE banner, as in Arcade. Off keeps the game's own chat.", null, new ConfigurationManagerAttributes()));
        ArcadeHud.PlusDamageAlways = config.Bind("HUD Layout", "PlusDamageAlways", true,
            new ConfigDescription("Plus style: keep the game's damage silhouette faintly visible with damaged parts tinted, instead of fading it out a few seconds after a hit.", null, new ConfigurationManagerAttributes()));
        ArcadeHud.IconRotation = config.Bind("HUD Layout", "ArcadeWeaponIconRotation", -90f,
            new ConfigDescription("Arcade HUD: degrees to turn the weapon icons beside the damage silhouette so they point up. Use 90 if they point down.",
                new AcceptableValueRange<float>(-180f, 180f)));
        ArcadeHud.ExtraGauges = config.Bind("HUD Layout", "ArcadeExtraGauges", "",
            "Arcade HUD: extra HUD gauge types (component type names from the F10 dump, comma separated) to move into the gauge tray beside the damage silhouette. Only list readouts, never aiming aids.");
        ArcadeHud.ThunderFactionColours = config.Bind("HUD Layout", "ThunderFactionColours", false,
            "Thunder style: colour the faction bar with each faction's own colour. Off colours it relative to you instead, with the game's friendly and hostile unit colours (which follow its colour settings).");
        ArcadeHud.ThunderFactionPalette = config.Bind("HUD Layout", "ThunderFactionPalette", "BDF=#4CAF50, Boscali=#4CAF50, PALA=#E53935, Primeva=#E53935",
            "Thunder style, with ThunderFactionColours on: each faction's colour, as TAG=#RRGGBB pairs separated by commas (faction tag or name). Factions not listed use the game's own faction colour; the log names each faction's tag on first sight.");
        ArcadeHud.WingmanSlant = config.Bind("HUD Layout", "WingmanSlant", true,
            "Wingman style: lean the readouts (italic text, slanted bars and shapes) like Project Wingman.");
        PlainBackingWhenMoved = config.Bind("HUD Layout", "PlainBackingWhenMoved", true,
            "Replace the corner-shaped panel art with a plain dark rectangle once the map or weapon panel is moved away from its default corner.");
        for (int i = 1; i < Count; i++)
        {
            if (i <= Waterline || i == Rwr || i == RwrAlerts)
            {
                NativeX[i] = BindOffset(config, Names[i] + "XPercent", 0f);
                NativeY[i] = BindOffset(config, Names[i] + "YPercent", 0f);
            }
            ArcadeX[i] = BindOffset(config, "Arcade" + Names[i] + "XPercent", ArcadeDefault(i, true));
            ArcadeY[i] = BindOffset(config, "Arcade" + Names[i] + "YPercent", ArcadeDefault(i, false));
            WingmanX[i] = BindOffset(config, "Wingman" + Names[i] + "XPercent", ArcadeDefault(i, true));
            WingmanY[i] = BindOffset(config, "Wingman" + Names[i] + "YPercent", ArcadeDefault(i, false));
            // Minimal keeps the map in the game's own corner, with its original backing art.
            MinimalX[i] = BindOffset(config, "Minimal" + Names[i] + "XPercent", i == Map ? 0f : ArcadeDefault(i, true));
            MinimalY[i] = BindOffset(config, "Minimal" + Names[i] + "YPercent", i == Map ? 0f : ArcadeDefault(i, false));
            // Thunder puts the map (with the RWR on it) in the top-right corner, like War Thunder's radar.
            ThunderX[i] = BindOffset(config, "Thunder" + Names[i] + "XPercent", i == Map ? 82.3f : 0f);
            ThunderY[i] = BindOffset(config, "Thunder" + Names[i] + "YPercent", i == Map ? -66.4f : 0f);
            // Plus starts with everything where the game puts it.
            PlusX[i] = BindOffset(config, "Plus" + Names[i] + "XPercent", 0f);
            PlusY[i] = BindOffset(config, "Plus" + Names[i] + "YPercent", 0f);
        }
        ElementLooks.Bind(config);
    }

    /// <summary>Elements whose size, rotation and visibility the editor can change.</summary>
    internal static bool SupportsLook(int id) =>
        id != LeftInstruments && id != RightInstruments && id != Compass && id != Waterline && id != Rwr && id != RwrAlerts
        && (id != Weapon || !Arcade);

    // The raw saved text of a setting that has not been bound yet (BepInEx keeps it as an orphan).
    private static string SavedValue(ConfigFile config, string section, string key)
    {
        object orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config);
        return orphans is Dictionary<ConfigDefinition, string> map && map.TryGetValue(new ConfigDefinition(section, key), out string value)
            ? value : null;
    }

    // Saved like any setting, but not listed in Configuration Manager: the layout editor is the way
    // to move elements, and dozens of offset sliders would bury the real options.
    private static ConfigEntry<float> BindOffset(ConfigFile config, string key, float defaultValue) =>
        config.Bind("HUD Layout", key, defaultValue, new ConfigDescription(
            "Screen offset for the chase HUD element, as a percentage of screen width or height. Set with the HUD layout editor.",
            new AcceptableValueRange<float>(-100f, 100f), new ConfigurationManagerAttributes()));

    // Configuration Manager reads this tag by name; no reference to its assembly is needed.
    private sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable = false;
    }

    // The saved entry for the active style.
    internal static ConfigEntry<float> Entry(int id, bool xAxis) => Style.Value switch
    {
        HudStyle.Arcade => (xAxis ? ArcadeX : ArcadeY)[id],
        HudStyle.Wingman => (xAxis ? WingmanX : WingmanY)[id],
        HudStyle.Dashboard => (xAxis ? MinimalX : MinimalY)[id],
        HudStyle.Thunder => (xAxis ? ThunderX : ThunderY)[id],
        HudStyles.Plus => (xAxis ? PlusX : PlusY)[id],
        _ => (xAxis ? NativeX : NativeY)[id],
    };

    // The value in force: the editor's unsaved working value while it is open, else the saved one.
    internal static float Offset(int id, bool xAxis)
    {
        if (!Valid(id)) return 0f;
        return HudLayoutEditor.IsOpen ? HudLayoutEditor.GetWorking(id, xAxis) : Entry(id, xAxis).Value;
    }

    // Offset in screen pixels, +x right and +y up, for items placed outside this class. An item
    // that follows another (see Leader) goes wherever its leader goes until it is moved itself.
    internal static Vector2 PixelOffset(int id)
    {
        if (Leader(id) is int leader && !Moved(id)) id = leader;
        return new(Screen.width * Offset(id, true) / 100f, -Screen.height * Offset(id, false) / 100f);
    }

    /// <summary>The element this one sits with and follows until moved on its own, if any.</summary>
    internal static int? Leader(int id) => id switch
    {
        RwrAlerts => Rwr,
        FuelTape => SpeedBlock,
        ThrottleTape => AltBlock,
        _ => null,
    };

    internal static void Tick(CameraStateManager cam)
    {
        bool active = Plugin.IsActive && HudViews.Showing(cam) && !DynamicMap.mapMaximized;
        if (!active)
        {
            Restore();
            return;
        }
        // Switching style changes which saved layout applies, so start from the native positions.
        if (Style.Value != _lastStyle)
        {
            Restore();
            _lastStyle = Style.Value;
        }
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        FlightHud flight = SceneSingleton<FlightHud>.i;
        // The dark backing art is on the anchor's parent (LowerLeftPanel, sprite "mapPanel"),
        // so move that panel; moving only the anchor leaves the backing behind.
        Transform mapPanel = map != null && map.hudMapAnchor != null ? map.hudMapAnchor.parent : null;
        // Use the serialized field because the scene object's name can vary.
        GameObject weapons = hud != null
            ? HarmonyLib.AccessTools.Field(typeof(CombatHUD), "topRightPanel")?.GetValue(hud) as GameObject
            : null;
        Place(Map, mapPanel);
        if (!Arcade) Place(Weapon, weapons != null ? weapons.transform : null);
        // The damage silhouette is parented under this anchor each time an aircraft spawns.
        // Arcade draws its own copy of it, so the native anchor is left alone there.
        if (!Arcade) Place(Damage, flight != null ? flight.statusAnchor : null);

        bool plain = PlainBackingWhenMoved.Value;
        // Arcade hides the native weapon art itself, so a backing would only draw a stray box.
        int mapSkin = ElementLooks.SkinOf(Map);   // 1 = always the game's art, 2 = always plain
        if (map != null) UpdateBacking(Map, mapSkin == 2 || (mapSkin == 0 && plain && Moved(Map)), () => AddRect(map.transform));
        UpdateBacking(Weapon, plain && !Arcade && Moved(Weapon), () =>
        {
            Transform panel = _target[Weapon];
            if (panel == null) return;
            foreach (Transform child in panel)
                if (child.gameObject.activeSelf && child.name != BackingName) AddRect(child);
        });
    }

    internal static bool Moved(int id) =>
        Mathf.Abs(Offset(id, true)) > 0.01f || Mathf.Abs(Offset(id, false)) > 0.01f;

    private static void AddRect(Transform t)
    {
        if (t is RectTransform r) _scratch.Add(r);
    }

    // Positions are stored and written in parent-local space. World positions go stale when the
    // game shifts the floating origin, which made the panels snap back and flash.
    private static void Place(int id, Transform target)
    {
        if (_target[id] != target)
        {
            if (_target[id] != null)
            {
                _target[id].localPosition = _base[id];
                _target[id].localScale = _baseScale[id];
                _target[id].localRotation = _baseRotation[id];
            }
            Unback(id);
            _target[id] = target;
            if (target != null)
            {
                _base[id] = target.localPosition;
                _baseScale[id] = target.localScale;
                _baseRotation[id] = target.localRotation;
            }
        }
        Transform current = _target[id];
        if (current == null) return;
        Transform parent = current.parent;
        float scale = parent != null ? parent.lossyScale.x : 1f;
        if (scale <= 0.0001f) return;
        Vector3 delta = new Vector3(Screen.width * Offset(id, true) / 100f,
            -Screen.height * Offset(id, false) / 100f, 0f) / scale;
        if (id == Map) delta += (Vector3)ArcadeHud.MapDock / scale;   // Dashboard docks the map to its strip
        Vector3 wanted = _base[id] + delta;
        // Wingman's perspective lean: the map turns slightly about its bottom-left corner. The
        // position is adjusted so that corner stays where it was.
        float lean = id == Map ? ArcadeHud.MapLeanDegrees : 0f;
        // The editor's size, rotation and hide (size 0) for this element.
        ElementLooks.Look look = ElementLooks.Get(id);
        float k = ElementLooks.ScaleFactor(id);
        Vector3 size = _baseScale[id] * k;
        if (current.localScale != size) current.localScale = size;
        Quaternion leaned = _baseRotation[id] * Quaternion.Euler(0f, 0f, lean);
        Quaternion rotation = leaned * Quaternion.Euler(0f, 0f, look.Rotation);
        if (current.localRotation != rotation) current.localRotation = rotation;
        if (current is RectTransform rect)
        {
            // The lean turns about the bottom-left corner (that corner stays where it was)...
            if (lean != 0f)
            {
                Vector3 corner = Vector3.Scale(new Vector3(rect.rect.xMin, rect.rect.yMin, 0f), _baseScale[id]);
                wanted += _baseRotation[id] * corner - leaned * corner;
            }
            // ...and the editor's size and rotation work about the element's centre.
            Vector3 centre = Vector3.Scale(rect.rect.center, _baseScale[id]);
            wanted += leaned * centre - rotation * (centre * k);
        }
        if (id == Map) RotatableClip(lean != 0f || look.Rotation != 0f);
        if ((current.localPosition - wanted).sqrMagnitude > 0.0001f)
            current.localPosition = wanted;
    }

    private static System.Reflection.FieldInfo _statusBackgroundField;

    // The silhouette image of the game's damage display (its private "aircraftBackground").
    private static RectTransform StatusBackground(Transform anchor)
    {
        var display = anchor.GetComponentInChildren<StatusDisplay>(true);
        if (display == null) return null;
        _statusBackgroundField ??= HarmonyLib.AccessTools.Field(typeof(StatusDisplay), "aircraftBackground");
        return (_statusBackgroundField?.GetValue(display) as Image)?.rectTransform;
    }

    private const string BackingName = "UI Plus Backing";
    private static readonly Color BackingColor = new(0f, 0f, 0f, 0.867f);

    private static void UpdateBacking(int id, bool plain, System.Action collectContent)
    {
        Transform panel = _target[id];
        if (!plain || panel == null)
        {
            Unback(id);
            return;
        }
        Backing b = _backing[id];
        if (b == null)
        {
            var image = panel.GetComponent<Image>();
            if (image == null) return;
            var go = new GameObject(BackingName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panel, false);
            go.transform.SetAsFirstSibling();
            var img = go.GetComponent<Image>();
            img.color = BackingColor;
            img.raycastTarget = false;
            b = new Backing { Panel = image, PanelColor = image.color, Rect = (RectTransform)go.transform };
            _backing[id] = b;
            image.color = new Color(image.color.r, image.color.g, image.color.b, 0f);
        }
        _scratch.Clear();
        collectContent();
        if (_scratch.Count == 0) return;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        foreach (RectTransform r in _scratch)
        {
            r.GetWorldCorners(corners);
            foreach (Vector3 c in corners)
            {
                Vector3 local = panel.InverseTransformPoint(c);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
        }
        // The map may show only part of its own box (a padded clip); size to what is shown.
        if (id == Map) ShrinkToMapClip(panel, ref min, ref max);
        const float pad = 10f;
        RectTransform rt = b.Rect;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = max - min + new Vector2(pad * 2f, pad * 2f);
        rt.localPosition = (min + max) * 0.5f;
        rt.localScale = Vector3.one;
    }

    private static bool _loggedMapClip;

    private static void ShrinkToMapClip(Transform panel, ref Vector2 min, ref Vector2 max)
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null) return;
        RectMask2D clip = map.GetComponent<RectMask2D>();
        if (!_loggedMapClip)
        {
            _loggedMapClip = true;
            Image image = map.GetComponent<Image>();
            Plugin.Logger.LogInfo($"Map clip: RectMask2D {(clip != null ? $"padding {clip.padding}, softness {clip.softness}" : "none")}, "
                + $"image {(image != null ? (image.sprite != null ? $"sprite '{image.sprite.name}' border {image.sprite.border}" : "no sprite") : "none")}, rect {((RectTransform)map.transform).rect}");
        }
        // Only the RectMask2D clips with padding; while the map leans it is off (a Mask is used).
        if (clip == null || !clip.enabled) return;
        Vector4 pad = clip.padding;   // left, bottom, right, top, in the map's own units
        if (pad == Vector4.zero) return;
        float k = map.transform.lossyScale.x / Mathf.Max(0.0001f, panel.lossyScale.x);
        min += new Vector2(pad.x, pad.y) * k;
        max -= new Vector2(pad.z, pad.w) * k;
    }

    private static void Unback(int id)
    {
        Backing b = _backing[id];
        if (b == null) return;
        if (b.Rect != null) Object.Destroy(b.Rect.gameObject);
        if (b.Panel != null) b.Panel.color = b.PanelColor;
        _backing[id] = null;
    }

    // Real on-screen bounds of an item, in IMGUI coordinates (origin top-left).
    internal static bool TryGetScreenRect(int id, out Rect rect)
    {
        rect = default;
        if (!Valid(id)) return false;
        if (id == Rwr) return RwrCompat.TryGetRect(out rect);
        if (id == RwrAlerts) return RwrCompat.TryGetAlertsRect(out rect);
        if (id >= SpeedBlock) return ArcadeHud.TryGetRect(id, out rect);
        if (id >= LeftInstruments && id <= Compass)
            return HudElements.TryGetGroupScreenRect(id - LeftInstruments, out rect);
        if (id == Waterline) return HudElements.TryGetGroupScreenRect(3, out rect);
        if (id == Damage && Arcade) return ArcadeHud.TryGetRect(id, out rect);
        Transform target = _target[id];
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        var corners = new Vector3[4];
        if (id == Damage)
        {
            // The silhouette is only visible after damage, so fall back to a box at the anchor.
            RectTransform shown = StatusBackground(target)
                ?? (target.childCount > 0 ? target.GetChild(0) as RectTransform : null);
            if (shown != null) shown.GetWorldCorners(corners);
            if (shown == null || Mathf.Abs(corners[2].x - corners[0].x) < 8f)
            {
                Vector3 p = target.position;
                corners[0] = p - new Vector3(80f, 80f, 0f);
                corners[2] = p + new Vector3(80f, 80f, 0f);
            }
        }
        else if (id == Weapon && Arcade)
        {
            // Only the capacitor bar is visible in Arcade; outline just that.
            Transform power = target.Find("PowerPanel");
            if (power == null || !power.gameObject.activeInHierarchy) return false;
            ((RectTransform)power).GetWorldCorners(corners);
        }
        else
        {
            Backing b = _backing[id];
            DynamicMap shown = id == Map ? SceneSingleton<DynamicMap>.i : null;
            // The map: just the picture (its panel also holds the threat list and spare margin).
            if (b == null && shown != null) ((RectTransform)shown.transform).GetWorldCorners(corners);
            else (b != null && b.Rect != null ? b.Rect : (RectTransform)target).GetWorldCorners(corners);
        }
        rect = ToGui(corners);
        return rect.width > 4f && rect.height > 4f;
    }

    internal static Rect ToGui(Vector3[] corners)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 c in corners)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
        }
        return new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
    }

    // The minimap's visible area is clipped by a RectMask2D, which always clips to an upright box.
    // While the map leans (Wingman), a stencil Mask is used instead, which turns with the panel; the
    // game's own clip is put back as soon as the lean ends.
    private static RectMask2D _mapRectMask;
    private static Mask _mapMask;
    private static Image _mapMaskImage;
    private static bool _ownsMaskImage;

    private static Sprite _maskSprite;
    private static bool _ownsMask;

    private static void RotatableClip(bool on)
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        GameObject view = map != null ? map.gameObject : null;
        if (on && view != null)
        {
            if (_mapMask != null && _mapMask.gameObject == view) return;
            RotatableClip(false);
            _mapRectMask = view.GetComponent<RectMask2D>();
            if (_mapRectMask != null) _mapRectMask.enabled = false;
            _mapMaskImage = view.GetComponent<Image>();
            _ownsMaskImage = _mapMaskImage == null;
            if (_ownsMaskImage) _mapMaskImage = view.AddComponent<Image>();
            _maskSprite = _mapMaskImage.sprite;
            _mapMaskImage.sprite = null;   // clip to the full box, not the sprite's inset shape
            _mapMaskImage.color = Color.white;
            _mapMaskImage.raycastTarget = false;
            _mapMask = view.GetComponent<Mask>();
            _ownsMask = _mapMask == null;
            if (_ownsMask) _mapMask = view.AddComponent<Mask>();
            if (_mapMask != null) _mapMask.showMaskGraphic = false;
            return;
        }
        if (_mapMaskImage != null && !_ownsMaskImage) _mapMaskImage.sprite = _maskSprite;
        _maskSprite = null;
        if (_mapMask != null && _ownsMask) Object.DestroyImmediate(_mapMask);
        if (_mapMaskImage != null && _ownsMaskImage) Object.DestroyImmediate(_mapMaskImage);
        if (_mapRectMask != null) _mapRectMask.enabled = true;
        _mapMask = null;
        _mapMaskImage = null;
        _mapRectMask = null;
    }

    internal static void Restore()
    {
        RotatableClip(false);
        for (int i = 1; i < Count; i++)
        {
            Unback(i);
            if (_target[i] != null)
            {
                _target[i].localPosition = _base[i];
                _target[i].localScale = _baseScale[i];
                _target[i].localRotation = _baseRotation[i];
            }
            _target[i] = null;
        }
    }
}
