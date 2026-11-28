using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Menu;

internal sealed class MenuTextShadow
{
    private readonly Dictionary<Material, Material> _shadows = new();
    private readonly MenuSnapshot _snapshot;

    internal MenuTextShadow(MenuSnapshot snapshot) => _snapshot = snapshot;

    internal void Shadow(Graphic label)
    {
        var black = new Color(0f, 0f, 0f, 0.45f);
        if (label is TMP_Text text && text.fontSharedMaterial is Material source && source.HasProperty("_UnderlayColor"))
        {
            if (!_shadows.TryGetValue(source, out Material shadow))
            {
                shadow = _snapshot.OwnAsset(new Material(source));
                shadow.EnableKeyword("UNDERLAY_ON");
                shadow.SetColor("_UnderlayColor", black);
                shadow.SetFloat("_UnderlayOffsetX", 0.4f);
                shadow.SetFloat("_UnderlayOffsetY", -0.4f);
                shadow.SetFloat("_UnderlaySoftness", 0.25f);
                _shadows.Add(source, shadow);
                _shadows.Add(shadow, shadow); // Late-added save buttons can inherit the existing shadow.
            }
            text.fontSharedMaterial = shadow;
            text.UpdateMeshPadding();
            return;
        }
        var effect = label.GetComponent<Shadow>() ?? _snapshot.Add<Shadow>(label.gameObject);
        effect.effectColor = black;
        effect.effectDistance = new Vector2(1.5f, -1.5f);
    }
}
