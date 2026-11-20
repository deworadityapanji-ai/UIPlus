using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// Optional integration with "Ace Combat Style Reloads" (Dandy-Yak). Read-only: it asks that mod for
// the local aircraft's reserves and reload progress. Nothing is referenced at build time, so
// ArcadeCam works the same when the mod is absent. Used with the authors' permission.
internal static class ReloadsCompat
{
    private static bool _resolved;
    private static MethodInfo _forLocalHud;
    private static FieldInfo _stations, _countermeasures;
    private static PropertyInfo _reserve, _queued;
    private static MethodInfo _nextProgress;
    private static object _state;
    private static int _stateFrame = -1;

    private static bool Resolve()
    {
        if (_resolved) return _forLocalHud != null;
        _resolved = true;
        Type state = AccessTools.TypeByName("AceCombatReloads.Game.ReloadState");
        Type aircraftState = AccessTools.TypeByName("AceCombatReloads.Game.AircraftState");
        Type tracker = AccessTools.TypeByName("AceCombatReloads.Core.StationTracker");
        if (state == null || aircraftState == null || tracker == null) return false;
        _forLocalHud = AccessTools.Method(state, "ForLocalHud");
        _stations = AccessTools.Field(aircraftState, "Stations");
        _countermeasures = AccessTools.Field(aircraftState, "Countermeasures");
        _reserve = AccessTools.Property(tracker, "Reserve");
        _queued = AccessTools.Property(tracker, "QueuedRounds");
        _nextProgress = AccessTools.Method(tracker, "NextProgress");
        if (_forLocalHud == null || _stations == null || _reserve == null || _nextProgress == null) _forLocalHud = null;
        else Plugin.Logger.LogInfo("Ace Combat Reloads found: Arcade HUD shows its reserves and reload progress.");
        return _forLocalHud != null;
    }

    private static object State()
    {
        if (_stateFrame == Time.frameCount) return _state;
        _stateFrame = Time.frameCount;
        _state = null;
        if (!Resolve()) return null;
        try
        {
            object[] args = { null };
            _state = _forLocalHud.Invoke(null, args);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("Ace Combat Reloads: could not read state, integration off: " + e.Message);
            _forLocalHud = null;
        }
        return _state;
    }

    private static object Tracker(IDictionary map, object key) =>
        map != null && key != null && map.Contains(key) ? map[key] : null;

    private static object StationTracker(int index) =>
        State() is object s ? Tracker(_stations.GetValue(s) as IDictionary, index) : null;

    // Rounds waiting in reserve for a weapon station (by its index in aircraft.weaponStations).
    internal static int Reserve(int stationIndex) =>
        StationTracker(stationIndex) is object t ? (int)_reserve.GetValue(t) : 0;

    internal static int CountermeasureReserve(Countermeasure cm)
    {
        if (cm == null || _countermeasures == null || !(State() is object s)) return 0;
        return Tracker(_countermeasures.GetValue(s) as IDictionary, cm) is object t ? (int)_reserve.GetValue(t) : 0;
    }

    private static FieldInfo _queue, _pendStart, _pendDue, _pendRounds;

    /// <summary>
    /// Progress (0..1) of every round currently reloading on a station, one entry per round,
    /// soonest to finish first. With consecutive reloads the later ones are still 0 (not started).
    /// </summary>
    internal static void PendingProgress(int stationIndex, List<float> output)
    {
        output.Clear();
        object t = StationTracker(stationIndex);
        if (t == null) return;
        _queue ??= AccessTools.Field(t.GetType(), "_queue");
        if (!(_queue?.GetValue(t) is IList queue) || queue.Count == 0) return;
        double now = Time.timeSinceLevelLoad;
        foreach (object pending in queue)
        {
            if (pending == null) continue;
            if (_pendStart == null)
            {
                Type type = pending.GetType();
                _pendStart = AccessTools.Field(type, "Start");
                _pendDue = AccessTools.Field(type, "Due");
                _pendRounds = AccessTools.Field(type, "Rounds");
                if (_pendStart == null || _pendDue == null || _pendRounds == null) return;
            }
            double start = (double)_pendStart.GetValue(pending), due = (double)_pendDue.GetValue(pending);
            float progress = (float)Math.Max(0.0, Math.Min(1.0, (now - start) / Math.Max(0.001, due - start)));
            int rounds = (int)_pendRounds.GetValue(pending);
            for (int r = 0; r < rounds && output.Count < 32; r++) output.Add(progress);
        }
        output.Sort((a, b) => b.CompareTo(a));   // soonest first
    }

    // Progress (0..1) of the next round being reloaded, or -1 when nothing is reloading.
    internal static float Progress(int stationIndex)
    {
        object t = StationTracker(stationIndex);
        if (t == null) return -1f;
        if (_queued != null && (int)_queued.GetValue(t) == 0) return -1f;
        object[] args = { (double)Time.timeSinceLevelLoad, 0.0 };
        return (float)_nextProgress.Invoke(t, args);
    }
}

// Optional integration with "KaceyTronic RWR" (KcTries). Moves its scope and alert panel with the
// HUD layout editor; in the Arcade style it also recolours it to the HUD green and drops its dark
// backgrounds. Its own config stays the source of truth: everything is re-applied each frame after
// the mod's own update, and handed back untouched when not needed. Used with the author's permission.
internal static class RwrCompat
{
    private const string Guid = "pavehog727.traditionalrwr";
    // Bottom-left of the scope in Arcade, in 1080p reference pixels: right of the map, same bottom.
    private static readonly Vector2 ArcadeScopeCorner = new(388f, 65f);

    private static bool _resolved;
    private static Type _type;
    private static FieldInfo _scope, _panel, _compact, _scopeBackground, _panelBackground, _useCompact, _userTheme;
    private static Component _instance;
    private static float _nextFind;
    private static Color? _savedTheme;

    internal static bool Present => Chainloader.PluginInfos.ContainsKey(Guid);

    private static bool Resolve()
    {
        if (_resolved) return _type != null;
        _resolved = true;
        if (!Present) return false;
        _type = AccessTools.TypeByName("TraditionalRWR.RwrScopeController");
        if (_type == null) return false;
        _scope = AccessTools.Field(_type, "_scopeRoot");
        _panel = AccessTools.Field(_type, "_warningPanelRoot");
        _compact = AccessTools.Field(_type, "_compactBillboardRoot");
        _scopeBackground = AccessTools.Field(_type, "_backgroundImage");
        _panelBackground = AccessTools.Field(_type, "_warningPanelBackground");
        _useCompact = AccessTools.Field(_type, "UseCompactBillboard");
        _userTheme = AccessTools.Field(_type, "UserThemeColor");
        if (_scope == null) { _type = null; return false; }
        Plugin.Logger.LogInfo("KaceyTronic RWR found: its scope follows the HUD layout editor.");
        return true;
    }

    // The controller lives on the RWR plugin's own GameObject, which BepInEx hides, so the normal
    // FindObjectOfType search never sees it. Ask the plugin first, then search hidden objects too.
    private static Component FindController()
    {
        Component found = null;
        if (Chainloader.PluginInfos.TryGetValue(Guid, out var info) && info.Instance != null)
            found = info.Instance.GetComponent(_type) ?? info.Instance.GetComponentInChildren(_type, true);
        if (found == null)
            foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(_type))
                if (candidate is Component c)
                {
                    found = c;
                    if (_scope.GetValue(c) != null) break; // prefer the one that has built its scope
                }
        if (found != null && !_loggedFound)
        {
            _loggedFound = true;
            Plugin.Logger.LogInfo($"KaceyTronic RWR: controller found on '{found.gameObject.name}'.");
        }
        return found;
    }

    private static bool _loggedFound;

    private static RectTransform Scope => _instance != null ? _scope.GetValue(_instance) as RectTransform : null;

    private static RectTransform Panel
    {
        get
        {
            if (_instance == null) return null;
            bool compact = _useCompact?.GetValue(null) is bool b && b;
            return (compact ? _compact : _panel)?.GetValue(_instance) as RectTransform;
        }
    }

    // Called every LateUpdate (after the RWR mod's own Update has placed and coloured its scope).
    internal static void Tick(bool chase, bool arcade, Color hudColour)
    {
        if (!Resolve()) return;
        if (_instance == null && Time.unscaledTime > _nextFind)
        {
            _nextFind = Time.unscaledTime + 2f;
            _instance = FindController();
        }
        RectTransform scope = Scope;
        if (!chase || scope == null)
        {
            if (scope != null) scope.localRotation = Quaternion.identity;
            if (Panel != null) Panel.localRotation = Quaternion.identity;
            Theme(null);
            RestoreRow();
            return;
        }
        Canvas canvas = scope.GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null && canvas.rootCanvas.scaleFactor > 0f ? canvas.rootCanvas.scaleFactor : 1f;
        Vector2 offset = HudElementPositions.PixelOffset(HudElementPositions.Rwr) / scaleFactor;
        // The alert boxes (TGT / SEEN / MSL...) have their own position, starting where the
        // style puts them (beside the scope or above the map).
        Vector2 alertOffset = HudElementPositions.PixelOffset(HudElementPositions.RwrAlerts) / scaleFactor;   // follows the scope until moved
        int alertSkin = ElementLooks.SkinOf(HudElementPositions.RwrAlerts);   // 1 = one row, 2 = 2x2 grid
        RectTransform panel = Panel;
        // Upright unless the map leans (Wingman), in which case the scope and alerts lean with it.
        Quaternion lean = Quaternion.identity;
        scope.localRotation = lean;
        if (panel != null) panel.localRotation = lean;

        if (arcade)
        {
            // Over the minimap: the scope's centre on the map's centre (Arcade centres the map on
            // the aircraft), the alert boxes in one row just above the map. Positions are derived
            // from where the mod placed things this frame, so its own settings still apply.
            if (ArcadeHud.RwrSlot(out Vector2 slotCentre, out float slotSize, out float stripTop))
            {
                // Minimal: the scope is fitted into its box in the instrument strip, the alert
                // boxes in a row just above the strip; the map is left alone.
                float fit = slotSize / Mathf.Max(1f, scope.rect.width * scaleFactor);
                scope.localScale = new Vector3(fit, fit, 1f);
                MoveCentreTo(scope, slotCentre + offset * scaleFactor, scaleFactor);
                // The alert boxes stack in a column beside the scope, inside the strip.
                if (panel != null && ArcadeHud.RwrAlertSlot(out Vector2 alertCentre, out float alertHeight))
                {
                    bool compact = _useCompact?.GetValue(null) is bool b && b;
                    if (compact || alertSkin == 2) KeepGrid(panel);
                    else if (alertSkin == 1) ArrangeRow(panel, alertHeight * 4f, scaleFactor);
                    else ArrangeRow(panel, alertHeight, scaleFactor, column: true);
                    MoveCentreTo(panel, alertCentre + alertOffset * scaleFactor, scaleFactor);
                }
                DropThreatList();
            }
            else if (MapViewport(out Vector2 mapCentre, out float mapTop, out float mapWidth))
            {
                lean = Quaternion.Euler(0f, 0f, ArcadeHud.MapLeanDegrees);
                scope.localRotation = lean;
                MoveCentreTo(scope, mapCentre + offset * scaleFactor, scaleFactor);
                if (panel != null)
                {
                    bool compact = _useCompact?.GetValue(null) is bool b && b;
                    bool grid = compact || alertSkin == 2;
                    if (grid) KeepGrid(panel);
                    else ArrangeRow(panel, mapWidth, scaleFactor);
                    panel.localRotation = lean;
                    Vector3[] c = new Vector3[4];
                    panel.GetWorldCorners(c);
                    float rowHalfHeight = grid ? (c[2].y - c[0].y) * 0.5f : RowHalfHeight(panel);
                    // Just above the map's top edge, following its tilt when it leans.
                    MapTopEdge(out Vector2 topMid, out Vector2 up);
                    MoveCentreTo(panel, topMid + up * (8f + rowHalfHeight) + alertOffset * scaleFactor, scaleFactor);
                    // The game's missile list grows up from the map's top edge; start it above the row.
                    LiftThreatList(mapTop + 8f + rowHalfHeight * 2f + 6f + Mathf.Min(0f, alertOffset.y) * scaleFactor);
                }
            }
            else
            {
                float s = Screen.height / 1080f;
                Vector2 corner = ArcadeScopeCorner * s / scaleFactor + offset;
                scope.anchoredPosition = corner;
                if (panel != null)
                    panel.anchoredPosition = corner - offset + alertOffset + new Vector2(0f, scope.rect.height * scope.localScale.y + 8f);
            }
            Clear(_scopeBackground);
            Clear(_panelBackground);
            Theme(hudColour);
        }
        else
        {
            // Native: the mod places itself each frame; just shift both by the editor offset
            // (the alerts by their own, once moved). One row is the only skin applied here.
            if (panel != null && alertSkin == 1) ArrangeRow(panel, 4f * (BoxWidth + BoxGap) * scaleFactor, scaleFactor);
            else if (panel != null && _rowPanel != null) KeepGrid(panel);
            else RestoreRow();
            scope.anchoredPosition += offset;
            if (panel != null) panel.anchoredPosition += alertOffset;
            Theme(null);
        }
    }

    // Middle of the minimap's top edge, and the direction pointing up from it (screen pixels).
    private static void MapTopEdge(out Vector2 middle, out Vector2 up)
    {
        middle = default; up = Vector2.up;
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null || !(map.transform is RectTransform view)) return;
        var c = new Vector3[4];
        view.GetWorldCorners(c);   // bottom-left, top-left, top-right, bottom-right
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(null, c[0]);
        Vector2 tl = RectTransformUtility.WorldToScreenPoint(null, c[1]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        middle = (tl + tr) * 0.5f;
        up = (tl - bl).normalized;
    }

    // Screen-pixel centre, top edge and width of the minimap's visible area.
    private static bool MapViewport(out Vector2 centre, out float top, out float width)
    {
        centre = default; top = 0f; width = 0f;
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null || !(map.transform is RectTransform view) || !view.gameObject.activeInHierarchy) return false;
        var c = new Vector3[4];
        view.GetWorldCorners(c);
        Vector2 lo = RectTransformUtility.WorldToScreenPoint(null, c[0]);
        Vector2 hi = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        centre = (lo + hi) * 0.5f;
        top = Mathf.Max(lo.y, hi.y);
        width = Mathf.Abs(hi.x - lo.x);
        return width > 10f;
    }

    // Shift a bottom-left-anchored panel so its drawn centre lands on a screen point.
    private static void MoveCentreTo(RectTransform rect, Vector2 screenCentre, float scaleFactor)
    {
        var c = new Vector3[4];
        rect.GetWorldCorners(c);
        Vector2 now = RectTransformUtility.WorldToScreenPoint(null, (c[0] + c[2]) * 0.5f);
        rect.anchoredPosition += (screenCentre - now) / scaleFactor;
    }

    // The full alert panel is four boxes in a 2x2 grid around its centre (TGT/SEEN on top, MSL and
    // HI-LO below). In Arcade they go in one row; their original spots are remembered and restored.
    private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> RowOriginals = new();
    private static RectTransform _rowPanel;
    private const float BoxWidth = 90f, BoxHeight = 42f, BoxGap = 6f;

    // Each box is several pieces (a border and a label; HI-LO is a line plus two labels), so the
    // pieces are grouped by the quadrant of the original 2x2 grid they sit in and each group is
    // moved as one unit, keeping the pieces' positions relative to each other.
    // column: stack the boxes top to bottom and fit them to a height, instead of a row fitted to a width.
    private static void ArrangeRow(RectTransform panel, float mapWidthPx, float scaleFactor, bool column = false)
    {
        if (_rowPanel != panel) { RestoreRow(); _rowPanel = panel; }
        var groups = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<RectTransform>>();
        foreach (Transform child in panel)
        {
            if (!(child is RectTransform r) || r.anchorMin != r.anchorMax) continue; // skip the stretched background
            if (!RowOriginals.ContainsKey(r)) RowOriginals[r] = r.anchoredPosition;
            Vector2 o = RowOriginals[r];
            int key = (o.y >= 0f ? 0 : 2) + (o.x >= 0f ? 1 : 0);   // 0 TGT, 1 SEEN, 2 MSL, 3 HI-LO
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new System.Collections.Generic.List<RectTransform>();
            list.Add(r);
        }
        var keys = new System.Collections.Generic.List<int>(groups.Keys);
        keys.Sort();
        float step = BoxWidth + BoxGap;
        float first = -(keys.Count - 1) * step * 0.5f;
        for (int i = 0; i < keys.Count; i++)
        {
            // The group's centre is the middle of the area its pieces cover (the border dominates).
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (RectTransform r in groups[keys[i]])
            {
                Vector2 o = RowOriginals[r];
                Vector2 size = Vector2.Scale(r.rect.size, r.localScale);
                Vector2 lo = o - Vector2.Scale(size, r.pivot), hi = lo + size;
                min = Vector2.Min(min, lo);
                max = Vector2.Max(max, hi);
            }
            Vector2 centre = (min + max) * 0.5f;
            Vector2 slot = column
                ? new Vector2(0f, (keys.Count - 1) * (BoxHeight + BoxGap) * 0.5f - i * (BoxHeight + BoxGap))
                : new Vector2(first + i * step, 0f);
            Vector2 shift = slot - centre;
            foreach (RectTransform r in groups[keys[i]]) r.anchoredPosition = RowOriginals[r] + shift;
        }
        // Fit the row to the map's width (or the column to its slot's height).
        float extent = column
            ? keys.Count * BoxHeight + (keys.Count - 1) * BoxGap
            : keys.Count * BoxWidth + (keys.Count - 1) * BoxGap;
        float fit = Mathf.Clamp(mapWidthPx / scaleFactor / Mathf.Max(1f, extent), column ? 0.3f : 0.5f, 1f);
        panel.localScale = new Vector3(fit, fit, 1f);
    }

    private static float RowHalfHeight(RectTransform panel) =>
        BoxHeight * 0.5f * panel.localScale.y * (panel.GetComponentInParent<Canvas>()?.rootCanvas.scaleFactor ?? 1f);

    // The game's incoming-missile list ("Missile [IR] 2.6km") sits on the map panel. In Arcade the
    // RWR alert row takes that spot, so the list is moved up to start just above the row.
    private static RectTransform _threatList;
    private static Vector3 _threatListOriginal;

    private static void LiftThreatList(float bottomPx)
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        Transform panel = map != null && map.hudMapAnchor != null ? map.hudMapAnchor.parent : null;
        ThreatList list = panel != null ? panel.GetComponentInChildren<ThreatList>(true) : null;
        RectTransform rect = list != null ? list.transform as RectTransform : null;
        if (rect != _threatList)
        {
            DropThreatList();
            _threatList = rect;
            if (rect != null) _threatListOriginal = rect.localPosition;
        }
        if (rect == null) return;
        rect.localPosition = _threatListOriginal;
        var c = new Vector3[4];
        rect.GetWorldCorners(c);
        float bottom = RectTransformUtility.WorldToScreenPoint(null, c[0]).y;
        float scale = rect.parent != null && rect.parent.lossyScale.y > 0.0001f ? rect.parent.lossyScale.y : 1f;
        if (bottom < bottomPx)
            rect.localPosition = _threatListOriginal + new Vector3(0f, (bottomPx - bottom) / scale, 0f);
    }

    private static void DropThreatList()
    {
        if (_threatList != null) _threatList.localPosition = _threatListOriginal;
        _threatList = null;
    }

    // The RWR mod's own 2x2 layout: undo any row arrangement, at its own size.
    private static void KeepGrid(RectTransform panel)
    {
        if (_rowPanel == null) return;
        panel.localScale = Vector3.one;
        RestoreRow();
    }

    private static void RestoreRow()
    {
        DropThreatList();
        foreach (var pair in RowOriginals) if (pair.Key != null) pair.Key.anchoredPosition = pair.Value;
        RowOriginals.Clear();
        _rowPanel = null;
    }

    private static void Clear(FieldInfo field)
    {
        if (field?.GetValue(_instance) is Image image && image.color.a > 0f) image.color = Color.clear;
    }

    // Arcade borrows the theme colour; the user's own colour is put back when leaving it.
    private static void Theme(Color? colour)
    {
        if (_userTheme == null) return;
        if (colour.HasValue)
        {
            if (!_savedTheme.HasValue) _savedTheme = (Color)_userTheme.GetValue(null);
            _userTheme.SetValue(null, colour.Value);
        }
        else if (_savedTheme.HasValue)
        {
            _userTheme.SetValue(null, _savedTheme.Value);
            _savedTheme = null;
        }
    }

    internal static void Dispose()
    {
        Theme(null);
        RestoreRow();
    }

    // The scope alone (the alert boxes are their own item).
    internal static bool TryGetRect(out Rect rect) => RectOf(Scope, out rect);

    internal static bool TryGetAlertsRect(out Rect rect) => RectOf(Panel, out rect);

    private static bool RectOf(RectTransform target, out Rect rect)
    {
        rect = default;
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        rect = HudElementPositions.ToGui(new[] { corners[0], corners[2] });
        return rect.width > 4f && rect.height > 4f;
    }
}

// Optional integration with NOAutopilot (qwerty1423): its "Show Fuel Overlay" draws a fuel time
// and range readout mid-screen. The Thunder style shows its own in the data block, so while
// Thunder is up the overlay is switched off in memory only (the config file is not rewritten),
// and put back as it was when Thunder ends or the game closes.
internal static class NoAutopilotCompat
{
    private const string Guid = "com.qwerty1423.NOAutopilot";
    private static ConfigEntry<bool> _overlay;
    private static bool _resolved, _hiding, _was;

    private static bool Resolve()
    {
        if (_resolved) return _overlay != null;
        _resolved = true;
        if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null) return false;
        if (!info.Instance.Config.TryGetEntry("Visuals", "Show Fuel Overlay", out _overlay)) _overlay = null;
        if (_overlay != null) Plugin.Logger.LogInfo("NOAutopilot found: its fuel overlay is hidden while the Thunder HUD shows its own.");
        return _overlay != null;
    }

    internal static void SetHidden(bool hide)
    {
        if (hide == _hiding || !Resolve()) return;
        ConfigFile file = _overlay.ConfigFile;
        bool save = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;   // never write our temporary change to their config file
        try
        {
            if (hide) { _was = _overlay.Value; _overlay.Value = false; }
            else _overlay.Value = _was;
        }
        finally { file.SaveOnConfigSet = save; }
        _hiding = hide;
    }
}
