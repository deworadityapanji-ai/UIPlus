using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIPlus.Helpers;

internal static class PusherAircraft
{
    private static readonly FieldInfo NegativeBar = AccessTools.Field(typeof(CustomAxis1Gauge), "negativeBar");
    private static readonly FieldInfo PositiveBar = AccessTools.Field(typeof(CustomAxis1Gauge), "positiveBar");
    private static readonly FieldInfo Split = AccessTools.Field(typeof(CustomAxis1Gauge), "axisSplitPosition");
    private static readonly FieldInfo IdleZone = AccessTools.Field(typeof(CustomAxis1Gauge), "idleZone");
    private static readonly FieldInfo RotorShaft = AccessTools.Field(typeof(DuctedFan), "rotorShaft");
    private static readonly FieldInfo Reverse = AccessTools.Field(typeof(DuctedFan), "idleThresholdNegative");
    private static readonly FieldInfo Forward = AccessTools.Field(typeof(DuctedFan), "idleThresholdPositive");
    private static readonly ConditionalWeakTable<Aircraft, Detection> Detections = new();

    private sealed class Detection
    {
        internal PusherGauge Kind;
        internal PusherSource Source;
        internal (float Reverse, float Forward)? GaugeBand, EngineBand;
        internal int EngineCount = -1;
        internal bool Logged, SeenPusher;
        internal readonly float FirstSeen = Time.unscaledTime;
        internal int MissingFrame = -1;

        internal (float Reverse, float Forward) Band => GaugeBand ?? EngineBand
            ?? (PusherThrottle.DefaultReverse, PusherThrottle.DefaultForward);
    }

    internal static bool HasPusher(Aircraft aircraft) => aircraft != null && Detect(aircraft).Source != PusherSource.None;

    internal static float Value(Aircraft aircraft)
    {
        var detected = Detect(aircraft);
        if (detected.Source == PusherSource.None) return 0f;
        var (reverse, forward) = detected.Band;
        return PusherThrottle.Signed(aircraft.GetInputs().customAxis1, reverse, forward);
    }

    private static Detection Detect(Aircraft aircraft)
    {
        var detected = Detections.GetValue(aircraft, _ => new Detection());
        detected.Kind = PusherStockHud.GaugeConfiguration(aircraft, out detected.GaugeBand);
        if (detected.Kind != PusherGauge.SingleSided && !detected.GaugeBand.HasValue &&
            !detected.EngineBand.HasValue && detected.EngineCount != aircraft.engines.Count)
        {
            detected.EngineCount = aircraft.engines.Count;
            detected.EngineBand = FindEngineBand(aircraft);
        }
        detected.Source = PusherThrottle.Source(detected.Kind, detected.EngineBand.HasValue);

        if (detected.Source == PusherSource.None && detected.MissingFrame < 0)
            detected.MissingFrame = Time.frameCount;
        if (!detected.SeenPusher && detected.Source != PusherSource.None)
        {
            detected.SeenPusher = true;
            if (detected.MissingFrame >= 0 && detected.MissingFrame < Time.frameCount)
            {
                detected.Logged = true;
                string name = aircraft.definition != null ? aircraft.definition.code : aircraft.name;
                string source = detected.Source == PusherSource.Gauge ? "gauge" : "engine";
                Plugin.Logger.LogInfo($"UI Plus pusher: {name} detected late via {source}");
            }
        }
        // Give the HUD manager time to bind before logging an engine-only result.
        if (!detected.Logged && (detected.Kind != PusherGauge.Missing ||
            detected.Source == PusherSource.Engine && Time.unscaledTime - detected.FirstSeen >= 2f))
        {
            detected.Logged = true;
            string name = aircraft.definition != null ? aircraft.definition.code : aircraft.name;
            var (reverse, forward) = detected.Band;
            string source = detected.Source == PusherSource.Gauge ? "gauge" : "engine";
            Plugin.Logger.LogInfo(detected.Kind == PusherGauge.SingleSided
                ? $"UI Plus pusher: {name} has custom-axis gauge but not reversible"
                : FormattableString.Invariant($"UI Plus pusher: {name} detected via {source}, band {reverse:0.###}-{forward:0.###}"));
        }
        return detected;
    }

    internal static PusherGauge ReadGauge(CustomAxis1Gauge gauge, out (float Reverse, float Forward)? band)
    {
        band = null;
        if (gauge == null) return PusherGauge.Missing;
        Image negative = NegativeBar?.GetValue(gauge) as Image;
        Image positive = PositiveBar?.GetValue(gauge) as Image;
        float? split = Split?.GetValue(gauge) as float?;
        float? idle = IdleZone?.GetValue(gauge) as float?;
        PusherGauge kind = PusherThrottle.Gauge(true, negative != null && positive != null && negative != positive, split, idle);
        if (kind == PusherGauge.Reversible && split.HasValue && idle.HasValue)
        {
            float reverse = split.Value - idle.Value, forward = split.Value + idle.Value;
            if (PusherThrottle.ValidBand(reverse, forward)) band = (reverse, forward);
        }
        return kind;
    }

    private static (float Reverse, float Forward)? FindEngineBand(Aircraft aircraft)
    {
        if (RotorShaft == null || Reverse == null || Forward == null) return null;
        // The local physics rig can unparent the fans' bodies, but the engine list stays with the aircraft.
        foreach (IEngine engine in aircraft.engines)
            if (engine is DuctedFan fan && ReadFan(fan) is { } band) return band;
        foreach (DuctedFan fan in aircraft.GetComponentsInChildren<DuctedFan>(true))
            if (ReadFan(fan) is { } band) return band;
        return null;
    }

    private static (float Reverse, float Forward)? ReadFan(DuctedFan fan)
    {
        // Shaft-driven tail fans use yaw instead of the custom axis.
        if (fan == null || RotorShaft.GetValue(fan) as Object != null ||
            Reverse.GetValue(fan) is not float reverse || Forward.GetValue(fan) is not float forward ||
            !PusherThrottle.ValidBand(reverse, forward)) return null;
        return (reverse, forward);
    }
}
