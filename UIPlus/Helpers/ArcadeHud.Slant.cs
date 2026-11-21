using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace UIPlus.Helpers;

// Wingman's perspective look. Project Wingman does not draw its HUD in real 3D: it rotates whole
// panels slightly around their outer bottom corner, so their inner edges rise toward the centre like
// a curved visor. Right-side panels (weapon cluster, HULL gauge) turn clockwise about their bottom-
// right corner; the map turns the other way about its bottom-left corner (see HudElementPositions).
// SPD / ALT are already tilted toward the aircraft; with the option on they are also italic.
internal static partial class ArcadeHud
{
    internal static ConfigEntry<bool> WingmanSlant;
    private static bool _builtSlant;
    private const float LeanDegrees = 4f;

    // Rotation for the map panel (degrees, about its bottom-left corner), 0 when not leaning.
    internal static float MapLeanDegrees =>
        _wingman && _builtSlant && _root != null && _root.activeSelf && !HudElementPositions.NuclearMinimapPresent ? LeanDegrees : 0f;

    private static void ApplySlant()
    {
        _builtSlant = WingmanSlant != null && WingmanSlant.Value;
        if (!_builtSlant) return;
        foreach (RectTransform group in new[] { _speed, _alt })
            if (group != null)
                foreach (TMP_Text text in group.GetComponentsInChildren<TMP_Text>(true))
                    text.fontStyle |= FontStyles.Italic;
        // Both are anchored and pivoted at their bottom-right corner, so this turns them about it.
        foreach (RectTransform group in new[] { _list, _bars })
            if (group != null) group.localRotation = Quaternion.Euler(0f, 0f, -LeanDegrees);
    }
}
