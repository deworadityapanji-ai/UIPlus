using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UIPlus.Helpers;

// The game owns the extras and keeps refreshing their graphics even while UI Plus hides them.
internal static class PusherStockHud
{
    private static readonly FieldInfo Owner = AccessTools.Field(typeof(HUDAppManager), "aircraft");
    private static readonly FieldInfo Apps = AccessTools.Field(typeof(HUDAppManager), "apps");
    private static readonly FieldInfo GaugeOwner = AccessTools.Field(typeof(CustomAxis1Gauge), "aircraft");
    private static readonly FieldInfo HoverLine = AccessTools.Field(typeof(HoverIndicator), "indicatorLine");
    private static readonly FieldInfo HoverTip = AccessTools.Field(typeof(HoverIndicator), "indicatorLineTip");
    private static readonly Mask GaugeMask = new(), HoverMask = new();
    private static readonly List<CustomAxis1Gauge> Gauges = new();
    private static Aircraft _aircraft;
    private static HUDAppManager _manager;
    private static Transform _centre;
    private static CustomAxis1Gauge _gauge;
    private static PusherGauge _kind;
    private static (float Reverse, float Forward)? _band;
    private static HoverIndicator _hover;
    private static Transform _line, _tip, _positionedLine;
    private static Vector3 _stockPosition, _hoverOffset;
    private static float _retryAt, _retryUntil;
    private static int _managerChildren, _centreChildren;
    private static bool _managerReady;

    internal static PusherGauge GaugeConfiguration(Aircraft aircraft, out (float Reverse, float Forward)? band)
    {
        Resolve(aircraft);
        band = _band;
        return _kind;
    }

    // Camera attachment follows the same widget the replacing styles would mask.
    internal static CustomAxis1Gauge ReversibleGauge => _kind == PusherGauge.Reversible ? _gauge : null;

    private static void Resolve(Aircraft aircraft)
    {
        HUDAppManager manager = SceneSingleton<HUDAppManager>.i;
        FlightHud hud = SceneSingleton<FlightHud>.i;
        CombatHUD combat = SceneSingleton<CombatHUD>.i;
        Transform centre = hud != null && combat != null && combat.aircraft == aircraft ? hud.GetHUDCenter() : null;
        float now = Time.unscaledTime;
        bool rebound = !ReferenceEquals(aircraft, _aircraft) || !ReferenceEquals(manager, _manager) ||
            !ReferenceEquals(centre, _centre);
        if (rebound)
        {
            Restore();
            _aircraft = aircraft;
            _manager = manager;
            _centre = centre;
            _gauge = null;
            _kind = PusherGauge.Missing;
            _band = null;
            _hover = null;
            _line = _tip = null;
            _managerReady = false;
            // Other mods can add or initialize the flight HUD extras after spawn.
            _retryUntil = now + 16f;
        }

        // Start can bind the manager after the first frame. Ignore another aircraft's apps.
        bool managerReady = manager != null && (_managerReady || Owner?.GetValue(manager) is Aircraft owner && owner == aircraft);
        int managerChildren = manager != null ? manager.transform.childCount : 0;
        int centreChildren = centre != null ? centre.childCount : 0;
        bool changed = rebound || managerReady != _managerReady || managerChildren != _managerChildren ||
            centreChildren != _centreChildren || _gauge == null && !ReferenceEquals(_gauge, null) ||
            _hover == null && !ReferenceEquals(_hover, null);
        if (!PusherThrottle.ShouldRefreshGauge(changed, _kind, now >= _retryAt, now <= _retryUntil)) return;
        _retryAt = now + 1f;
        _managerReady = managerReady;
        _managerChildren = managerChildren;
        _centreChildren = centreChildren;

        CustomAxis1Gauge attached = _gauge;
        _gauge = null;
        _kind = PusherGauge.Missing;
        _band = null;
        _hover = null;
        if (managerReady)
        {
            if (Apps?.GetValue(manager) is HUDApp[] apps)
                foreach (HUDApp app in apps)
                {
                    if (app is CustomAxis1Gauge gauge) ConsiderGauge(gauge);
                    else if (app is HoverIndicator hover && hover != null) _hover = hover;
                }
            if (_hover == null) _hover = manager.GetComponentInChildren<HoverIndicator>(true);
        }
        if (_kind != PusherGauge.Reversible)
        {
            // Late extras can sit outside the singleton manager's registered app list.
            if (centre != null) FindGauges(centre, aircraft, GaugeOwner == null && managerReady);
            else if (managerReady) FindGauges(manager.transform, aircraft, true);
        }
        if (_kind != PusherGauge.Reversible && attached != null && HudElements.IsCameraAttached(attached.transform))
            ConsiderGauge(attached);
        _line = _hover != null ? HoverLine?.GetValue(_hover) as Transform : null;
        _tip = _hover != null ? HoverTip?.GetValue(_hover) as Transform : null;
    }

    private static void FindGauges(Transform root, Aircraft aircraft, bool ownerAlreadyChecked)
    {
        root.GetComponentsInChildren(true, Gauges);
        foreach (CustomAxis1Gauge gauge in Gauges)
        {
            if (gauge != null && (ownerAlreadyChecked ||
                GaugeOwner?.GetValue(gauge) is Aircraft gaugeAircraft && gaugeAircraft == aircraft))
                ConsiderGauge(gauge);
            if (_kind == PusherGauge.Reversible) break;
        }
        Gauges.Clear();
    }

    private static void ConsiderGauge(CustomAxis1Gauge gauge)
    {
        if (gauge == null || _kind == PusherGauge.Reversible) return;
        PusherGauge kind = PusherAircraft.ReadGauge(gauge, out var band);
        if (_kind != PusherGauge.Missing && kind != PusherGauge.Reversible) return;
        _gauge = gauge;
        _kind = kind;
        _band = band;
    }

    internal static void Sync(Aircraft aircraft, bool replaceThrottle, Transform centre, Canvas canvas, Camera camera)
    {
        Resolve(aircraft);
        if (Plugin.HoverDriftIndicator.Value)
        {
            HoverMask.Restore();
            PlaceHoverLine(centre, canvas, camera);
        }
        else
        {
            RestoreHoverPosition();
            HoverMask.Hide(_line != null ? _line.gameObject : _hover != null ? _hover.gameObject : null);
        }
        CustomAxis1Gauge gauge = ReversibleGauge;
        GaugeMask.Hide(replaceThrottle && gauge != null ? gauge.gameObject : null);
    }

    private static void PlaceHoverLine(Transform centre, Canvas canvas, Camera camera)
    {
        if (_line == null || _tip == null || centre == null || !_line.IsChildOf(centre) ||
            canvas.transform is not RectTransform rect || camera == null ||
            !HudElements.TryCanvasPoint(canvas, rect, camera.pixelRect.center, out Vector3 cameraCentre))
        {
            RestoreHoverPosition();
            return;
        }
        if (_positionedLine != _line)
        {
            RestoreHoverPosition();
            _positionedLine = _line;
            _stockPosition = _line.localPosition;
            _hoverOffset = HudElements.NativeLocalPoint(_line, centre);
        }
        Vector3 offset = HudCanvasMath.RelativeOffset(_hoverOffset, centre.lossyScale, rect.lossyScale);
        _line.position = rect.TransformPoint(cameraCentre + offset);
        // Stock Refresh still sets the length and enables the graphic. Reuse its direction
        // vector after any HUD-centre rotation has run, so roll can't tilt that vector.
        Vector3 drift = _tip.localPosition;
        _line.eulerAngles = new Vector3(0f, 0f, 180f - Mathf.Atan2(drift.x, drift.y) * Mathf.Rad2Deg);
    }

    private static void RestoreHoverPosition()
    {
        if (_positionedLine != null) _positionedLine.localPosition = _stockPosition;
        _positionedLine = null;
    }

    internal static void Restore()
    {
        GaugeMask.Restore();
        HoverMask.Restore();
        RestoreHoverPosition();
    }

    private sealed class Mask
    {
        private CanvasGroup _group;
        private bool _owned, _enabled;
        private float _alpha;

        internal void Hide(GameObject target)
        {
            if (_group != null && _group.gameObject != target) Restore();
            if (target == null) return;
            if (_group == null)
            {
                _group = target.GetComponent<CanvasGroup>();
                _owned = _group == null;
                if (_owned) _group = target.AddComponent<CanvasGroup>();
                _alpha = _group.alpha;
                _enabled = _group.enabled;
            }
            if (!_group.enabled) _group.enabled = true;
            if (_group.alpha != 0f) _group.alpha = 0f;
        }

        internal void Restore()
        {
            if (_group != null)
            {
                _group.alpha = _alpha;
                _group.enabled = _enabled;
                if (_owned) Object.Destroy(_group);
            }
            _group = null;
        }
    }
}
