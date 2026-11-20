using System.Collections.Generic;
using BepInEx.Configuration;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

public enum KillFeedMode { Off, All, Mine }

// A compact, stylised kill feed (CoD / Counter-Strike style) from the game's own kill messages:
//     KILLER  >>  VICTIM
// in the friendly / hostile colours of the player's theme, newest on top, each row fading after a
// few seconds. Your own kills and deaths get a highlighted band. Shot-down missiles are left out.
internal static partial class ArcadeHud
{
    internal const int KillFeedItem = 24;
    internal static ConfigEntry<KillFeedMode> KillFeed;

    private const int FeedRows = 5;
    private const float FeedLife = 7f, FeedFade = 0.5f, FeedRowHeight = 24f;

    private sealed class FeedEntry { internal string Text; internal bool Mine; internal float Time; }
    private sealed class FeedRow { internal RectTransform Rect; internal Image Band; internal TextMeshProUGUI Text; }

    private static readonly List<FeedEntry> FeedEntries = new();
    private static readonly List<FeedRow> FeedRowsUi = new();
    private static RectTransform _killFeed;
    private static Vector2 KillFeedBase;
    private static bool _feedRight;

    /// <summary>From the game's kill message (every client).</summary>
    internal static void AddKill(PersistentID killer, PersistentID killed, int killedType)
    {
        if (KillFeed == null || KillFeed.Value == KillFeedMode.Off) return;
        if ((KillKind)killedType == KillKind.Missile) return;
        UnitRegistry.TryGetPersistentUnit(killed, out PersistentUnit victim);
        if (victim == null) return;
        UnitRegistry.TryGetPersistentUnit(killer, out PersistentUnit attacker);
        GameManager.GetLocalAircraft(out Aircraft me);
        bool mine = me != null && (killer == me.persistentID || killed == me.persistentID);
        if (KillFeed.Value == KillFeedMode.Mine && !mine) return;
        string Name(PersistentUnit u) =>
            $"<color={Hex(SideColour(u))}><noparse>{(string.IsNullOrEmpty(u.unitName) ? "?" : u.unitName)}</noparse></color>";
        string arrow = "<alpha=#99><noparse>>></noparse><alpha=#FF>";
        string text = attacker == null || attacker == victim ? $"{arrow}  {Name(victim)}" : $"{Name(attacker)}  {arrow}  {Name(victim)}";
        FeedEntries.Insert(0, new FeedEntry { Text = text, Mine = mine, Time = Time.unscaledTime });
        if (FeedEntries.Count > FeedRows) FeedEntries.RemoveAt(FeedEntries.Count - 1);
    }

    // Friendly or hostile to the local player, in the theme's unit colours (as the game's feed).
    private static Color SideColour(PersistentUnit unit)
    {
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        GameManager.GetLocalHQ(out FactionHQ local);
        FactionHQ hq = unit != null ? unit.GetHQ() : null;
        if (theme == null) return hq != null && hq == local ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.3f, 0.3f);
        return hq == null ? theme.HudUnitNeutral : hq == local ? theme.HudUnitFriendly : theme.HudUnitHostile;
    }

    private static void BuildKillFeed()
    {
        // Out of the way of each style's own corner elements. Skin: 1 left, 2 centre, 3 right.
        _builtFeedSkin = ElementLooks.SkinOf(KillFeedItem);
        int align = _builtFeedSkin > 0 ? _builtFeedSkin - 1 : _thunder ? 0 : 2;   // 0 left, 1 centre, 2 right
        _feedRight = align == 2;
        float y = _thunder ? -470f : _minimal ? -30f : _plus ? -380f : -300f;   // Arcade / Wingman: below the top-right elements
        KillFeedBase = new Vector2(align == 0 ? 30f : align == 2 ? -30f : 0f, y);
        Vector2 corner = new(align * 0.5f, 1f);
        TextAlignmentOptions textAlign = align == 0 ? TextAlignmentOptions.Left : align == 1 ? TextAlignmentOptions.Center : TextAlignmentOptions.Right;
        _killFeed = Rect("KillFeed", _root.transform, corner, corner, KillFeedBase, new Vector2(460f, FeedRows * FeedRowHeight));
        FeedRowsUi.Clear();
        TMP_FontAsset font = ChatFont();
        for (int i = 0; i < FeedRows; i++)
        {
            var row = new FeedRow();
            row.Rect = Rect("Row" + i, _killFeed, corner, corner, new Vector2(0f, -i * FeedRowHeight), new Vector2(460f, FeedRowHeight - 3f));
            row.Band = row.Rect.gameObject.AddComponent<Image>();
            row.Band.raycastTarget = false;
            row.Text = Text("Text", row.Rect, 15, textAlign);
            if (font != null) { row.Text.font = font; row.Text.fontSharedMaterial = font.material; }
            row.Text.enableWordWrapping = false;
            row.Text.rectTransform.anchorMin = Vector2.zero;
            row.Text.rectTransform.anchorMax = Vector2.one;
            row.Text.rectTransform.offsetMin = new Vector2(8f, 0f);
            row.Text.rectTransform.offsetMax = new Vector2(-8f, 0f);
            AddShadow(row.Text);
            row.Rect.gameObject.SetActive(false);
            FeedRowsUi.Add(row);
        }
    }

    private static void UpdateKillFeed()
    {
        if (_killFeed == null) return;
        bool on = KillFeed != null && KillFeed.Value != KillFeedMode.Off;
        bool preview = HudLayoutEditor.IsOpen;
        float now = Time.unscaledTime;
        if (!preview) FeedEntries.RemoveAll(e => now - e.Time > FeedLife);
        for (int i = 0; i < FeedRowsUi.Count; i++)
        {
            FeedRow row = FeedRowsUi[i];
            FeedEntry e = preview ? (i < 3 ? new FeedEntry { Text = i == 0 ? "PILOT  <alpha=#99><noparse>>></noparse><alpha=#FF>  TARGET" : "KILL FEED PREVIEW", Mine = i == 0, Time = now } : null)
                : i < FeedEntries.Count ? FeedEntries[i] : null;
            bool show = on && e != null;
            if (row.Rect.gameObject.activeSelf != show) row.Rect.gameObject.SetActive(show);
            if (!show) continue;
            float alpha = Mathf.Clamp01((FeedLife - (now - e.Time)) / FeedFade);
            row.Text.text = e.Text;
            row.Text.alpha = alpha;
            // Band sized to the text: dark for others' kills, a HUD-coloured tint for your own.
            float width = row.Text.GetPreferredValues(e.Text).x + 16f;
            row.Rect.sizeDelta = new Vector2(Mathf.Min(width, 460f), FeedRowHeight - 3f);
            row.Band.color = e.Mine ? new Color(Green.r, Green.g, Green.b, 0.22f * alpha) : new Color(0f, 0f, 0f, 0.42f * alpha);
        }
    }
}
