using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NuclearOption.UIStyleSystem;

namespace UIPlus.Helpers;

// Ace Combat style combat callouts for the Arcade style: DESTROYED (your kill), HIT (your missile
// damaged its target) and MISS (it did not), with the target's name underneath.
internal static partial class ArcadeHud
{
    private static readonly Vector2 CalloutBase = new(0f, 150f);
    private const float CalloutSeconds = 1.6f;
    private static RectTransform _callout;
    private static CanvasGroup _calloutGroup;
    private static TextMeshProUGUI _calloutTitle, _calloutName;
    private static Image _calloutTop, _calloutBottom;
    private static float _calloutShown = -100f;
    private static bool _calloutAlert;

    private static void BuildCallout()
    {
        _callout = null;
        if (_builtStyle != HudStyle.Arcade && _builtStyle != HudStyles.Plus) return;
        _callout = Rect("Callout", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), CalloutBase, new Vector2(360f, 64f));
        _calloutGroup = _callout.gameObject.AddComponent<CanvasGroup>();
        _calloutGroup.alpha = 0f;
        _calloutTitle = Text("Title", _callout, _builtStyle == HudStyles.Plus ? 24 : 30, TextAlignmentOptions.Center);
        _calloutTitle.characterSpacing = 10f;
        _calloutTitle.fontStyle = FontStyles.Bold;
        Pin(_calloutTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(360f, 36f), new Vector2(0f, -4f));
        _calloutName = Text("Name", _callout, 14, TextAlignmentOptions.Center);
        _calloutName.characterSpacing = 4f;
        Pin(_calloutName.rectTransform, new Vector2(0.5f, 0f), new Vector2(360f, 18f), new Vector2(0f, 2f));
        _calloutTop = Line("Top", Vector2.zero);
        _calloutBottom = Line("Bottom", new Vector2(0f, -42f));
    }

    private static Image Line(string name, Vector2 at)
    {
        RectTransform line = Rect(name, _callout, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), at, new Vector2(240f, 2f));
        Fill(line, Green);
        return line.GetComponent<Image>();
    }

    /// <summary>Shows a callout on the Arcade and Plus HUDs. Ignored in the other styles.</summary>
    internal static void ShowCallout(string title, string name, bool alert)
    {
        if (_callout == null || _root == null || !_root.activeInHierarchy) return;
        _calloutTitle.text = title;
        _calloutName.text = string.IsNullOrEmpty(name) ? "" : name.ToUpperInvariant();
        _calloutAlert = alert;
        _calloutShown = Time.unscaledTime;
    }

    private static void UpdateCallout()
    {
        if (_callout == null) return;
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        Color colour = _calloutAlert ? (theme != null ? theme.Alert : new Color(1f, 0.25f, 0.2f)) : Green;
        _calloutTitle.color = _calloutName.color = _calloutTop.color = _calloutBottom.color = colour;
        if (HudLayoutEditor.IsOpen) { _calloutGroup.alpha = 0f; return; }
        float age = Time.unscaledTime - _calloutShown;
        // Pops in, holds, then fades over the last 0.3 s.
        _calloutGroup.alpha = age < 0f ? 0f : Mathf.Clamp01((CalloutSeconds - age) / 0.3f);
        float pop = 1f + 0.15f * Mathf.Clamp01(1f - age / 0.12f);
        _callout.localScale = new Vector3(pop, pop, 1f);
    }
}
