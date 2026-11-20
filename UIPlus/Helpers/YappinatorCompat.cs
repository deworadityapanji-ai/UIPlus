using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace UIPlus.Helpers;

// Optional integration with WSO Yappinator (nikkorap): when it plays a voice line, show the
// line as a subtitle, from the voice pack's "*_dialogue.txt" files:
//     fox2_1.wav, Galaxy: Fox Two, good separation. [00:03]
// (file, speaker, text, duration). Voice lines are the lowest priority: chat and mission text
// replace them at once. Nothing is changed in Yappinator; its playback is only observed.
internal static class YappinatorCompat
{
    internal const string Guid = "com.nikkorap.WSOYappinator";
    internal static bool Working { get; private set; }
    private sealed class Line { internal string Speaker, Text; internal float Seconds; }

    // By the clip's own name (Yappinator names clips from their path), and by plain file name.
    private static readonly Dictionary<string, Line> ByClipName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Line> ByFile = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Duration = new(@"\[(\d+):(\d+)\]\s*$");
    private static bool _done;

    /// <summary>Called every frame until Yappinator is loaded (it loads after this mod).</summary>
    internal static void TryInitialize()
    {
        if (_done) return;
        if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info)) { _done = true; return; }
        if (info.Instance == null) return;   // not loaded yet
        _done = true;
        try
        {
            Type player = info.Instance.GetType().Assembly.GetType("WSOYappinator.AudioPlayer");
            MethodInfo tryPlay = player != null ? AccessTools.Method(player, "TryPlay") : null;
            if (tryPlay == null) { Plugin.Logger.LogWarning("WSO Yappinator found, but its audio player changed: no voice subtitles."); return; }
            int lines = Load(info.Location);
            new Harmony(MyPluginInfo.PLUGIN_GUID + ".yappinator").Patch(tryPlay,
                postfix: new HarmonyMethod(typeof(YappinatorCompat), nameof(Played)));
            Working = true;
            Plugin.Logger.LogInfo($"WSO Yappinator found: {lines} dialogue lines will show as subtitles.");
        }
        catch (Exception e) { Plugin.Logger.LogWarning("WSO Yappinator voice subtitles unavailable: " + e.Message); }
    }

    private static int Load(string location)
    {
        string root = Path.Combine(Path.GetDirectoryName(location) ?? "", "audio");
        if (!Directory.Exists(root)) return 0;
        int count = 0;
        foreach (string file in Directory.GetFiles(root, "*_dialogue.txt", SearchOption.AllDirectories))
        {
            string pack = Path.GetDirectoryName(file) ?? "";
            foreach (string raw in File.ReadAllLines(file))
            {
                int comma = raw.IndexOf(',');
                if (comma <= 0) continue;
                string name = raw.Substring(0, comma).Trim();
                string rest = raw.Substring(comma + 1).Trim();
                if (name.Length == 0 || rest.Length == 0) continue;
                var line = new Line();
                Match m = Duration.Match(rest);
                if (m.Success)
                {
                    line.Seconds = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                    rest = rest.Substring(0, m.Index).Trim();
                }
                int colon = rest.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0 && colon < 40) { line.Speaker = rest.Substring(0, colon).Trim(); rest = rest.Substring(colon + 2).Trim(); }
                line.Text = rest;
                if (line.Seconds <= 0f) line.Seconds = Mathf.Clamp(1.5f + rest.Length * 0.05f, 2f, 6f);
                // Yappinator names a clip path.TrimStart(<its DLL path's characters>); match that.
                string clipName = Path.Combine(pack, name).TrimStart(location.ToCharArray());
                ByClipName[clipName] = line;
                if (!ByFile.ContainsKey(name)) ByFile[name] = line;
                count++;
            }
        }
        return count;
    }

    private static void Played(AudioClip clip)
    {
        try
        {
            if (clip == null || string.IsNullOrEmpty(clip.name)) return;
            if (Integrations.Yappinator != null && !Integrations.Yappinator.On) return;
            if (!ByClipName.TryGetValue(clip.name, out Line line) && !ByFile.TryGetValue(Path.GetFileName(clip.name), out line)) return;
            ArcadeHud.QueueVoice(line.Speaker ?? "", line.Text, line.Seconds);
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Voice subtitle: " + e.Message); }
    }
}
