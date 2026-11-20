using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;
using NuclearOption.UIStyleSystem;

namespace UIPlus.Helpers;

// Ace Combat 7 style missile warning: a short red arc on a ring around the aircraft for each incoming
// missile the game's missile warning knows about, on the side it comes from, read like a radar from
// above (top = ahead, bottom = behind). The
// arc grows longer, thicker and brighter as the missile closes, and flashes faster when it is near.
// Part of Arcade; an extra in the other styles. Hidden in player-versus-player games (another human
// on a hostile faction), where it could be an advantage.
internal static partial class ArcadeHud
{
    internal const int MissileArcsItem = HudElementPositions.MissileArcs;
    private const float ArcRadius = 170f, NearMetres = 600f, FarMetres = 7000f;
    private static readonly Vector2 MissileArcsBase = new(0f, -40f);
    private static RectTransform _arcs;
    private static readonly List<Image> Arcs = new();
    private static Sprite[] _ringSprites;   // thin, medium, thick
    private static float _nextPvpCheck;
    private static bool _pvp;

    private static void BuildMissileArcs()
    {
        _arcs = Rect("MissileArcs", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            MissileArcsBase, new Vector2(ArcRadius * 2f, ArcRadius * 2f));
        Arcs.Clear();
    }

    private static Image ArcImage(int index)
    {
        while (Arcs.Count <= index)
        {
            RectTransform rt = Rect("Arc", _arcs, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(ArcRadius * 2f, ArcRadius * 2f));
            Image image = rt.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            Arcs.Add(image);
        }
        return Arcs[index];
    }

    private static void UpdateMissileArcs(Aircraft aircraft)
    {
        if (_arcs == null) return;
        int shown = 0;
        Camera camera = SceneSingleton<CameraStateManager>.i != null ? SceneSingleton<CameraStateManager>.i.mainCamera : null;
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        Color red = theme != null ? theme.Alert : new Color(1f, 0.2f, 0.15f);
        if (HudLayoutEditor.IsOpen)
        {
            // Preview: one far, one mid-range, one close.
            Place(ref shown, 60f, 0.1f, red);
            Place(ref shown, 200f, 0.55f, red);
            Place(ref shown, 300f, 1f, red);
        }
        else if (camera != null && !IsPvp() && aircraft.GetMissileWarningSystem() is MissileWarning warning)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            foreach (Missile missile in warning.knownMissiles)
            {
                if (missile == null || missile.disabled || hud == null) continue;
                if (!KnownPosition(hud, aircraft, missile, out GlobalPosition at, out bool stale)) continue;
                Vector3 local = at.ToLocalPosition();
                // Read like a radar seen from above, as Ace Combat does: up = ahead of the view,
                // down = behind, left and right = the sides (height ignored). Camera space: x right, z ahead.
                Vector3 view = camera.transform.InverseTransformPoint(local);
                if (view.x * view.x + view.z * view.z < 0.0001f) view = new Vector3(0f, view.y, 1f);
                float bearing = Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg;   // clockwise from up
                float distance = Vector3.Distance(local, aircraft.transform.position);
                float near = Mathf.Clamp01((FarMetres - distance) / (FarMetres - NearMetres));
                Color colour = red;
                if (stale) colour.a *= 0.5f;
                Place(ref shown, bearing, near, colour);
            }
        }
        for (int i = shown; i < Arcs.Count; i++)
            if (Arcs[i].enabled) Arcs[i].enabled = false;
    }

    // One arc centred on a bearing; nearer missiles get a longer, thicker, brighter, faster-flashing arc.
    private static void Place(ref int index, float bearing, float near, Color colour)
    {
        Image arc = ArcImage(index++);
        Sprite[] rings = RingSprites();
        arc.sprite = rings[near > 0.66f ? 2 : near > 0.33f ? 1 : 0];
        float span = Mathf.Lerp(22f, 72f, near);
        arc.fillAmount = span / 360f;
        arc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -(bearing - span * 0.5f));
        float rate = Mathf.Lerp(3f, 12f, near);
        float flash = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * rate);
        colour.a *= Mathf.Lerp(0.45f, 1f, near) * flash;
        arc.color = colour;
        if (!arc.enabled) arc.enabled = true;
    }

    // The game's own idea of where the missile is: live while its marker is current, else the
    // faction's last known position (shown at half strength). False when it is not known at all.
    private static bool KnownPosition(CombatHUD hud, Aircraft aircraft, Missile missile, out GlobalPosition at, out bool stale)
    {
        at = default;
        stale = false;
        if (!hud.TryGetMarker(missile, out HUDUnitMarker marker) || marker == null) return false;
        if (!marker.outdated) { at = missile.GlobalPosition(); return true; }
        stale = true;
        FactionHQ hq = aircraft.NetworkHQ;
        return hq != null && hq.TryGetKnownPosition(missile, out at);
    }

    // Multiplayer with another human player not on your faction (checked once a second).
    private static bool IsPvp()
    {
        if (Time.unscaledTime < _nextPvpCheck) return _pvp;
        _nextPvpCheck = Time.unscaledTime + 1f;
        _pvp = false;
        if (GameManager.gameState != GameState.Multiplayer) return false;
        GameManager.GetLocalPlayer(out Player local);
        FactionHQ mine = local != null ? local.HQ : null;
        if (mine == null) { _pvp = true; return true; }   // not known yet: stay hidden
        foreach (Player player in UnitRegistry.playerLookup.Values)
            if (player != null && player != local && player.HQ != mine) { _pvp = true; break; }
        return _pvp;
    }

    // White rings (the colour comes from the Image), drawn once: the arc is a slice of one.
    private static Sprite[] RingSprites()
    {
        if (_ringSprites != null && _ringSprites[0] != null) return _ringSprites;
        _ringSprites = new[] { MakeRing(7f), MakeRing(12f), MakeRing(19f) };
        return _ringSprites;
    }

    private static Sprite MakeRing(float thickness)
    {
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float outer = size * 0.5f - 1.5f, inner = outer - thickness;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(outer - r + 0.5f) * Mathf.Clamp01(r - inner + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>The missile arcs are on screen in this style (the missile mod's arrows then step aside).</summary>
    internal static bool MissileArcsShowing => _arcs != null && _root != null && _root.activeInHierarchy
        && !ElementLooks.Get(MissileArcsItem).Hidden;
}
