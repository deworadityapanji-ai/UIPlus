using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Menu;

internal sealed class MinimalCorner
{
    internal const float Left = 44f, ListWidth = 290f, RowHeight = 34f;
    private const float QuitBottom = 90f;
    internal static readonly Color Dim = new(0.80f, 0.83f, 0.85f);
    private static readonly Vector2 TopLeft = new(0f, 1f);
    private Texture2D _scrim;
    private readonly MenuSnapshot _snapshot;

    internal MinimalCorner(MenuSnapshot snapshot) => _snapshot = snapshot;

    internal void PlaceList(RectTransform list) =>
        TitleMenu.Place(list, Vector2.zero, Vector2.zero,
            new Vector2(Left, QuitBottom + RowHeight + MenuLayout.GroupGap), new Vector2(ListWidth, 0f));

    internal void PlaceQuit(RectTransform quit) =>
        TitleMenu.Place(quit, Vector2.zero, Vector2.zero, new Vector2(Left, QuitBottom), new Vector2(ListWidth, RowHeight));

    internal void PlaceLogoAndPilot(RectTransform logo, RectTransform caption, RectTransform name, Vector2 railLogoSize)
    {
        Vector2 size = railLogoSize * 0.70f;
        TitleMenu.Place(logo, TopLeft, TopLeft, new Vector2(Left, -44f), size);
        float captionTop = 44f + size.y + 12f;
        TitleMenu.Place(caption, TopLeft, TopLeft, new Vector2(Left, -captionTop), new Vector2(ListWidth, 18f));
        TitleMenu.Place(name, TopLeft, TopLeft, new Vector2(Left, -captionTop - 18f), new Vector2(ListWidth, 30f));
    }

    internal Transform GroupGap(Transform parent)
    {
        var spacer = new GameObject("Group gap", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(parent, false);
        var layout = spacer.GetComponent<LayoutElement>();
        // The layout group contributes two units on either side of the spacer.
        layout.minHeight = layout.preferredHeight = MenuLayout.GroupGap - 4f;
        return spacer.transform;
    }

    internal RectTransform BuildScrim(Transform parent)
    {
        if (_scrim == null)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = new Vector2(x, y).magnitude / (size - 1f);
                float alpha = Mathf.SmoothStep(0f, 0.45f, 1f - radius);
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            _scrim = _snapshot.OwnAsset(new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UI Plus corner scrim",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            });
            _scrim.SetPixels32(pixels);
            _scrim.Apply(false, true);
        }
        var go = new GameObject("UI Plus corner scrim", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<RawImage>();
        image.texture = _scrim;
        image.raycastTarget = false;
        TitleMenu.Place(image.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(700f, 520f));
        return image.rectTransform;
    }

}
