using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace UIPlus.Helpers;

public enum WeaponPopupMode { WingmanOnly, AllStyles, Off }

// Project Wingman style weapon pop-up: switching weapons briefly shows
//     WEAPON SYSTEM
//     [[ AAM-29 ]]
//     FULL WEAPON NAME
// at the top centre, then fades. On by default in Wingman, optional in the other custom styles.
internal static partial class ArcadeHud
{
    internal static ConfigEntry<WeaponPopupMode> WeaponPopup;
    internal static ConfigEntry<bool> DashboardDockMap;
    private const float PopupHold = 2.2f, PopupFade = 0.4f;
    private static RectTransform _popup;
    private static CanvasGroup _popupGroup;
    private static TextMeshProUGUI _popupHeader, _popupName, _popupFull;
    private static WeaponStation _popupStation;
    private static bool _popupPrimed;
    private static float _popupShown = -100f;

    private static bool PopupWanted => WeaponPopup == null
        ? _wingman
        : WeaponPopup.Value == WeaponPopupMode.AllStyles || (WeaponPopup.Value == WeaponPopupMode.WingmanOnly && _wingman);

    private static void BuildWeaponPopup()
    {
        _popup = null;
        _popupStation = null;
        _popupPrimed = false;
        if (!PopupWanted) return;
        // Screen centre, just above the status tags (GEAR, BRAKE...) under the aircraft.
        _popup = Rect("WeaponPopup", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
            new Vector2(0f, StatusBase.y + 22f), new Vector2(600f, 76f));
        _popupGroup = _popup.gameObject.AddComponent<CanvasGroup>();
        _popupGroup.alpha = 0f;
        TextMeshProUGUI header = _popupHeader = Text("Header", _popup, 12, TextAlignmentOptions.Center);
        header.text = "WEAPON SYSTEM";
        header.characterSpacing = 6f;
        Pin(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(600f, 16f), Vector2.zero);
        _popupName = Text("Name", _popup, 30, TextAlignmentOptions.Center);
        _popupName.fontStyle = FontStyles.Bold;
        _popupName.characterSpacing = 4f;
        Pin(_popupName.rectTransform, new Vector2(0.5f, 1f), new Vector2(600f, 36f), new Vector2(0f, -16f));
        _popupFull = Text("Full", _popup, 15, TextAlignmentOptions.Center);
        _popupFull.characterSpacing = 3f;
        Pin(_popupFull.rectTransform, new Vector2(0.5f, 1f), new Vector2(600f, 20f), new Vector2(0f, -54f));
        foreach (TextMeshProUGUI t in new[] { header, _popupName, _popupFull }) AddShadow(t);
    }

    private static void UpdateWeaponPopup(Aircraft aircraft)
    {
        if (_popup == null) return;
        WeaponStation current = aircraft.weaponManager != null ? aircraft.weaponManager.currentWeaponStation : null;
        if (current != _popupStation)
        {
            // Not on spawn: only when the player actually switches.
            if (_popupPrimed && current != null && current.WeaponInfo != null)
            {
                _popupName.text = $"[[ {ShortName(current)} ]]";
                string full = current.WeaponInfo.weaponName ?? "";
                _popupFull.text = full.Length > 0 && full != ShortName(current) ? full.ToUpperInvariant() : "";
                _popupShown = Time.unscaledTime;
            }
            _popupStation = current;
            _popupPrimed = true;
        }
        float age = Time.unscaledTime - _popupShown;
        if (HudLayoutEditor.IsOpen) { _popupGroup.alpha = 0f; return; }
        _popupGroup.alpha = age < 0f ? 0f : Mathf.Clamp01((PopupHold + PopupFade - age) / PopupFade) * Mathf.Clamp01(age / 0.08f);
        float pop = 1f + 0.08f * Mathf.Clamp01(1f - age / 0.15f);
        _popup.localScale = new Vector3(pop, pop, 1f);
        _popupHeader.color = Faded(0.8f);
        _popupName.color = _popupFull.color = Green;
    }
}
