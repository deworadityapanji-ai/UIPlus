using System.Collections.Generic;

namespace UIPlus.Helpers;

internal static class WeaponStarLayout
{
    internal const int Petals = 8, Empty = -1, Link = -2, Auto = -3;

    internal static void Assign(IReadOnlyList<int> weapons, int selected, bool link, bool auto, int[] petals)
    {
        int count = weapons.Count;
        if (count + (link ? 1 : 0) + (auto ? 1 : 0) > Petals) link = auto = false;
        int next = 0;
        if (count > Petals)
            for (int i = 0; i < count; i++)
                if (weapons[i] == selected)
                {
                    next = (i - 3 + count) % count;
                    break;
                }

        int shown = 0;
        for (int i = 0; i < Petals; i++)
        {
            if (i == 0 && link) petals[i] = Link;
            else if (i == 4 && auto) petals[i] = Auto;
            else petals[i] = shown++ < count ? weapons[next++ % count] : Empty;
        }
    }
}
