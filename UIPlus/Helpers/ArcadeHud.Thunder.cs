using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// "Thunder" style (War Thunder inspired): text first and information dense.
//   top left:    flight data, system tags and the full weapon list as text lines;
//   top centre:  faction balance bar (standing buildings per side) and the nuclear escalation meter;
//   top right:   minimap with the RWR on top (set by the per-style map default);
//   bottom left: x-ray damage silhouette with a red failed-systems report, gauge tray beside it;
//   centre:      frameless red warnings.
internal static partial class ArcadeHud
{
    internal const int TopBarItem = 20, DataBlockItem = 21;

    private static bool _thunder;
    private static RectTransform _dataBlock, _topBar, _ownFill, _escFill;
    private static TextMeshProUGUI _dataText, _damageReport, _ownLabel, _enemyLabel, _escLabel;
    private static Image _ownImage, _enemyImage, _escImage;
    private static Vector2 DataBlockBase = new(30f, -30f), TopBarBase = new(0f, -22f);
    private const float TopBarWidth = 520f, EscTickTactical = 0f;
    internal static ConfigEntry<bool> ThunderFactionColours;
    internal static ConfigEntry<string> ThunderFactionPalette;
    private static string _paletteSource;
    private static readonly Dictionary<string, Color> Palette = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<Faction> LoggedFactions = new();
    private static Faction _ownFaction, _enemyFaction;
    private static float _nextFactionCount;

    // Fuel endurance: burn rate smoothed over ~10 s from fuel samples at least 0.5 s apart.
    private static Aircraft _fuelAircraft;
    private static float _fuelSampleTime, _fuelSampleQuantity, _burnRate;
    private static int _fuelSamples;
    private static TMP_Text _qolFuelLine;
    private static bool _qolFuelHidden;
    // Building count in progress, a slice per frame so no single frame pays for it.
    private const int IdsPerFrame = 128;
    private static FactionHQ _countOwnHq, _countEnemyHq;
    private static int _countIndex, _countPhase = -1, _countOwn, _countEnemy;
    private static int _ownBuildings, _enemyBuildings;
    private static string _ownName = "", _enemyName = "";

    private static void BuildThunderLayout()
    {
        // Readouts the shared code writes every frame are kept on a hidden holder; this style
        // shows the same data as text lines instead.
        RectTransform hidden = Rect("Hidden", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        hidden.gameObject.SetActive(false);
        _speedValue = Text("Speed", hidden, 10, TextAlignmentOptions.Left);
        _altValue = Text("Altitude", hidden, 10, TextAlignmentOptions.Left);
        _speedExtra = Text("FlightDetail", hidden, 10, TextAlignmentOptions.Left);
        _altExtra = Text("AltDetail", hidden, 10, TextAlignmentOptions.Left);
        _throttle = HBar(hidden, "THR", 0f);
        _fuel = HBar(hidden, "FUEL", 0f);
        ReloadBars.Clear();

        BuildDataBlock();
        BuildTopBar();
    }

    // Top-left data block (Thunder's; an extra in the other styles).
    private static void BuildDataBlock()
    {
        _dataBlock = Rect("DataBlock", _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), DataBlockBase, new Vector2(440f, 420f));
        _dataText = Text("Data", _dataBlock, 17, TextAlignmentOptions.TopLeft);
        _dataText.rectTransform.anchorMin = Vector2.zero;
        _dataText.rectTransform.anchorMax = Vector2.one;
        _dataText.rectTransform.offsetMin = _dataText.rectTransform.offsetMax = Vector2.zero;
        _dataText.lineSpacing = 6f;
        AddShadow(_dataText);
    }

    // Top-centre faction bar and escalation meter (Thunder, and Plus).
    private static void BuildTopBar()
    {
        _topBar = Rect("FactionBar", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), TopBarBase, new Vector2(TopBarWidth, 46f));
        RectTransform bar = Rect("Bar", _topBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(TopBarWidth, 10f));
        _enemyImage = bar.gameObject.AddComponent<Image>();
        _enemyImage.raycastTarget = false;
        _ownFill = Rect("Own", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(TopBarWidth * 0.5f, 10f));
        _ownImage = _ownFill.gameObject.AddComponent<Image>();
        _ownImage.raycastTarget = false;
        Fill(Rect("Middle", bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 16f)), new Color(1f, 1f, 1f, 0.8f));
        _ownLabel = Text("OwnLabel", _topBar, 14, TextAlignmentOptions.Left);
        Pin(_ownLabel.rectTransform, new Vector2(0f, 1f), new Vector2(240f, 18f), Vector2.zero);
        _enemyLabel = Text("EnemyLabel", _topBar, 14, TextAlignmentOptions.Right);
        Pin(_enemyLabel.rectTransform, new Vector2(1f, 1f), new Vector2(240f, 18f), Vector2.zero);
        AddShadow(_ownLabel);
        AddShadow(_enemyLabel);

        RectTransform esc = Rect("Escalation", _topBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(TopBarWidth, 5f));
        Fill(esc, new Color(0f, 0f, 0f, 0.5f));
        _escFill = Rect("Fill", esc, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 5f));
        _escImage = _escFill.gameObject.AddComponent<Image>();
        _escImage.raycastTarget = false;
        _escLabel = Text("EscalationLabel", _topBar, 12, TextAlignmentOptions.Center);
        Pin(_escLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(TopBarWidth, 16f), new Vector2(0f, -42f));
        AddShadow(_escLabel);
        _escTicks.Clear();
        foreach (string label in new[] { "TAC", "STRAT" })
        {
            RectTransform tick = Rect(label, esc, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 11f));
            Fill(tick, new Color(1f, 1f, 1f, 0.85f));
            _escTicks.Add(tick);
        }
        _nextFactionCount = 0f;
        _countPhase = -1;
    }

    private static readonly List<RectTransform> _escTicks = new();

    // Bottom-left damage report, above the silhouette (it moves with it).
    private static void BuildDamageReport()
    {
        _damageReport = Text("DamageReport", _damage, 15, TextAlignmentOptions.BottomLeft);
        RectTransform rt = _damageReport.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(300f, 120f);
        rt.anchoredPosition = new Vector2(-110f, 6f);
        _damageReport.color = new Color(1f, 0.32f, 0.25f, 1f);
        AddShadow(_damageReport);
    }

    // ---- per-frame data -------------------------------------------------------------------

    private static string Line(string label, string value) => $"<alpha=#99>{label}<alpha=#FF> {value}";

    private static void RefreshThunder(Aircraft aircraft, List<WeaponStation> stations, WeaponStation current)
    {
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        string alert = Hex(theme != null ? theme.Alert : Color.red);
        string warn = Hex(theme != null ? theme.Warning : new Color(1f, 0.8f, 0.2f));
        var sb = new StringBuilder(512);

        float speed = aircraft.speed;
        float sound = LevelInfo.GetSpeedOfSound(aircraft.GlobalPosition().y);
        bool over = speed > GetFloat(App<SpeedGauge>(), "overspeedThreshold", float.MaxValue);
        string spd = UnitConverter.SpeedReading(speed);
        sb.AppendLine($"{Line("SPD", over ? $"<color={alert}>{spd}</color>" : spd)}   {Line("M", $"{speed / sound:F2}")}");
        sb.AppendLine($"{Line("ALT", UnitConverter.AltitudeReading(aircraft.transform.position.GlobalY()))}   {Line("AGL", UnitConverter.AltitudeReading(aircraft.radarAlt))}");
        float climb = Vector3.Dot(aircraft.CockpitRB().velocity, Vector3.up);
        sb.AppendLine(Line("V/S", UnitConverter.ClimbRateReading(climb)));
        float g = Vector3.Dot(aircraft.pilots[0].GetAccel() + Vector3.up, aircraft.transform.up);
        string maxG = Get<TextMeshProUGUI>(App<GIndicators>(), "gMaxForceLabel")?.text ?? "";
        TextMeshProUGUI aoa = Get<TextMeshProUGUI>(App<AoADisplay>(), "AoAText");
        string aoaText = aoa != null && aoa.enabled ? $"<color={Hex(aoa.color)}>{aoa.text}</color>" : "--";
        sb.AppendLine($"{Line("G", $"{g:F1} <size=80%>{maxG}</size>")}   {Line("AOA", aoaText)}");

        // Throttle in War Thunder's words: WEP on afterburner.
        float throttle = aircraft.GetInputs().throttle;
        TextMeshProUGUI reading = Get<TextMeshProUGUI>(App<ThrottleGauge>(), "throttleReading");
        string thr = $"{throttle * 100f:F0}%";
        if (reading != null && !string.IsNullOrEmpty(reading.text))
        {
            string word = reading.text.Replace("AFTERBURNER", "WEP").Replace("AIRBRAKE", "BRK");
            thr = word.Contains("%") ? word : $"{throttle * 100f:F0}% {word}";
            if (word.Contains("WEP")) thr = $"<color={warn}>{thr}</color>";
        }
        float fuel = aircraft.GetFuelLevel();
        string fuelText = $"{fuel * 100f:F0}%";
        if (fuel < 0.15f) fuelText = $"<color={alert}>{fuelText}</color>";
        else if (fuel < 0.3f) fuelText = $"<color={warn}>{fuelText}</color>";
        sb.AppendLine($"{Line("THR", thr)}   {Line("FUEL", fuelText)}  {FuelEndurance(aircraft, alert, warn)}");
        TextMeshProUGUI rpm = Get<TextMeshProUGUI>(App<RPMGauge>(), "rpmText");
        if (rpm != null && !string.IsNullOrEmpty(rpm.text))
            sb.AppendLine(Line("RPM", $"<color={Hex(rpm.color)}>{rpm.text}</color>"));

        // System tags as one text line (gear, brake, auto-hover), coloured as in the other styles.
        if (TagValues.Count > 0)
        {
            var tags = new List<string>();
            foreach (var (text, colour) in TagValues) tags.Add($"<color={Hex(colour)}>{text}</color>");
            sb.AppendLine(string.Join("  ·  ", tags));
        }

        // Full weapon list, selected first-marked, with reserves.
        sb.AppendLine();
        int count = stations != null ? stations.Count : 0;
        for (int i = 0; i < count; i++)
        {
            WeaponStation st = stations[i];
            bool sel = st == current;
            bool empty = st.GetAmmoLoaded() <= 0 && st.Ammo <= 0;
            string line = $"{(sel ? "> " : "  ")}{ShortName(st),-14} {WithReserve(st, i)}";
            sb.AppendLine(empty ? $"<color=#8C8C8C>{line}</color>" : sel ? line : $"<alpha=#B0>{line}<alpha=#FF>");
        }
        string rng = RangeText(aircraft, false);
        if (rng != null) sb.AppendLine(rng);
        string cm = CountermeasureText(aircraft);
        if (HasCapacitor()) cm += (cm.Length > 0 ? "   " : "") + $"CAP {100f * aircraft.GetPowerSupply().GetCharge():F0}%";
        if (cm.Length > 0) sb.AppendLine(cm);
        _dataText.text = sb.ToString();
        _dataText.color = Green;
        TextAlignmentOptions align = ElementLooks.SkinOf(DataBlockItem) switch
        {
            1 => TextAlignmentOptions.Top,
            2 => TextAlignmentOptions.TopRight,
            _ => TextAlignmentOptions.TopLeft,
        };
        if (_dataText.alignment != align) _dataText.alignment = align;

        RefreshDamageReport();
        RefreshTopBar();
    }

    private static FieldInfo _failureField;

    // Failed systems, as the game's own damage display names them (its failure indicators).
    private static void RefreshDamageReport()
    {
        if (_damageReport == null) return;
        _failureField ??= AccessTools.Field(typeof(StatusDisplay), "failureIndicators");
        var sb = new StringBuilder();
        if (_source != null && _failureField?.GetValue(_source) is List<GameObject> failures)
            foreach (GameObject f in failures)
                if (f != null && f.activeSelf)
                {
                    if (sb.Length > 0) sb.Append(_minimal ? "  " : "\n");
                    sb.Append(f.name.Replace('_', ' ').ToUpperInvariant());
                }
        _damageReport.text = sb.ToString();
        if (_minimal)
        {
            ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
            _damageReport.color = theme != null ? theme.Alert : new Color(1f, 0.32f, 0.25f);
        }
    }

    // Faction balance (standing buildings per side, counted every 2 s) and the escalation meter.
    private static void RefreshTopBar()
    {
        if (_topBar == null) return;
        if (_countPhase < 0 && Time.unscaledTime >= _nextFactionCount)
        {
            _nextFactionCount = Time.unscaledTime + 2f;
            BeginBuildingCount();
        }
        if (_countPhase >= 0) StepBuildingCount();
        int total = _ownBuildings + _enemyBuildings;
        float share = total > 0 ? (float)_ownBuildings / total : 0.5f;
        _ownFill.sizeDelta = new Vector2(TopBarWidth * share, 10f);
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        // Either each side's own faction colour, or relative to you: the game's friendly and
        // hostile unit colours (which follow its colour / accessibility settings).
        bool factionColours = ThunderFactionColours != null && ThunderFactionColours.Value;
        _ownImage.color = factionColours && _ownFaction != null ? FactionColour(_ownFaction)
            : theme != null ? theme.HudUnitFriendly : new Color(0.3f, 0.6f, 1f);
        _enemyImage.color = factionColours && _enemyFaction != null ? FactionColour(_enemyFaction)
            : theme != null ? theme.HudUnitHostile : new Color(1f, 0.3f, 0.3f);
        _ownLabel.text = $"{_ownName}  {_ownBuildings}";
        _enemyLabel.text = $"{_enemyBuildings}  {_enemyName}";
        _ownLabel.color = _ownImage.color;
        _enemyLabel.color = _enemyImage.color;

        MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
        if (mission == null) return;
        float tac = Mathf.Max(1f, mission.tacticalThreshold), strat = Mathf.Max(tac, mission.strategicThreshold);
        float scale = strat * 1.25f;
        float level = Mathf.Clamp01(mission.currentEscalation / scale);
        _escFill.sizeDelta = new Vector2(TopBarWidth * level, 5f);
        bool pastStrat = mission.currentEscalation >= strat, pastTac = mission.currentEscalation >= tac;
        Color alert = theme != null ? theme.Alert : Color.red, warn = theme != null ? theme.Warning : Color.yellow;
        _escImage.color = pastStrat ? alert : pastTac ? warn : Green;
        if (_escTicks.Count == 2)
        {
            _escTicks[0].anchoredPosition = new Vector2(TopBarWidth * tac / scale, 0f);
            _escTicks[1].anchoredPosition = new Vector2(TopBarWidth * strat / scale, 0f);
        }
        string state = pastStrat ? "STRATEGIC CLEARED" : pastTac ? "TACTICAL CLEARED" : "NUCLEAR ESCALATION";
        _escLabel.text = $"{state}  {mission.currentEscalation:N0} / {tac:N0} / {strat:N0}";
        _escLabel.color = pastStrat ? alert : pastTac ? warn : Faded(0.75f);
    }

    private static Color Opaque(Color c) => new(c.r, c.g, c.b, 1f);

    // The palette setting ("BDF=#4CAF50, PALA=#E53935") by faction tag or name; factions not in it
    // (e.g. from mods) use the game's own faction colour. The game's colours are purple/yellow,
    // not the green/red players know the two sides by, hence the palette.
    private static Color FactionColour(Faction faction)
    {
        string source = ThunderFactionPalette != null ? ThunderFactionPalette.Value : "";
        if (source != _paletteSource)
        {
            _paletteSource = source;
            Palette.Clear();
            foreach (string entry in (source ?? "").Split(','))
            {
                string[] pair = entry.Split('=');
                if (pair.Length == 2 && ColorUtility.TryParseHtmlString(pair[1].Trim(), out Color c))
                    Palette[pair[0].Trim()] = c;
            }
        }
        if (LoggedFactions.Add(faction))
            Plugin.Logger.LogInfo($"Thunder HUD: faction '{faction.factionName}' tag '{faction.factionTag}', game colour #{ColorUtility.ToHtmlStringRGB(faction.color)}");
        if ((faction.factionTag != null && Palette.TryGetValue(faction.factionTag, out Color byTag))
            || (faction.factionName != null && Palette.TryGetValue(faction.factionName, out byTag)))
            return Opaque(byTag);
        return Opaque(faction.color);
    }

    // "14min 101km": flight time left at the current burn and how far that takes you at the
    // current speed. Yellow under 7 min, red under 3. Shows "--min" until the burn is known.
    private static string FuelEndurance(Aircraft aircraft, string alert, string warn)
    {
        float now = Time.timeSinceLevelLoad;
        float quantity = aircraft.GetFuelQuantity();
        if (_fuelAircraft != aircraft || quantity > _fuelSampleQuantity + 1f)
        {
            // New aircraft, or refuelled: start over.
            _fuelAircraft = aircraft; _fuelSampleTime = now; _fuelSampleQuantity = quantity;
            _burnRate = 0f; _fuelSamples = 0;
        }
        else if (now - _fuelSampleTime >= 0.5f)
        {
            float rate = Mathf.Max(0f, (_fuelSampleQuantity - quantity) / (now - _fuelSampleTime));
            float k = 1f - Mathf.Exp(-(now - _fuelSampleTime) / 10f);
            _burnRate = _fuelSamples == 0 ? rate : _burnRate + (rate - _burnRate) * k;
            _fuelSamples++;
            _fuelSampleTime = now; _fuelSampleQuantity = quantity;
        }
        if (_fuelSamples < 3 || _burnRate < 0.0001f) return "<alpha=#99>--min<alpha=#FF>";
        float seconds = Mathf.Min(quantity / _burnRate, 36000f);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        string time = minutes >= 60 ? $"{minutes / 60}h{minutes % 60:D2}" : minutes >= 1 ? $"{minutes}min" : "<1min";
        string text = $"{time} {UnitConverter.DistanceReading(aircraft.speed * seconds)}";
        return minutes < 3 ? $"<color={alert}>{text}</color>" : minutes < 7 ? $"<color={warn}>{text}</color>" : $"<alpha=#B0>{text}<alpha=#FF>";
    }

    // NeutralQoL's own fuel time hangs off the game's fuel gauge, which Thunder hides; it would
    // float on its own. Thunder shows its own reading, so hide NeutralQoL's while Thunder is up.
    private static float _nextQolLook;

    private static void SyncNeutralQolFuel(bool hide)
    {
        NoAutopilotCompat.SetHidden(hide);
        if (hide && _qolFuelLine == null && Time.unscaledTime >= _nextQolLook)
        {
            _nextQolLook = Time.unscaledTime + 2f;
            FuelGauge gauge = App<FuelGauge>();
            if (gauge != null)
                foreach (TMP_Text t in gauge.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == "NeutralQoL FuelTime") { _qolFuelLine = t; break; }
        }
        if (_qolFuelLine == null || _qolFuelHidden == hide) return;
        _qolFuelLine.enabled = !hide;
        _qolFuelHidden = hide;
        if (!hide) _qolFuelLine = null;
    }

    // Picks the two sides from the game's own HQ registry (no scene search), then starts a pass.
    private static void BeginBuildingCount()
    {
        GameManager.GetLocalHQ(out FactionHQ local);
        FactionHQ own = local, enemy = null;
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null) continue;
            if (own == null) own = hq;
            else if (hq != own && enemy == null) enemy = hq;
        }
        _countOwnHq = own; _countEnemyHq = enemy;
        _ownFaction = own != null ? own.faction : null;
        _enemyFaction = enemy != null ? enemy.faction : null;
        _ownName = own != null && own.faction != null ? own.faction.factionName : "";
        _enemyName = enemy != null && enemy.faction != null ? enemy.faction.factionName : "";
        _countIndex = _countOwn = _countEnemy = 0;
        _countPhase = 0;
    }

    // Counts up to IdsPerFrame units, then publishes the totals once both sides are done.
    private static void StepBuildingCount()
    {
        int budget = IdsPerFrame;
        while (budget > 0 && _countPhase <= 1)
        {
            FactionHQ hq = _countPhase == 0 ? _countOwnHq : _countEnemyHq;
            int count = hq != null && hq.factionUnits != null ? hq.factionUnits.Count : 0;
            if (_countIndex >= count) { _countPhase++; _countIndex = 0; continue; }
            if (UnitRegistry.TryGetUnit(hq.factionUnits[_countIndex], out Building building)
                && building != null && !building.disabled)
            {
                if (_countPhase == 0) _countOwn++; else _countEnemy++;
            }
            _countIndex++;
            budget--;
        }
        if (_countPhase > 1)
        {
            _ownBuildings = _countOwn;
            _enemyBuildings = _countEnemy;
            _countPhase = -1;
        }
    }
}
