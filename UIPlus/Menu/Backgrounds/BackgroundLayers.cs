using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIPlus.Menu;

internal sealed class BackgroundLayers : IDisposable
{
    private readonly Graphic _stock;
    private readonly Image _black;
    private readonly Color _tint;
    private GameObject _root;
    private readonly RawImage _front, _incoming;
    private bool _showing;

    internal BackgroundLayers(Graphic stock, Color tint)
    {
        _stock = stock;
        _tint = tint;
        _root = new GameObject("UI Plus menu background", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        try
        {
            _root.SetActive(false);
            _root.GetComponent<LayoutElement>().ignoreLayout = true;
            var backing = _root.GetComponent<Image>();
            backing.color = Color.black;
            backing.raycastTarget = false;
            Stretch(backing.rectTransform, stock.transform.parent);
            _root.transform.SetSiblingIndex(stock.transform.GetSiblingIndex() + 1);
            _front = MakeLayer("Current");
            _incoming = MakeLayer("Incoming");
            var fade = new GameObject("Fade through black", typeof(RectTransform), typeof(Image));
            _black = fade.GetComponent<Image>();
            _black.raycastTarget = false;
            _black.color = Color.clear;
            Stretch(_black.rectTransform, _root.transform);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Present(BackgroundMedia current, BackgroundMedia next, float incomingAlpha, float blackAlpha)
    {
        if (_root == null || _stock == null) return;
        if (current == null || !_stock.isActiveAndEnabled)
        {
            RestoreStock();
            return;
        }
        SetMedia(_front, current);
        if (next != null) SetMedia(_incoming, next);
        else _incoming.texture = null;
        _front.color = new Color(_tint.r, _tint.g, _tint.b, 1f);
        _incoming.color = new Color(_tint.r, _tint.g, _tint.b, incomingAlpha);
        _black.color = new Color(0f, 0f, 0f, blackAlpha);
        if (!_showing)
        {
            // Cover the stock graphic in its draw slot, preserving its state and its raycast behaviour.
            _showing = true;
            _root.SetActive(true);
        }
    }

    private void RestoreStock()
    {
        if (!_showing) return;
        _showing = false;
        if (_root != null) _root.SetActive(false);
        if (_front != null) _front.texture = null;
        if (_incoming != null) _incoming.texture = null;
    }

    private RawImage MakeLayer(string name)
    {
        var layer = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        Stretch(layer.rectTransform, _root.transform);
        layer.raycastTarget = false;
        return layer;
    }

    private static void SetMedia(RawImage layer, BackgroundMedia item)
    {
        Texture texture = item.Texture;
        if (layer.texture != texture) layer.texture = texture;
        var rect = CoverUv(item);
        if (layer.uvRect != rect) layer.uvRect = rect;
    }

    private static Rect CoverUv(BackgroundMedia item)
    {
        // VideoPlayer already crops the decoded picture into a screen-sized render texture.
        if (item.IsVideo) return new Rect(0f, 0f, 1f, 1f);
        var texture = item.Texture;
        var uv = BackgroundPlayback.CoverUv((float)texture.width / texture.height,
            (float)Screen.width / Mathf.Max(1, Screen.height));
        return new Rect(uv.X, uv.Y, uv.Width, uv.Height);
    }

    private static void Stretch(RectTransform rect, Transform parent)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void Dispose()
    {
        try { RestoreStock(); }
        finally
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }
    }
}
