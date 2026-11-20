using System;
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
    private static int _focus = -1;        // the element chosen for size / rotation
    private static int _handle;            // 1 = dragging the size corner, 2 = the rotation knob
    private static Vector2 _handleCentre;
    private static float _handleStart, _startScale, _startRotation;
    private static Rect _window = new(16f, 80f, 330f, 600f);
    private static Vector2 _listScroll;
    private static readonly HudStyle[] StyleChoices =
        { HudStyle.Native, HudStyle.Arcade, HudStyle.Wingman, HudStyle.Dashboard, HudStyle.Thunder };
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
        "FACTION / ESCALATION", "DATA BLOCK", "WEAPON RING", "TARGETING SCREEN", "KILL FEED", "RWR ALERTS", "FUEL TAPE", "THROTTLE TAPE", "WEAPON WHEEL", "MISSILE ARCS" };
    private static float _startX, _startY;
    private static int _selected;
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
        _focus = -1;
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
            _dragging = false;
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
        if (UpdateHandles(mouse, viewport)) return;
        // The floating panel handles its own clicks (IMGUI); nothing behind it reacts.
        if (Input.GetMouseButtonDown(0) && _window.Contains(mouse)) { _dragging = false; _pressedButton = -1; return; }
        if (Input.GetMouseButtonDown(0))
        {
            _pressedButton = ButtonAt(mouse);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            // The selected element keeps winning clicks inside its own box (pick the RWR from the
            // list, then drag it even where it lies on the map); Shift drags it from anywhere.
            bool focusValid = _focus > 0 && HudElementPositions.Valid(_focus);
            bool onFocus = focusValid && ItemRect(_focus, viewport) is Rect own && own.width > 1f && own.Contains(mouse);
            _selected = focusValid && (shift || onFocus) ? _focus : WidgetAt(mouse, panel, viewport);
            _dragging = _pressedButton < 0 && _selected >= 0;
            if (_selected > 0) _focus = _selected;
            // An element that was never moved follows its leader (RWR alerts the scope, Arcade's
            // tapes their blocks): start its drag from where the leader has taken it.
            if (_selected > 0 && HudElementPositions.Leader(_selected) is int leader
                && Mathf.Abs(_x[_selected]) < 0.01f && Mathf.Abs(_y[_selected]) < 0.01f)
            {
                _x[_selected] = _x[leader];
                _y[_selected] = _y[leader];
            }
            if (_dragging)
            {
                _resizing = _selected == 0 && GripRect(panel).Contains(mouse);
                _dragStart = mouse;
                _startFrame = frame;
                if (_selected > 0)
                {
                    _startX = _x[_selected];
                    _startY = _y[_selected];
                }
            }
        }
        if (_dragging && Input.GetMouseButton(0))
        {
            Vector2 delta = mouse - _dragStart;
            float left = _startFrame.Left;
            float top = _startFrame.Top;
            if (_selected > 0)
            {
                // Offsets are percentages of the whole screen, as the HUD code applies them.
                _x[_selected] = Mathf.Clamp(_startX + delta.x / Screen.width * 100f, -100f, 100f);
                _y[_selected] = Mathf.Clamp(_startY + delta.y / Screen.height * 100f, -100f, 100f);
                // Snap onto the item's default spot when dragged close to it (for the map, that is
                // the game's own corner with its original backing art).
                if (HudElementPositions.Valid(_selected))
                {
                    float dx = (float)HudElementPositions.Entry(_selected, true).DefaultValue;
                    float dy = (float)HudElementPositions.Entry(_selected, false).DefaultValue;
                    if (Mathf.Abs(_x[_selected] - dx) < 1f && Mathf.Abs(_y[_selected] - dy) < 1.5f)
                    {
                        _x[_selected] = dx;
                        _y[_selected] = dy;
                    }
                }
            }
            else if (_resizing)
            {
                // Project onto the panel diagonal to keep its aspect ratio.
                var diagonal = new Vector2(1f, 1f / _aspect);
                float sizeDelta = Vector2.Dot(delta, diagonal) / diagonal.sqrMagnitude;
                _widthPercent = Mathf.Clamp((_startFrame.Width + sizeDelta) / viewport.width * 100f, 10f, 50f);
            }
            else
            {
                left += delta.x;
                top += delta.y;
            }
            TargetingScreenLayout.PositionPercent(viewport.width, viewport.height, _widthPercent,
                _aspect, left, top, out _xPercent, out _yPercent);
        }
        if (!Input.GetMouseButtonUp(0)) return;

        _dragging = false;
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
            if (_focus > 0 && HudElementPositions.Valid(_focus) && ItemRect(_focus, GetViewport()) is Rect picked && picked.width > 0f
                && !Focusable(_focus))
                Outline(picked, "", new Color(1f, 0.85f, 0.3f, 1f));
            if (Focusable(_focus) && ItemRect(_focus, GetViewport()) is Rect chosen && chosen.width > 0f)
            {
                Outline(chosen, "", new Color(1f, 0.85f, 0.3f, 1f));
                Fill(SizeHandle(chosen), new Color(1f, 0.85f, 0.3f, 1f));
                Rect knob = TurnHandle(chosen);
                Fill(new Rect(knob.center.x - 1f, knob.yMax, 2f, chosen.y - knob.yMax), new Color(1f, 0.85f, 0.3f, 0.8f));
                GUI.Box(knob, "↻");
            }
            var heading = new Rect(Mathf.Max(8f, (Screen.width - 410f) / 2f), 12f, 410f, 48f);
            GUI.Box(heading, LegacyTargeting
                ? "Customize HUD Layout\nDrag an item to move it. Select one to size or turn it."
                : "Customize HUD Layout\nDrag an item to move it. Select one to size or turn it.");
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
        if (!Focusable(_focus)) { _handle = 0; return false; }
        Rect item = ItemRect(_focus, viewport);
        if (item.width <= 0f) { _handle = 0; return false; }
        if (Input.GetMouseButtonDown(0) && !_window.Contains(mouse))
        {
            _handle = SizeHandle(item).Contains(mouse) ? 1 : TurnHandle(item).Contains(mouse) ? 2 : 0;
            if (_handle == 0) return false;
            _handleCentre = item.center;
            _startScale = _looks[_focus].Scale;
            _startRotation = _looks[_focus].Rotation;
            Vector2 from = mouse - _handleCentre;
            _handleStart = _handle == 1 ? Mathf.Max(4f, from.magnitude) : Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg;
            return true;
        }
        if (_handle == 0) return false;
        if (!Input.GetMouseButton(0)) { _handle = 0; return true; }
        Vector2 at = mouse - _handleCentre;
        if (_handle == 1)
            _looks[_focus].Scale = Mathf.Clamp(_startScale * at.magnitude / _handleStart, 0.3f, 3f);
        else
        {
            // Screen y points down here, so a clockwise drag is a negative (clockwise) turn.
            float turned = Mathf.DeltaAngle(_handleStart, Mathf.Atan2(at.y, at.x) * Mathf.Rad2Deg);
            float rotation = Mathf.DeltaAngle(0f, _startRotation - turned);
            if (Mathf.Abs(rotation) < 3f) rotation = 0f;   // snaps back to level
            _looks[_focus].Rotation = Mathf.Round(rotation);
        }
        return true;
    }

    // ---- the floating panel ----

    private static string Label(int id) => id > 0 && id < _labels.Length ? _labels[id] : "";

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
                    if (GUILayout.Button(word, GUILayout.Width(72f))) _looks[id].Hidden = shown;
                    GUI.color = before;
                }
                else GUILayout.Label("", GUILayout.Width(72f));
                Color previous = GUI.color;
                if (id == _focus) GUI.color = new Color(0.5f, 1f, 0.6f);
                if (GUILayout.Button(Label(id), GUI.skin.label)) _focus = id;
                GUI.color = previous;
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        if (_focus > 0 && HudElementPositions.Valid(_focus))
        {
            GUILayout.Label("Selected: " + Label(_focus));
            if (HudElementPositions.SupportsLook(_focus))
            {
                ref ElementLooks.Look look = ref _looks[_focus];
                GUILayout.Label($"Size  {look.Scale:0.00}x");
                look.Scale = Mathf.Round(GUILayout.HorizontalSlider(look.Scale, 0.3f, 3f) * 20f) / 20f;
                GUILayout.Label($"Rotation  {look.Rotation:0}°");
                look.Rotation = Mathf.Round(GUILayout.HorizontalSlider(look.Rotation, -180f, 180f));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset size")) look.Scale = 1f;
                if (GUILayout.Button("Reset rotation")) look.Rotation = 0f;
                GUILayout.EndHorizontal();
                if (!DrawSkin(ref look)) GUILayout.Label("No other skins for this one yet.", SmallLabel);
            }
            else
            {
                GUILayout.Label("This one can be moved, but not resized or turned yet.", SmallLabel);
                DrawSkin(ref _looks[_focus]);
            }
            if (HudElementPositions.Leader(_focus) is int leads)
                GUILayout.Label($"Follows {Label(leads)} until you move it; Reset position attaches it again.", SmallLabel);
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
            if (GUILayout.Button("Reset position")) ResetPosition(_focus);
            if (GUILayout.Button("Reset element"))
            {
                // Position, size, rotation, skin and shown/hidden back to the style's own.
                ResetPosition(_focus);
                _looks[_focus] = ElementLooks.DefaultFor(HudElementPositions.Style.Value, _focus);
            }
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label("Click an element (on screen or in the list) to size or turn it.", SmallLabel);
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    // The Skin row (‹ name ›) and its note; false when the element has no other skins.
    private static bool DrawSkin(ref ElementLooks.Look look)
    {
        string[] skins = ElementLooks.Skins(_focus);
        if (skins == null) return false;
        GUILayout.Space(4f);
        GUILayout.Label("Skin");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Width(32f))) look.Skin = (look.Skin + skins.Length - 1) % skins.Length;
        GUILayout.Label(skins[Mathf.Clamp(look.Skin, 0, skins.Length - 1)], CentredLabel);
        if (GUILayout.Button(">", GUILayout.Width(32f))) look.Skin = (look.Skin + 1) % skins.Length;
        GUILayout.EndHorizontal();
        string note = ElementLooks.SkinNote(_focus, look.Skin);
        if (note != null) GUILayout.Label(note, SmallLabel);
        return true;
    }

    // Move the selected element by a step (percent of the screen); a follower detaches first.
    private static void Nudge(float dx, float dy)
    {
        int id = _focus;
        if (!HudElementPositions.Valid(id)) return;
        if (HudElementPositions.Leader(id) is int leader && Mathf.Abs(_x[id]) < 0.01f && Mathf.Abs(_y[id]) < 0.01f)
        {
            _x[id] = _x[leader];
            _y[id] = _y[leader];
        }
        _x[id] = Mathf.Clamp(_x[id] + dx, -100f, 100f);
        _y[id] = Mathf.Clamp(_y[id] + dy, -100f, 100f);
    }

    private static void ResetPosition(int id)
    {
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
        _focus = -1;
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
            if (!Shown(id)) continue;
            Rect r = ItemRect(id, viewport);
            float area = r.width * r.height;
            if (area <= 1f || !r.Contains(mouse)) continue;
            int layer = id == HudElementPositions.RwrAlerts ? 3 : id == HudElementPositions.Rwr ? 2 : id == HudElementPositions.Map ? 0 : 1;
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
        LegacyTargeting || HudElementPositions.TryGetScreenRect(id, out _);

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
        _dragging = false;
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
