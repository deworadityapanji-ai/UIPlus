using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UIPlus.Menu;

internal sealed class TitleMenuPanels
{
    private enum Edge { Top, BottomLeft, Bottom }

    private sealed class SlidingPanel
    {
        internal readonly RectTransform Rect;
        private readonly CanvasGroup _group;
        private readonly Canvas _canvas;
        private readonly Camera _camera;
        private readonly Edge _edge;
        internal readonly Vector2 Home;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _progress = 1f;
        private bool _pointerRevealed;
        internal bool CanNavigate { get; private set; }

        internal SlidingPanel(MenuSnapshot snapshot, RectTransform rect, Edge edge)
        {
            Rect = rect;
            _edge = edge;
            Home = rect.anchoredPosition;
            // Keep the GameObject and its own update logic alive, especially the tips carousel.
            _group = snapshot.Add<CanvasGroup>(rect.gameObject);
            _canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            _camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        }

        internal bool Tick(Vector2 mouse, bool gamepad, bool modal, bool hidden, bool autoHide, bool expanded,
            Vector2 homeOffset = default, float? fadeFloor = null, float? bottomRevealEdge = null)
        {
            bool active = Rect.gameObject.activeInHierarchy;
            var parent = (RectTransform)Rect.parent;
            Vector2 homePosition = Home + homeOffset;
            Vector2 screenHome = RectTransformUtility.WorldToScreenPoint(_camera,
                Rect.position + parent.TransformVector(homePosition - Rect.anchoredPosition));
            Vector2 screenNow = RectTransformUtility.WorldToScreenPoint(_camera, Rect.position);
            Vector2 shift = screenHome - screenNow;
            Rect.GetWorldCorners(_corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(_camera, _corners[0]) + shift;
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(_camera, _corners[2]) + shift;
            const float legacyZone = 36f;
            float zoneSize = bottomRevealEdge.HasValue ? legacyZone * _canvas.scaleFactor : legacyZone;
            float pointerFloor = bottomRevealEdge ?? float.NegativeInfinity;
            // Measure both hit targets at rest so the slide cannot catch or lose a stationary pointer.
            var shownBounds = new MenuLayout.PanelBounds(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
            var revealZone = _edge == Edge.Top
                ? new MenuLayout.PanelBounds(float.NegativeInfinity, Screen.height - zoneSize, topRight.x + zoneSize, float.PositiveInfinity)
                : new MenuLayout.PanelBounds(_edge == Edge.BottomLeft ? float.NegativeInfinity : bottomLeft.x - zoneSize,
                    pointerFloor, topRight.x + zoneSize, (bottomRevealEdge ?? 0f) + zoneSize);
            _pointerRevealed = active && !hidden && autoHide && MenuLayout.PointerRevealsPanel(
                _pointerRevealed, mouse.x, mouse.y, shownBounds, revealZone, 12f * _canvas.scaleFactor, pointerFloor);
            var selected = EventSystem.current?.currentSelectedGameObject;
            bool focused = selected != null && selected.transform.IsChildOf(Rect);
            bool shown = MenuLayout.Reveal(hidden, autoHide, gamepad, _pointerRevealed, focused, expanded);
            _progress = hidden ? 0f : MenuLayout.Slide(_progress, shown, Time.unscaledDeltaTime);

            // Convert from screen pixels back to canvas units so the panel clears the edge at any scale.
            Vector2 offscreen = _edge == Edge.Top
                ? new Vector2(0f, Screen.height - bottomLeft.y + 8f)
                : new Vector2(_edge == Edge.BottomLeft ? -legacyZone : 0f, -topRight.y - 8f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenHome, _camera, out Vector2 home);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenHome + offscreen, _camera, out Vector2 off);
            float eased = MenuLayout.Ease(_progress);
            Vector2 position = homePosition + (off - home) * (1f - eased);
            if (Rect.anchoredPosition != position) Rect.anchoredPosition = position;
            float alpha = hidden ? 0f : eased;
            if (fadeFloor.HasValue)
            {
                // Let Bottom bar tips slide as usual, fading out before they cross an occupied area.
                float floor = RectTransformUtility.WorldToScreenPoint(_camera,
                    parent.TransformPoint(new Vector3(home.x, fadeFloor.Value, 0f))).y;
                float clearance = bottomLeft.y - floor;
                float bottom = bottomLeft.y + offscreen.y * (1f - eased);
                alpha *= Mathf.Clamp01((bottom - floor) / Mathf.Max(0.1f, clearance * 0.5f));
            }
            if (_group.alpha != alpha) _group.alpha = alpha;
            bool available = active && !hidden && shown && _progress >= 0.98f;
            bool acceptsInput = available && !modal;
            if (_group.interactable != acceptsInput) _group.interactable = acceptsInput;
            if (_group.blocksRaycasts != acceptsInput) _group.blocksRaycasts = acceptsInput;
            bool changed = CanNavigate != available;
            CanNavigate = available;
            return changed;
        }
    }

    private readonly SlidingPanel _logo, _news, _tips;
    private readonly TitleMenuLayout _layout;
    private readonly CompactMenuNews _compact;
    private readonly Button[] _newsLinks, _tipButtons;
    private MenuNews? _lastNews;

    internal TitleMenuPanels(MenuSnapshot snapshot, RectTransform logo, RectTransform news, RectTransform tips, Text version,
        Vector2 newsPosition, TitleMenuLayout layout)
    {
        _layout = layout;
        _newsLinks = news.GetComponentsInChildren<Button>(true);
        _tipButtons = tips.GetComponentsInChildren<Button>(true);
        TitleMenu.Place(news, Vector2.zero, Vector2.zero, newsPosition, news.rect.size);
        _compact = new CompactMenuNews(layout, version, _newsLinks);
        _logo = new SlidingPanel(snapshot, logo, Edge.Top);
        _news = new SlidingPanel(snapshot, news, Edge.BottomLeft);
        _tips = new SlidingPanel(snapshot, tips, Edge.Bottom);
    }

    internal bool Tick(Vector2 mouse, bool gamepad, bool modal)
    {
        MenuNews news = MenuSettings.News.Value;
        bool compact = news == MenuNews.Compact;
        bool changed = _lastNews != news;
        if (changed)
        {
            _lastNews = news;
            _compact.SetVisible(compact);
        }

        MenuTips tips = MenuSettings.Tips.Value;
        changed |= _compact.Tick(mouse, modal);
        _logo.Tick(mouse, gamepad, modal, false, MenuSettings.Logo.Value == MenuLogo.AutoHide, false);
        float? bottomRevealEdge = _layout.BottomRevealEdge;
        float? newsFadeFloor = _layout.IsBar && news == MenuNews.AutoHide
            ? ((RectTransform)_news.Rect.parent).rect.yMin + BottomBar.Height
            : null;
        changed |= _news.Tick(mouse, gamepad, modal, compact, news == MenuNews.AutoHide, false,
            fadeFloor: newsFadeFloor, bottomRevealEdge: bottomRevealEdge);
        Button version = Version;
        Vector2 tipsOffset = _layout.TipsShift(_tips.Rect, _tips.Home, version != null ? (RectTransform)version.transform : null,
            out float? fadeFloor);
        changed |= _tips.Tick(mouse, gamepad, modal, tips == MenuTips.Hidden, tips == MenuTips.AutoHide, false,
            tipsOffset, fadeFloor, bottomRevealEdge);
        return changed;
    }

    internal bool OwnsFocus(Transform focus) => focus.IsChildOf(_news.Rect) || focus.IsChildOf(_tips.Rect);
    internal Button Version => _compact.Version;
    internal Button FirstCompactLink => _compact.FirstLink;
    internal void Close() => _compact.Close();
    internal GameObject HoverTarget(Vector2 mouse) => _compact.HoverTarget(mouse);
    internal void Highlight(GameObject target) => _compact.Highlight(target);
    internal void SetAccent(Color colour) => _compact.SetAccent(colour);

    internal void AddNavigation(List<Selectable> chain)
    {
        _compact.AddNavigation(chain);
        if (_news.CanNavigate) AddVisible(chain, _newsLinks);
        if (_tips.CanNavigate) AddVisible(chain, _tipButtons);
    }

    private static void AddVisible(List<Selectable> chain, Button[] buttons)
    {
        foreach (Button button in buttons)
            if (button != null && button.isActiveAndEnabled && button.interactable) chain.Add(button);
    }
}
