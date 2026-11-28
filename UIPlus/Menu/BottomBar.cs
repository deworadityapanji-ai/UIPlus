using UnityEngine;

namespace UIPlus.Menu;

internal static class BottomBar
{
    internal const float Height = 72f, Inset = 44f;
    private static readonly Vector2 TopLeft = new(0f, 1f), TopRight = Vector2.one;
    internal static readonly Vector2 BottomRight = new(1f, 0f);

    internal static RectTransform BuildBackground(Transform parent)
    {
        var bar = TitleMenuLayout.Paint("UI Plus bottom bar", parent, TitleMenuLayout.Background);
        PlaceList(bar);
        var rule = TitleMenuLayout.Paint("Top rule", bar, new Color(1f, 1f, 1f, 0.10f));
        rule.anchorMin = TopLeft;
        rule.anchorMax = TopRight;
        rule.offsetMin = new Vector2(0f, -1f);
        rule.offsetMax = Vector2.zero;
        return bar;
    }

    internal static void PlaceList(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = BottomRight;
        rect.pivot = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new Vector2(0f, Height);
    }

    internal static void PlaceLogoAndPilot(RectTransform logo, RectTransform caption, RectTransform name, Vector2 railLogoSize)
    {
        TitleMenu.Place(logo, TopLeft, TopLeft, new Vector2(Inset, -Inset), railLogoSize * 0.70f);
        TitleMenu.Place(caption, TopRight, TopRight, new Vector2(-Inset, -Inset), new Vector2(290f, 18f));
        TitleMenu.Place(name, TopRight, TopRight, new Vector2(-Inset, -Inset - 18f), new Vector2(290f, 30f));
    }

    internal static void PlaceVersion(RectTransform label, RectTransform popup)
    {
        TitleMenu.Place(label, BottomRight, BottomRight, new Vector2(-Inset, Height + 10f), new Vector2(126f, 37f));
        TitleMenu.Place(popup, BottomRight, BottomRight, new Vector2(-Inset, Height + 51f), new Vector2(238f, 16f));
    }

    internal static RectTransform MakeDivider(Transform parent) =>
        TitleMenuLayout.Paint("Group divider", parent, new Color(1f, 1f, 1f, 0.14f));

    internal static void PlaceDivider(RectTransform divider, float start, bool fromRight)
    {
        Vector2 anchor = fromRight ? BottomRight : Vector2.zero;
        TitleMenu.Place(divider, anchor, new Vector2(0f, 0.5f), new Vector2(start + 10f, Height * 0.5f), new Vector2(1f, 24f));
    }
}
