using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;

namespace UIPlus.Helpers;

/// <summary>
/// Per-element looks set in the HUD editor, kept per style like positions: size (one uniform
/// scale), rotation (degrees, counter-clockwise), whether the element is hidden, and its skin
/// (0 = the style's own look). Saved as one line per style, for example
/// <c>ArcadeElementLooks = 8:1.2,0,0,0;17:1,0,0,2</c> (id:scale,rotation,hidden,skin), listing only
/// elements changed from the default.
/// </summary>
internal static class ElementLooks
{
    internal struct Look
    {
        public float Scale;
        public float Rotation;
        public bool Hidden;
        public int Skin;
        public bool IsDefault => !Hidden && Skin == 0 && Rotation == 0f && UnityEngine.Mathf.Approximately(Scale, 1f);
    }

    internal static Look Default => new() { Scale = 1f };

    /// <summary>
    /// An element from another style that this style can show as an extra: off until added in the
    /// editor (Arcade's fuel and throttle tapes outside Arcade).
    /// </summary>
    internal static bool IsExtra(HudStyle style, int id) =>
        ((id == HudElementPositions.FuelTape || id == HudElementPositions.ThrottleTape) && style != HudStyle.Arcade)
        || (id == HudElementPositions.WeaponWheel)
        || (id == HudElementPositions.MissileArcs && style != HudStyle.Arcade)
        || (id == HudElementPositions.Bars && style != HudStyle.Wingman && style != HudStyle.Native && style != HudStyles.Plus)
        || ((id == HudElementPositions.DataBlock || id == HudElementPositions.TopBar)
            && style != HudStyle.Thunder && style != HudStyle.Native && style != HudStyles.Plus);


    internal static Look DefaultFor(HudStyle style, int id) => new() { Scale = 1f, Hidden = IsExtra(style, id) };

    private static bool Same(Look a, Look b) =>
        a.Hidden == b.Hidden && a.Skin == b.Skin && a.Rotation == b.Rotation && UnityEngine.Mathf.Approximately(a.Scale, b.Scale);

    private static readonly HudStyle[] Styles =
        { HudStyle.Native, HudStyle.Arcade, HudStyle.Wingman, HudStyle.Dashboard, HudStyle.Thunder, HudStyles.Plus };
    private static readonly Dictionary<HudStyle, ConfigEntry<string>> Entries = new();
    private static readonly Dictionary<HudStyle, Look[]> Parsed = new();

    // Not listed in Configuration Manager: the editor is the way to set these.
    private sealed class ConfigurationManagerAttributes { public bool? Browsable = false; }

    // Same prefixes as the position keys (Dashboard keeps its old "Minimal" name).
    private static string Prefix(HudStyle style) => style switch
    {
        HudStyle.Arcade => "Arcade",
        HudStyle.Wingman => "Wingman",
        HudStyle.Dashboard => "Minimal",
        HudStyle.Thunder => "Thunder",
        HudStyles.Plus => "Plus",
        _ => "",
    };

    internal static void Bind(ConfigFile config)
    {
        foreach (HudStyle style in Styles)
        {
            ConfigEntry<string> entry = config.Bind("HUD Layout", Prefix(style) + "ElementLooks", "",
                new ConfigDescription("Size, rotation and visibility of each HUD element in this style. Set with the HUD editor.",
                    null, new ConfigurationManagerAttributes()));
            Entries[style] = entry;
            Parsed[style] = Parse(entry.Value, style);
            HudStyle captured = style;
            entry.SettingChanged += (_, _) => Parsed[captured] = Parse(entry.Value, captured);
        }
    }

    /// <summary>A copy of the saved looks for a style (every element, defaults filled in).</summary>
    internal static Look[] Saved(HudStyle style)
    {
        var copy = new Look[HudElementPositions.Count];
        if (Parsed.TryGetValue(style, out Look[] saved)) System.Array.Copy(saved, copy, copy.Length);
        else for (int i = 0; i < copy.Length; i++) copy[i] = DefaultFor(style, i);
        return copy;
    }

    internal static void Save(HudStyle style, Look[] looks)
    {
        if (Entries.TryGetValue(style, out ConfigEntry<string> entry)) entry.Value = Format(looks, style);
    }

    /// <summary>The look in force: the editor's unsaved one while it is open, else the saved one.</summary>
    internal static Look Get(int id)
    {
        if (id <= 0 || id >= HudElementPositions.Count) return Default;
        if (HudLayoutEditor.IsOpen) return HudLayoutEditor.GetLook(id);
        HudStyle style = HudElementPositions.Style != null ? HudElementPositions.Style.Value : HudStyle.Native;
        return Parsed.TryGetValue(style, out Look[] looks) ? looks[id] : DefaultFor(style, id);
    }

    /// <summary>The size to draw at: 0 when hidden (which hides it without touching other code's visibility).</summary>
    internal static float ScaleFactor(int id)
    {
        Look look = Get(id);
        return look.Hidden ? 0f : look.Scale;
    }

    // ---- skins: other looks an element can take; the first is always the style's own ----

    private static readonly string[] MapSkins = { "Style default", "Game corner art", "Plain box" };
    private static readonly string[] BannerSkins = { "Style default", "Panel", "Outline", "Cut-out" };
    private static readonly string[] AlertSkins = { "Style default", "One row", "2x2 grid" };
    private static readonly string[] ReadoutSkins = { "Style default", "Arcade box", "Wingman" };
    private static readonly string[] AlignSkins = { "Left", "Centre", "Right" };
    private static readonly string[] ListSkins = { "Style default", "Arcade list", "Wingman cluster" };
    private static readonly string[] WheelSkins = { "Game radial menu", "Wingman star" };

    /// <summary>The weapon display is Wingman's cluster (which carries its own star) in this style.</summary>
    internal static bool WingmanClusterIn(HudStyle style)
    {
        if (style == HudStyle.Dashboard || style == HudStyle.Thunder) return false;
        int skin = SkinOf(HudElementPositions.WeaponList);
        return skin == 2 || (skin == 0 && style == HudStyle.Wingman);
    }

    private static readonly string[] FeedSkins = { "Style default", "Left", "Centre", "Right" };
    private static readonly string[] DamageSkins = { "Style default", "Silhouette", "Arcade (with weapons)", "Native" };
    private static readonly string[] TargetSkins = { "Style default", "Corner brackets", "Box", "Feed only" };

    /// <summary>The skin names for an element, or null if it has only its style's look.</summary>
    internal static string[] Skins(int id) => id switch
    {
        HudElementPositions.Map => MapSkins,
        HudElementPositions.Banner => BannerSkins,
        HudElementPositions.RwrAlerts => AlertSkins,
        HudElementPositions.SpeedBlock or HudElementPositions.AltBlock => ReadoutSkins,
        HudElementPositions.DataBlock => AlignSkins,
        HudElementPositions.KillFeed => FeedSkins,
        HudElementPositions.WeaponList => ListSkins,
        HudElementPositions.WeaponWheel => WheelSkins,
        HudElementPositions.Damage => DamageSkins,
        HudElementPositions.TargetScreen => TargetSkins,
        _ => null,
    };

    internal static string SkinNote(int id, int skin) => (id, skin) switch
    {
        (HudElementPositions.Map, 0) => "The game's slanted corner art, or a plain box once moved (Plain map backing setting).",
        (HudElementPositions.Map, 1) => "Always the game's slanted corner art, even when moved.",
        (HudElementPositions.Map, 2) => "Always a plain dark box sized to the map.",
        (HudElementPositions.Banner, 0) => "Wingman: outline. Other styles: panel.",
        (HudElementPositions.Banner, 1) => "A see-through panel in the HUD colour, dark text.",
        (HudElementPositions.Banner, 2) => "A thin box in the HUD colour, text in the HUD colour.",
        (HudElementPositions.Banner, 3) => "A solid panel with the letters cut out of it.",
        (HudElementPositions.RwrAlerts, 0) => "Arcade and Wingman: one row above the map. Dashboard: a column beside the scope. Native: the RWR mod's own 2x2.",
        (HudElementPositions.RwrAlerts, 1) => "TGT, SEEN, MSL and the rest side by side in one row.",
        (HudElementPositions.RwrAlerts, 2) => "The RWR mod's own 2x2 grid.",
        (HudElementPositions.SpeedBlock or HudElementPositions.AltBlock, 0) => "The style's own: Arcade box in Arcade, Wingman in Wingman. (Dashboard and Thunder show these in their strip and data block.)",
        (HudElementPositions.SpeedBlock or HudElementPositions.AltBlock, 1) => "Label, outlined value box and two detail lines.",
        (HudElementPositions.SpeedBlock or HudElementPositions.AltBlock, 2) => "Label over a bare value, tilted toward the aircraft.",
        (HudElementPositions.WeaponList, 0) => "Arcade: the list. Wingman: the cluster with the star. (Dashboard and Thunder show weapons in their strip and data block.)",
        (HudElementPositions.WeaponList, 1) => "Ace Combat style list of your weapons, the selected one highlighted.",
        (HudElementPositions.WeaponList, 2) => "Project Wingman cluster: guns, the selected weapon with a big counter and reload bars, and the star of the others (it replaces the weapon wheel on a controller).",
        (HudElementPositions.WeaponWheel, 0) => "The game's own radial weapon menu.",
        (HudElementPositions.WeaponWheel, 1) => "The Wingman star on its own: up to 8 weapons around it, and on a controller the star replaces the game's weapon wheel (aim the stick at a weapon).",
        (HudElementPositions.KillFeed, 0) => "Thunder: left. The other styles: right.",
        (HudElementPositions.KillFeed, 1) => "Lines start at the left edge.",
        (HudElementPositions.KillFeed, 2) => "Lines centred.",
        (HudElementPositions.KillFeed, 3) => "Lines end at the right edge.",
        (HudElementPositions.Damage, 0) => "Arcade: with the selected weapon's icons. The other styles: just the aircraft.",
        (HudElementPositions.Damage, 1) => "Just the aircraft outline, damaged parts tinted.",
        (HudElementPositions.Damage, 2) => "The aircraft with the selected weapon's icons on both sides (Ace Combat).",
        (HudElementPositions.Damage, 3) => "The game's own large damage display, where the game puts it (it fades out after a while, as in the vanilla HUD).",
        (HudElementPositions.DataBlock, 0) => "Lines start at the left edge.",
        (HudElementPositions.DataBlock, 1) => "Lines centred.",
        (HudElementPositions.DataBlock, 2) => "Lines end at the right edge.",
        (HudElementPositions.TargetScreen, 0) => "Arcade and Wingman: corner brackets. Dashboard and Thunder: a box.",
        (HudElementPositions.TargetScreen, 1) => "Corner brackets and the TGT header, no backing.",
        (HudElementPositions.TargetScreen, 2) => "A dark box behind the feed, with the TGT header.",
        (HudElementPositions.TargetScreen, 3) => "Just the camera feed: no frame, no header.",
        _ => null,
    };

    /// <summary>The chosen skin (0 = the style's own) for an element, in force now.</summary>
    internal static int SkinOf(int id) => Get(id).Skin;

    private static Look[] Parse(string text, HudStyle style)
    {
        var looks = new Look[HudElementPositions.Count];
        for (int i = 0; i < looks.Length; i++) looks[i] = DefaultFor(style, i);
        if (string.IsNullOrEmpty(text)) return looks;
        foreach (string item in text.Split(';'))
        {
            string[] head = item.Split(':');
            if (head.Length != 2 || !int.TryParse(head[0], out int id) || id <= 0 || id >= looks.Length) continue;
            string[] parts = head[1].Split(',');
            if (parts.Length < 3) continue;
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale);
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotation);
            looks[id] = new Look
            {
                Scale = scale > 0.05f ? scale : 1f,
                Rotation = rotation,
                Hidden = parts[2] == "1",
                Skin = parts.Length > 3 && int.TryParse(parts[3], out int skin) ? skin : 0,
            };
        }
        return looks;
    }

    private static string Format(Look[] looks, HudStyle style)
    {
        var text = new StringBuilder();
        for (int id = 1; id < looks.Length; id++)
        {
            Look look = looks[id];
            if (Same(look, DefaultFor(style, id))) continue;
            if (text.Length > 0) text.Append(';');
            text.Append(id).Append(':')
                .Append(look.Scale.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(look.Rotation.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                .Append(look.Hidden ? '1' : '0').Append(',')
                .Append(look.Skin);
        }
        return text.ToString();
    }
}
