using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIPlus.Helpers;
using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;

namespace UIPlus.Patches;

// Chat, system messages and mission text go through GameplayUI.GameMessage. Observe their
// sources so the custom HUD can route subtitles and banners without changing the native log.
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
            if (p.Length == 3 && p[0].ParameterType == typeof(PersistentID) && p[1].ParameterType == typeof(PersistentID)
                && p[2].ParameterType == typeof(KillType))
                yield return m;
        }
    }

    private static bool Prepare() => TargetMethods().Any();

    private static void Prefix(PersistentID __0, PersistentID __1, KillType __2) =>
        ArcadeHud.AddKill(__0, __1, __2);
}

// Observe the two session messages at their source so faction joins and chat stay separate.
[HarmonyPatch]
internal static class PlayerFeedMessagePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { nameof(MessageManager.JoinMessage), nameof(MessageManager.DisconnectedMessage) })
        {
            MethodInfo method = AccessTools.Method(typeof(MessageManager), name, new[] { typeof(Player) });
            if (method != null) yield return method;
        }
    }

    private static bool Prepare() => TargetMethods().Any();

    private static void Postfix(MethodBase __originalMethod, Player __0) =>
        ArcadeHud.AddFeedPlayer(__0, __originalMethod.Name == nameof(MessageManager.JoinMessage));
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
