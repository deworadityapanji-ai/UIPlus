using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace UIPlus.Helpers;

/// <summary>
/// Every optional integration with another mod, in one list: its on/off setting, whether the mod is
/// installed, and whether UI Plus actually hooked into it (the working check). The UI PLUS settings
/// tab shows this list as its MOD COMPATIBILITY box.
/// </summary>
internal static class Integrations
{
    internal sealed class Info
    {
        internal string Name, Key, What;
        internal Func<bool> Installed, Working;
        internal ConfigEntry<bool> Enabled;
        internal bool On => Enabled == null || Enabled.Value;
    }

    internal static readonly List<Info> All = new();
    internal static Info ArcadeCam, Rwr, Reloads, Yappinator, NeutralQol, NoAutopilot, MissileIndicators;

    private static Func<bool> Plugin(string guid) => () => Chainloader.PluginInfos.ContainsKey(guid);

    internal static void Bind(ConfigFile config)
    {
        ArcadeCam = Add(config, "ArcadeCam", "ArcadeCamLink",
            "HIT / MISS / DESTROYED callouts, and the targeting screen hides during the weapon camera.",
            Plugin(ArcadeCamBridge.Guid), () => ArcadeCamBridge.Connected);
        Rwr = Add(config, "KaceyTronic RWR", "KaceyTronicRwr",
            "The RWR scope and its alerts follow the HUD style and the layout editor.",
            Plugin(RwrCompat.Guid), () => RwrCompat.Working);
        Reloads = Add(config, "Ace Combat Style Reloads", "AceCombatReloads",
            "Weapon reserves and reload progress on the HUD.",
            () => AccessTools.TypeByName("AceCombatReloads.Game.ReloadState") != null, () => ReloadsCompat.Working);
        Yappinator = Add(config, "WSO Yappinator", "YappinatorSubtitles",
            "Voice lines as subtitles (needs a dialogue file in the voice pack).",
            Plugin(YappinatorCompat.Guid), () => YappinatorCompat.Working);
        NeutralQol = Add(config, "NeutralQoL", "NeutralQolFuel",
            "Its fuel time steps aside while the Thunder style shows its own.",
            Plugin("com.devhrytsan.neutralqol"), Plugin("com.devhrytsan.neutralqol"));
        NoAutopilot = Add(config, "NOAutopilot", "NoAutopilotFuel",
            "Its fuel overlay steps aside while the Thunder style shows its own.",
            Plugin(NoAutopilotCompat.Guid), () => NoAutopilotCompat.Working);
        MissileIndicators = Add(config, "NO Missile Indicators", "MatchMissileIndicators",
            "Its style follows the HUD style, and its arrows step aside where UI Plus's missile arcs show.",
            Plugin(MissileIndicatorsCompat.Guid), () => MissileIndicatorsCompat.Working);
    }

    private static Info Add(ConfigFile config, string name, string key, string what, Func<bool> installed, Func<bool> working)
    {
        var info = new Info
        {
            Name = name, Key = key, What = what, Installed = installed, Working = working,
            Enabled = config.Bind("Compatibility", key, true, $"{name}: {what} Off leaves that mod alone."),
        };
        All.Add(info);
        return info;
    }

    /// <summary>Installed, switched on, and hooked in.</summary>
    internal static string Status(Info info)
    {
        bool installed;
        try { installed = info.Installed(); } catch { installed = false; }
        if (!installed) return "not installed";
        if (!info.On) return "off";
        bool working;
        try { working = info.Working(); } catch { working = false; }
        return working ? "working" : "installed, waiting (hooks in once flying)";
    }
}
