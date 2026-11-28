using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIPlus.Menu;

// Styles share the stock controls, layout order, and input. Only their presentation is different.
internal sealed class TitleMenuLayout
{
    internal const int FooterFontSize = 15;
    internal const float HighlightSeconds = 0.08f;
    internal static readonly Color Background = new(0.035f, 0.045f, 0.055f, 0.70f);
    internal static readonly Color Dim = new(0.77f, 0.80f, 0.82f);
    internal static readonly Color Bright = new(1f, 1f, 1f);
    private static readonly Color HeadingColour = new(0.55f, 0.58f, 0.60f);
    private static readonly Vector2 TopLeft = new(0f, 1f);

    private sealed class Row
    {
        internal Transform Root;
        internal Button Button;
        internal TMP_Text Label;
        internal Text LegacyLabel;
        internal RectTransform Fill;
        internal Image Accent;
        internal CanvasGroup FillOpacity, AccentOpacity;
        internal float HotAmount;
        internal Vector3 BarWidths;
        internal string BarCaption;
        internal int Order, DiscoveryIndex;
        internal bool Visible, Available;
    }

    private readonly RectTransform _left, _buttons, _rowArt;
    private readonly Button _missions;
    private readonly TMP_InputField _name;
    private readonly TMP_Text _fontSample;
    private readonly CanvasGroup _input;
    private readonly Canvas _canvas;
    private readonly Camera _camera;
    private readonly MinimalCorner _corner;
    private readonly MenuTextShadow _shadow;
    private readonly MenuSnapshot _snapshot;
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<Row> _rows = new();
    private readonly Transform[] _headings = new Transform[3];
    private readonly RectTransform[] _barDividers;
    private readonly Row _quit;
    private int _buttonChildren = -1, _leftChildren = -1;
    private readonly float _logoDividerDepth;
    private float _lastRailHeight, _groupHeight;
    private float _lastBarWidth;
    private bool _barQuitVisible;
    private Color _accent;
    private Button _lateContinue, _lateLoad;

    internal TitleMenuLayout(MenuSnapshot snapshot, MenuStyle style, MenuSide side, RectTransform left, RectTransform container, RectTransform buttons,
        Button quit, Button missions, TMP_InputField name, RectTransform logo)
    {
        _left = left;
        _snapshot = snapshot;
        _buttons = buttons;
        _missions = missions;
        _name = name;
        Mirrored = side == MenuSide.Right;
        IsBar = style == MenuStyle.BottomBar;
        if (style == MenuStyle.MinimalCorner) _corner = new MinimalCorner(snapshot, Mirrored);
        if (IsCorner || IsBar) _shadow = new MenuTextShadow(snapshot);
        _canvas = left.GetComponentInParent<Canvas>().rootCanvas;
        _camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        var leftArt = left.GetComponent<Image>();
        var innerArt = container.GetComponent<Image>();
        if (leftArt != null) leftArt.enabled = false;
        if (innerArt != null) innerArt.enabled = false;

        float railEdge = IsCorner || IsBar ? 0f : MenuLayout.AnchorX(0f, Mirrored);
        left.anchorMin = new Vector2(railEdge, 0f);
        left.anchorMax = new Vector2(IsCorner || IsBar ? 1f : railEdge, 1f);
        left.pivot = new Vector2(railEdge, 0.5f);
        left.anchoredPosition = Vector2.zero;
        left.sizeDelta = new Vector2(IsCorner || IsBar ? 0f : SideRail.Width, 0f);
        _input = snapshot.Add<CanvasGroup>(left.gameObject);
        RectTransform background = IsBar ? BottomBar.BuildBackground(left) :
            IsCorner ? _corner.BuildScrim(left) : SideRail.BuildBackground(left, Mirrored);
        snapshot.Own(background.gameObject);
        background.SetAsFirstSibling();
        _rowArt = background;
        if (IsCorner)
        {
            var art = snapshot.Own(new GameObject("UI Plus selection", typeof(RectTransform)));
            art.transform.SetParent(left, false);
            art.transform.SetSiblingIndex(1);
            _rowArt = (RectTransform)art.transform;
            _rowArt.anchorMin = Vector2.zero;
            _rowArt.anchorMax = Vector2.one;
            _rowArt.offsetMin = _rowArt.offsetMax = Vector2.zero;
        }
        // Keep the stock path and missions button's sibling list for mods that find them by either.
        var containerLayout = container.GetComponent<VerticalLayoutGroup>();
        if (containerLayout != null) containerLayout.enabled = false;
        var containerFitter = container.GetComponent<ContentSizeFitter>();
        if (containerFitter != null) containerFitter.enabled = false;
        container.anchorMin = Vector2.zero;
        container.anchorMax = Vector2.one;
        container.offsetMin = container.offsetMax = Vector2.zero;
        if (IsBar) BottomBar.PlaceList(buttons);
        else if (IsCorner) _corner.PlaceList(buttons);
        else SideRail.PlaceList(buttons, Mirrored);
        var layout = buttons.GetComponent<VerticalLayoutGroup>();
        if (IsBar)
        {
            if (layout != null) layout.enabled = false;
        }
        else
        {
            layout ??= snapshot.Add<VerticalLayoutGroup>(buttons.gameObject);
            layout.enabled = true;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset();
            layout.spacing = 2f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
        var fitter = buttons.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;

        _fontSample = missions.GetComponentInChildren<TMP_Text>(true);
        if (IsBar)
        {
            _barDividers = new RectTransform[3];
            for (int i = 0; i < _barDividers.Length; i++) _barDividers[i] = BottomBar.MakeDivider(_rowArt);
        }
        else for (int i = 0; i < _headings.Length; i++)
        {
            _headings[i] = IsCorner ? _corner.GroupGap(_buttons)
                : Heading(((MenuSection)i).ToString().ToUpperInvariant(), _buttons, i == 0 ? 0f : 18f);
            snapshot.Own(_headings[i].gameObject);
        }
        _quit = Style(quit.transform, quit, MenuLayout.QuitOrder, 0, "QUIT");
        if (IsCorner) _corner.PlaceQuit((RectTransform)quit.transform);
        else if (!IsBar) SideRail.PlaceQuit((RectTransform)quit.transform, Mirrored);

        float aspect = logo.rect.height > 0f ? logo.rect.width / logo.rect.height : 2f;
        var logoImage = logo.GetComponent<Image>();
        if (logoImage != null) logoImage.preserveAspect = true;
        logo.SetParent(left, false);
        float logoWidth = Mathf.Min(SideRail.Width - 52f, 82f * aspect);
        float logoHeight = Mathf.Min(82f, logoWidth / Mathf.Max(0.1f, aspect));
        var logoSize = new Vector2(logoWidth, logoHeight);
        if (IsCorner || IsBar)
        {
            var label = MakeLabel("PILOT", left, 13f,
                PilotOnRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            label.characterSpacing = 4f;
            label.color = MinimalCorner.Dim;
            ShadowLabel(label);
            if (IsBar) BottomBar.PlaceLogoAndPilot(logo, label.rectTransform, (RectTransform)_name.transform, logoSize, Mirrored);
            else _corner.PlaceLogoAndPilot(logo, label.rectTransform, (RectTransform)_name.transform, logoSize);
        }
        else
        {
            SideRail.PlacePilot(Heading("PILOT", left, 0f), (RectTransform)_name.transform, Mirrored);
            // Use the logo's home position so auto-hide never moves the divider or the rows.
            _logoDividerDepth = SideRail.PlaceLogo(logo, background, logoSize, Mirrored);
        }
        StyleName();
    }

    internal RectTransform Root => _left;
    internal bool IsCorner => _corner != null;
    internal bool IsBar { get; }
    internal bool Mirrored { get; }
    private bool PilotOnRight => IsBar != Mirrored;
    internal float? BottomRevealEdge => IsBar
        ? RectTransformUtility.WorldToScreenPoint(_camera,
            _rowArt.TransformPoint(new Vector3(_rowArt.rect.center.x, _rowArt.rect.yMax, 0f))).y
        : null;
    private Color RowDim => IsCorner || IsBar ? MinimalCorner.Dim : Dim;
    private float RowWidth => IsCorner ? MinimalCorner.ListWidth : SideRail.InnerWidth;
    private float ButtonHeight => IsCorner ? MinimalCorner.RowHeight : SideRail.RowHeight;

    internal void Own(GameObject created) => _snapshot.Own(created);

    internal Vector2 TipsShift(RectTransform tips, Vector2 home, RectTransform version, RectTransform news,
        Vector2 newsRestOffset, out float? fadeFloor)
    {
        float? floor = null;
        Transform parent = tips.parent;
        Bounds panel = BoundsIn(tips, parent);
        // Use the visible home position even while the panel is sliding offscreen.
        panel.center += (Vector3)(home - tips.anchoredPosition);
        float x = 0f, y = 0f;
        if (!IsBar)
        {
            Bounds list = BoundsIn(IsCorner ? _buttons : _left, parent);
            if (IsCorner) list.Encapsulate(BoundsIn((RectTransform)_quit.Root, parent));
            x = MenuLayout.TipsSideShift(list.min.x, list.max.x, list.min.y, list.max.y,
                panel.min.x, panel.max.x, panel.min.y, panel.max.y, Mirrored);
            panel.center += new Vector3(x, 0f, 0f);
        }
        else
        {
            floor = BoundsIn(_rowArt, parent).max.y;
            y = Mathf.Max(0f, floor.Value + 16f - panel.min.y);
        }
        if (version != null) Avoid(BoundsIn(version, parent));
        if (news != null)
        {
            Bounds links = BoundsIn(news, parent);
            links.center += (Vector3)newsRestOffset;
            Avoid(links);
        }
        fadeFloor = floor;
        return new Vector2(x, y);

        void Avoid(Bounds other)
        {
            if (panel.min.x >= other.max.x || panel.max.x <= other.min.x) return;
            y = MenuLayout.TipsAboveBar(float.NegativeInfinity, panel.min.y, panel.max.y, true,
                other.min.y, other.max.y, y);
            if (panel.min.y + y >= other.max.y && panel.min.y < other.max.y)
                floor = Mathf.Max(floor ?? float.NegativeInfinity, other.max.y);
        }
    }

    internal TMP_InputField Pilot => _name;

    private Bounds BoundsIn(RectTransform rect, Transform parent)
    {
        rect.GetWorldCorners(_corners);
        var bounds = new Bounds(parent.InverseTransformPoint(_corners[0]), Vector3.zero);
        for (int i = 1; i < _corners.Length; i++) bounds.Encapsulate(parent.InverseTransformPoint(_corners[i]));
        return bounds;
    }

    internal void SetCompactFooter(bool compact)
    {
        if (IsCorner || IsBar) return;
        var rect = (RectTransform)_quit.Button.transform;
        rect.sizeDelta = new Vector2(compact ? 126f : SideRail.Width - 44f, rect.sizeDelta.y);
    }

    internal static RectTransform Paint(string name, Transform parent, Color colour)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        var image = item.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image.rectTransform;
    }

    internal TextMeshProUGUI MakeLabel(string caption, Transform parent, float size, TextAlignmentOptions alignment)
    {
        var go = _snapshot.Own(new GameObject(caption, typeof(RectTransform), typeof(TextMeshProUGUI)));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        UseRailFont(label);
        label.text = caption;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = RowDim;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    private void UseRailFont(TMP_Text label)
    {
        if (_fontSample == null) return;
        label.font = _fontSample.font;
        label.fontSharedMaterial = _fontSample.fontSharedMaterial;
    }

    private RectTransform Heading(string caption, Transform parent, float gap)
    {
        const float labelHeight = 18f;
        var go = _snapshot.Own(new GameObject(caption, typeof(RectTransform)));
        go.transform.SetParent(parent, false);
        var heading = (RectTransform)go.transform;
        var label = MakeLabel(caption, heading, 13f, Mirrored ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
        label.characterSpacing = 4f;
        label.color = HeadingColour;
        float textWidth = Mathf.Ceil(label.GetPreferredValues(caption).x);
        TitleMenu.Place(label.rectTransform, TopLeft, TopLeft, new Vector2(SideRail.TextInset, -gap),
            new Vector2(textWidth, labelHeight), Mirrored);
        var rule = Paint("Heading rule", heading, new Color(1f, 1f, 1f, 0.14f));
        float ruleY = -gap - labelHeight * 0.5f;
        rule.anchorMin = TopLeft;
        rule.anchorMax = Vector2.one;
        float inset = SideRail.TextInset + textWidth + 10f;
        rule.offsetMin = new Vector2(Mirrored ? 0f : inset, ruleY - 0.5f);
        rule.offsetMax = new Vector2(Mirrored ? -inset : 0f, ruleY + 0.5f);
        // The layout group's 2-unit spacing adds to the 4 units below the heading band.
        if (parent == _buttons)
        {
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = gap + labelHeight + 4f;
        }
        return heading;
    }

    private void StyleName()
    {
        if (_name.targetGraphic != null)
            _name.targetGraphic.color = IsCorner || IsBar ? Color.clear : new Color(1f, 1f, 1f, 0.18f);
        if (_name.textViewport != null)
        {
            var viewport = _name.textViewport;
            viewport.anchorMin = new Vector2(0f, viewport.anchorMin.y);
            viewport.anchorMax = new Vector2(1f, viewport.anchorMax.y);
            float left = IsCorner || IsBar ? 0f : 13f, right = IsBar ? 0f : 10f;
            viewport.offsetMin = new Vector2(Mirrored ? right : left, viewport.offsetMin.y);
            viewport.offsetMax = new Vector2(-(Mirrored ? left : right), viewport.offsetMax.y);
        }
        StyleNameLabel(_name.textComponent);
        if (_name.placeholder is TMP_Text placeholder) StyleNameLabel(placeholder);
    }

    private void StyleNameLabel(TMP_Text label)
    {
        if (label == null) return;
        UseRailFont(label);
        label.alignment = PilotOnRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        label.color = RowDim;
        label.fontSize = IsCorner ? 15f : 16f;
        label.enableAutoSizing = false;
        label.margin = Vector4.zero;
        StretchLabel(label.rectTransform, _name.textViewport);
        ShadowLabel(label);
    }

    internal void ShadowLabel(Graphic label) => _shadow?.Shadow(label);

    private Row Style(Transform root, Button button, int order, int index, string caption = null)
    {
        float inset = IsCorner || IsBar ? 0f : caption == null ? SideRail.TextInset : 13f;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        var legacy = label == null ? button.GetComponentInChildren<Text>(true) : null;
        button.transition = Selectable.Transition.None;
        if (button.targetGraphic is Graphic graphic && graphic != label && graphic != legacy)
        {
            // A transparent graphic keeps the original clickable area alive.
            graphic.color = Color.clear;
            graphic.raycastTarget = true;
        }
        if (label != null)
        {
            if (caption != null)
            {
                _snapshot.RememberCaption(label);
                label.text = caption;
            }
            label.horizontalAlignment = IsBar ? HorizontalAlignmentOptions.Center :
                Mirrored ? HorizontalAlignmentOptions.Right : HorizontalAlignmentOptions.Left;
            label.verticalAlignment = VerticalAlignmentOptions.Middle;
            label.fontSize = IsBar ? 17f : IsCorner ? 18f : caption == null ? 19f : FooterFontSize;
            label.enableAutoSizing = false;
            label.characterSpacing = 2f;
            label.margin = new Vector4(Mirrored ? 0f : inset, 0f, Mirrored ? inset : 0f, 0f);
            label.color = RowDim;
            label.fontStyle |= FontStyles.UpperCase;
            if (order == 0) label.fontStyle |= FontStyles.Bold;
            StretchLabel(label.rectTransform, button.transform);
            if (IsBar)
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            else ShadowLabel(label);
        }
        if (legacy != null)
        {
            _snapshot.RememberCaption(legacy);
            legacy.text = (caption ?? legacy.text).ToUpperInvariant();
            legacy.alignment = IsBar ? TextAnchor.MiddleCenter : Mirrored ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            legacy.fontSize = IsBar ? 17 : IsCorner ? 18 : caption == null ? 19 : FooterFontSize;
            legacy.color = RowDim;
            StretchLabel(legacy.rectTransform, button.transform, Mirrored ? 0f : inset, Mirrored ? inset : 0f);
            if (!IsBar) ShadowLabel(legacy);
        }

        // Keep the decoration outside the button so a later save clone cannot inherit it.
        var art = _snapshot.Own(new GameObject("Row highlight", typeof(RectTransform)));
        art.transform.SetParent(_rowArt, false);
        var fill = (RectTransform)art.transform;
        float width = caption == null ? RowWidth : Mathf.Ceil(label != null ? label.GetPreferredValues(caption).x :
            legacy != null ? legacy.preferredWidth : 48f) + 26f;
        TitleMenu.Place(fill, Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero,
            new Vector2(width, IsBar ? BottomBar.Height : IsCorner ? 20f : caption == null ? SideRail.RowHeight : 37f),
            Mirrored && !IsBar);
        CanvasGroup fillOpacity = null;
        if (!IsCorner)
        {
            var tint = Paint("Fill", fill, Color.white);
            tint.anchorMin = Vector2.zero;
            tint.anchorMax = Vector2.one;
            tint.offsetMin = tint.offsetMax = Vector2.zero;
            fillOpacity = tint.gameObject.AddComponent<CanvasGroup>();
            fillOpacity.alpha = 0f;
        }
        var stripe = Paint("Accent", fill, _accent);
        float stripeSide = IsCorner && Mirrored ? 1f : 0f;
        stripe.anchorMin = IsBar ? TopLeft : new Vector2(stripeSide, 0f);
        stripe.anchorMax = IsBar ? Vector2.one : new Vector2(stripeSide, 1f);
        stripe.pivot = new Vector2(stripeSide, IsBar ? 1f : 0.5f);
        stripe.anchoredPosition = Vector2.zero;
        stripe.sizeDelta = IsBar ? new Vector2(0f, 3f) : new Vector2(IsCorner ? 2f : 3f, 0f);
        var accentOpacity = stripe.gameObject.AddComponent<CanvasGroup>();
        accentOpacity.alpha = 0f;
        fill.gameObject.SetActive(false);
        return new Row
        {
            Root = root, Button = button, Label = label, LegacyLabel = legacy, Order = order, DiscoveryIndex = index,
            Fill = fill, FillOpacity = fillOpacity, Accent = stripe.GetComponent<Image>(), AccentOpacity = accentOpacity,
        };
    }

    private static void StretchLabel(RectTransform label, Transform button, float left = 0f, float right = 0f)
    {
        if (label.parent != button) return;
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(left, 0f);
        label.offsetMax = new Vector2(-right, 0f);
    }

    internal bool RefreshRows()
    {
        bool geometryChanged = _buttons.childCount != _buttonChildren || _left.childCount != _leftChildren;
        if (!geometryChanged)
            foreach (Row row in _rows)
                if (row.Root == null || row.Root.parent != _buttons || row.Button == null)
                {
                    geometryChanged = true;
                    break;
                }
        if (geometryChanged) DiscoverRows();
        bool navigationChanged = geometryChanged;
        foreach (Row row in _rows)
        {
            bool visible = row.Button != null && row.Button.gameObject.activeInHierarchy;
            bool available = visible && row.Button.interactable;
            geometryChanged |= row.Visible != visible || (IsBar && visible && row.BarCaption != Caption(row));
            navigationChanged |= row.Visible != visible || row.Available != available;
            row.Visible = visible;
            row.Available = available;
        }
        if (IsBar)
        {
            bool quitVisible = _quit.Button.gameObject.activeInHierarchy;
            navigationChanged |= _barQuitVisible != quitVisible;
            geometryChanged |= _barQuitVisible != quitVisible || (quitVisible && _quit.BarCaption != Caption(_quit));
            _barQuitVisible = quitVisible;
            float width = _buttons.rect.width;
            if (geometryChanged || _lastBarWidth != width)
            {
                _lastBarWidth = width;
                LayoutBottomBar(width);
            }
            return navigationChanged;
        }
        if (geometryChanged)
        {
            int sections = 0;
            foreach (Row row in _rows)
                if (row.Visible) sections |= 1 << (int)MenuLayout.Section(row.Order);
            for (int i = 0; i < _headings.Length; i++)
                _headings[i].gameObject.SetActive(IsCorner
                    ? MenuLayout.HasGroupGap(sections, (MenuSection)i)
                    : (sections & (1 << i)) != 0);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_buttons);
            _groupHeight = LayoutUtility.GetPreferredHeight(_buttons);
            _buttons.sizeDelta = new Vector2(RowWidth, _groupHeight);
        }
        float height = _left.rect.height;
        if (!IsCorner && (geometryChanged || _lastRailHeight != height))
        {
            _lastRailHeight = height;
            SideRail.CenterList(_buttons, height, _logoDividerDepth, _groupHeight);
        }
        return navigationChanged;
    }

    internal void ValidateGeometry(RectTransform logo)
    {
        bool visible = false;
        var viewport = _canvas.pixelRect;
        foreach (Row row in _rows)
        {
            if (!row.Visible) continue;
            visible = true;
            if (!IntersectsCanvas((RectTransform)row.Button.transform, viewport))
                throw new InvalidOperationException($"Title row '{row.Root.name}' is outside the canvas or has no area.");
        }
        if (!visible) throw new InvalidOperationException("The title layout has no visible menu rows.");
        if (logo.gameObject.activeInHierarchy && !IntersectsCanvas(logo, viewport))
            throw new InvalidOperationException("The title logo is outside the canvas or has no area.");
    }

    private bool IntersectsCanvas(RectTransform rect, Rect viewport)
    {
        rect.GetWorldCorners(_corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(_camera, _corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(_camera, _corners[2]);
        return max.x > min.x && max.y > min.y && min.x < viewport.xMax && max.x > viewport.xMin &&
            min.y < viewport.yMax && max.y > viewport.yMin;
    }

    private void LayoutBottomBar(float width)
    {
        int count = 0, dividers = 0;
        int? previous = null;
        Vector3 widths = Vector3.zero;
        foreach (Row row in _rows)
        {
            if (!row.Visible) continue;
            if (MenuLayout.BarDividerBefore(previous, row.Order)) dividers++;
            previous = row.Order;
            widths += MeasureBarRow(row);
            count++;
        }
        if (_barQuitVisible)
        {
            dividers++;
            widths += MeasureBarRow(_quit);
            count++;
        }
        var fit = MenuLayout.FitBar(width - 2f * BottomBar.Inset, count, dividers, widths.x, widths.y, widths.z);
        float x = BottomBar.Inset;
        int divider = 0;
        previous = null;
        foreach (Row row in _rows)
        {
            if (!row.Visible) continue;
            if (MenuLayout.BarDividerBefore(previous, row.Order))
            {
                PlaceBarDivider(divider++, x);
                x += MenuLayout.BarDividerWidth;
            }
            previous = row.Order;
            x += PlaceBarRow(row, fit, x);
        }
        if (_barQuitVisible)
        {
            float quitWidth = PlaceBarRow(_quit, fit, -BottomBar.Inset, fromRight: true);
            PlaceBarDivider(divider++, -BottomBar.Inset - quitWidth - MenuLayout.BarDividerWidth, fromRight: true);
        }
        for (int i = divider; i < _barDividers.Length; i++) _barDividers[i].gameObject.SetActive(false);
    }

    private void PlaceBarDivider(int index, float x, bool fromRight = false)
    {
        RectTransform divider = _barDividers[index];
        BottomBar.PlaceDivider(divider, x, fromRight, Mirrored);
        divider.gameObject.SetActive(true);
    }

    private static Vector3 MeasureBarRow(Row row)
    {
        string caption = Caption(row);
        if (row.BarCaption != caption)
        {
            row.BarWidths = new Vector3(MeasureLabel(row, 17), MeasureLabel(row, 16), MeasureLabel(row, 15));
            row.BarCaption = caption;
        }
        return row.BarWidths;
    }

    private static string Caption(Row row) => row.Label != null ? row.Label.text ?? "" :
        row.LegacyLabel != null ? row.LegacyLabel.text ?? "" : "";

    private static float MeasureLabel(Row row, int size)
    {
        if (row.Label != null)
        {
            row.Label.fontSize = size;
            return Mathf.Ceil(row.Label.GetPreferredValues(row.Label.text, float.PositiveInfinity, float.PositiveInfinity).x) + 1f;
        }
        if (row.LegacyLabel == null) return 0f;
        row.LegacyLabel.fontSize = size;
        return Mathf.Ceil(row.LegacyLabel.preferredWidth) + 1f;
    }

    private float PlaceBarRow(Row row, MenuLayout.BarFit fit, float x, bool fromRight = false)
    {
        float textWidth = row.BarWidths[17 - fit.FontSize] * fit.WidthScale;
        float width = textWidth + 2f * fit.Padding;
        Vector2 anchor = fromRight ? BottomBar.BottomRight : Vector2.zero;
        var root = (RectTransform)row.Root;
        TitleMenu.Place(root, anchor, anchor, new Vector2(x, 0f), new Vector2(width, BottomBar.Height), Mirrored);
        var button = (RectTransform)row.Button.transform;
        if (root != button) StretchLabel(button, root);
        if (row.Label != null)
        {
            row.Label.fontSize = fit.FontSize;
            SetBarLabelPadding(row.Label.rectTransform, button, fit.Padding);
        }
        if (row.LegacyLabel != null)
        {
            row.LegacyLabel.fontSize = fit.FontSize;
            SetBarLabelPadding(row.LegacyLabel.rectTransform, button, fit.Padding);
        }
        row.Fill.sizeDelta = new Vector2(width, BottomBar.Height);
        return width;
    }

    private static void SetBarLabelPadding(RectTransform label, RectTransform parent, float padding)
    {
        if (label.parent != parent) return;
        StretchLabel(label, parent, padding);
        label.offsetMax = new Vector2(-padding, 0f);
    }

    private void DiscoverRows()
    {
        for (int i = _left.childCount - 1; i >= 0; i--)
        {
            Transform child = _left.GetChild(i);
            if (MenuLayout.IsLanRoot(child.name))
            {
                _snapshot.CaptureTree(child);
                child.SetParent(_buttons, false);
            }
        }
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            Row row = _rows[i];
            if (row.Root != null && row.Root.parent == _buttons && row.Button != null) continue;
            row.Fill.gameObject.SetActive(false);
            Object.Destroy(row.Fill.gameObject);
            _rows.RemoveAt(i);
        }
        foreach (Transform child in _buttons)
        {
            Button button = child.GetComponent<Button>();
            if (button == null && MenuLayout.IsLanRoot(child.name)) button = child.GetComponentInChildren<Button>(true);
            if (button == null || _rows.Exists(row => row.Root == child)) continue;
            string caption = button.GetComponentInChildren<TMP_Text>(true)?.text ?? button.GetComponentInChildren<Text>(true)?.text;
            int order = button == _missions ? 2 : MenuLayout.ButtonOrder(child.name, caption);
            bool lateSave = !_snapshot.Owns(child.gameObject) && order is 0 or 1;
            _snapshot.CaptureTree(child, lateSave ? _missions.transform : null);
            if (lateSave)
            {
                if (order == 0) _lateContinue = button;
                else _lateLoad = button;
            }
            if (!IsBar)
            {
                var layout = child.GetComponent<LayoutElement>() ?? _snapshot.Add<LayoutElement>(child.gameObject);
                layout.ignoreLayout = false;
                layout.minHeight = layout.preferredHeight = ButtonHeight;
                layout.flexibleHeight = 0f;
            }
            _rows.Add(Style(child, button, order, child.GetSiblingIndex()));
        }
        _rows.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.DiscoveryIndex.CompareTo(b.DiscoveryIndex));
        int index = 0;
        for (int section = 0; section < _headings.Length; section++)
        {
            if (!IsBar) _headings[section].SetSiblingIndex(index++);
            foreach (Row row in _rows)
                if ((int)MenuLayout.Section(row.Order) == section) row.Root.SetSiblingIndex(index++);
        }
        _buttonChildren = _buttons.childCount;
        _leftChildren = _left.childCount;
    }

    internal void SetInput(bool on) => _input.interactable = _input.blocksRaycasts = on;

    internal void RestoreLateSaves()
    {
        if (_missions == null || (_lateContinue == null && _lateLoad == null)) return;
        if (_lateContinue != null) PlaceAfter(_lateContinue.transform, _missions.transform);
        if (_lateLoad != null) PlaceAfter(_lateLoad.transform, _lateContinue != null ? _lateContinue.transform : _missions.transform);

        // MissionSaveState adds these immediately after Singleplayer. Replay its explicit
        // links only if the original menu used them. Automatic layout needs no link changes.
        Navigation original = _missions.navigation;
        if (original.mode != Navigation.Mode.Explicit) return;
        bool horizontal = _buttons.GetComponent<HorizontalLayoutGroup>() != null ||
            (original.selectOnDown == null && original.selectOnRight != null);
        Selectable next = horizontal ? original.selectOnRight : original.selectOnDown;
        bool hasContinue = _lateContinue != null && _lateContinue.gameObject.activeSelf;
        Button first = hasContinue ? _lateContinue : _lateLoad;
        Button last = _lateLoad != null ? _lateLoad : first;
        if (first == null) return;
        Link(_missions, horizontal, null, first);
        if (hasContinue) Link(_lateContinue, horizontal, _missions, _lateLoad != null ? _lateLoad : next);
        if (_lateLoad != null) Link(_lateLoad, horizontal, hasContinue ? _lateContinue : _missions, next);
        if (next != null && next != _missions && next.navigation.mode == Navigation.Mode.Explicit &&
            (horizontal ? next.navigation.selectOnLeft : next.navigation.selectOnUp) == _missions)
            Link(next, horizontal, last, null);
    }

    private static void PlaceAfter(Transform item, Transform previous)
    {
        int index = previous.GetSiblingIndex();
        item.SetSiblingIndex(item.GetSiblingIndex() < index ? index : index + 1);
    }

    private static void Link(Selectable control, bool horizontal, Selectable previous, Selectable next)
    {
        Navigation nav = control.navigation;
        if (horizontal)
        {
            if (previous != null) nav.selectOnLeft = previous;
            if (next != null) nav.selectOnRight = next;
        }
        else
        {
            if (previous != null) nav.selectOnUp = previous;
            if (next != null) nav.selectOnDown = next;
        }
        control.navigation = nav;
    }

    internal void SetAccent(Color colour)
    {
        _accent = colour;
        foreach (Row row in _rows) row.Accent.color = colour;
        _quit.Accent.color = colour;
    }

    internal bool OwnsFocus(Transform focus) => focus.IsChildOf(_left);

    internal void AddNavigation(List<Selectable> chain)
    {
        if (_name.isActiveAndEnabled && _name.interactable) chain.Add(_name);
        foreach (Row row in _rows)
            if (row.Available) chain.Add(row.Button);
        if (_quit.Button.isActiveAndEnabled && _quit.Button.interactable) chain.Add(_quit.Button);
    }

    internal void SelectFirst()
    {
        foreach (Row row in _rows)
            if (row.Available) { row.Button.Select(); return; }
    }

    internal GameObject HoverTarget(Vector2 mouse)
    {
        foreach (Row row in _rows)
            if (Contains(row, mouse)) return row.Button.gameObject;
        return Contains(_quit, mouse) ? _quit.Button.gameObject : null;
    }

    private bool Contains(Row row, Vector2 mouse) =>
        row.Button != null && row.Button.isActiveAndEnabled && row.Button.interactable &&
        RectTransformUtility.RectangleContainsScreenPoint((RectTransform)row.Button.transform, mouse, _camera);

    internal void Highlight(GameObject hot)
    {
        float step = Time.unscaledDeltaTime / HighlightSeconds;
        foreach (Row row in _rows) Highlight(row, hot, step);
        Highlight(_quit, hot, step);
    }

    private void Highlight(Row row, GameObject hot, float step)
    {
        bool visible = row.Button != null && row.Button.isActiveAndEnabled;
        bool available = visible && row.Button.interactable;
        bool isHot = available && hot == row.Button.gameObject;
        bool primary = visible && row.Order == 0;
        row.HotAmount = isHot ? Mathf.MoveTowards(row.HotAmount, 1f, step) : 0f;
        if (row.Fill.gameObject.activeSelf != isHot) row.Fill.gameObject.SetActive(isHot);
        if (isHot)
        {
            if (row.FillOpacity != null) row.FillOpacity.alpha = (IsBar ? 0.08f : 0.11f) * row.HotAmount;
            row.AccentOpacity.alpha = row.HotAmount;
            var rect = (RectTransform)row.Root;
            Vector2 reference = rect.rect.center;
            if (IsBar) reference.x = rect.rect.xMin;
            Vector3 centre = _rowArt.InverseTransformPoint(rect.TransformPoint(reference));
            float x = IsBar ? centre.x - _rowArt.rect.xMin : IsCorner ? MinimalCorner.Left - 10f : row == _quit ? 22f : 0f;
            row.Fill.anchoredPosition = new Vector2(MenuLayout.OffsetX(x, Mirrored && !IsBar), centre.y - _rowArt.rect.yMin);
        }
        Color colour = isHot || primary ? Bright : available ? RowDim : new Color(0.45f, 0.48f, 0.50f);
        if (row.Label != null && row.Label.color != colour) row.Label.color = colour;
        if (row.LegacyLabel != null && row.LegacyLabel.color != colour) row.LegacyLabel.color = colour;
    }
}
