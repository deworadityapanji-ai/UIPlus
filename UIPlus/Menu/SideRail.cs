using UnityEngine;

namespace UIPlus.Menu;

// Side Rail presentation. Stock controls and focus are managed by TitleMenuLayout and TitleMenu.
internal static class SideRail
{
    internal const float Width = 320f, InnerWidth = Width - 12f, RowHeight = 38f, TextInset = 35f;
    private const float PilotBottom = 112f, PilotHeight = 24f, LogoTop = 28f, DividerGap = 16f;
    private static readonly Vector2 TopLeft = new(0f, 1f);

    internal static RectTransform BuildBackground(Transform parent)
    {
        var background = TitleMenuLayout.Paint("UI Plus Side Rail", parent, TitleMenuLayout.Background);
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = background.offsetMax = Vector2.zero;
        var edge = TitleMenuLayout.Paint("Edge", background, new Color(0.88f, 0.93f, 1f, 0.28f));
        edge.anchorMin = new Vector2(1f, 0f);
        edge.anchorMax = Vector2.one;
        edge.pivot = new Vector2(1f, 0.5f);
        edge.anchoredPosition = Vector2.zero;
        edge.sizeDelta = new Vector2(1f, 0f);
        return background;
    }

    internal static void PlaceList(RectTransform buttons) =>
        TitleMenu.Place(buttons, TopLeft, TopLeft, Vector2.zero, new Vector2(InnerWidth, 0f));

    internal static void PlaceQuit(RectTransform quit) =>
        TitleMenu.Place(quit, Vector2.zero, Vector2.zero, new Vector2(22f, 23f), new Vector2(Width - 44f, 37f));

    internal static void PlacePilot(RectTransform caption, RectTransform name)
    {
        TitleMenu.Place(name, Vector2.zero, Vector2.zero, new Vector2(22f, 76f), new Vector2(Width - 44f, 35f));
        TitleMenu.Place(caption, Vector2.zero, Vector2.zero, new Vector2(0f, PilotBottom), new Vector2(InnerWidth, PilotHeight));
    }

    internal static float PlaceLogo(RectTransform logo, RectTransform background, Vector2 size)
    {
        TitleMenu.Place(logo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -LogoTop), size);
        float depth = LogoTop + size.y + DividerGap;
        var divider = TitleMenuLayout.Paint("Logo divider", background, new Color(1f, 1f, 1f, 0.10f));
        TitleMenu.Place(divider, TopLeft, new Vector2(0f, 0.5f), new Vector2(0f, -depth), new Vector2(InnerWidth, 1f));
        return depth;
    }

    internal static void CenterList(RectTransform list, float railHeight, float dividerDepth, float groupHeight)
    {
        float logoBottom = dividerDepth - DividerGap;
        float pilotTop = railHeight - PilotBottom - PilotHeight;
        float top = Mathf.Max(dividerDepth + 1f, (logoBottom + pilotTop - groupHeight) * 0.5f);
        list.anchoredPosition = new Vector2(0f, -top);
    }
}
