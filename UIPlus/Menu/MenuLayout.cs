using System;
using System.Text;

namespace UIPlus.Menu;

internal enum MenuStyle { Vanilla, SideRail, MinimalCorner, BottomBar }
internal enum MenuTips { Shown, AutoHide, Hidden }
internal enum MenuNews { Shown, Compact, AutoHide }
internal enum MenuLogo { Shown, AutoHide }
internal enum MenuAccent { Green, Amber, White, HudColour }
internal enum MenuSection { Play, Create, More }
internal enum MenuFocus { Keep, Hovered, LastHovered, First }
internal enum MenuStyleChange { None, Restore, Build }

internal static class MenuLayout
{
    internal const MenuStyle DefaultStyle = MenuStyle.BottomBar;
    internal const MenuTips DefaultTips = MenuTips.AutoHide;
    internal const MenuNews DefaultNews = MenuNews.Compact;
    internal const MenuLogo DefaultLogo = MenuLogo.Shown;
    internal const MenuAccent DefaultAccent = MenuAccent.Green;
    internal const float GroupGap = 14f;
    internal const int QuitOrder = 100;
    internal const float BarDividerWidth = 21f;

    internal readonly struct BarFit
    {
        internal readonly int FontSize;
        internal readonly float Padding, WidthScale;

        internal BarFit(int fontSize, float padding, float widthScale = 1f)
        {
            FontSize = fontSize;
            Padding = padding;
            WidthScale = widthScale;
        }
    }

    internal static BarFit FitBar(float width, int count, int dividers, float text17, float text16, float text15)
    {
        if (count <= 0) return new BarFit(17, 18f);
        float available = width - BarDividerWidth * dividers;
        float padding = (available - text17) / (2f * count);
        if (padding >= 10f) return new BarFit(17, Math.Min(18f, padding));
        float textSpace = Math.Max(0f, available - 20f * count);
        if (text16 <= textSpace) return new BarFit(16, 10f);
        // Ellipsize unexpectedly long mod labels once the font and padding reach their minimum.
        return new BarFit(15, 10f, text15 <= textSpace ? 1f : textSpace / text15);
    }

    internal static MenuStyleChange StyleChange(MenuStyle? built, MenuStyle wanted, bool ready)
    {
        if (!ready || (built ?? MenuStyle.Vanilla) == wanted) return MenuStyleChange.None;
        return built.HasValue ? MenuStyleChange.Restore : MenuStyleChange.Build;
    }

    internal static int IndexBefore(int current, int next, int count) =>
        next < 0 ? count - 1 : next - (current < next ? 1 : 0);

    internal static bool IsLanRoot(string name) => name == "StartLanButtonRoot" || name == "JoinLanButtonRoot";

    internal static int ButtonOrder(string name, string caption)
    {
        int order = Order(name);
        return order == 99 ? Order(caption) : order;
    }

    private static int Order(string name)
    {
        var key = new StringBuilder();
        foreach (char c in name ?? "")
            if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
        return key.ToString() switch
        {
            "continue" => 0,
            "loadsave" => 1,
            "singleplayerbutton" or "singleplayer" => 2,
            "multiplayerbutton" or "multiplayer" => 3,
            "startlanbuttonroot" => 4,
            "joinlanbuttonroot" => 5,
            "missioneditorbutton" or "missioneditor" => 10,
            "workshopbutton" or "workshop" => 11,
            "encyclopediabutton" or "encyclopedia" => 20,
            "settingsbutton" or "settings" => 21,
            _ => 99,
        };
    }

    internal static MenuSection Section(int order) =>
        order < 10 ? MenuSection.Play : order < 20 ? MenuSection.Create : MenuSection.More;

    internal static bool BarDividerBefore(int? previous, int order) =>
        order == QuitOrder || (previous.HasValue && Section(previous.Value) != Section(order));

    internal static bool HasGroupGap(int visibleGroups, MenuSection section)
    {
        int bit = 1 << (int)section;
        return (visibleGroups & bit) != 0 && (visibleGroups & (bit - 1)) != 0;
    }

    internal static float TipsRightShift(float listLeft, float listRight, float listBottom, float listTop,
        float tipsLeft, float tipsRight, float tipsBottom, float tipsTop) =>
        tipsLeft < listRight && tipsRight > listLeft && tipsBottom < listTop && tipsTop > listBottom
            ? listRight + 16f - tipsLeft : 0f;

    internal static float TipsAboveBar(float barTop, float tipsBottom, float tipsTop, bool overlapsVersionHorizontally,
        float versionBottom, float versionTop)
    {
        float shift = Math.Max(0f, barTop + 16f - tipsBottom);
        if (overlapsVersionHorizontally && tipsBottom + shift < versionTop && tipsTop + shift > versionBottom)
            shift = versionTop + 8f - tipsBottom;
        return shift;
    }

    internal static MenuFocus ChooseFocus(bool hasSelection, bool movedOverItem, bool restoreLastHovered, bool startNavigation)
    {
        if (movedOverItem) return MenuFocus.Hovered;
        if (hasSelection) return MenuFocus.Keep;
        if (restoreLastHovered) return MenuFocus.LastHovered;
        return startNavigation ? MenuFocus.First : MenuFocus.Keep;
    }

    internal static bool Reveal(bool hidden, bool autoHide, bool gamepad, bool nearEdge, bool hovered, bool expanded) =>
        !hidden && (!autoHide || gamepad || nearEdge || hovered || expanded);

    internal readonly struct PanelBounds
    {
        private readonly float _left, _bottom, _right, _top;

        internal PanelBounds(float left, float bottom, float right, float top)
        {
            _left = left;
            _bottom = bottom;
            _right = right;
            _top = top;
        }

        internal bool Within(float x, float y, float distance)
        {
            float dx = Math.Max(0f, Math.Max(_left - x, x - _right));
            float dy = Math.Max(0f, Math.Max(_bottom - y, y - _top));
            return dx * dx + dy * dy <= distance * distance;
        }
    }

    internal static bool PointerRevealsPanel(bool wasRevealed, float x, float y, PanelBounds shown, PanelBounds zone,
        float hysteresis, float pointerFloor)
    {
        // The bar is a hard boundary, including when the pointer moves there from the reveal zone.
        if (y < pointerFloor) return false;
        float distance = wasRevealed ? hysteresis : 0f;
        return shown.Within(x, y, distance) || zone.Within(x, y, distance);
    }

    // Smoothstep gives the slide a soft start and finish, including when the pointer reverses it.
    internal static float Slide(float progress, bool shown, float unscaledSeconds)
    {
        float step = unscaledSeconds / 0.22f;
        return shown ? Math.Min(1f, progress + step) : Math.Max(0f, progress - step);
    }

    internal static float Ease(float progress) => progress * progress * (3f - 2f * progress);
}
