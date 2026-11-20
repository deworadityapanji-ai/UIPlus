using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIPlus.Helpers;
using HarmonyLib;
using NuclearOption.Chat;

namespace UIPlus.Patches;

// Every game message (chat, server, mission text, HQ alerts, joins, kills...) ends up in
// GameplayUI.GameMessage. These patches only *observe*: they note which game method is sending,
// so the Arcade HUD can show each kind in its own place. The native chat log is unchanged.
[HarmonyPatch]
internal static class MessageSourcePatches
{
    private static IEnumerable<MethodBase> Targets()
    {
        // Mirage generates these names with a numeric suffix, so match on the prefix.
        foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(ChatManager)))
            if (m.Name.StartsWith("UserCode_TargetReceiveMessage")
                || m.Name.StartsWith("UserCode_RpcServerMessage")
                || m.Name.StartsWith("UserCode_RpcTargetServerMessage"))
                yield return m;
        MethodInfo mission = AccessTools.Method(typeof(MissionMessages), "ShowMessgeLocal");
        if (mission != null) yield return mission;
        MethodInfo hq = AccessTools.Method(typeof(MessageManager), "HQMessageInternal");
        if (hq != null) yield return hq;
    }

    private static bool Prepare() => Targets().Any();

    private static IEnumerable<MethodBase> TargetMethods() => Targets();

    private static void Prefix(MethodBase __originalMethod, object[] __args) =>
        ArcadeMessages.BeginSource(__originalMethod.Name, __args);

    private static Exception Finalizer(Exception __exception)
    {
        ArcadeMessages.EndSource();
        return __exception;
    }
}

// The kill feed message (killer, victim, victim type) drives the HUD's kill feed.
// Observed only; the native kill feed is unchanged.
[HarmonyPatch]
internal static class KillMessagePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(MessageManager)))
        {
            if (!m.Name.StartsWith("UserCode_RpcKillMessage")) continue;
            var p = m.GetParameters();
            if (p.Length == 3 && p[0].ParameterType == typeof(PersistentID) && p[1].ParameterType == typeof(PersistentID))
                yield return m;
        }
    }

    private static bool Prepare() => TargetMethods().Any();

    private static void Prefix(object[] __args) =>
        ArcadeHud.AddKill((PersistentID)__args[0], (PersistentID)__args[1], Convert.ToInt32(__args[2]));
}

[HarmonyPatch]
internal static class GameMessagePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        MethodInfo plain = AccessTools.Method(typeof(GameplayUI), "GameMessage", new[] { typeof(string) });
        MethodInfo delayed = AccessTools.Method(typeof(GameplayUI), "GameMessage", new[] { typeof(string), typeof(float) });
        if (plain != null) yield return plain;
        if (delayed != null) yield return delayed;
    }

    private static bool Prepare() => TargetMethods().Any();

    private static void Prefix(string message) => ArcadeMessages.OnGameMessage(message);
}
