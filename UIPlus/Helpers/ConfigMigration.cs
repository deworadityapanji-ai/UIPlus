using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace UIPlus.Helpers;

/// <summary>
/// The first time UI Plus runs, copy the HUD settings it took over from ArcadeCam.cfg into
/// UIPlus.cfg, so styles, layouts and toggles carry over. Runs only while UIPlus.cfg does not
/// exist yet. ArcadeCam.cfg is only read, never changed; its old HUD keys simply go unused.
/// </summary>
internal static class ConfigMigration
{
    // Whole sections that moved.
    private static readonly HashSet<string> MovedSections = new(StringComparer.Ordinal)
        { "HUD Layout", "Minimap", "Targeting Screen" };
    // Keys that moved out of sections ArcadeCam keeps: (old section, key) → new section.
    private static readonly Dictionary<(string, string), string> MovedKeys = new()
    {
        { ("Chase HUD", "FlightHud"), "Chase HUD" },
        { ("Chase HUD", "Map"), "Chase HUD" },
        { ("Chase HUD", "PitchLadder"), "Chase HUD" },
        { ("Chase HUD", "Waterline"), "Chase HUD" },
        { ("Chase HUD", "LeftInstruments"), "Chase HUD" },
        { ("Chase HUD", "RightInstruments"), "Chase HUD" },
        { ("Chase HUD", "Compass"), "Chase HUD" },
        { ("Chase HUD", "VirtualJoystickIndicator"), "Chase HUD" },
        { ("Kill Cam", "KillCallouts"), "HUD Layout" },
    };

    internal static void ImportFromArcadeCam(ConfigFile config)
    {
        try
        {
            string target = config.ConfigFilePath;
            if (File.Exists(target)) return;
            string source = Path.Combine(Path.GetDirectoryName(target) ?? "", "ArcadeCam.cfg");
            if (!File.Exists(source)) return;

            var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string section = null;
            int count = 0;
            foreach (string raw in File.ReadAllLines(source))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int eq = line.IndexOf('=');
                if (section == null || eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string into = MovedSections.Contains(section) ? section
                    : MovedKeys.TryGetValue((section, key), out string moved) ? moved : null;
                if (into == null) continue;
                if (!sections.TryGetValue(into, out var lines)) sections[into] = lines = new List<string>();
                lines.Add(line);
                count++;
            }
            if (count == 0) return;

            var text = new StringBuilder();
            foreach (var pair in sections)
            {
                text.Append('[').Append(pair.Key).AppendLine("]");
                foreach (string line in pair.Value) text.AppendLine(line);
                text.AppendLine();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            File.WriteAllText(target, text.ToString());
            config.Reload();
            Plugin.Logger.LogInfo($"Imported {count} HUD settings from ArcadeCam.cfg.");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not import HUD settings from ArcadeCam.cfg: {e.Message}");
        }
    }
}
