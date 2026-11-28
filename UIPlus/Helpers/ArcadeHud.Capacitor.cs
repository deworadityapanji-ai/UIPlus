using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

internal enum CapacitorDisplay { Text, Bar }

internal static partial class ArcadeHud
{
    internal static ConfigEntry<CapacitorDisplay> CapacitorDisplaySetting;
    private static bool WantCapacitorBar => CapacitorDisplaySetting?.Value == CapacitorDisplay.Bar;
    private static bool _builtCapacitorBar;
    private static TextMeshProUGUI _capLine;
    private static string _capText = "", _capListText = "";
    private static int _capPercent = -1;
    private static AccessTools.FieldRef<PowerSupply, float> _maxCharge;
    private static PowerSupply _capSupply;
    private static float _capCurrent, _capMaximum, _capSampleTime, _capSampleEnergy, _capRate;
    private static CapacitorBar _weaponCap, _dataCap;

    private sealed class CapacitorBar
    {
        internal RectTransform Rect, Fill;
        internal TextMeshProUGUI Amount;
        internal Image Direction;
        internal int Current = -1, Maximum = -1, LastDirection;
    }

    private static CapacitorBar BuildCapacitorBar(RectTransform parent, Vector2 anchor, Vector2 at,
        float width, float height, float fontSize)
    {
        RectTransform row = Rect("Capacitor", parent, anchor, anchor, at, new Vector2(width, height));
        // Use the same plain track and inset HUD-coloured fill as Wingman's horizontal gauges.
        float trackHeight = height >= 24f ? 10f : 5f;
        RectTransform track = Rect("Track", row, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(width, trackHeight));
        Fill(track, Faded(0.18f));
        RectTransform fill = Rect("Fill", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(0f, trackHeight * 0.6f));
        Fill(fill, Faded(0.85f));

        TextMeshProUGUI amount = Text("Energy", row, fontSize, TextAlignmentOptions.Left);
        float arrowSize = fontSize * 0.65f;
        float labelHeight = height - trackHeight;
        Pin(amount.rectTransform, new Vector2(0f, 1f), new Vector2(width - arrowSize - 3f, labelHeight), Vector2.zero);
        amount.enableAutoSizing = true;
        amount.fontSizeMax = fontSize;
        amount.fontSizeMin = fontSize * 0.7f;
        amount.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform arrow = Rect("Charge direction", row, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(-arrowSize * 0.5f, -labelHeight * 0.5f), new Vector2(arrowSize, arrowSize));
        Image direction = Picture(arrow, Triangle());
        direction.color = Green;
        direction.enabled = false;
        row.gameObject.SetActive(false);
        return new CapacitorBar { Rect = row, Fill = fill, Amount = amount, Direction = direction };
    }

    private static void SampleCapacitor(Aircraft aircraft)
    {
        PowerSupply supply = aircraft.GetPowerSupply();
        _capCurrent = _capMaximum = 0f;
        if (supply == null || !HasCapacitor())
        {
            _capSupply = null;
            _capText = _capListText = "";
            _capPercent = -1;
            return;
        }

        if (!_builtCapacitorBar)
        {
            _capSupply = null;
            float percent = 100f * supply.GetCharge();
            int rounded = CapacitorReadout.WholeKj(percent);
            if (_capPercent != rounded)
            {
                _capPercent = rounded;
                _capText = $"CAP {percent:F0}%";
                _capListText = $"CAP   {percent:F0}%";
            }
            return;
        }

        // GetChargeKJ is the native readout's value. Read the actual capacity, even at zero charge.
        _maxCharge ??= AccessTools.FieldRefAccess<PowerSupply, float>("maxCharge");
        _capMaximum = _maxCharge(supply);
        _capCurrent = supply.GetChargeKJ();
        float now = Time.fixedTime;
        float seconds = now - _capSampleTime;
        if (_capSupply != supply || seconds < 0f || seconds > 1f)
        {
            _capSupply = supply;
            _capRate = 0f;
        }
        else
        {
            // Charge changes in physics steps. Sample several steps together before smoothing.
            if (seconds < 0.1f) return;
            _capRate = CapacitorReadout.SmoothRate(_capRate, _capCurrent - _capSampleEnergy, seconds);
        }
        _capSampleTime = now;
        _capSampleEnergy = _capCurrent;
    }

    private static bool ShowCapacitor(CapacitorBar bar)
    {
        if (bar == null) return false;
        bool visible = _capMaximum > 0f;
        if (bar.Rect.gameObject.activeSelf != visible) bar.Rect.gameObject.SetActive(visible);
        if (!visible) return false;

        float width = bar.Rect.sizeDelta.x * Mathf.Clamp01(_capCurrent / _capMaximum);
        Vector2 fillSize = bar.Fill.sizeDelta;
        if (fillSize.x != width) bar.Fill.sizeDelta = new Vector2(width, fillSize.y);
        int current = CapacitorReadout.WholeKj(_capCurrent);
        int maximum = CapacitorReadout.WholeKj(_capMaximum);
        if (bar.Current != current || bar.Maximum != maximum)
        {
            bar.Current = current;
            bar.Maximum = maximum;
            bar.Amount.text = $"CAP {current} / {maximum} kJ";
        }
        int direction = CapacitorReadout.Direction(_capRate);
        if (bar.LastDirection != direction)
        {
            bar.LastDirection = direction;
            bar.Direction.enabled = direction != 0;
            bar.Direction.rectTransform.localRotation = Quaternion.Euler(0f, 0f, direction < 0 ? 180f : 0f);
        }
        return true;
    }

    private static string AppendCapacitorText(string countermeasure, string separator)
    {
        if (_builtCapacitorBar || _capText.Length == 0) return countermeasure;
        return countermeasure.Length == 0 ? _capText : countermeasure + separator + _capText;
    }
}
