using UnityEngine;

namespace UIPlus.Helpers;

/// <summary>
/// Paints instrument art (dial faces, bezels, scale marks, coloured arcs, needles) into a texture
/// once, so each dial is drawn as one image instead of hundreds of small ones. Shapes are smoothed
/// at their edges (anti-aliased) by how much of each pixel they cover.
///
/// Dial coordinates: angles in degrees clockwise from 12 o'clock; radii in dial units, where 1 is
/// the texture's outer edge.
/// </summary>
internal sealed class DialArt
{
    private readonly int _w, _h;
    private readonly Color[] _px;
    private readonly Vector2 _centre;
    private readonly float _radius;

    internal DialArt(int width, int height)
    {
        _w = width; _h = height;
        _px = new Color[width * height];
        // Transparent white, so softened edges fade toward white markings rather than black.
        for (int i = 0; i < _px.Length; i++) _px[i] = new Color(1f, 1f, 1f, 0f);
        _centre = new Vector2(width * 0.5f, height * 0.5f);
        _radius = Mathf.Min(width, height) * 0.5f - 1f;
    }

    internal DialArt(int size = 256) : this(size, size) { }

    // ---- positions ----

    internal Vector2 At(float angle, float radius)
    {
        float a = angle * Mathf.Deg2Rad;
        return _centre + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius * _radius;
    }

    internal float Pixels(float radius) => radius * _radius;

    // ---- shapes ----

    /// <summary>A ring (or a disc, with inner = 0), shaded from top to bottom.</summary>
    internal DialArt Ring(float inner, float outer, Color top, Color bottom)
    {
        float r0 = inner * _radius, r1 = outer * _radius;
        int x0 = Mathf.Max(0, (int)(_centre.x - r1 - 2)), x1 = Mathf.Min(_w - 1, (int)(_centre.x + r1 + 2));
        int y0 = Mathf.Max(0, (int)(_centre.y - r1 - 2)), y1 = Mathf.Min(_h - 1, (int)(_centre.y + r1 + 2));
        for (int y = y0; y <= y1; y++)
        {
            float t = Mathf.InverseLerp(_centre.y - r1, _centre.y + r1, y + 0.5f);
            Color c = Color.Lerp(bottom, top, t);
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), _centre);
                float cover = Mathf.Clamp01(r1 - d + 0.5f);
                if (r0 > 0f) cover *= Mathf.Clamp01(d - r0 + 0.5f);
                Blend(x, y, c, cover);
            }
        }
        return this;
    }

    internal DialArt Disc(float radius, Color colour) => Ring(0f, radius, colour, colour);

    /// <summary>A band between two radii from one angle to another (clockwise).</summary>
    internal DialArt Arc(float from, float to, float inner, float outer, Color colour)
    {
        float r0 = inner * _radius, r1 = outer * _radius;
        float span = Mathf.Repeat(to - from, 360f);
        if (span <= 0f) span = 360f;
        int x0 = Mathf.Max(0, (int)(_centre.x - r1 - 2)), x1 = Mathf.Min(_w - 1, (int)(_centre.x + r1 + 2));
        int y0 = Mathf.Max(0, (int)(_centre.y - r1 - 2)), y1 = Mathf.Min(_h - 1, (int)(_centre.y + r1 + 2));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - _centre;
                float d = p.magnitude;
                float cover = Mathf.Clamp01(r1 - d + 0.5f) * Mathf.Clamp01(d - r0 + 0.5f);
                if (cover <= 0f) continue;
                float a = Mathf.Repeat(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg - from, 360f);
                // Softened ends: how far inside the band's ends this pixel is, in pixels.
                float inside = Mathf.Min(a, span - a) * Mathf.Deg2Rad * d;
                if (a > span) inside = -Mathf.Min(a - span, 360f - a) * Mathf.Deg2Rad * d;
                cover *= Mathf.Clamp01(inside + 0.5f);
                Blend(x, y, colour, cover);
            }
        return this;
    }

    /// <summary>A scale mark: a bar across the radius at an angle, its width in pixels.</summary>
    internal DialArt Tick(float angle, float inner, float outer, float width, Color colour)
    {
        Vector2 a = At(angle, inner), b = At(angle, outer);
        Vector2 dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        return Polygon(colour, a - side, b - side, b + side, a + side);
    }

    /// <summary>A small triangle pointing at the centre, its tip at the given radius.</summary>
    internal DialArt Pointer(float angle, float tip, float length, float halfWidthPx, Color colour)
    {
        Vector2 t = At(angle, tip), baseMid = At(angle, tip + length);
        Vector2 dir = (baseMid - t).normalized, side = new Vector2(-dir.y, dir.x) * halfWidthPx;
        return Polygon(colour, t, baseMid + side, baseMid - side);
    }

    /// <summary>A filled convex shape, corners in pixels.</summary>
    internal DialArt Polygon(Color colour, params Vector2[] points)
    {
        int n = points.Length;
        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = points[i], q = points[(i + 1) % n];
            area += p.x * q.y - q.x * p.y;
        }
        float sign = area >= 0f ? 1f : -1f;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Vector2 p in points)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        int x0 = Mathf.Max(0, (int)minX - 1), x1 = Mathf.Min(_w - 1, (int)maxX + 1);
        int y0 = Mathf.Max(0, (int)minY - 1), y1 = Mathf.Min(_h - 1, (int)maxY + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 s = new(x + 0.5f, y + 0.5f);
                float inside = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = points[i], q = points[(i + 1) % n];
                    Vector2 e = q - p;
                    float len = e.magnitude;
                    if (len < 0.0001f) continue;
                    // Distance from the edge, positive inside.
                    inside = Mathf.Min(inside, sign * (e.x * (s.y - p.y) - e.y * (s.x - p.x)) / len);
                }
                Blend(x, y, colour, Mathf.Clamp01(inside + 0.5f));
            }
        return this;
    }

    private void Blend(int x, int y, Color colour, float cover)
    {
        if (cover <= 0f) return;
        int i = y * _w + x;
        Color under = _px[i];
        float a = colour.a * Mathf.Min(1f, cover);
        float outA = a + under.a * (1f - a);
        if (outA <= 0f) return;
        float k = under.a * (1f - a);
        _px[i] = new Color(
            (colour.r * a + under.r * k) / outA,
            (colour.g * a + under.g * k) / outA,
            (colour.b * a + under.b * k) / outA,
            outA);
    }

    /// <summary>The finished picture. The pivot is where it turns (for needles), 0..1 across it.</summary>
    internal Sprite ToSprite(string name, Vector2 pivot)
    {
        var texture = new Texture2D(_w, _h, TextureFormat.RGBA32, true)
        {
            name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2,
        };
        texture.SetPixels(_px);
        texture.Apply(true, true);   // mipmaps for smooth scaling down; no longer readable (frees the copy)
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, _w, _h), pivot, 100f);
        sprite.name = name;
        return sprite;
    }

    internal Sprite ToSprite(string name) => ToSprite(name, new Vector2(0.5f, 0.5f));

    // ---- ready-made pieces ----

    /// <summary>
    /// A needle pointing up from its pivot: a short counterweight tail below, tapering to a point.
    /// The pivot is at <paramref name="tail"/> / (tail + length) of the height.
    /// </summary>
    internal static Sprite Needle(string name, float length, float tail, float halfWidth, Color colour, Color tailColour)
    {
        const int w = 24;
        int h = Mathf.CeilToInt(length + tail) + 2;
        var art = new DialArt(w, h);
        float cx = w * 0.5f, py = tail + 1f;
        // Tail: a wider, darker counterweight.
        art.Polygon(tailColour,
            new Vector2(cx - halfWidth * 1.4f, 1f), new Vector2(cx + halfWidth * 1.4f, 1f),
            new Vector2(cx + halfWidth, py), new Vector2(cx - halfWidth, py));
        // Blade: straight for most of its length, then a point.
        float shoulder = py + length * 0.8f;
        art.Polygon(colour,
            new Vector2(cx - halfWidth, py), new Vector2(cx + halfWidth, py),
            new Vector2(cx + halfWidth * 0.8f, shoulder), new Vector2(cx, py + length), new Vector2(cx - halfWidth * 0.8f, shoulder));
        return art.ToSprite(name, new Vector2(0.5f, py / h));
    }
}
