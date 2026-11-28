using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Menu;

// Use a small footer control and borrow the stock link buttons only while the popup is open.
internal sealed class CompactMenuNews
{
    private static readonly Color VersionDim = new(0.60f, 0.63f, 0.65f);

    private sealed class Link
    {
        internal readonly Button Button;
        private readonly RectTransform _rect, _slot, _hitArea;
        private readonly Transform _parent;
        private readonly int _index;
        private readonly Vector2 _min, _max, _pivot, _size;
        private readonly Vector3 _position;
        private readonly TMP_Text _label;
        private readonly Image _accent;
        private readonly CanvasGroup _accentOpacity;

        internal Link(Button button, TitleMenuLayout layout, RectTransform panel, int row)
        {
            Button = button;
            _rect = (RectTransform)button.transform;
            _parent = _rect.parent;
            _index = _rect.GetSiblingIndex();
            _min = _rect.anchorMin;
            _max = _rect.anchorMax;
            _pivot = _rect.pivot;
            _size = _rect.sizeDelta;
            _position = _rect.anchoredPosition3D;

            var top = new Vector2(0f, 1f);
            var at = new Vector2(0f, -8f - row * 33f);
            var size = new Vector2(panel.sizeDelta.x, 33f);
            // Let the stock graphics receive clicks and selection inside an invisible slot.
            var slot = new GameObject("Link input", typeof(RectTransform), typeof(CanvasGroup));
            slot.transform.SetParent(panel, false);
            slot.GetComponent<CanvasGroup>().alpha = 0f;
            _slot = (RectTransform)slot.transform;
            TitleMenu.Place(_slot, top, top, at, size);
            _hitArea = TitleMenuLayout.Paint("Link hit area", _slot, Color.clear);
            var hit = _hitArea.GetComponent<Image>();
            hit.raycastTarget = true;
            hit.canvasRenderer.cullTransparentMesh = false;

            string caption = button.GetComponentInChildren<TMP_Text>(true)?.text
                ?? button.GetComponentInChildren<Text>(true)?.text ?? button.name;
            _label = layout.MakeLabel(caption, panel, 16f, TextAlignmentOptions.MidlineLeft);
            _label.margin = new Vector4(13f, 0f, 13f, 0f);
            _label.overflowMode = TextOverflowModes.Ellipsis;
            TitleMenu.Place(_label.rectTransform, top, top, at, size);
            var accent = TitleMenuLayout.Paint("Link accent", panel, Color.clear);
            _accent = accent.GetComponent<Image>();
            TitleMenu.Place(accent, top, top, at, new Vector2(3f, size.y));
            _accentOpacity = accent.gameObject.AddComponent<CanvasGroup>();
            _accentOpacity.alpha = 0f;
        }

        internal void Move(bool intoPopup)
        {
            if (intoPopup)
            {
                _rect.SetParent(_slot, false);
                _rect.anchorMin = Vector2.zero;
                _rect.anchorMax = Vector2.one;
                _rect.offsetMin = _rect.offsetMax = Vector2.zero;
                _hitArea.SetParent(_rect, false);
                _hitArea.anchorMin = Vector2.zero;
                _hitArea.anchorMax = Vector2.one;
                _hitArea.offsetMin = _hitArea.offsetMax = Vector2.zero;
                return;
            }
            _accentOpacity.alpha = 0f;
            _hitArea.SetParent(_slot, false);
            _rect.SetParent(_parent, false);
            _rect.SetSiblingIndex(_index);
            _rect.anchorMin = _min;
            _rect.anchorMax = _max;
            _rect.pivot = _pivot;
            _rect.sizeDelta = _size;
            _rect.anchoredPosition3D = _position;
        }

        internal bool Contains(Vector2 mouse, Camera camera) => Button.isActiveAndEnabled && Button.interactable &&
            RectTransformUtility.RectangleContainsScreenPoint(_slot, mouse, camera);

        internal void SetAccent(Color colour) => _accent.color = colour;

        internal void Highlight(GameObject target, float step)
        {
            bool hot = Button.interactable && target == Button.gameObject;
            _accentOpacity.alpha = hot ? Mathf.MoveTowards(_accentOpacity.alpha, 1f, step) : 0f;
            _label.color = hot ? TitleMenuLayout.Bright : TitleMenuLayout.Dim;
        }
    }

    private readonly TitleMenuLayout _layout;
    private readonly RectTransform _popup;
    private readonly TextMeshProUGUI _label;
    private readonly Button _toggle;
    private readonly Camera _camera;
    private readonly List<Link> _links = new();
    private readonly string _closedCaption, _openCaption;
    private bool _open, _dirty;

    internal CompactMenuNews(TitleMenuLayout layout, Text version, Button[] links)
    {
        _layout = layout;
        Canvas canvas = layout.Root.GetComponentInParent<Canvas>().rootCanvas;
        _camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        string original = version.text;
        int space = original.LastIndexOf(' ');
        string number = (space >= 0 ? original.Substring(space + 1) : original).TrimStart('v', 'V');
        _label = layout.MakeLabel("v" + number, layout.Root, TitleMenuLayout.FooterFontSize,
            layout.Mirrored ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
        _label.color = VersionDim;
        // The rail's font may lack the small triangles; fall back to plain signs rather than a missing-glyph box.
        bool arrows = !layout.IsCorner && !layout.IsBar && _label.font != null &&
            _label.font.HasCharacter('▾', true) && _label.font.HasCharacter('▴', true);
        _closedCaption = "v" + number + (arrows ? "  ▾" : "  +");
        _openCaption = "v" + number + (arrows ? "  ▴" : "  -");
        _label.text = _closedCaption;
        layout.ShadowLabel(_label);
        _label.raycastTarget = true;
        _toggle = _label.gameObject.AddComponent<Button>();
        _toggle.targetGraphic = _label;
        _toggle.transition = Selectable.Transition.None;
        _toggle.onClick.AddListener(() => SetOpen(!_open));
        var anchor = new Vector2(1f, 0f);
        Vector2 labelAt = layout.IsCorner ? new Vector2(-44f, 23f) : new Vector2(-35f, 23f);
        if (!layout.IsBar) TitleMenu.Place(_label.rectTransform, anchor, anchor, labelAt, new Vector2(126f, 37f), layout.Mirrored);

        _popup = TitleMenuLayout.Paint("Version links", layout.Root, TitleMenuLayout.Background);
        layout.Own(_popup.gameObject);
        _popup.GetComponent<Image>().raycastTarget = true;
        // Keep the flyout clear of both the name field and the version control.
        if (layout.IsBar)
            BottomBar.PlaceVersion(_label.rectTransform, _popup, layout.Mirrored);
        else if (layout.IsCorner)
            TitleMenu.Place(_popup, anchor, anchor, new Vector2(-44f, 64f), new Vector2(238f, 16f), layout.Mirrored);
        else
            TitleMenu.Place(_popup, Vector2.zero, Vector2.zero, new Vector2(SideRail.Width + 8f, 60f),
                new Vector2(238f, 16f), layout.Mirrored);
        foreach (Button link in links)
            if (link.gameObject.activeSelf) _links.Add(new Link(link, layout, _popup, _links.Count));
        _popup.sizeDelta = new Vector2(_popup.sizeDelta.x, 16f + _links.Count * 33f);
        _popup.gameObject.SetActive(false);
        _toggle.gameObject.SetActive(false);
    }

    internal void SetVisible(bool visible)
    {
        if (!visible) SetOpen(false);
        _toggle.gameObject.SetActive(visible);
        _layout.SetCompactFooter(visible);
    }

    internal void Close() => SetOpen(false);

    internal Button Version => _toggle.isActiveAndEnabled ? _toggle : null;
    internal RectTransform OpenPopup => _open ? _popup : null;

    internal Button FirstLink
    {
        get
        {
            if (_open)
                foreach (Link link in _links)
                    if (link.Button.isActiveAndEnabled && link.Button.interactable) return link.Button;
            return null;
        }
    }

    private void SetOpen(bool open)
    {
        if (_open == open) return;
        _open = open;
        foreach (Link link in _links) link.Move(open);
        _label.text = open ? _openCaption : _closedCaption;
        _popup.gameObject.SetActive(open);
        _dirty = true;
    }

    internal bool Tick(Vector2 mouse, bool modal)
    {
        if (_open && _toggle.isActiveAndEnabled)
        {
            bool outsideClick = Input.GetMouseButtonDown(0) &&
                !RectTransformUtility.RectangleContainsScreenPoint(_label.rectTransform, mouse, _camera) &&
                !RectTransformUtility.RectangleContainsScreenPoint(_popup, mouse, _camera);
            if (modal || outsideClick) SetOpen(false);
        }
        bool changed = _dirty;
        _dirty = false;
        return changed;
    }

    internal GameObject HoverTarget(Vector2 mouse)
    {
        if (!_toggle.isActiveAndEnabled) return null;
        if (_open)
            foreach (Link link in _links)
                if (link.Contains(mouse, _camera)) return link.Button.gameObject;
        return RectTransformUtility.RectangleContainsScreenPoint(_label.rectTransform, mouse, _camera) ? _toggle.gameObject : null;
    }

    internal void Highlight(GameObject target)
    {
        if (!_toggle.isActiveAndEnabled) return;
        _label.color = target == _toggle.gameObject ? TitleMenuLayout.Bright : VersionDim;
        if (!_open) return;
        float step = Time.unscaledDeltaTime / TitleMenuLayout.HighlightSeconds;
        foreach (Link link in _links) link.Highlight(target, step);
    }

    internal void SetAccent(Color colour)
    {
        foreach (Link link in _links) link.SetAccent(colour);
    }

    internal void AddNavigation(List<Selectable> chain)
    {
        if (!_toggle.isActiveAndEnabled) return;
        chain.Add(_toggle);
        if (_open)
            foreach (Link link in _links)
                if (link.Button.isActiveAndEnabled && link.Button.interactable) chain.Add(link.Button);
    }
}
