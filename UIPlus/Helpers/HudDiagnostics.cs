using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// TEMPORARY investigation aids, remove before release:
//  - Ctrl+Shift+H in chase view writes the map and weapon-panel UI hierarchies to
//    BepInEx\LogOutput.log (was F10, which WT Mouse Aim uses as its on/off key).
//  - Any frame much longer than usual is logged as "[Hitch]", to match camera twitches to stalls.
internal static class HudDiagnostics
{
    private static float _averageFrame = 1f / 60f, _nextHitchLog;
    private static int _hitchesSkipped, _lastGcCount = -1;

    internal static void Tick()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!(ctrl && shift && Input.GetKeyDown(KeyCode.H))) return;
        var sb = new StringBuilder("[HudDiagnostics] dump, screen " + Screen.width + "x" + Screen.height + "\n");
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map != null)
        {
            sb.AppendLine("== DynamicMap: parents of hudMapAnchor, then each ancestor's subtree");
            Dump(sb, map.hudMapAnchor != null ? map.hudMapAnchor.transform : null, true);
            sb.AppendLine("== DynamicMap.mapBackground / mapTransform");
            Dump(sb, map.mapBackground != null ? map.mapBackground.transform : null, false);
            Dump(sb, map.mapTransform, false);
        }
        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        GameObject panel = hud != null
            ? HarmonyLib.AccessTools.Field(typeof(CombatHUD), "topRightPanel")?.GetValue(hud) as GameObject
            : null;
        if (panel != null)
        {
            sb.AppendLine("== topRightPanel parents + subtree");
            Dump(sb, panel.transform, true);
        }
        // Every native instrument in the scene, so missing features can be traced on any airframe.
        sb.AppendLine("== HUD apps (type | visible | screen centre | path)");
        foreach (HUDApp app in Resources.FindObjectsOfTypeAll<HUDApp>())
        {
            if (app == null || !app.gameObject.scene.IsValid()) continue;
            string where = "-";
            if (app.transform is RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                Vector3 m = (c[0] + c[2]) * 0.5f;
                where = $"({(int)m.x},{(int)m.y})";
            }
            sb.Append("  ").Append(app.GetType().Name).Append(" | ")
              .Append(app.gameObject.activeInHierarchy ? "shown" : "HIDDEN").Append(" | ")
              .Append(where).Append(" | ").AppendLine(Path(app.transform));
        }
        Plugin.Logger.LogInfo(sb.ToString());
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private static void LogHitches()
    {
        float dt = Time.unscaledDeltaTime;
        // Did the .NET garbage collector run since last frame? A long frame with a GC is allocation-driven.
        int gc = System.GC.CollectionCount(0);
        bool gcRan = _lastGcCount >= 0 && gc != _lastGcCount;
        _lastGcCount = gc;
        if (dt <= 0f || GameplayUI.GameIsPaused) return;
        if (dt > Mathf.Max(_averageFrame * 2.5f, 0.03f))
        {
            if (Time.unscaledTime >= _nextHitchLog || dt > 1f)
            {
                Plugin.Logger.LogInfo($"[Hitch] {dt * 1000f:F0} ms frame (usual {_averageFrame * 1000f:F0} ms) at {Time.unscaledTime:F1}s"
                    + (gcRan ? " [garbage collection ran]" : "") + (dt > 1f ? " LONG FREEZE" : "")
                    + (_hitchesSkipped > 0 ? $", {_hitchesSkipped} more since last line" : ""));
                _nextHitchLog = Time.unscaledTime + 0.5f;
                _hitchesSkipped = 0;
            }
            else _hitchesSkipped++;
            return;   // keep hitches out of the average
        }
        _averageFrame += (dt - _averageFrame) * 0.05f;
    }

    private static void Dump(StringBuilder sb, Transform t, bool withParents)
    {
        if (t == null) { sb.AppendLine("(null)"); return; }
        Transform start = t;
        if (withParents)
        {
            for (int i = 0; i < 3 && start.parent != null && start.parent.GetComponent<Canvas>() == null; i++)
                start = start.parent;
            for (Transform p = t.parent; p != null; p = p.parent)
                sb.AppendLine("  parent: " + p.name + " canvas=" + (p.GetComponent<Canvas>() != null));
        }
        Walk(sb, start, 0, t);
    }

    private static void Walk(StringBuilder sb, Transform t, int depth, Transform mark)
    {
        if (depth > 6) return;
        var rt = t as RectTransform;
        sb.Append(new string(' ', depth * 2)).Append(t == mark ? "*" : "-").Append(t.name)
          .Append(t.gameObject.activeInHierarchy ? "" : " [inactive]");
        if (rt != null)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            sb.Append(" rect=(").Append((int)c[0].x).Append(',').Append((int)c[0].y).Append(")-(")
              .Append((int)c[2].x).Append(',').Append((int)c[2].y).Append(')');
        }
        foreach (Component comp in t.GetComponents<Component>())
        {
            if (comp == null || comp is Transform) continue;
            sb.Append(" <").Append(comp.GetType().Name);
            if (comp is Graphic g)
                sb.Append(" color=").Append(g.color).Append(g.enabled ? "" : " off");
            if (comp is Image im && im.sprite != null) sb.Append(" sprite=").Append(im.sprite.name);
            if (comp is CanvasGroup cg) sb.Append(" alpha=").Append(cg.alpha);
            sb.Append('>');
        }
        sb.AppendLine();
        foreach (Transform child in t) Walk(sb, child, depth + 1, mark);
    }
}
