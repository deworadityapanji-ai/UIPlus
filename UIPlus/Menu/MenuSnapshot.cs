using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIPlus.Menu;

// Keep game and mod objects in place. Only objects explicitly owned by this build are destroyed.
internal sealed class MenuSnapshot
{
    private sealed class Node
    {
        internal Transform Target, Parent;
        internal Transform[] Following;
        internal int Index;
        internal bool Active, Late, Reparented;
    }

    private readonly Dictionary<Transform, Node> _nodes = new();
    private readonly List<Node> _hierarchy = new();
    private readonly Dictionary<Component, Action<Component>> _original = new();
    private readonly List<Action> _restore = new();
    private readonly HashSet<GameObject> _created = new();
    private readonly List<Behaviour> _components = new();
    private readonly List<Object> _assets = new();
    private bool _sealed;

    internal GameObject Own(GameObject created)
    {
        _created.Add(created);
        return created;
    }

    internal T OwnAsset<T>(T asset) where T : Object
    {
        _assets.Add(asset);
        return asset;
    }

    internal void ReleaseAssets()
    {
        foreach (Object asset in _assets) Object.Destroy(asset);
        _assets.Clear();
    }

    internal void Seal() => _sealed = true;

    internal T Add<T>(GameObject target) where T : Behaviour
    {
        T component = target.AddComponent<T>();
        _components.Add(component);
        return component;
    }

    internal bool Owns(GameObject target) => target != null &&
        (_created.Contains(target) || _nodes.ContainsKey(target.transform));

    internal void RememberCaption(TMP_Text label)
    {
        string caption = label.text;
        _restore.Add(() => { if (label != null) label.text = caption; });
    }

    internal void RememberCaption(Text label)
    {
        string caption = label.text;
        _restore.Add(() => { if (label != null) label.text = caption; });
    }

    internal void CaptureTree(Transform target, Transform template = null)
    {
        if (_nodes.ContainsKey(target)) return;
        int index = 0;
        if (target.parent != null)
            for (int i = 0; i < target.GetSiblingIndex(); i++)
            {
                Transform sibling = target.parent.GetChild(i);
                if (!_created.Contains(sibling.gameObject) &&
                    (!_nodes.TryGetValue(sibling, out Node original) || original.Parent == target.parent))
                    index++;
            }
        var node = new Node
        {
            Target = target, Parent = target.parent, Index = index, Active = target.gameObject.activeSelf, Late = _sealed,
        };
        // Scene-level siblings are shared with the background player and other mods.
        // A new backdrop can appear here after the snapshot, invalidating numeric indices.
        if (node.Parent != null && !_nodes.ContainsKey(node.Parent))
        {
            int first = target.GetSiblingIndex() + 1;
            node.Following = new Transform[node.Parent.childCount - first];
            for (int i = 0; i < node.Following.Length; i++)
                node.Following[i] = node.Parent.GetChild(first + i);
        }
        _nodes.Add(target, node);
        _hierarchy.Add(node);
        CaptureComponents(target, template);
        for (int i = 0; i < target.childCount; i++)
        {
            Transform child = target.GetChild(i);
            CaptureTree(child, template != null ? template.Find(child.name) : null);
        }
    }

    private void Watch<T>(T target, T template, Func<T, Action<T>> capture) where T : Component
    {
        if (target == null) return;
        // Late save buttons clone the styled Singleplayer object. Restore the original appearance
        // from its snapshot while keeping the clone's own title, active flag and callbacks.
        Action<Component> apply;
        if (template == null || !_original.TryGetValue(template, out apply))
        {
            var original = capture(target);
            apply = component => original((T)component);
        }
        _original.Add(target, apply);
        _restore.Add(() => { if (target != null) apply(target); });
    }

    private void CaptureComponents(Transform target, Transform template)
    {
        Watch(target as RectTransform, template as RectTransform, rect =>
        {
            var (min, max, pivot, size, position, rotation, scale) = (rect.anchorMin, rect.anchorMax, rect.pivot,
                rect.sizeDelta, rect.anchoredPosition3D, rect.localRotation, rect.localScale);
            return r =>
            {
                r.anchorMin = min;
                r.anchorMax = max;
                r.pivot = pivot;
                r.sizeDelta = size;
                r.anchoredPosition3D = position;
                r.localRotation = rotation;
                r.localScale = scale;
            };
        });
        Watch(target.GetComponent<Selectable>(), template?.GetComponent<Selectable>(), control =>
        {
            var (transition, navigation) = (control.transition, control.navigation);
            return c =>
            {
                c.transition = transition;
                c.navigation = navigation;
            };
        });
        foreach (Graphic graphic in target.GetComponents<Graphic>())
        {
            // Graphic properties and text properties live on the same component. Capture them together.
            Graphic basis = template != null ? template.GetComponent(graphic.GetType()) as Graphic : null;
            Watch(graphic, basis, CaptureGraphic);
        }
        Watch(target.GetComponent<VerticalLayoutGroup>(), null, layout =>
        {
            var (enabled, alignment, padding, spacing, width, height, expandWidth, expandHeight) =
                (layout.enabled, layout.childAlignment, layout.padding, layout.spacing, layout.childControlWidth,
                    layout.childControlHeight, layout.childForceExpandWidth, layout.childForceExpandHeight);
            return l =>
            {
                l.childAlignment = alignment;
                l.padding = padding;
                l.spacing = spacing;
                l.childControlWidth = width;
                l.childControlHeight = height;
                l.childForceExpandWidth = expandWidth;
                l.childForceExpandHeight = expandHeight;
                l.enabled = enabled;
            };
        });
        Watch(target.GetComponent<ContentSizeFitter>(), null, fitter =>
        {
            bool enabled = fitter.enabled;
            return f => f.enabled = enabled;
        });
        WatchOptional(target.GetComponent<LayoutElement>(), template, element =>
        {
            var (ignore, min, preferred, flexible) = (element.ignoreLayout, element.minHeight, element.preferredHeight, element.flexibleHeight);
            return e =>
            {
                e.ignoreLayout = ignore;
                e.minHeight = min;
                e.preferredHeight = preferred;
                e.flexibleHeight = flexible;
            };
        });
        WatchOptional(target.GetComponent<Shadow>(), template, effect =>
        {
            var (colour, distance) = (effect.effectColor, effect.effectDistance);
            return e =>
            {
                e.effectColor = colour;
                e.effectDistance = distance;
            };
        });
    }

    private void WatchOptional<T>(T target, Transform template, Func<T, Action<T>> capture) where T : Behaviour
    {
        T source = template != null ? template.GetComponent<T>() : null;
        if (target != null && template != null && (source == null || !_original.ContainsKey(source)))
            _components.Add(target); // The clone inherited a component created by this style.
        else Watch(target, source, capture);
    }

    private static Action<Graphic> CaptureGraphic(Graphic graphic)
    {
        var (enabled, colour, raycast, rendererColour) =
            (graphic.enabled, graphic.color, graphic.raycastTarget, graphic.canvasRenderer.GetColor());
        Action<Graphic> appearance = _ => { };
        if (graphic is Image image)
        {
            bool aspect = image.preserveAspect;
            appearance = g => ((Image)g).preserveAspect = aspect;
        }
        else if (graphic is TMP_Text text)
        {
            var (font, material, size, alignment, style, autosize, spacing, margin, overflow, wrap) =
                (text.font, text.fontSharedMaterial, text.fontSize, text.alignment, text.fontStyle,
                    text.enableAutoSizing, text.characterSpacing, text.margin, text.overflowMode, text.enableWordWrapping);
            appearance = g =>
            {
                var t = (TMP_Text)g;
                t.font = font;
                t.fontSharedMaterial = material;
                t.fontSize = size;
                t.alignment = alignment;
                t.fontStyle = style;
                t.enableAutoSizing = autosize;
                t.characterSpacing = spacing;
                t.margin = margin;
                t.overflowMode = overflow;
                t.enableWordWrapping = wrap;
                t.UpdateMeshPadding();
            };
        }
        else if (graphic is Text legacy)
        {
            var (size, alignment) = (legacy.fontSize, legacy.alignment);
            appearance = g =>
            {
                var t = (Text)g;
                t.fontSize = size;
                t.alignment = alignment;
            };
        }
        return g =>
        {
            appearance(g);
            g.color = colour;
            g.enabled = enabled;
            g.raycastTarget = raycast;
            g.canvasRenderer.SetColor(rendererColour);
        };
    }

    internal void Restore()
    {
        // Rescue borrowed objects before disabling or destroying any object that now parents them.
        foreach (Node node in _hierarchy)
            if (node.Target != null && node.Target.parent != node.Parent)
            {
                node.Reparented = true;
                node.Target.SetParent(node.Parent, false);
            }
        foreach (GameObject go in _created)
        {
            if (go == null) continue;
            go.SetActive(false);
            go.transform.SetParent(null, false);
            Object.Destroy(go);
        }
        foreach (Behaviour component in _components)
        {
            if (component == null) continue;
            if (component is CanvasGroup group)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }
            component.enabled = false;
            Object.Destroy(component);
        }
        // Restore the original sibling sequence before inserting any later additions.
        _hierarchy.Sort((a, b) => a.Late != b.Late ? a.Late.CompareTo(b.Late) : a.Index.CompareTo(b.Index));
        foreach (Node node in _hierarchy)
            if (node.Target != null && node.Following == null) node.Target.SetSiblingIndex(node.Index);
        // Restore the anchors first, from right to left, then put each root before its saved anchor.
        for (int i = _hierarchy.Count - 1; i >= 0; i--)
        {
            Node node = _hierarchy[i];
            if (node.Target == null || node.Parent == null || node.Following == null) continue;
            int next = -1;
            foreach (Transform sibling in node.Following)
                if (sibling != null && sibling.parent == node.Parent)
                {
                    next = sibling.GetSiblingIndex();
                    break;
                }
            if (next < 0 && !node.Reparented) continue;
            int current = node.Target.GetSiblingIndex();
            int index = MenuLayout.IndexBefore(current, next, node.Parent.childCount);
            if (index != current) node.Target.SetSiblingIndex(index);
        }
        foreach (Action restore in _restore) restore();
        foreach (Node node in _hierarchy)
            if (node.Target != null && node.Target.gameObject.activeSelf != node.Active) node.Target.gameObject.SetActive(node.Active);
        ReleaseAssets();
    }
}
