using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UIPlus.Helpers;

internal static partial class HudElements
{
    private static readonly FieldInfo FuelReading = AccessTools.Field(typeof(FuelGauge), "fuelReading");
    private static readonly FieldInfo ThrottleReading = AccessTools.Field(typeof(ThrottleGauge), "throttleReading");
    private static readonly FieldInfo WeaponState = AccessTools.Field(typeof(CombatHUD), "weaponState");
    private static readonly FieldInfo MissileHint = AccessTools.Field(typeof(HUDMissileState), "hint");
    private static Component _nativeFuel, _nativeThrottle;
    private static HUDMissileState _missileState;
    private static readonly Upright FuelText = new(), ThrottleText = new(), RangeLadder = new(), RangeHint = new();

    private sealed class Upright
    {
        private Transform _node;
        private Quaternion _original, _applied;
        private bool _changed;

        internal void Bind(Transform node)
        {
            if (ReferenceEquals(_node, node)) return;
            Restore();
            _node = node;
        }

        internal void Apply(Quaternion rotation)
        {
            if (_node == null) return;
            Quaternion local = _node.localRotation;
            if (!_changed || local != _applied) _original = local;
            // Rotation about the widget's own pivot keeps its screen position and native anchors.
            if (_node.rotation != rotation) _node.rotation = rotation;
            _applied = _node.localRotation;
            _changed = true;
        }

        internal void Restore()
        {
            if (_changed && _node != null && _node.localRotation == _applied) _node.localRotation = _original;
            _changed = false;
            _node = null;
        }
    }

    private static void RestoreStockRoll()
    {
        FuelText.Restore();
        ThrottleText.Restore();
        RangeLadder.Restore();
        RangeHint.Restore();
        _missileState = null;
    }

    private static void CorrectStockRoll(Canvas canvas)
    {
        bool left = LeftMode == HudAttachment.Camera;
        bool right = RightMode == HudAttachment.Camera;
        if (!left && !right) return;
        Quaternion upright = canvas.transform.rotation;
        AlignLabel(_nativeFuel, FuelReading, FuelText, canvas, left, upright);
        AlignLabel(_nativeThrottle, ThrottleReading, ThrottleText, canvas, right, upright);

        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        var missile = hud != null ? WeaponState?.GetValue(hud) as HUDMissileState : null;
        if (missile == null) missile = null; // Discard Unity's destroyed references as well.
        if (!ReferenceEquals(missile, _missileState))
        {
            _missileState = missile;
            RangeLadder.Bind(missile != null ? missile.transform.Find("RangeLadder") : null);
            RangeHint.Bind(missile != null && MissileHint?.GetValue(missile) is Component hint ? hint.transform : null);
        }
        RangeLadder.Apply(upright);
        RangeHint.Apply(upright);
    }

    private static void AlignLabel(Component gauge, FieldInfo field, Upright pose, Canvas canvas, bool cameraAttached, Quaternion upright)
    {
        if (cameraAttached && gauge != null && gauge.transform.parent == canvas.transform &&
            field?.GetValue(gauge) is Component label)
        {
            pose.Bind(label.transform);
            pose.Apply(upright);
        }
        else pose.Restore();
    }
}
