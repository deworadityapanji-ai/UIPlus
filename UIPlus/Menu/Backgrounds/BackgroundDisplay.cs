using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UIPlus.Menu;

// Each display borrows the host's textures. All of its UI belongs to the active menu scene.
internal sealed class BackgroundDisplay : IDisposable
{
    private const float SearchInterval = 0.5f, SearchDuration = 3f;
    private readonly List<GameObject> _roots = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly HashSet<string> _logged = new();
    private Graphic _stock, _picker;
    private BackgroundLayers _mainLayers, _pickerLayers;
    private float _nextSearch, _searchUntil;
    private bool _searchStarted;

    internal void Present(Scene scene, BackgroundMedia current, BackgroundMedia next, float incoming, float black)
    {
        if (current != null && _stock == null) TryFindBackdrops(scene);

        Color tint = _stock != null ? _stock.color : Color.white;
        PresentSlot(scene, _stock, ref _mainLayers, tint, current, next, incoming, black);
        // The multiplayer picker puts an almost opaque modal scrim over the browser's art.
        // Draw in that slot too, with the same tint as the browser backdrop and no input handling.
        PresentSlot(scene, _picker, ref _pickerLayers, tint, current, next, incoming, black);
    }

    private void TryFindBackdrops(Scene scene)
    {
        float now = Time.unscaledTime;
        if (!_searchStarted)
        {
            _searchStarted = true;
            _searchUntil = now + SearchDuration;
        }
        if (now < _nextSearch || now >= _searchUntil || !scene.isLoaded) return;
        _nextSearch = now + SearchInterval;
        _mainLayers?.Dispose();
        _mainLayers = null;
        FindBackdrops(scene);
    }

    private void PresentSlot(Scene scene, Graphic stock, ref BackgroundLayers layers, Color tint,
        BackgroundMedia current, BackgroundMedia next, float incoming, float black)
    {
        if (layers == null && current != null && stock != null && stock.isActiveAndEnabled)
        {
            layers = new BackgroundLayers(stock, tint);
            string screen = stock.transform.parent.name;
            if (_logged.Add(scene.name + "/" + screen))
                Plugin.Logger.LogInfo($"UI Plus background: {scene.name}/{screen} uses '{stock.name}' as its backdrop.");
        }
        layers?.Present(current, next, incoming, black);
    }

    private void FindBackdrops(Scene scene)
    {
        using var timing = new UiTiming("menu backdrop discovery");
        string knownPath = scene.name switch
        {
            "MainMenu" => "Prejoin menu/background",
            "MissionsMenu" => "background",
            "MultiplayerMenu" => "BackgroundImage",
            _ => null,
        };
        int rootCount = scene.rootCount;
        if (_roots.Capacity < rootCount) _roots.Capacity = rootCount;
        scene.GetRootGameObjects(_roots);
        try
        {
            if (knownPath != null)
            {
                foreach (var root in _roots)
                {
                    if (root.name != "MainCanvas") continue;
                    _stock = GetBackdrop(root.transform.Find(knownPath));
                    if (scene.name == "MultiplayerMenu")
                        _picker = GetBackdrop(root.transform.Find("Mission Picker/background"));
                    if (_stock != null) return;
                }
            }

            // Only consider the back of a screen-space canvas. A fullscreen modal or a UI
            // widget deeper in the hierarchy can safely keep its own art.
            int order = int.MaxValue;
            foreach (var root in _roots)
            {
                var canvas = root.GetComponent<Canvas>();
                if (canvas == null || !canvas.isRootCanvas || !canvas.isActiveAndEnabled ||
                    canvas.renderMode == RenderMode.WorldSpace || canvas.sortingOrder >= order) continue;
                var candidate = AtBack(canvas);
                if (candidate == null) continue;
                _stock = candidate;
                order = canvas.sortingOrder;
            }
        }
        finally { _roots.Clear(); }
    }

    private Graphic AtBack(Canvas canvas)
    {
        Transform child = canvas.transform;
        for (int depth = 0; depth < 3 && child.childCount != 0; depth++)
        {
            child = child.GetChild(0);
            if (!child.gameObject.activeInHierarchy || child.GetComponent<Selectable>() != null) return null;
            var graphic = GetBackdrop(child);
            if (graphic == null) continue;
            bool textured = graphic is Image image ? image.overrideSprite != null : ((RawImage)graphic).texture != null;
            if (textured && graphic.isActiveAndEnabled && graphic.color.a >= 0.99f &&
                CoversCanvas(graphic.rectTransform, canvas) && CoversCanvas(child.parent as RectTransform, canvas))
                return graphic;
            return null;
        }
        return null;
    }

    private bool CoversCanvas(RectTransform rect, Canvas canvas)
    {
        if (rect == null) return false;
        var viewport = canvas.pixelRect;
        if (viewport.width < Screen.width * 0.99f || viewport.height < Screen.height * 0.99f) return false;
        rect.GetWorldCorners(_corners);
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, _corners[2]);
        return bottomLeft.x <= viewport.xMin + 2f && bottomLeft.y <= viewport.yMin + 2f &&
            topRight.x >= viewport.xMax - 2f && topRight.y >= viewport.yMax - 2f;
    }

    private static Graphic GetBackdrop(Transform slot) =>
        slot == null ? null : (Graphic)slot.GetComponent<Image>() ?? slot.GetComponent<RawImage>();

    public void Dispose()
    {
        try { _mainLayers?.Dispose(); }
        finally
        {
            _mainLayers = null;
            try { _pickerLayers?.Dispose(); }
            finally
            {
                _pickerLayers = null;
                _stock = _picker = null;
                _nextSearch = 0f;
                _searchStarted = false;
            }
        }
    }
}
