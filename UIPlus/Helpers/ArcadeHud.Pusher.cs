using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

internal static partial class ArcadeHud
{
    private static bool _hasPusher, _pusherHorizontal;
    private static RectTransform _pusherFill, _pusherNeedle;
    private static Image _pusherInk;
    private static TextMeshProUGUI _pusherReverse, _pusherIdle;
    private static float _pusherLength, _pusherThickness;

    private static void BuildPusherBar(RectTransform parent, Vector2 anchor, Vector2 at, float length, float thickness,
        bool horizontal, Color background, bool sharedRuler = false)
    {
        Vector2 centre = new(0.5f, 0.5f);
        Vector2 size = horizontal ? new Vector2(length, thickness) : new Vector2(thickness, length);
        RectTransform track = Rect("Pusher throttle", parent, anchor, centre, at, size);
        Fill(track, background);
        _pusherFill = Rect("Signed fill", track, centre, centre, Vector2.zero, Vector2.zero);
        Fill(_pusherFill, Faded(0.85f));
        _pusherInk = _pusherFill.GetComponent<Image>();
        if (sharedRuler)
        {
            // Keep the ruler's centre prong and ticks visible over the mirrored fill.
            track.SetAsFirstSibling();
        }
        else
        {
            Vector2 tick = horizontal ? new Vector2(1.5f, thickness + 5f) : new Vector2(thickness + 5f, 1.5f);
            Fill(Rect("Zero tick", track, centre, centre, Vector2.zero, tick), Green);
        }
        _pusherHorizontal = horizontal;
        _pusherLength = length;
        _pusherThickness = thickness;
    }

    private static void BuildPusherTape(RectTransform throttle)
    {
        BuildPusherBar(throttle, new Vector2(0.5f, 0.5f), new Vector2(-TapeFillOffset, 0f), GaugeHeight,
            TapeFillWidth, false, Faded(0.24f), sharedRuler: true);
    }

    private static void BuildPusherHullStrip(RectTransform parent)
    {
        BuildPusherBar(parent, new Vector2(0f, 1f), new Vector2(HullStart + HullLength * 0.5f, -37f),
            HullLength, 3f, true, Faded(0.45f));
        var caption = Text("Pusher label", parent, 11f, TextAlignmentOptions.Left);
        caption.text = "PROP";
        caption.characterSpacing = 3f;
        caption.color = Faded(0.85f);
        Pin(caption.rectTransform, new Vector2(0f, 1f), new Vector2(50f, 14f), new Vector2(0f, -31f));
        _pusherIdle = Text("Pusher idle", parent, 11f, TextAlignmentOptions.Left);
        _pusherIdle.text = "0%";
        _pusherIdle.color = Faded(0.85f);
        Pin(_pusherIdle.rectTransform, new Vector2(0f, 1f), new Vector2(50f, 14f),
            new Vector2(HullStart + HullLength + 18f, -31f));
    }

    private static void BuildPusherDial(RectTransform engine)
    {
        Sprite face = DialSprite("pusher", () =>
        {
            var art = new DialArt(96);
            art.Disc(0.94f, FaceColour);
            art.Ring(0.88f, 1f, BezelTop, BezelBottom);
            for (int angle = -60; angle <= 60; angle += 30)
                art.Tick(angle, angle == 0 ? 0.63f : 0.73f, 0.83f, 2.5f, Marking);
            return art.ToSprite("Pusher");
        });
        var dial = Rect("Pusher sub-dial", engine, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 34f), new Vector2(28f, 28f));
        Picture(dial, face);
        _pusherNeedle = NeedleOn(dial, "pusher", 9f, 2.5f, 1.05f, Marking);
        Hub(dial, 4f);
        DialText(dial, "PROP", 7f, new Vector2(0f, -17f), MarkingDim, 23f);
        _pusherReverse = DialText(dial, "REV", 7f, new Vector2(0f, -8f), Marking, 20f);
        _pusherReverse.gameObject.SetActive(false);
    }

    private static void UpdatePusher(Aircraft aircraft)
    {
        if (_pusherFill == null && _pusherNeedle == null) return;
        float command = PusherAircraft.Value(aircraft);
        if (_pusherFill != null)
        {
            // The fill's centre sits halfway between zero and the commanded endpoint.
            float span = command * _pusherLength * 0.5f;
            _pusherFill.sizeDelta = _pusherHorizontal
                ? new Vector2(Mathf.Abs(span), _pusherThickness) : new Vector2(_pusherThickness, Mathf.Abs(span));
            _pusherFill.anchoredPosition = _pusherHorizontal ? new Vector2(span * 0.5f, 0f) : new Vector2(0f, span * 0.5f);
            if (!_pusherHorizontal) _pusherInk.color = _throttle.FillImage.color;
            if (_pusherIdle != null && _pusherIdle.gameObject.activeSelf != (command == 0f))
                _pusherIdle.gameObject.SetActive(command == 0f);
        }
        if (_pusherNeedle != null)
        {
            Rotate(_pusherNeedle, 60f * command);
            bool reverse = command < 0f;
            if (_pusherReverse.gameObject.activeSelf != reverse) _pusherReverse.gameObject.SetActive(reverse);
        }
    }
}
