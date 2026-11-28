using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// "Wingman" style (Project Wingman inspired, flat version). It shares everything with the Arcade
// style (data, warnings, messages, RWR, silhouette copy, tags, gauge tray) and swaps the layout:
//   - SPD / ALT as slightly tilted text beside the aircraft, no boxes or tapes;
//   - a weapon cluster bottom right: gun line, selected weapon with its icons and big counter, and
//     an 8-point star of the other weapons in cycle order, clockwise from the top;
//   - THR over FUEL as two thin bars where Project Wingman shows HULL;
//   - the damage silhouette small in the top-right corner.
internal static partial class ArcadeHud
{
    internal const int SubtitleItem = 16, BannerItem = 17, BarsItem = 18;

    private static bool _wingman;
    private static HudStyle _builtStyle;
    private static RectTransform _bars;
    private static Vector2 BarsBase = new(-40f, 40f);
    private static TextMeshProUGUI _gunLine, _flrLine, _selName, _selCount, _selReserve, _overflow;
    private static RectTransform _wingDivider;
    private const float BarLength = 170f, StarRadius = 82f;
    private const float ClusterWidth = 520f, ClusterHeight = 215f;
    private static readonly Vector2 StarCentre = new(388f, -112f);  // from the cluster's top-left

    // In Wingman the star IS the controller weapon wheel (hold weapon switch): the game's wheel is
    // shrunk out of sight and the stick picks a star pointer instead (see WingmanWheel below).
    private static RectTransform _starMarker, _wheelHidden;
    private static Vector3 _wheelScale;
    private static System.Reflection.FieldInfo _menuObjectField, _menuStateField;
    // Each pointer holds a station index or one of the negative action/empty codes.
    private static readonly int[] StarStations = new int[WeaponStarLayout.Petals];
    internal static int WheelPetal = -1;
    private static int _aimedToggle = WeaponStarLayout.Empty;

    private static int MenuState(RadialMenuMain menu, out RectTransform wheel)
    {
        _menuObjectField ??= HarmonyLib.AccessTools.Field(typeof(RadialMenuMain), "menuObject");
        _menuStateField ??= HarmonyLib.AccessTools.Field(typeof(RadialMenuMain), "currentState");
        wheel = (_menuObjectField?.GetValue(menu) as GameObject)?.transform as RectTransform;
        object s = _menuStateField?.GetValue(menu);
        return s != null ? System.Convert.ToInt32(s) : 0;   // 0 closed, 1 main menu, 2 weapons
    }

    // Controller only: with mouse and keyboard the game's own wheel is used as normal.
    private static bool StarWheelReady => (_wingCluster || _starWheel != null) && _root != null && _root.activeInHierarchy && Slots.Count == 8
        && GameManager.playerInput?.controllers.GetLastActiveController() is Rewired.Controller c
        && c.type == Rewired.ControllerType.Joystick;

    // Per frame: hide the game's weapon wheel in Wingman; the main radial menu stays visible.
    private static void PlaceWeaponWheel(bool allow)
    {
        RadialMenuMain menu = SceneSingleton<RadialMenuMain>.i;
        RectTransform wheel = null;
        int state = menu != null ? MenuState(menu, out wheel) : 0;
        if (!allow || !StarWheelReady || wheel == null || (_wheelHidden != null && _wheelHidden != wheel))
        {
            RestoreWeaponWheel();
            if (!allow || !StarWheelReady || wheel == null) { WheelPetal = -1; return; }
        }
        if (state == 2)
        {
            if (_wheelHidden == null) { _wheelHidden = wheel; _wheelScale = wheel.localScale; }
            wheel.localScale = Vector3.zero;   // the game fades it; size is ours to change
        }
        else if (state == 1) RestoreWeaponWheel();
        if (state != 2) WheelPetal = -1;
        // Closed: stay hidden so the weapon wheel does not flash while it fades out.
    }

    private static void RestoreWeaponWheel()
    {
        if (_wheelHidden != null) _wheelHidden.localScale = _wheelScale;
        _wheelHidden = null;
    }

    private static System.Reflection.FieldInfo _selectedField, _weaponsField, _menuAircraftField;
    private static readonly System.Reflection.FieldInfo GunsLinked =
        HarmonyLib.AccessTools.Field(typeof(WeaponManager), "gunsLinked");

    /// <summary>
    /// Replaces the game's per-frame weapon-wheel control in Wingman: the stick is snapped to the
    /// nearest of the star's 8 pointers and the weapon there is selected; releasing the button
    /// fires the game's own action for it. Returns false when it handled the frame.
    /// </summary>
    internal static bool WingmanWheel(RadialMenuMain menu, List<RadialMenuAction> actions, string button)
    {
        try
        {
            if (!StarWheelReady || menu == null || MenuState(menu, out _) != 2) return true;
            _weaponsField ??= HarmonyLib.AccessTools.Field(typeof(RadialMenuMain), "allowedActionsWeapons");
            if (!ReferenceEquals(_weaponsField?.GetValue(menu), actions)) return true;
            _selectedField ??= HarmonyLib.AccessTools.Field(typeof(RadialMenuMain), "selectedAction");
            _menuAircraftField ??= HarmonyLib.AccessTools.Field(typeof(RadialMenuMain), "aircraft");
            var aircraft = _menuAircraftField?.GetValue(menu) as Aircraft;
            var input = GameManager.playerInput;
            if (aircraft == null || input == null || _selectedField == null) return true;

            if (!input.GetButton(button))
            {
                // Released: fire the game's own action for the aimed weapon (if any), then close.
                if (WheelPetal >= 0 && _aimedToggle != WeaponStarLayout.Empty)
                {
                    // A loadout update can take the aimed action off the star before release.
                    if (StarStations[WheelPetal] == _aimedToggle)
                        TriggerStarToggle(aircraft, _aimedToggle);
                }
                else if (_selectedField.GetValue(menu) is RadialMenuAction chosen) chosen.TriggerAction(aircraft);
                menu.CloseMenu();
                WheelPetal = -1;
                return false;
            }
            // The game's vertical radial axis reads positive when the stick is pushed down.
            float x = input.GetAxis("Radial Menu Horizontal"), y = -input.GetAxis("Radial Menu Vertical");
            if (x * x + y * y > 0.25f)
            {
                float angle = Mathf.Atan2(x, y) * Mathf.Rad2Deg;   // clockwise from up, like the star
                if (angle < 0f) angle += 360f;
                int petal = Mathf.RoundToInt(angle / 45f) % 8;
                WheelPetal = petal;
                RadialMenuAction pick = null;
                int station = StarStations[petal];
                _aimedToggle = station < WeaponStarLayout.Empty ? station : WeaponStarLayout.Empty;
                if (station >= 0 && aircraft.weaponStations != null && station < aircraft.weaponStations.Count)
                {
                    int number = aircraft.weaponStations[station].Number;
                    foreach (RadialMenuAction a in actions)
                        if (a != null && a.GetActionType() == RadialMenuAction.ActionType.SelectWeapon
                            && a.weapon_number == number) { pick = a; break; }
                }
                _selectedField.SetValue(menu, pick);   // an empty pointer selects nothing
            }
            // Stick back at centre: keep the last aim, so releasing the button still picks it.
            return false;
        }
        catch (System.Exception e)
        {
            Plugin.Logger.LogWarning("Wingman weapon wheel: falling back to the game's wheel: " + e.Message);
            return true;
        }
    }

    private static void TriggerStarToggle(Aircraft aircraft, int action)
    {
        WeaponManager manager = aircraft.weaponManager;
        if (manager == null) return;
        if (action == WeaponStarLayout.Link)
        {
            if (manager.HasMultipleGuns()) manager.ToggleGunsLinked();
        }
        else
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud != null && hud.aircraft == aircraft && manager.StationsWithTurrets() > 0) hud.ToggleAutoControl();
        }
    }

    // While the wheel is held, light the aimed pointer and its slot (every frame, over the refresh).
    private static void UpdateStarAim()
    {
        if (!StarWheelReady || WheelPetal < 0) return;
        for (int p = 0; p < Petals.Count; p++)
            if (p == WheelPetal) Petals[p].color = Green;
        if (WheelPetal < Slots.Count)
            foreach (Image edge in Slots[WheelPetal].Frame) if (edge != null) edge.color = Green;
    }

    private sealed class StarSlot
    {
        internal RectTransform Rect;
        internal TextMeshProUGUI Name, Count;
        internal readonly List<Image> Frame = new();
    }

    private static readonly List<StarSlot> Slots = new();
    private static readonly List<Image> Petals = new();
    private static readonly List<RectTransform> ReloadBars = new();
    private const float ReloadBarHeight = 64f, PetalLength = 52f;

    // A diamond petal: a square turned 45 degrees inside a parent squashed across its width, so it
    // becomes a long rhombus, then turned to point at its slot. Cardinal petals are a bit longer.
    private static Image MakePetal(int index, RectTransform parent, Vector2 centre)
    {
        float angle = index * 45f;   // clockwise from the top, same as the slots
        float length = index % 2 == 0 ? PetalLength : PetalLength * 0.8f;
        RectTransform pivot = Rect("Petal", parent, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), centre, Vector2.zero);
        pivot.localRotation = Quaternion.Euler(0f, 0f, -angle);
        RectTransform squash = Rect("Shape", pivot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, length * 0.5f + 2f), Vector2.zero);
        squash.localScale = new Vector3(0.32f, 1f, 1f);
        float side = length / Mathf.Sqrt(2f);
        RectTransform square = Rect("Diamond", squash, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(side, side));
        square.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image image = square.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = Faded(0.3f);
        return image;
    }

    // Reload bars: one per pylon of the selected weapon (one for guns and pods), rebuilt when the
    // count changes, laid out across a fixed span so many pylons stay compact.
    private static readonly List<RectTransform> ReloadTracks = new();
    private static Transform _reloadParent;
    private static Vector2 _reloadOrigin;
    private static float _reloadSpan, _reloadWidth, _reloadFixedStep, _reloadStep, _reloadBarWidth;
    private static int _reloadCount = -1;

    /// <param name="fixedStep">When above 0, bars keep this spacing and the row grows (Wingman);
    /// otherwise they share the span and get thinner (Dashboard).</param>
    private static void SetupReloadBars(Transform parent, Vector2 origin, float span, float width, float fixedStep = 0f)
    {
        _reloadParent = parent; _reloadOrigin = origin; _reloadSpan = span; _reloadWidth = width; _reloadFixedStep = fixedStep;
        ReloadTracks.Clear(); ReloadBars.Clear(); _reloadCount = -1;
        BuildReloadBars(2);
    }

    private static void BuildReloadBars(int count)
    {
        foreach (RectTransform t in ReloadTracks) if (t != null) Object.Destroy(t.gameObject);
        ReloadTracks.Clear(); ReloadBars.Clear();
        _reloadCount = count;
        if (_reloadParent == null) return;
        float step = _reloadFixedStep > 0f ? _reloadFixedStep : _reloadSpan / Mathf.Max(2, count);
        float width = Mathf.Min(_reloadWidth, Mathf.Max(3f, step * 0.7f));
        _reloadStep = step; _reloadBarWidth = width;
        for (int i = 0; i < count; i++)
        {
            RectTransform track = Rect("ReloadBar", _reloadParent, new Vector2(0f, 1f), new Vector2(0.5f, 0f),
                _reloadOrigin + new Vector2(i * step, 0f), new Vector2(width, _reloadBarHeight));
            // Analog: white bars on the stores plate, like its markings.
            Fill(track, _analog ? new Color(1f, 1f, 1f, 0.12f) : Faded(0.18f));
            RectTransform fill = Rect("Fill", track, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero,
                new Vector2(width, 0f));
            Fill(fill, _analog ? Marking : Green);
            ReloadTracks.Add(track);
            ReloadBars.Add(fill);
        }
    }

    private static void UpdateReloadBars(WeaponStation current, int index)
    {
        if (_reloadParent == null) return;
        if (current == null)
        {
            if (_reloadCount != 2) BuildReloadBars(2);
            for (int i = 0; i < ReloadBars.Count; i++) SetReloadBar(i, 0f);
            return;
        }
        PylonFills(current, index, PylonScratch);
        int count = Mathf.Max(1, PylonScratch.Count);
        if (count != _reloadCount) BuildReloadBars(count);
        for (int i = 0; i < ReloadBars.Count; i++) SetReloadBar(i, i < PylonScratch.Count ? PylonScratch[i] : 0f);
    }

    private static void SetReloadBar(int index, float amount)
    {
        if (index >= ReloadBars.Count || ReloadBars[index] == null) return;
        ReloadBars[index].sizeDelta = new Vector2(ReloadBars[index].sizeDelta.x, _reloadBarHeight * Mathf.Clamp01(amount));
    }

    // Per-style starting positions (reference pixels).
    private static void ApplyStyleBases()
    {
        StatusBase = new Vector2(0f, -230f);
        DamageScale = 0.5f;
        TraySlot = 150f;
        SubtitleBase = new Vector2(0f, -36f);
        if (_plus)
        {
            SubtitleBase = new Vector2(0f, -110f);   // below the faction bar
            VectorBase = new Vector2(-270f, 140f);   // right edge of the gauge tray, left of the game's silhouette
            return;
        }
        if (_thunder)
        {
            DamageBase = new Vector2(160f, 125f);    // bottom left (anchored to that corner)
            DamageScale = 0.65f;                     // a little larger than the other styles
            VectorBase = new Vector2(255f, 125f);    // LEFT edge of the gauge tray, just right of the silhouette
            TraySlot = 130f;
            SubtitleBase = new Vector2(0f, -110f);   // below the faction bar
            return;
        }
        if (_analog)
        {
            // The instrument panel along the bottom; the silhouette in its own section on it.
            StatusBase = new Vector2(0f, -350f);
            DamageBase = new Vector2(1352f, -64f);
            DamageScale = 0.3f;
            TraySlot = 100f;
            return;
        }
        if (_minimal)
        {
            // Everything else lives in the bottom strip; tags sit just above it.
            StatusBase = new Vector2(0f, -362f);
            DamageBase = new Vector2(1033f, -68f);   // inside the strip, its own section
            DamageScale = 0.3f;
            TraySlot = 100f;                         // extra gauges must fit the strip's height
            return;
        }
        if (_wingman)
        {
            SpeedBase = new Vector2(-320f, -45f);
            AltBase = new Vector2(320f, -45f);
            ListBase = new Vector2(-40f, 110f);      // bottom-right corner of the weapon cluster
            DamageBase = new Vector2(-130f, -110f);  // anchored to the top-right corner
            VectorBase = new Vector2(-240f, -110f);  // right edge of the gauge tray, beside the silhouette (top right)
            BarsBase = new Vector2(-40f, 62f);       // the HULL-style gauge, right under the cluster
        }
        else
        {
            SpeedBase = new Vector2(-370f, -20f);
            AltBase = new Vector2(370f, -20f);
            ListBase = new Vector2(-40f, 200f);
            DamageBase = new Vector2(-165f, 105f);
            VectorBase = new Vector2(-315f, 105f);
        }
    }

    private static void BuildWingmanLayout()
    {
        _speed = ReadoutBlock(true, arcadeStyle: false);
        _alt = ReadoutBlock(false, arcadeStyle: false);

        // One HULL-style gauge under the cluster, the same width: throttle is the thick bar, fuel
        // fills the thin L-shaped line beneath it; values at the right end, Project Wingman style.
        if (_hasPusher) BarsBase.y -= 10f;
        _bars = Rect("Bars", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), BarsBase,
            new Vector2(ClusterWidth, _hasPusher ? 46f : 36f));
        BuildHull(_bars);
        if (_hasPusher) BuildPusherHullStrip(_bars);

        // The weapon display in its chosen skin: Wingman's cluster (with the star), or Arcade's list.
        if (WingmanClusterSkin) BuildWingmanCluster(); else BuildArcadeList();
    }

    // Wingman's weapon cluster: gun line, the selected weapon with a big counter and reload bars,
    // countermeasures, and the star of the other weapons (which takes over the weapon wheel).
    private static void BuildWingmanCluster()
    {
        _wingCluster = true;
        _list = Rect("WeaponCluster", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), ListBase,
            new Vector2(ClusterWidth, ClusterHeight));
        _flrLine = Text("Flares", _list, 17, TextAlignmentOptions.Right);
        Pin(_flrLine.rectTransform, new Vector2(1f, 1f), new Vector2(300f, 22f), new Vector2(0f, 26f));
        if (_builtCapacitorBar)
            _weaponCap = BuildCapacitorBar(_list, new Vector2(0f, 1f), new Vector2(20f, 0f), 220f, 26f, 14f);
        // Weapon text is right-aligned, flush against the divider (x 252).
        _gunLine = Text("Gun", _list, 17, TextAlignmentOptions.Right);
        Pin(_gunLine.rectTransform, new Vector2(0f, 1f), new Vector2(220f, 22f), new Vector2(20f, -40f));
        _selName = Text("Selected", _list, 16, TextAlignmentOptions.Right);
        Pin(_selName.rectTransform, new Vector2(0f, 1f), new Vector2(200f, 20f), new Vector2(40f, -66f));
        _selCount = Text("Count", _list, 46, TextAlignmentOptions.Right);
        Pin(_selCount.rectTransform, new Vector2(0f, 1f), new Vector2(200f, 54f), new Vector2(40f, -94f));
        // Reserve (Ace Combat Reloads) on its own line under the big counter.
        _selReserve = Text("Reserve", _list, 18, TextAlignmentOptions.Right);
        _selReserve.color = Faded(0.7f);
        Pin(_selReserve.rectTransform, new Vector2(0f, 1f), new Vector2(200f, 22f), new Vector2(40f, -148f));

        // Divider close to the weapon text, leaving the right side to the star.
        RectTransform divider = Rect("Divider", _list, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(252f, -30f),
            new Vector2(2f, 160f));
        Fill(divider, Faded(0.6f));
        _wingDivider = divider;   // doubles as the weapon range scale

        BuildStar(_list, StarCentre);

        // Reload bars left of the counter, one per pylon, Project Wingman style.
        _reloadBarHeight = ReloadBarHeight;
        SetupReloadBars(_list, new Vector2(34f, -150f), 40f, 9f, 15f);
    }

    // The star: 8 diamond petals around a centre, one pointing at each weapon slot, "+N" for more.
    private static void BuildStar(RectTransform parent, Vector2 centre)
    {
        for (int i = 0; i < StarStations.Length; i++) StarStations[i] = WeaponStarLayout.Empty;
        WheelPetal = -1;
        _aimedToggle = WeaponStarLayout.Empty;
        Petals.Clear();
        for (int i = 0; i < 8; i++) Petals.Add(MakePetal(i, parent, centre));
        _starMarker = Rect("StarCentre", parent, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), centre, Vector2.zero);
        Slots.Clear();
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;   // clockwise from the top
            Vector2 at = centre + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * StarRadius;
            var slot = new StarSlot
            {
                Rect = Rect("Slot", parent, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), at, new Vector2(80f, 38f)),
            };
            int before = slot.Rect.childCount;
            Frame(slot.Rect, Green, 1.2f);
            for (int c = before; c < slot.Rect.childCount; c++) slot.Frame.Add(slot.Rect.GetChild(c).GetComponent<Image>());
            slot.Count = Text("Count", slot.Rect, 16, TextAlignmentOptions.Center);
            Pin(slot.Count.rectTransform, new Vector2(0.5f, 1f), new Vector2(80f, 20f), new Vector2(0f, -1f));
            slot.Name = Text("Name", slot.Rect, 12, TextAlignmentOptions.Center);
            Pin(slot.Name.rectTransform, new Vector2(0.5f, 0f), new Vector2(86f, 16f), new Vector2(0f, 1f));
            Slots.Add(slot);
        }
        _overflow = Text("More", parent, 13, TextAlignmentOptions.Center);
        Pin(_overflow.rectTransform, new Vector2(0f, 1f), new Vector2(60f, 16f), centre + new Vector2(StarRadius + 20f, -StarRadius - 6f));
    }

    // The WEAPON WHEEL element with the Wingman star skin: the star on its own, beside an Arcade list.
    internal const int WeaponWheelItem = HudElementPositions.WeaponWheel;
    private static RectTransform _starWheel;
    private static Vector2 StarWheelBase = new(-40f, 430f);

    private static void BuildStandaloneStar()
    {
        Vector2 size = new(StarRadius * 2f + 120f, StarRadius * 2f + 80f);
        _starWheel = Rect("WeaponWheel", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), StarWheelBase, size);
        BuildStar(_starWheel, new Vector2(size.x * 0.5f, -size.y * 0.5f));
    }

    /// <summary>Wingman's cluster is the weapon display (skin 2, or the Wingman style's own).</summary>
    private static bool WingmanClusterSkin
    {
        get
        {
            int skin = ElementLooks.SkinOf(WeaponList);
            return skin == 2 || (skin == 0 && _wingman);
        }
    }

    /// <summary>The standalone star wheel was added and set to the Wingman star skin.</summary>
    private static bool StarWheelWanted
    {
        get
        {
            ElementLooks.Look look = ElementLooks.Get(WeaponWheelItem);
            return !_wingCluster && !look.Hidden && look.Skin == 1;
        }
    }

    // SPD / ALT: label above a bare value, tilted slightly toward the aircraft.
    private static RectTransform WingmanBlock(string name, bool leftOfAircraft, string label,
        out TextMeshProUGUI value, out TextMeshProUGUI extra)
    {
        float inner = leftOfAircraft ? 1f : 0f;
        TextAlignmentOptions align = leftOfAircraft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        RectTransform rect = Rect(name, _root.transform, new Vector2(0.5f, 0.5f), new Vector2(inner, 0.5f),
            leftOfAircraft ? SpeedBase : AltBase, new Vector2(200f, 96f));
        rect.localRotation = Quaternion.Euler(0f, 0f, leftOfAircraft ? 7f : -7f);
        TextMeshProUGUI title = Text("Label", rect, 20, align);
        title.text = label;
        title.characterSpacing = 4f;
        Pin(title.rectTransform, new Vector2(inner, 1f), new Vector2(180f, 24f), Vector2.zero);
        value = Text("Value", rect, 30, align);
        Pin(value.rectTransform, new Vector2(inner, 1f), new Vector2(200f, 36f), new Vector2(0f, -22f));
        extra = Text("Extra", rect, 14, align);
        extra.color = Faded(0.75f);
        Pin(extra.rectTransform, new Vector2(inner, 1f), new Vector2(200f, 36f), new Vector2(0f, -58f));
        return rect;
    }

    private const float HullStart = 52f, HullLength = 380f;

    private static void BuildHull(RectTransform parent)
    {
        // Labels on the left: THR beside the thick bar, FUEL beside the thin line.
        TextMeshProUGUI thr = Text("ThrLabel", parent, 13, TextAlignmentOptions.Left);
        thr.text = "THR";
        thr.characterSpacing = 3f;
        thr.color = Faded(0.8f);
        Pin(thr.rectTransform, new Vector2(0f, 1f), new Vector2(50f, 16f), new Vector2(0f, -2f));
        TextMeshProUGUI fuelLabel = Text("FuelLabel", parent, 11, TextAlignmentOptions.Left);
        fuelLabel.text = "FUEL";
        fuelLabel.characterSpacing = 3f;
        fuelLabel.color = Faded(0.6f);
        Pin(fuelLabel.rectTransform, new Vector2(0f, 1f), new Vector2(50f, 14f), new Vector2(0f, -22f));

        // Throttle: the thick bar on a faint track.
        RectTransform track = Rect("ThrTrack", parent, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(HullStart, -10f), new Vector2(HullLength, 11f));
        // The empty part of the bar is black, as on Project Wingman's HULL bar.
        Fill(track, new Color(0f, 0f, 0f, 0.65f));
        RectTransform fill = Rect("Fill", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 11f));
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.raycastTarget = false;
        RectTransform level = Rect("Level", track, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 15f));
        Fill(level, Green);
        TextMeshProUGUI thrValue = Text("ThrValue", parent, 15, TextAlignmentOptions.Left);
        Pin(thrValue.rectTransform, new Vector2(0f, 1f), new Vector2(90f, 18f), new Vector2(HullStart + HullLength + 18f, -1f));
        _throttle = new Gauge
        {
            Fill = fill, FillImage = fillImage, Level = level, Percent = thrValue,
            Horizontal = true, Length = HullLength, Thickness = 11f, LevelStart = 0f,
        };

        // Fuel: a thin line below the bar, with a gap, running a little past its end and turning
        // up into an L that rises above the bar; it fills with the fuel left. It never touches the bar.
        RectTransform line = Rect("FuelLine", parent, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(HullStart, -26f), new Vector2(HullLength + 7f, 2f));
        Fill(line, Faded(0.25f));
        RectTransform corner = Rect("FuelCorner", parent, new Vector2(0f, 1f), new Vector2(0.5f, 0f),
            new Vector2(HullStart + HullLength + 7f, -27f), new Vector2(2f, 29f));
        Fill(corner, Faded(0.8f));
        RectTransform fuelFill = Rect("Fill", line, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 3f));
        Image fuelImage = fuelFill.gameObject.AddComponent<Image>();
        fuelImage.raycastTarget = false;
        RectTransform fuelLevel = Rect("Level", line, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 8f));
        Fill(fuelLevel, Green);
        TextMeshProUGUI fuelValue = Text("FuelValue", parent, 12, TextAlignmentOptions.Left);
        Pin(fuelValue.rectTransform, new Vector2(0f, 1f), new Vector2(90f, 16f), new Vector2(HullStart + HullLength + 18f, -21f));
        _fuel = new Gauge
        {
            Fill = fuelFill, FillImage = fuelImage, Level = fuelLevel, Percent = fuelValue,
            Horizontal = true, Length = HullLength, Thickness = 3f, LevelStart = 0f,
        };
    }

    // One thin horizontal bar: label, track with a level tick, and the value text after it.
    private static Gauge HBar(RectTransform parent, string label, float y)
    {
        RectTransform row = Rect(label, parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, y), new Vector2(340f, 20f));
        TextMeshProUGUI title = Text("Label", row, 14, TextAlignmentOptions.Left);
        title.text = label;
        title.characterSpacing = 4f;
        title.color = Faded(0.85f);
        Pin(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(54f, 20f), Vector2.zero);
        RectTransform track = Rect("Track", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(BarLength, 10f));
        Fill(track, Faded(0.18f));
        RectTransform end = Rect("End", row, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(56f + BarLength, 0f), new Vector2(2f, 18f));
        Fill(end, Faded(0.8f));
        RectTransform fill = Rect("Fill", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(0f, 6f));
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.raycastTarget = false;
        RectTransform level = Rect("Level", row, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(56f, 0f), new Vector2(2f, 18f));
        Fill(level, Green);
        TextMeshProUGUI percent = Text("Value", row, 15, TextAlignmentOptions.Left);
        Pin(percent.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 20f), new Vector2(56f + BarLength + 10f, 0f));
        return new Gauge { Fill = fill, FillImage = fillImage, Level = level, Percent = percent, Horizontal = true, Thickness = 10f };
    }

    private static string ShortName(WeaponStation station)
    {
        WeaponInfo info = station.WeaponInfo;
        string name = info == null ? "---" : (string.IsNullOrEmpty(info.shortName) ? info.weaponName : info.shortName);
        return (name ?? "---").ToUpperInvariant();
    }

    private static bool IsGun(string name) => name.StartsWith("GUN") || name.Contains("CANNON");

    private static string WithReserve(WeaponStation station, int index)
    {
        int reserve = ReloadsCompat.Reserve(index);
        return reserve > 0 ? $"{station.GetAmmoReadout()} <size=60%><alpha=#B0>+{reserve}</size>" : station.GetAmmoReadout();
    }

    private static readonly List<int> Others = new(), Guns = new();

    private static void RefreshStar(Aircraft aircraft, List<WeaponStation> stations, WeaponStation current)
    {
        int count = stations != null ? stations.Count : 0;
        Others.Clear();
        Guns.Clear();
        for (int i = 0; i < count; i++)
            (IsGun(ShortName(stations[i])) ? Guns : Others).Add(i);
        // One gun reads "GUN · 960"; several are listed by name, one per line.
        string guns = "";
        bool gunSelected = false;
        foreach (int i in Guns)
        {
            WeaponStation st = stations[i];
            bool sel = st == current;
            gunSelected |= sel;
            string label = Guns.Count == 1 ? "GUN" : ShortName(st);
            guns += (guns.Length > 0 ? "\n" : "") + $"{(sel ? "> " : "")}{label} · {WithReserve(st, i)}";
        }
        _gunLine.text = guns;
        _gunLine.color = gunSelected ? Green : Faded(0.75f);

        // Selected weapon: name and a big counter.
        int currentIndex = current != null && stations != null ? stations.IndexOf(current) : -1;
        _selName.text = current != null ? ShortName(current) : "";
        _selCount.text = current == null ? "---" : current.GetAmmoReadout();
        // Reload bars, right-aligned just left of the number whatever its width: the last bar
        // nearest the number, extra pylons adding bars to the left.
        float countWidth = _selCount.GetPreferredValues(_selCount.text).x;
        float rightEdge = 240f - countWidth - 10f;   // the counter's text is right-aligned to x 240
        int bars = ReloadBars.Count;
        for (int b = 0; b < bars; b++)
            if (ReloadBars[b] != null && ReloadBars[b].parent is RectTransform track)
                track.anchoredPosition = new Vector2(rightEdge - _reloadBarWidth * 0.5f - (bars - 1 - b) * _reloadStep,
                    track.anchoredPosition.y);
        int selReserve = currentIndex >= 0 ? ReloadsCompat.Reserve(currentIndex) : 0;
        _selReserve.text = selReserve > 0 ? $"+{selReserve}" : "";

        RefreshStarSlots(aircraft, stations, current, currentIndex);

        _flrLine.text = AppendCapacitorText(CountermeasureText(aircraft, wingmanCount: true), "   ");
        ShowCapacitor(_weaponCap);
    }

    // The star alone (the WEAPON WHEEL element beside an Arcade list).
    private static void RefreshStarOnly(Aircraft aircraft, List<WeaponStation> stations, WeaponStation current)
    {
        int count = stations != null ? stations.Count : 0;
        Others.Clear();
        for (int i = 0; i < count; i++)
            if (!IsGun(ShortName(stations[i]))) Others.Add(i);
        RefreshStarSlots(aircraft, stations, current, current != null && stations != null ? stations.IndexOf(current) : -1);
    }

    private static void RefreshStarSlots(Aircraft aircraft, List<WeaponStation> stations, WeaponStation current, int currentIndex)
    {
        // Star: up to 8 other weapons clockwise from the top, in the game's cycle order. With more,
        // the window keeps the selected one in view and "+N" counts the rest.
        int n = Others.Count;
        WeaponManager manager = aircraft.weaponManager;
        WeaponStarLayout.Assign(Others, currentIndex, manager?.HasMultipleGuns() == true,
            manager?.StationsWithTurrets() > 0, StarStations);
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        for (int s = 0; s < Slots.Count; s++)
        {
            StarSlot slot = Slots[s];
            int station = StarStations[s];
            bool used = station != WeaponStarLayout.Empty;
            bool selected = station >= 0 && stations[station] == current;
            Petals[s].color = selected ? Faded(0.95f) : used ? Faded(0.32f) : Faded(0.12f);
            if (!slot.Rect.gameObject.activeSelf) slot.Rect.gameObject.SetActive(true);
            if (station == WeaponStarLayout.Link || station == WeaponStarLayout.Auto)
            {
                bool link = station == WeaponStarLayout.Link;
                bool on = link ? GunsLinked?.GetValue(manager) is true : hud != null && hud.aircraft == aircraft && hud.turretAutoControl;
                slot.Name.text = link ? "LINK" : "AUTO";
                slot.Count.text = on ? "ON" : "OFF";
                slot.Name.color = Faded(0.62f);
                slot.Count.color = on ? Green : Faded(0.35f);
                foreach (Image edge in slot.Frame) if (edge != null) edge.color = Color.clear;
                continue;
            }
            if (!used)
            {
                // No weapon in this position: a dim placeholder keeps the star complete.
                slot.Count.text = "---";
                slot.Name.text = "";
                slot.Count.color = Faded(0.3f);
                foreach (Image edge in slot.Frame) if (edge != null) edge.color = Color.clear;
                continue;
            }
            WeaponStation st = stations[station];
            bool empty = st.GetAmmoLoaded() <= 0 && st.Ammo <= 0;
            float alpha = selected ? 1f : 0.62f;
            Color c = empty ? new Color(0.55f, 0.55f, 0.55f, alpha) : Faded(alpha);
            slot.Name.text = ShortName(st);
            slot.Count.text = st.GetAmmoReadout();
            slot.Name.color = slot.Count.color = c;
            foreach (Image edge in slot.Frame) if (edge != null) edge.color = selected ? Green : Color.clear;
        }
        _overflow.text = n > 8 ? $"+{n - 8}" : "";
        _overflow.color = Faded(0.7f);

    }
}
