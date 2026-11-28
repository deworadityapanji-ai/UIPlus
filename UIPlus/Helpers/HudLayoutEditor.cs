using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

internal static class HudLayoutEditor
{
    // Configuration Manager reads these tags by name. The editor itself runs
    // outside its window and does not require its assembly at build time.
    private sealed class ConfigurationManagerAttributes
    {
        public Action<ConfigEntryBase> CustomDrawer = DrawLauncher;
        public bool? HideSettingName = true;
        public bool? HideDefaultButton = true;
        public int? Order = 100;
    }

    private const string ManagerGuid = "com.bepis.bepinex.configurationmanager";
    private static readonly Color PanelColor = new(0.08f, 0.22f, 0.26f, 0.96f);
    private static readonly Color GripColor = new(0.45f, 0.85f, 0.95f);
    private static ConfigFile _pending;
    private static bool _fromManager;
    // Working (unsaved) size / rotation / hidden for each element, like _x and _y.
    private static readonly ElementLooks.Look[] _looks = new ElementLooks.Look[HudElementPositions.Count];
    private static readonly List<int> Selection = new();
    private static int Focus => Selection.Count == 1 ? Selection[0] : -1;
    private static bool Control => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    private static readonly List<int> Moving = new();
    private static readonly Vector2[] Origins = new Vector2[HudElementPositions.Count];
    private static bool _marquee, _moved;
    private static int _clickOnly;
    private static Vector2 _pointer;
    private static int _handle;            // 1 = dragging the size corner, 2 = the rotation knob
    private static Vector2 _handleCentre;
    private static float _handleStart, _startScale, _startRotation;
    private static Rect _window = new(16f, 80f, 330f, 600f);
    private static Vector2 _listScroll;
    private static readonly HudStyle[] StyleChoices =
        { HudStyle.Native, HudStyle.Arcade, HudStyle.Wingman, HudStyle.Dashboard, HudStyle.Thunder, HudStyle.Analog };
    private static ConfigFile _config;
    private static object _manager;
    private static PropertyInfo _managerWindow;
    private static FieldInfo _managerHotkey;
    private static bool _previousHotkeyOverride;
    private static bool _previousCursorVisible;
    private static CursorLockMode _previousCursorLock;
    private static GameObject _inputBlocker;
    private static float _xPercent, _yPercent, _widthPercent, _aspect;
    // Working (unsaved) offsets for each movable item, indexed by HudElementPositions ids.
    private static readonly float[] _x = new float[HudElementPositions.Count];
    private static readonly float[] _y = new float[HudElementPositions.Count];
    private static readonly string[] _labels = { "", "MAP", "WEAPONS", "LEFT INSTRUMENTS",
        "RIGHT INSTRUMENTS", "COMPASS", "DAMAGE (shown when hit)", "WATERLINE",
        "SPEED", "ALTITUDE", "WEAPON LIST", "STATUS TAGS", "GAUGE TRAY", "CAUTION / MISSILE", "PULL UP / STALL", "RWR",
        "SUBTITLE", "MISSION UPDATE", "THROTTLE / FUEL", "INSTRUMENT STRIP",
        "FACTION / ESCALATION", "DATA BLOCK", "WEAPON RING", "TARGETING SCREEN", "KILL FEED", "RWR ALERTS", "FUEL TAPE", "THROTTLE TAPE", "WEAPON WHEEL", "MISSILE ARCS",
        "ENGINE", "AIRSPEED", "ATTITUDE", "ALTIMETER", "VERTICAL SPEED", "HEADING", "G METER", "AOA GAUGE", "STORES PLATE" };
    private static Vector2 _dragStart;
    private static TargetingScreenRect _startFrame;
    private static bool _dragging, _resizing;
    private static int _pressedButton = -1;

    internal static bool IsOpen => _config != null;

    private static bool CanEdit => GameManager.gameState == GameState.Menu
        || CursorManager.GetFlag(CursorFlags.GameMenu);

    internal static void Bind(ConfigFile config, string section)
    {
        config.Bind(section, "Customize HUD Layout", false,
            new ConfigDescription("Open the fullscreen targeting-screen layout editor.", null,
                new ConfigurationManagerAttributes()));
    }

    private static void DrawLauncher(ConfigEntryBase entry)
    {
        if (CanEdit)
        {
            if (GUILayout.Button("Customize HUD Layout")) _pending = entry.ConfigFile;
        }
        else
        {
            GUILayout.Label("Pause the game or return to the main menu\nto customize the HUD layout.");
        }
    }

    private static void Open(ConfigFile config)
    {
        // Opened from Configuration Manager: hide its window (and its hotkey) while editing, and
        // bring it back afterwards. Opened from the settings tab: there is no window to hide.
        _manager = null;
        _fromManager = false;
        if (Chainloader.PluginInfos.TryGetValue(ManagerGuid, out var plugin) && plugin.Instance != null)
        {
            object manager = plugin.Instance;
            Type type = manager.GetType();
            var window = type.GetProperty("DisplayingWindow");
            var hotkey = type.GetField("OverrideHotkey");
            if (window?.CanWrite == true && hotkey != null)
            {
                _fromManager = window.GetValue(manager) is true;
                _previousHotkeyOverride = (bool)hotkey.GetValue(manager);
                hotkey.SetValue(manager, true);
                if (_fromManager) window.SetValue(manager, false);
                _manager = manager;
                _managerWindow = window;
                _managerHotkey = hotkey;
            }
        }
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;

        _xPercent = Plugin.TargetingScreenXPercent.Value;
        _yPercent = Plugin.TargetingScreenYPercent.Value;
        _widthPercent = Plugin.TargetingScreenWidthPercent.Value;
        _aspect = TargetingScreenOverlay.LastFeedAspect;
        LoadWorking();
        Selection.Clear();
        _config = config;

        // Stop mouse clicks from reaching the game menu behind our editor.
        _inputBlocker = new GameObject("UI Plus HUD Layout Editor", typeof(RectTransform),
            typeof(Canvas), typeof(GraphicRaycaster));
        var canvas = _inputBlocker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)background.transform;
        rect.SetParent(_inputBlocker.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.04f, 0.66f);
    }

    /// <summary>Open the editor on the next frame (from the UI PLUS settings tab).</summary>
    internal static void RequestOpen() => _pending = Plugin.Instance.Config;

    internal static void Update()
    {
        if (_pending != null)
        {
            ConfigFile config = _pending;
            _pending = null;
            if (CanEdit) Open(config);
            return;
        }
        if (!IsOpen) return;
        if (!CanEdit || _inputBlocker == null)
        {
            Close(false);
            return;
        }
        if (!Application.isFocused)
        {
            _dragging = _marquee = false;
            _pressedButton = -1;
            return;
        }

        // Poll the pointer in fullscreen coordinates independently of IMGUI's
        // window/scroll events, which can consume the original drag.
        Vector2 mouse = Input.mousePosition;
        mouse.y = Screen.height - mouse.y;
        Rect viewport = GetViewport();
        TargetingScreenRect frame = GetFrame(viewport);
        Rect panel = PanelRect(viewport, frame);
        _pointer = mouse;
        if (UpdateHandles(mouse, viewport)) return;
        if (Input.GetMouseButtonDown(0))
        {
            _dragging = _marquee = _moved = false;
            _clickOnly = -1;
            _pressedButton = -1;
            if (_window.Contains(mouse)) return;
            _pressedButton = ButtonAt(mouse);
            if (_pressedButton >= 0) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int hit = WidgetAt(mouse, panel, viewport);
            if (Control && hit >= 0) { Select(hit, true); return; }
            // A list-selected element can still be dragged where another element overlaps it.
            // The strip always yields its interior to its individual instruments.
            int clicked = hit;
            if (shift && Selection.Count > 0) hit = Selection[0];
            else if (Focus > 0 && Focus != HudElementPositions.Strip && Shown(Focus) && ItemRect(Focus, viewport).Contains(mouse))
                hit = Focus;
            _dragStart = mouse;
            if (hit < 0) _marquee = true;
            else
            {
                if (!Selection.Contains(hit)) Select(hit, false);
                else if (!shift) _clickOnly = clicked;
                _resizing = Focus == 0 && GripRect(panel).Contains(mouse);
                _startFrame = frame;
                CaptureMovement();
                _dragging = true;
            }
        }
        if ((_dragging || _marquee) && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)))
        {
            Vector2 delta = mouse - _dragStart;
            if (delta.sqrMagnitude >= 9f) _moved = true;
            if (_dragging && _moved)
            {
                if (_resizing)
                {
                    var diagonal = new Vector2(1f, 1f / _aspect);
                    float change = Vector2.Dot(delta, diagonal) / diagonal.sqrMagnitude;
                    _widthPercent = Mathf.Clamp((_startFrame.Width + change) / viewport.width * 100f, 10f, 50f);
                }
                else
                {
                    MoveCaptured(delta.x / Screen.width * 100f, delta.y / Screen.height * 100f);
                    if (Focus > 0 && HudElementPositions.Valid(Focus))
                    {
                        float dx = (float)HudElementPositions.Entry(Focus, true).DefaultValue;
                        float dy = (float)HudElementPositions.Entry(Focus, false).DefaultValue;
                        if (Mathf.Abs(_x[Focus] - dx) < 1f && Mathf.Abs(_y[Focus] - dy) < 1.5f) ResetPosition(Focus);
                    }
                }
                if (Selection.Contains(0)) MoveLegacy(viewport, _resizing ? Vector2.zero : delta);
            }
        }
        if (!Input.GetMouseButtonUp(0)) return;
        if (_marquee)
        {
            if (!_moved || !Control) Selection.Clear();
            if (_moved)
            {
                Rect box = MarqueeRect();
                for (int id = LegacyTargeting ? 0 : 1; id < HudElementPositions.Count; id++)
                {
                    if (id > 0 && (!HudElementPositions.Valid(id) || !Shown(id))) continue;
                    Rect item = id == 0 ? panel : ItemRect(id, viewport);
                    if (item.width > 0f && item.xMin <= box.xMax && item.xMax >= box.xMin
                        && item.yMin <= box.yMax && item.yMax >= box.yMin && !Selection.Contains(id))
                        Selection.Add(id);
                }
            }
        }
        else if (_dragging && !_moved && _clickOnly >= 0) Select(_clickOnly, false);
        _dragging = _marquee = false;
        int pressed = _pressedButton;
        _pressedButton = -1;
        if (pressed < 0 || pressed != ButtonAt(mouse)) return;
        switch (pressed)
        {
            case 0:
                Apply(_config);
                Close(true);
                break;
            case 1:
                Close(true);
                break;
            case 2:
                _xPercent = (float)Plugin.TargetingScreenXPercent.DefaultValue;
                _yPercent = (float)Plugin.TargetingScreenYPercent.DefaultValue;
                _widthPercent = (float)Plugin.TargetingScreenWidthPercent.DefaultValue;
                // Back to the active style's starting layout (not always zero: Arcade has its own).
                for (int i = 1; i < HudElementPositions.Count; i++)
                {
                    bool valid = HudElementPositions.Valid(i);
                    _x[i] = valid ? (float)HudElementPositions.Entry(i, true).DefaultValue : 0f;
                    _y[i] = valid ? (float)HudElementPositions.Entry(i, false).DefaultValue : 0f;
                    _looks[i] = ElementLooks.DefaultFor(HudElementPositions.Style.Value, i);
                }
                break;
        }
    }

    internal static void RefreshCursor()
    {
        if (!IsOpen || !Application.isFocused) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    internal static void Draw()
    {
        if (!IsOpen) return;
        Matrix4x4 windowMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        _window = GUILayout.Window(0x55495044, _window, DrawWindow, "HUD EDITOR", GUILayout.Width(330f));
        _window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, Screen.width - _window.width));
        _window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, Screen.height - 40f));
        GUI.matrix = windowMatrix;
        if (Event.current.type != EventType.Repaint) return;
        Rect viewport = GetViewport();
        Rect panel = PanelRect(viewport, GetFrame(viewport));
        Rect map = ItemRect(HudElementPositions.Map, viewport);
        Rect weapons = ItemRect(HudElementPositions.Weapon, viewport);
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.depth = -100;
        GUI.matrix = Matrix4x4.identity;
        try
        {
            if (LegacyTargeting)
            {
                Fill(panel, PanelColor);
                GUI.Box(panel, "Targeting screen");
                Fill(GripRect(panel), GripColor);
            }
            // With a live HUD the real widget is visible and moves with the drag, so only
            // outline it. Without one (main menu), fall back to the labelled placeholder.
            for (int id = HudElementPositions.LeftInstruments; id < HudElementPositions.Count; id++)
                if (HudElementPositions.TryGetScreenRect(id, out Rect item)) Outline(item, _labels[id]);
            if (HudElementPositions.TryGetScreenRect(HudElementPositions.Map, out _)) Outline(map, "MAP");
            else if (LegacyTargeting)
            {
                Fill(map, PanelColor);
                GUI.Box(map, "MAP");
                GUI.Label(new Rect(map.x + 12f, map.y + 32f, map.width - 24f, 48f),
                    "  +  +  +\n  •     ▲     •");
            }
            if (HudElementPositions.TryGetScreenRect(HudElementPositions.Weapon, out _)) Outline(weapons, "WEAPONS");
            else if (LegacyTargeting)
            {
                Fill(weapons, PanelColor);
                GUI.Box(weapons, "WEAPONS");
                GUI.Label(new Rect(weapons.x + 12f, weapons.y + 28f, weapons.width - 24f, 65f),
                    "MISSILE   4 / 4\nGUN       320\nCOUNTERMEASURES   24");
            }
            // Every selected element gets the yellow outline; the size and turn handles only
            // where they work.
            Color yellow = new(1f, 0.85f, 0.3f, 1f);
            foreach (int id in Selection)
            {
                Rect picked = id == 0 ? panel : ItemRect(id, viewport);
                if ((id == 0 || Shown(id)) && picked.width > 0f) Outline(picked, "", yellow);
            }
            if (_marquee && _moved) Outline(MarqueeRect(), "", yellow);
            if (Focusable(Focus) && ItemRect(Focus, viewport) is Rect chosen && chosen.width > 0f)
            {
                Fill(SizeHandle(chosen), new Color(1f, 0.85f, 0.3f, 1f));
                Rect knob = TurnHandle(chosen);
                Fill(new Rect(knob.center.x - 1f, knob.yMax, 2f, chosen.y - knob.yMax), new Color(1f, 0.85f, 0.3f, 0.8f));
                GUI.Box(knob, "↻");
            }
            var heading = new Rect(Mathf.Max(8f, (Screen.width - 410f) / 2f), 12f, 410f, 48f);
            GUI.Box(heading, "Customize HUD Layout\nCtrl+click to toggle items. Drag empty space to select a group.");
            GUI.Box(ButtonRect(0), "Apply", GUI.skin.button);
            GUI.Box(ButtonRect(1), "Cancel", GUI.skin.button);
            GUI.Box(ButtonRect(2), "Reset", GUI.skin.button);
        }
        finally
        {
            GUI.depth = previousDepth;
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }

    private static Rect GetViewport()
    {
        var view = SceneSingleton<CameraStateManager>.i;
        Rect viewport = view != null && view.mainCamera != null ? view.mainCamera.pixelRect : default;
        if (viewport.width <= 0f || viewport.height <= 0f)
            return new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
        return new Rect(viewport.x, Screen.height - viewport.yMax, viewport.width, viewport.height);
    }

    private static TargetingScreenRect GetFrame(Rect viewport) => TargetingScreenLayout.Calculate(
        viewport.width, viewport.height, _xPercent, _yPercent, _widthPercent, _aspect);

    private static Rect PanelRect(Rect viewport, TargetingScreenRect frame) =>
        new(viewport.x + frame.Left, viewport.y + frame.Top, frame.Width, frame.Height);

    internal static float GetWorking(int id, bool xAxis) => (xAxis ? _x : _y)[id];
    internal static ElementLooks.Look GetLook(int id) => _looks[id];

    // The active style's saved positions and looks into the working copies.
    private static void LoadWorking()
    {
        for (int i = 1; i < HudElementPositions.Count; i++)
        {
            bool valid = HudElementPositions.Valid(i);
            _x[i] = valid ? HudElementPositions.Entry(i, true).Value : 0f;
            _y[i] = valid ? HudElementPositions.Entry(i, false).Value : 0f;
        }
        ElementLooks.Look[] saved = ElementLooks.Saved(HudElementPositions.Style.Value);
        System.Array.Copy(saved, _looks, _looks.Length);
    }

    // ---- size and rotation handles on the chosen element ----

    private static bool Focusable(int id) =>
        id > 0 && HudElementPositions.Valid(id) && HudElementPositions.SupportsLook(id) && !_looks[id].Hidden;

    // Bottom-right square: drag to resize. Small circle above the top-right corner: drag to turn.
    private static Rect SizeHandle(Rect r) => new(r.xMax - 9f, r.yMax - 9f, 18f, 18f);
    private static Rect TurnHandle(Rect r) => new(r.xMax - 9f, r.y - 34f, 18f, 18f);

    private static bool UpdateHandles(Vector2 mouse, Rect viewport)
    {
        if (!Focusable(Focus)) { _handle = 0; return false; }
        Rect item = ItemRect(Focus, viewport);
        if (item.width <= 0f) { _handle = 0; return false; }
        if (Input.GetMouseButtonDown(0) && !Control && !_window.Contains(mouse) && ButtonAt(mouse) < 0)
        {
            _handle = SizeHandle(item).Contains(mouse) ? 1 : TurnHandle(item).Contains(mouse) ? 2 : 0;
            if (_handle == 0) return false;
            _handleCentre = item.center;
            _startScale = _looks[Focus].Scale;
            _startRotation = _looks[Focus].Rotation;
            Vector2 from = mouse - _handleCentre;
            _handleStart = _handle == 1 ? Mathf.Max(4f, from.magnitude) : Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg;
            return true;
        }
        if (_handle == 0) return false;
        if (!Input.GetMouseButton(0)) { _handle = 0; return true; }
        Vector2 at = mouse - _handleCentre;
        if (_handle == 1)
            _looks[Focus].Scale = Mathf.Clamp(_startScale * at.magnitude / _handleStart, 0.3f, 3f);
        else
        {
            // Screen y points down here, so a clockwise drag is a negative (clockwise) turn.
            float turned = Mathf.DeltaAngle(_handleStart, Mathf.Atan2(at.y, at.x) * Mathf.Rad2Deg);
            float rotation = Mathf.DeltaAngle(0f, _startRotation - turned);
            if (Mathf.Abs(rotation) < 3f) rotation = 0f;   // snaps back to level
            _looks[Focus].Rotation = Mathf.Round(rotation);
        }
        return true;
    }

    // ---- the floating panel ----

    private static string Label(int id) => id == 0 ? "TARGETING SCREEN" : id > 0 && id < _labels.Length ? _labels[id] : "";

    private static void DrawWindow(int windowId)
    {
        GUILayout.Label("HUD style");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Width(32f))) SwitchStyle(-1);
        GUILayout.Label(HudElementPositions.Style.Value.ToString(), CentredLabel);
        if (GUILayout.Button(">", GUILayout.Width(32f))) SwitchStyle(1);
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        HudStyle style = HudElementPositions.Style.Value;
        GUILayout.Label("Elements: click a name to select it");
        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(270f));
        for (int pass = 0; pass < 2; pass++)
        {
            bool extras = pass == 1;
            bool any = false;
            for (int id = 1; id < HudElementPositions.Count; id++)
            {
                if (!HudElementPositions.Valid(id) || ElementLooks.IsExtra(style, id) != extras) continue;
                if (!any && extras) { GUILayout.Space(6f); GUILayout.Label("Extras from other styles", CentredLabel); }
                any = true;
                GUILayout.BeginHorizontal();
                if (HudElementPositions.SupportsLook(id) || id == HudElementPositions.Waterline)
                {
                    bool shown = !_looks[id].Hidden;
                    string word = extras ? (shown ? "REMOVE" : "ADD") : (shown ? "SHOWN" : "HIDDEN");
                    Color before = GUI.color;
                    if (!shown) GUI.color = new Color(1f, 0.7f, 0.5f);
                    if (GUILayout.Button(word, GUILayout.Width(72f)))
                    {
                        if (Selection.Contains(id)) SetHidden(shown);
                        else _looks[id].Hidden = shown;
                    }
                    GUI.color = before;
                }
                else GUILayout.Label("", GUILayout.Width(72f));
                Color previous = GUI.color;
                if (Selection.Contains(id)) GUI.color = new Color(0.5f, 1f, 0.6f);
                if (GUILayout.Button(Label(id), GUI.skin.label)) Select(id, Control);
                GUI.color = previous;
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        if (Selection.Count > 0)
        {
            GUILayout.Label(Selection.Count > 1 ? $"{Selection.Count} selected" : "Selected: " + Label(Focus));
            int editable = Selection.Find(id => id > 0 && HudElementPositions.SupportsLook(id));
            if (editable > 0)
            {
                ElementLooks.Look look = _looks[editable];
                GUILayout.Label($"Size  {look.Scale:0.00}x");
                float scale = GUILayout.HorizontalSlider(look.Scale, 0.3f, 3f);
                GUILayout.Label($"Rotation  {look.Rotation:0}°");
                float rotation = GUILayout.HorizontalSlider(look.Rotation, -180f, 180f);
                GUILayout.BeginHorizontal();
                bool resetSize = GUILayout.Button("Reset size");
                bool resetRotation = GUILayout.Button("Reset rotation");
                GUILayout.EndHorizontal();
                foreach (int id in Selection)
                {
                    if (id <= 0 || !HudElementPositions.SupportsLook(id)) continue;
                    if (resetSize || scale != look.Scale) _looks[id].Scale = resetSize ? 1f : Mathf.Round(scale * 20f) / 20f;
                    if (resetRotation || rotation != look.Rotation) _looks[id].Rotation = resetRotation ? 0f : Mathf.Round(rotation);
                }
            }
            if (Selection.Count > 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Show")) SetHidden(false);
                if (GUILayout.Button("Hide")) SetHidden(true);
                GUILayout.EndHorizontal();
            }
            if (Focus > 0)
            {
                if (!DrawSkin(ref _looks[Focus]) && editable > 0) GUILayout.Label("No other skins for this one yet.", SmallLabel);
                if (HudElementPositions.Leader(Focus) is int leads)
                    GUILayout.Label($"Follows {Label(leads)} until you move it; Reset position attaches it again.", SmallLabel);
            }
            GUILayout.Space(4f);
            GUILayout.Label("Move (hold the arrows; or hold Shift and drag anywhere)", SmallLabel);
            GUILayout.BeginHorizontal();
            float step = 0.05f;
            if (GUILayout.RepeatButton("◄", GUILayout.Width(40f))) Nudge(-step, 0f);
            if (GUILayout.RepeatButton("▲", GUILayout.Width(40f))) Nudge(0f, -step);
            if (GUILayout.RepeatButton("▼", GUILayout.Width(40f))) Nudge(0f, step);
            if (GUILayout.RepeatButton("►", GUILayout.Width(40f))) Nudge(step, 0f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset position"))
            {
                foreach (int id in Selection) ResetPosition(id);
            }
            if (GUILayout.Button("Reset element"))
            {
                // Position, size, rotation, skin and shown/hidden back to the style's own.
                foreach (int id in Selection)
                {
                    ResetPosition(id);
                    if (id > 0) _looks[id] = ElementLooks.DefaultFor(HudElementPositions.Style.Value, id);
                    else _widthPercent = (float)Plugin.TargetingScreenWidthPercent.DefaultValue;
                }
            }
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label("Click an element (on screen or in the list) to size or turn it.", SmallLabel);
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    // The Skin row (‹ name ›) and its note; false when the element has no other skins.
    private static bool DrawSkin(ref ElementLooks.Look look)
    {
        string[] skins = ElementLooks.Skins(Focus);
        if (skins == null) return false;
        GUILayout.Space(4f);
        GUILayout.Label("Skin");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Width(32f))) look.Skin = (look.Skin + skins.Length - 1) % skins.Length;
        GUILayout.Label(skins[Mathf.Clamp(look.Skin, 0, skins.Length - 1)], CentredLabel);
        if (GUILayout.Button(">", GUILayout.Width(32f))) look.Skin = (look.Skin + 1) % skins.Length;
        GUILayout.EndHorizontal();
        string note = ElementLooks.SkinNote(Focus, look.Skin);
        if (note != null) GUILayout.Label(note, SmallLabel);
        return true;
    }

    private static void Select(int id, bool toggle)
    {
        if (!toggle) Selection.Clear();
        else if (Selection.Remove(id)) return;
        Selection.Add(id);
    }

    private static Rect MarqueeRect() => Rect.MinMaxRect(Mathf.Min(_dragStart.x, _pointer.x),
        Mathf.Min(_dragStart.y, _pointer.y), Mathf.Max(_dragStart.x, _pointer.x), Mathf.Max(_dragStart.y, _pointer.y));

    private static void SetHidden(bool hidden)
    {
        foreach (int id in Selection)
            if (id > 0 && (HudElementPositions.SupportsLook(id) || id == HudElementPositions.Waterline))
                _looks[id].Hidden = hidden;
    }

    private static void CaptureMovement()
    {
        Moving.Clear();
        foreach (int id in Selection)
        {
            if (id == 0 || !HudElementPositions.Valid(id)) continue;
            // Children already travel with their strip, and attached followers with their leader.
            if (Selection.Contains(HudElementPositions.Strip) && ArcadeHud.MovesWithStrip(id)) continue;
            if (Selection.Contains(HudElementPositions.Map) && ArcadeHud.RwrOnMap &&
                !ArcadeHud.MovesWithStrip(id) && (id == HudElementPositions.Rwr || id == HudElementPositions.RwrAlerts))
                continue;
            int from = id;
            if (HudElementPositions.Leader(id) is int leader && !HudElementPositions.Moved(id))
            {
                if (Selection.Contains(leader)) continue;
                from = leader;
            }
            Origins[id] = new Vector2(_x[from], _y[from]);
            // When the map leaves the dock its offset is measured from the native corner again.
            if (id == HudElementPositions.Map && ArcadeHud.MapDocked)
                Origins[id] += new Vector2(100f * ArcadeHud.MapDock.x / Screen.width, -100f * ArcadeHud.MapDock.y / Screen.height);
            Moving.Add(id);
        }
    }

    private static void MoveCaptured(float dx, float dy)
    {
        // Stop the whole group when one member reaches the edge of its allowed offset.
        foreach (int id in Moving)
        {
            dx = Mathf.Clamp(dx, -100f - Origins[id].x, 100f - Origins[id].x);
            dy = Mathf.Clamp(dy, -100f - Origins[id].y, 100f - Origins[id].y);
        }
        foreach (int id in Moving)
        {
            _x[id] = Origins[id].x + dx;
            _y[id] = Origins[id].y + dy;
        }
    }

    private static void MoveLegacy(Rect viewport, Vector2 pixels) =>
        TargetingScreenLayout.PositionPercent(viewport.width, viewport.height, _widthPercent, _aspect,
            _startFrame.Left + pixels.x, _startFrame.Top + pixels.y, out _xPercent, out _yPercent);

    private static void Nudge(float dx, float dy)
    {
        CaptureMovement();
        MoveCaptured(dx, dy);
        if (Selection.Contains(0))
        {
            Rect viewport = GetViewport();
            _startFrame = GetFrame(viewport);
            MoveLegacy(viewport, new Vector2(dx * Screen.width, dy * Screen.height) / 100f);
        }
    }

    private static void ResetPosition(int id)
    {
        if (id == 0)
        {
            _xPercent = (float)Plugin.TargetingScreenXPercent.DefaultValue;
            _yPercent = (float)Plugin.TargetingScreenYPercent.DefaultValue;
            return;
        }
        if (!HudElementPositions.Valid(id)) return;
        _x[id] = (float)HudElementPositions.Entry(id, true).DefaultValue;
        _y[id] = (float)HudElementPositions.Entry(id, false).DefaultValue;
    }

    // Keep the edits made so far, then edit the next style's layout.
    private static void SwitchStyle(int step)
    {
        int at = Array.IndexOf(StyleChoices, HudElementPositions.Style.Value);
        if (at < 0) at = 0;
        Apply(_config);
        HudElementPositions.Style.Value = StyleChoices[(at + step + StyleChoices.Length) % StyleChoices.Length];
        LoadWorking();
        Selection.Clear();
        _handle = 0;
    }

    private static GUIStyle _centred, _small;
    private static GUIStyle CentredLabel => _centred ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
    private static GUIStyle SmallLabel => _small ??= new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };

    // Real on-screen bounds when the HUD exists. Map and weapons fall back to labelled
    // placeholders (for example from the main menu); other items are simply not shown.
    private static Rect ItemRect(int id, Rect viewport)
    {
        if (HudElementPositions.TryGetScreenRect(id, out Rect real)) return real;
        if (id == HudElementPositions.Map)
        {
            float size = Mathf.Min(210f, viewport.height * 0.28f);
            return new Rect(viewport.x + 20f + viewport.width * _x[id] / 100f,
                viewport.yMax - size - 20f + viewport.height * _y[id] / 100f, size, size);
        }
        if (id == HudElementPositions.Weapon)
            return new Rect(viewport.xMax - 255f + viewport.width * _x[id] / 100f,
                viewport.y + 72f + viewport.height * _y[id] / 100f, 235f, 100f);
        return default;
    }

    private static int WidgetAt(Vector2 mouse, Rect panel, Rect viewport)
    {
        // Where elements overlap: the RWR alerts, then the RWR scope, sit above everything (they lie
        // on the map); otherwise the smallest element under the cursor wins.
        int best = -1, bestLayer = -1;
        float bestArea = float.MaxValue;
        for (int id = 1; id < HudElementPositions.Count; id++)
        {
            if (!HudElementPositions.Valid(id) || !Shown(id)) continue;
            Rect r = ItemRect(id, viewport);
            float area = r.width * r.height;
            if (area <= 1f || !r.Contains(mouse)) continue;
            int layer = id == HudElementPositions.RwrAlerts ? 3 : id == HudElementPositions.Rwr ? 2 : id == HudElementPositions.Map ? 0 : 1;
            // A large, rotated dial can have a bigger bounding box than its own panel.
            if (best == HudElementPositions.Strip && id >= HudElementPositions.AnalogEngine) bestArea = float.MaxValue;
            if (layer > bestLayer || (layer == bestLayer && area < bestArea)) { best = id; bestLayer = layer; bestArea = area; }
        }
        if (best >= 0) return best;
        if (LegacyTargeting && panel.Contains(mouse)) return 0;
        return -1;
    }

    // Native (and Plus) place the targeting screen with the old percentage box; the custom
    // styles place it themselves (its own TARGETING item), so the old box is not shown there.
    private static bool LegacyTargeting =>
        HudElementPositions.Style == null || HudElementPositions.Style.Value == HudStyle.Native
        || HudElementPositions.Style.Value == HudStyles.Plus;

    // A custom style shows only the items it actually uses; the placeholders are for Native.
    private static bool Shown(int id) =>
        !_looks[id].Hidden && (LegacyTargeting || HudElementPositions.TryGetScreenRect(id, out _));

    private static Rect GripRect(Rect panel)
    {
        float width = Mathf.Min(22f, panel.width / 2f);
        float height = Mathf.Min(22f, panel.height / 2f);
        return new Rect(panel.xMax - width, panel.yMax - height, width, height);
    }

    private static Rect ButtonRect(int index) =>
        new((Screen.width - 300f) / 2f + index * 102f, Screen.height - 49f, 96f, 32f);

    private static int ButtonAt(Vector2 mouse)
    {
        for (int index = 0; index < 3; index++)
            if (ButtonRect(index).Contains(mouse)) return index;
        return -1;
    }

    private static void Outline(Rect rect, string label) => Outline(rect, label, new Color(0.4f, 1f, 0.5f, 0.9f));

    private static void Outline(Rect rect, string label, Color c)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, 2f), c);
        Fill(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), c);
        Fill(new Rect(rect.x, rect.y, 2f, rect.height), c);
        Fill(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), c);
        GUI.Label(new Rect(rect.x + 4f, Mathf.Max(0f, rect.y - 20f), 120f, 20f), label);
    }

    private static void Fill(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    internal static void Close(bool returnToSettings)
    {
        _pending = null;
        if (!IsOpen) return;
        _config = null;
        _dragging = _marquee = false;
        _pressedButton = -1;
        if (_inputBlocker != null) UnityEngine.Object.Destroy(_inputBlocker);
        _inputBlocker = null;
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
        if (_manager != null)
        {
            _managerHotkey.SetValue(_manager, _previousHotkeyOverride);
            if (returnToSettings && _fromManager) _managerWindow.SetValue(_manager, true);
        }
        _manager = null;
        _handle = 0;
    }

    private static void Apply(ConfigFile config)
    {
        bool save = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try
        {
            Plugin.TargetingScreenXPercent.Value = _xPercent;
            Plugin.TargetingScreenYPercent.Value = _yPercent;
            Plugin.TargetingScreenWidthPercent.Value = _widthPercent;
            for (int i = 1; i < HudElementPositions.Count; i++)
            {
                if (!HudElementPositions.Valid(i)) continue;
                HudElementPositions.Entry(i, true).Value = _x[i];
                HudElementPositions.Entry(i, false).Value = _y[i];
            }
            ElementLooks.Save(HudElementPositions.Style.Value, _looks);
        }
        finally
        {
            config.SaveOnConfigSet = save;
        }
        config.Save();
    }
}
