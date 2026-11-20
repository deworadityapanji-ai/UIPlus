using TMPro;
using UnityEngine;

namespace UIPlus.Helpers;

/// <summary>
/// High contrast: a soft dark halo behind a HUD text, as games do to keep HUD text readable over
/// bright sky without changing its look. The HUD text itself uses the game's additive font
/// material, which can only add light (a black shadow drawn with it is invisible), so the halo is
/// a separate copy just behind it, drawn with the font's normal material: black, thickened and
/// blurred, partly transparent. It follows its text every frame (content, size, position, fade).
/// </summary>
[DefaultExecutionOrder(10002)]   // after the HUD has written this frame's text
internal sealed class TextHalo : MonoBehaviour
{
    internal enum Strength { Off, Soft, Strong }

    private TextMeshProUGUI _source, _halo;
    private float _alpha;
    private Strength _strength;
    private static Material _soft, _strong;
    private static TMP_FontAsset _materialFont;

    /// <summary>Puts a halo behind a text (a sibling just before it, so it draws underneath).</summary>
    internal static void Add(TextMeshProUGUI source, Strength strength)
    {
        if (source == null || strength == Strength.Off || source.font == null) return;
        var go = new GameObject(source.name + " Halo", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(source.transform.parent, false);
        go.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
        var halo = go.GetComponent<TextMeshProUGUI>();
        halo.font = source.font;
        halo.fontSharedMaterial = HaloMaterial(source.font, strength);
        halo.raycastTarget = false;
        halo.overrideColorTags = true;   // all of it dark, whatever colours the text itself uses
        var follower = go.AddComponent<TextHalo>();
        follower._source = source;
        follower._halo = halo;
        follower._alpha = strength == Strength.Strong ? 0.8f : 0.55f;
        follower._strength = strength;
        follower.Follow();
    }

    // One material per strength: the font's own (normal blend) material, thickened and softened.
    private static Material HaloMaterial(TMP_FontAsset font, Strength strength)
    {
        if (_materialFont != font) { _soft = _strong = null; _materialFont = font; }
        ref Material cached = ref (strength == Strength.Strong ? ref _strong : ref _soft);
        if (cached != null) return cached;
        cached = new Material(font.material) { name = "UI Plus Halo " + strength };
        cached.SetFloat(ShaderUtilities.ID_FaceDilate, strength == Strength.Strong ? 0.45f : 0.3f);
        cached.SetFloat(ShaderUtilities.ID_OutlineSoftness, strength == Strength.Strong ? 0.45f : 0.6f);
        return cached;
    }

    private void LateUpdate() => Follow();

    private void Follow()
    {
        if (_source == null) { Destroy(gameObject); return; }
        bool show = _source.enabled && _source.gameObject.activeSelf && !string.IsNullOrEmpty(_source.text);
        if (_halo.enabled != show) _halo.enabled = show;
        if (!show) return;
        if (_halo.font != _source.font && _source.font != null)
        {
            _halo.font = _source.font;
            _halo.fontSharedMaterial = HaloMaterial(_source.font, _strength);
        }
        if (!ReferenceEquals(_halo.text, _source.text)) _halo.text = _source.text;
        if (_halo.fontSize != _source.fontSize) _halo.fontSize = _source.fontSize;
        if (_halo.alignment != _source.alignment) _halo.alignment = _source.alignment;
        if (_halo.fontStyle != _source.fontStyle) _halo.fontStyle = _source.fontStyle;
        _halo.characterSpacing = _source.characterSpacing;
        _halo.lineSpacing = _source.lineSpacing;
        _halo.enableWordWrapping = _source.enableWordWrapping;
        _halo.overflowMode = _source.overflowMode;
        _halo.richText = _source.richText;
        var color = new Color(0f, 0f, 0f, _alpha * _source.color.a * _source.alpha);
        if (_halo.color != color) _halo.color = color;
        RectTransform from = _source.rectTransform, to = _halo.rectTransform;
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.sizeDelta = from.sizeDelta;
        to.anchoredPosition3D = from.anchoredPosition3D;
        to.localRotation = from.localRotation;
        to.localScale = from.localScale;
        // Keep it directly behind its text (just before it among its siblings).
        if (to.parent == from.parent)
        {
            int halo = to.GetSiblingIndex(), text = from.GetSiblingIndex();
            if (halo != text - 1) to.SetSiblingIndex(halo < text ? text - 1 : text);
        }
    }
}
