using System;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using TMPro;
using UIPlus.Helpers;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Settings;

/// <summary>
/// The UI PLUS settings page. It reuses the game's own pieces (panel background, label and
/// heading text, toggle, slider and dropdown) so it looks like the rest of the settings menu,
/// but lays out its own rows. Every control writes straight to UIPlus.cfg, the same settings
/// Configuration Manager shows, so the two always agree.
///
/// The game's Gameplay page (the source) is: Scroll View → Viewport → Content, holding a units
/// panel (heading + dropdown) and a side-by-side container with the camera and kill-feed panels.
/// Its Content is emptied and refilled with our panels.
/// </summary>
internal sealed class SettingsPage : MonoBehaviour
{
    private const string Parts = "UIP Parts";
    private const string PanelPart = "Panel", LabelPart = "Label", HeadingPart = "Heading",
        TogglePart = "Toggle", SliderPart = "Slider", DropdownPart = "Dropdown";

    // What the page shows. A null section starts a new panel with that heading. Rows whose
    // setting does not exist (or has an unsupported type) are skipped.
    private static readonly (string Section, string Key, string Label)[] Layout =
    {
        (null, null, "WHERE THE HUD SHOWS"),
        ("Chase HUD", "FlightHud", "Chase view"),
        (HudViews.Section, "ShowInCockpit", "Cockpit view"),
        (HudViews.Section, "ShowInOrbit", "Orbit camera"),
        (null, null, "HUD ELEMENTS"),
        ("HUD Layout", "TextContrast", "Text contrast (dark halo)"),
        ("HUD Layout", "KillCallouts", "Hit / miss / destroyed callouts"),
        ("HUD Layout", "KillFeed", "Kill feed"),
        ("HUD Layout", "WeaponPopup", "Weapon pop-up"),
        ("Targeting Screen", "TargetingScreenEnabled", "Targeting screen"),
        ("Minimap", "Zoom", "Minimap zoom"),
        ("HUD Layout", "PlainBackingWhenMoved", "Plain map backing when moved"),
        ("Compatibility", "FixTargetArrow", "Fix the target arrow for targets behind you"),
        (null, null, "STYLE OPTIONS"),
        ("HUD Layout", "ArcadeTargetingScreen", "Arcade: targeting screen"),
        ("HUD Layout", "ArcadeWeaponIconRotation", "Arcade: weapon icon rotation"),
        ("HUD Layout", "WingmanTargetingScreen", "Wingman: targeting screen"),
        ("HUD Layout", "WingmanSlant", "Wingman: lean the side panels"),
        ("HUD Layout", "DashboardDockMap", "Dashboard: dock the map to the strip"),
        ("HUD Layout", "ThunderFactionColours", "Thunder: faction colours"),
        ("HUD Layout", "ThunderFactionPalette#Boscali", "Thunder: Boscali colour"),
        ("HUD Layout", "ThunderFactionPalette#Primeva", "Thunder: Primeva colour"),
        (null, null, "CHASE AND ORBIT HUD"),
        ("Chase HUD", "Map", "Minimap"),
        ("Chase HUD", "PitchLadder", "Pitch ladder"),
        ("Chase HUD", "VirtualJoystickIndicator", "Virtual joystick indicator"),
        ("Chase HUD", "Waterline", "Waterline"),
        ("Chase HUD", "LeftInstruments", "Left instruments"),
        ("Chase HUD", "RightInstruments", "Right instruments"),
        ("Chase HUD", "Compass", "Compass"),
    };

    private Transform _content, _parts;

    // ---- template (built once, from a copy of the game's Gameplay page) ----

    internal static GameObject CreateTemplate(GameObject source)
    {
        // Under an inactive holder so the copy never wakes up: the Gameplay page's own script
        // would otherwise run and load or apply the game's settings.
        var holder = new GameObject("UI Plus Settings Template");
        holder.SetActive(false);
        DontDestroyOnLoad(holder);
        GameObject page = Instantiate(source, holder.transform);
        page.name = "UI Plus Settings";
        foreach (GameplayMenu script in page.GetComponentsInChildren<GameplayMenu>(true)) DestroyImmediate(script);

        ScrollRect scroll = page.GetComponentInChildren<ScrollRect>(true);
        Transform content = scroll != null && scroll.content != null ? scroll.content
            : throw new InvalidOperationException("the source page has no scroll content");
        Toggle toggle = page.GetComponentsInChildren<Toggle>(true)
            .FirstOrDefault(t => t.GetComponentInParent<Dropdown>(true) == null);
        Slider slider = page.GetComponentInChildren<Slider>(true);
        Dropdown dropdown = page.GetComponentInChildren<Dropdown>(true);
        if (toggle == null || slider == null || dropdown == null)
            throw new InvalidOperationException("the source page lacks a toggle, slider or dropdown");
        TMP_Text[] texts = page.GetComponentsInChildren<TMP_Text>(true)
            .Where(t => t.GetComponentInParent<Selectable>(true) == null).ToArray();
        // The heading is the units panel's first text; a label is a text in the row of a toggle.
        TMP_Text heading = texts.FirstOrDefault(t => t.name.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? texts.FirstOrDefault();
        TMP_Text label = texts.FirstOrDefault(t => t != heading && t.transform.parent != null
                && t.transform.parent.GetComponentsInChildren<Toggle>(true).Contains(toggle))
            ?? texts.FirstOrDefault(t => t != heading) ?? heading;
        if (heading == null || label == null) throw new InvalidOperationException("the source page has no text to copy");
        // A panel: a boxed list directly in the content (the units panel), else the toggle's own box.
        Transform panel = content.Cast<Transform>().FirstOrDefault(c => c.GetComponent<Image>() != null
            && c.GetComponent<VerticalLayoutGroup>() != null);
        if (panel == null)
            for (Transform t = toggle.transform.parent; t != null && t != content; t = t.parent)
                if (t.GetComponent<Image>() != null && t.GetComponent<VerticalLayoutGroup>() != null) { panel = t; break; }
        if (panel == null) throw new InvalidOperationException("the source page has no panel to copy");

        // Logged now: the originals are deleted with the page's rows below.
        Plugin.Logger.LogInfo($"Settings tab: built from '{source.name}' (panel '{panel.name}', label '{label.name}', heading '{heading.name}').");
        var parts = new GameObject(Parts, typeof(RectTransform)).transform;
        parts.SetParent(page.transform, false);
        parts.gameObject.SetActive(false);
        Keep(panel, PanelPart, parts, emptied: true);
        Keep(label.transform, LabelPart, parts);
        Keep(heading.transform, HeadingPart, parts);
        Keep(toggle.transform, TogglePart, parts);
        Keep(slider.transform, SliderPart, parts);
        Keep(dropdown.transform, DropdownPart, parts);
        for (int i = content.childCount - 1; i >= 0; i--) DestroyImmediate(content.GetChild(i).gameObject);

        // Rows are placed by hand (see Place); automatic layout on the content would fight that.
        foreach (LayoutGroup group in content.GetComponents<LayoutGroup>()) DestroyImmediate(group);
        foreach (ContentSizeFitter fitter in content.GetComponents<ContentSizeFitter>()) DestroyImmediate(fitter);
        page.AddComponent<SettingsPage>();
        return page;
    }

    // Copy a piece into the hidden parts box, remembering its laid-out size (in a LayoutElement,
    // only as a note: nothing lays these out automatically).
    private static void Keep(Transform piece, string name, Transform parts, bool emptied = false)
    {
        Vector2 size = ((RectTransform)piece).rect.size;
        Transform copy = Instantiate(piece.gameObject, parts).transform;
        copy.name = name;
        if (emptied)
        {
            for (int i = copy.childCount - 1; i >= 0; i--) DestroyImmediate(copy.GetChild(i).gameObject);
            foreach (LayoutGroup group in copy.GetComponents<LayoutGroup>()) DestroyImmediate(group);
        }
        foreach (ContentSizeFitter fitter in copy.GetComponents<ContentSizeFitter>()) DestroyImmediate(fitter);
        LayoutElement note = copy.GetComponent<LayoutElement>() ?? copy.gameObject.AddComponent<LayoutElement>();
        note.ignoreLayout = true;
        if (size.x > 1f) note.preferredWidth = size.x;
        if (size.y > 1f) note.preferredHeight = size.y;
    }

    // ---- the live page ----

    private void Start()
    {
        _parts = transform.Find(Parts);
        ScrollRect scroll = GetComponentInChildren<ScrollRect>(true);
        _content = scroll != null ? scroll.content : null;
        if (_parts == null || _content == null) return;
        try { Build(); }
        catch (Exception e) { Plugin.Logger.LogWarning($"Settings tab: could not fill the page: {e}"); }
    }

    private GameObject Part(string name) => _parts.Find(name)?.gameObject;

    // ---- layout (by hand: boxes in two columns, rows down each box) ----

    private const float Margin = 40f, Gap = 24f, PanelPad = 22f, RowGap = 6f, RowHeight = 50f,
        BigRowHeight = 68f, HeadingHeight = 40f, Inset = 28f;
    private readonly float[] _columnY = new float[2];   // where the next box starts in each column
    private RectTransform _panel;   // the box being filled
    private bool _panelWide;        // spans both columns
    private float _panelY;          // where its next row starts, from its top

    private void Build()
    {
        ConfigFile config = Plugin.Instance.Config;
        _columnY[0] = _columnY[1] = Margin;
        // The two main controls first, in a box across the top, larger.
        AddPanel(null, wide: true);
        if (config.TryGetEntry(new ConfigDefinition("HUD Layout", "HudStyle"), out ConfigEntry<HudStyle> style))
            AddDropdown("HUD style", style, big: true);
        AddButton("HUD layout", "OPEN EDITOR", OpenEditor, big: true);

        foreach (var (section, key, label) in Layout)
        {
            if (section == null) { AddPanel(label, wide: false); continue; }
            if (key.Contains("#"))
            {
                // Thunder's palette is one text setting; each faction gets a colour list instead.
                var palette = new ConfigDefinition(section, key.Substring(0, key.IndexOf('#')));
                if (config.ContainsKey(palette) && config[palette] is ConfigEntry<string> text)
                    AddFactionColour(label, text, key.Substring(key.IndexOf('#') + 1));
                continue;
            }
            var definition = new ConfigDefinition(section, key);
            if (!config.ContainsKey(definition)) continue;
            ConfigEntryBase entry = config[definition];
            if (entry.SettingType == typeof(bool)) AddToggle(label, entry);
            else if (entry.SettingType.IsEnum) AddDropdown(label, entry);
            else if (entry.SettingType == typeof(float) && entry.Description?.AcceptableValues is AcceptableValueRange<float> range)
                AddSlider(label, entry, range.MinValue, range.MaxValue);
        }
        // Mod compatibility: one row per integration. Greyed out when that mod is not installed;
        // otherwise its switch, with whether it is working (hooked in).
        AddPanel("MOD COMPATIBILITY", wide: true);
        foreach (Integrations.Info info in Integrations.All) AddIntegration(info);
        EndPanel();

        // The content scrolls: as tall as the boxes, pinned to the top, opened at the top.
        var content = (RectTransform)_content;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, Mathf.Max(_columnY[0], _columnY[1]) + Margin - Gap);
        ScrollRect scroll = GetComponentInChildren<ScrollRect>(true);
        if (scroll != null)
        {
            // Standard orientation for top-pinned content: wheel up goes up, bar starts at the top.
            scroll.scrollSensitivity = Mathf.Abs(scroll.scrollSensitivity);
            if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.direction = Scrollbar.Direction.BottomToTop;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
        }
    }

    private void OpenEditor()
    {
        HudLayoutEditor.RequestOpen();
        // Close the settings so the HUD (and the editor over it) is visible.
        GetComponentInParent<SettingsMenu>()?.CloseSettingsMenu();
    }

    // Pin a rect to its parent's top edge, stretched across with the given side insets.
    private static void TopStretch(RectTransform rect, float y, float height, float left, float right)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -y - height);
        rect.offsetMax = new Vector2(-right, -y);
    }

    private void AddPanel(string heading, bool wide)
    {
        EndPanel();
        GameObject panel = Instantiate(Part(PanelPart), _content);
        panel.name = "UIP Panel";
        panel.SetActive(true);
        _panel = (RectTransform)panel.transform;
        _panelWide = wide;
        _panelY = PanelPad;
        if (heading == null) return;
        GameObject title = Instantiate(Part(HeadingPart), _panel);
        title.SetActive(true);
        TMP_Text text = title.GetComponent<TMP_Text>();
        text.text = heading;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        TopStretch((RectTransform)title.transform, _panelY, HeadingHeight, Inset, Inset);
        _panelY += HeadingHeight + RowGap;
    }

    // Size the finished box and put it in place: across the top, or in the shorter column.
    private void EndPanel()
    {
        if (_panel == null) return;
        float height = _panelY + PanelPad;
        if (_panelWide)
        {
            float y = Mathf.Max(_columnY[0], _columnY[1]);
            TopStretch(_panel, y, height, Margin, Margin);
            _columnY[0] = _columnY[1] = y + height + Gap;
        }
        else
        {
            int column = _columnY[1] < _columnY[0] ? 1 : 0;
            float y = _columnY[column];
            _panel.anchorMin = new Vector2(column * 0.5f, 1f);
            _panel.anchorMax = new Vector2(column * 0.5f + 0.5f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.offsetMin = new Vector2(column == 0 ? Margin : Gap / 2f, -y - height);
            _panel.offsetMax = new Vector2(column == 0 ? -Gap / 2f : -Margin, -y);
            _columnY[column] = y + height + Gap;
        }
        _panel = null;
    }

    // A row: the label down the left half, the control at the right edge (its own size).
    private GameObject Row(string label, string controlPart, bool big, out GameObject control, out Vector2 size)
    {
        float height = big ? BigRowHeight : RowHeight;
        var row = new GameObject("UIP Row", typeof(RectTransform));
        row.transform.SetParent(_panel, false);
        TopStretch((RectTransform)row.transform, _panelY, height, Inset, Inset);
        _panelY += height + RowGap;

        GameObject title = Instantiate(Part(LabelPart), row.transform);
        title.SetActive(true);
        TMP_Text text = title.GetComponent<TMP_Text>();
        text.text = label;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        if (big) text.fontSize *= 1.2f;

        GameObject part = Part(controlPart);
        LayoutElement note = part.GetComponent<LayoutElement>();
        size = new Vector2(note.preferredWidth, note.preferredHeight);
        size = controlPart switch
        {
            TogglePart => new Vector2(Mathf.Clamp(size.x, 30f, 44f), Mathf.Clamp(size.y, 30f, 44f)),
            SliderPart => new Vector2(Mathf.Clamp(size.x, 200f, 260f), Mathf.Clamp(size.y, 16f, 36f)),
            _ => new Vector2(Mathf.Clamp(size.x, 220f, 280f), Mathf.Clamp(size.y, 34f, 44f)),
        };
        if (big) size.y = Mathf.Max(size.y, 48f);
        var titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(0f, 0.5f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = new Vector2(-size.x - 20f, 0f);
        text.enableAutoSizing = true;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = Mathf.Max(10f, text.fontSize * 0.7f);
        text.overflowMode = TextOverflowModes.Ellipsis;
        control = Instantiate(part, row.transform);
        control.SetActive(true);
        var rect = (RectTransform)control.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        if (big)
        {
            foreach (TMP_Text t in control.GetComponentsInChildren<TMP_Text>(true)) t.fontSize *= 1.2f;
            foreach (Text t in control.GetComponentsInChildren<Text>(true)) t.fontSize = Mathf.RoundToInt(t.fontSize * 1.2f);
        }
        return row;
    }

    private void AddIntegration(Integrations.Info info)
    {
        string status = Integrations.Status(info);
        bool installed = status != "not installed";
        Row($"{info.Name}  <size=75%><alpha=#99>{status}</size>", TogglePart, false, out GameObject control, out _);
        Toggle toggle = control.GetComponent<Toggle>();
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify(installed && info.On);
        toggle.interactable = installed;
        if (installed) toggle.onValueChanged.AddListener(on => info.Enabled.Value = on);
        else
        {
            // Greyed out: the whole row dimmed, the box unticked and locked.
            foreach (Graphic g in control.transform.parent.GetComponentsInChildren<Graphic>(true))
                g.color = new Color(g.color.r, g.color.g, g.color.b, g.color.a * 0.4f);
        }
    }

    // Thunder faction colours: named colours (plus the current one if it is a custom value).
    private static readonly (string Name, string Hex)[] Colours =
    {
        ("Green", "#4CAF50"), ("Red", "#E53935"), ("Blue", "#1E88E5"), ("Cyan", "#00BCD4"),
        ("Orange", "#FB8C00"), ("Yellow", "#FDD835"), ("Purple", "#8E24AA"), ("White", "#FFFFFF"),
    };

    private void AddFactionColour(string label, ConfigEntry<string> palette, string faction)
    {
        string[] tags = faction == "Boscali" ? new[] { "BDF", "Boscali" } : new[] { "PALA", "Primeva" };
        string current = PaletteColour(palette.Value, tags);
        var names = new System.Collections.Generic.List<string>();
        var hexes = new System.Collections.Generic.List<string>();
        foreach (var (name, hex) in Colours) { names.Add(name); hexes.Add(hex); }
        int index = hexes.FindIndex(h => string.Equals(h, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && !string.IsNullOrEmpty(current)) { names.Insert(0, "Custom " + current); hexes.Insert(0, current); index = 0; }
        Row(label, DropdownPart, false, out GameObject control, out _);
        Dropdown dropdown = control.GetComponent<Dropdown>();
        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.ClearOptions();
        dropdown.AddOptions(names);
        dropdown.SetValueWithoutNotify(Math.Max(0, index));
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(i =>
        {
            if (i >= 0 && i < hexes.Count) palette.Value = SetPaletteColour(palette.Value, tags, hexes[i]);
        });
    }

    // "BDF=#4CAF50, PALA=#E53935" → the colour of the first tag listed.
    private static string PaletteColour(string palette, string[] tags)
    {
        foreach (string pair in (palette ?? "").Split(','))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string tag = pair.Substring(0, eq).Trim();
            foreach (string t in tags)
                if (string.Equals(tag, t, StringComparison.OrdinalIgnoreCase)) return pair.Substring(eq + 1).Trim();
        }
        return null;
    }

    // Replace (or add) the colour for every tag of one faction, keeping the other entries.
    private static string SetPaletteColour(string palette, string[] tags, string hex)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (string pair in (palette ?? "").Split(','))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string tag = pair.Substring(0, eq).Trim();
            if (Array.Exists(tags, t => string.Equals(tag, t, StringComparison.OrdinalIgnoreCase))) continue;
            kept.Add(pair.Trim());
        }
        foreach (string t in tags) kept.Add($"{t}={hex}");
        return string.Join(", ", kept);
    }

    private void AddToggle(string label, ConfigEntryBase entry)
    {
        Row(label, TogglePart, false, out GameObject control, out _);
        Toggle toggle = control.GetComponent<Toggle>();
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify((bool)entry.BoxedValue);
        toggle.onValueChanged.AddListener(on => entry.BoxedValue = on);
    }

    private void AddSlider(string label, ConfigEntryBase entry, float min, float max)
    {
        GameObject row = Row(label, SliderPart, false, out GameObject control, out Vector2 size);
        Slider slider = control.GetComponent<Slider>();
        // The value readout: a text inside the slider if the game's has one, else our own to its left.
        TMP_Text value = control.GetComponentInChildren<TMP_Text>(true);
        Text legacyValue = value == null ? control.GetComponentInChildren<Text>(true) : null;
        if (value == null && legacyValue == null)
        {
            GameObject readout = Instantiate(Part(LabelPart), row.transform);
            readout.SetActive(true);
            value = readout.GetComponent<TMP_Text>();
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.enableWordWrapping = false;
            var rect = (RectTransform)readout.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-size.x - 12f, 0f);
            rect.sizeDelta = new Vector2(80f, RowHeight);
        }
        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = false;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify((float)entry.BoxedValue);
        Action<float> show = v =>
        {
            string shown = v.ToString("0") + "\u00B0";
            if (value != null) value.text = shown; else legacyValue.text = shown;
        };
        show((float)entry.BoxedValue);
        slider.onValueChanged.AddListener(v =>
        {
            v = Mathf.Round(v);
            entry.BoxedValue = v;
            show(v);
        });
    }

    private void AddDropdown(string label, ConfigEntryBase entry, bool big = false)
    {
        Row(label, DropdownPart, big, out GameObject control, out _);
        Dropdown dropdown = control.GetComponent<Dropdown>();
        // Only the values the setting accepts (HudStyle leaves Plus out while it is unfinished).
        object[] values = entry.Description?.AcceptableValues is AcceptableValueBase list
            && list.GetType().GetProperty("AcceptableValues")?.GetValue(list) is Array allowed
            ? allowed.Cast<object>().ToArray()
            : Enum.GetValues(entry.SettingType).Cast<object>().ToArray();
        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.ClearOptions();
        dropdown.AddOptions(values.Select(v => Pretty(v.ToString())).ToList());
        dropdown.SetValueWithoutNotify(Math.Max(0, Array.IndexOf(values, entry.BoxedValue)));
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(i => { if (i >= 0 && i < values.Length) entry.BoxedValue = values[i]; });
    }

    // A button made from a dropdown: same frame and caption, no list.
    private void AddButton(string label, string caption, Action onClick, bool big = false)
    {
        Row(label, DropdownPart, big, out GameObject control, out _);
        Dropdown dropdown = control.GetComponent<Dropdown>();
        Graphic graphic = dropdown.targetGraphic;
        ColorBlock colours = dropdown.colors;
        Selectable.Transition transition = dropdown.transition;
        SpriteState sprites = dropdown.spriteState;
        Text captionText = dropdown.captionText;
        TMP_Text captionTmp = control.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
        if (dropdown.template != null) dropdown.template.gameObject.SetActive(false);
        DestroyImmediate(dropdown);
        foreach (Transform child in control.transform)
            if (child.name.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0) child.gameObject.SetActive(false);
        if (captionText != null) { captionText.text = caption; captionText.alignment = TextAnchor.MiddleCenter; }
        else if (captionTmp != null) { captionTmp.text = caption; captionTmp.alignment = TextAlignmentOptions.Center; }
        var button = control.AddComponent<Button>();
        button.targetGraphic = graphic;
        button.transition = transition;
        button.colors = colours;
        button.spriteState = sprites;
        button.onClick.AddListener(() => onClick());
    }

    // "WingmanOnly" → "Wingman only".
    private static string Pretty(string name)
    {
        var s = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) { s.Append(' '); c = char.ToLowerInvariant(c); }
            s.Append(c);
        }
        return s.ToString();
    }
}
