using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPlus.Helpers;

// Sorts game messages for the Arcade HUD, by the game method that sent them:
//   scripted mission text           -> subtitle (priority, queued)
//   player chat, server messages    -> subtitle when free, otherwise native log only
//   HQ alerts (nuclear thresholds),
//   nuclear launch, airbase capture -> MISSION UPDATE banner
//   everything else (joins, kills…) -> native log only
// The native log always receives every message; in Arcade it is only hidden until chat is opened.
internal static class ArcadeMessages
{
    private enum Kind { Other, Chat, Server, Mission, Update }

    private static Kind _source;
    private static object[] _sourceArgs;

    internal static void BeginSource(string method, object[] args)
    {
        _source = method.StartsWith("UserCode_TargetReceiveMessage") ? Kind.Chat
            : method.Contains("ServerMessage") ? Kind.Server
            : method == "ShowMessgeLocal" ? Kind.Mission
            : method == "HQMessageInternal" ? Kind.Update
            : Kind.Other;
        _sourceArgs = args;
    }

    internal static void EndSource()
    {
        _source = Kind.Other;
        _sourceArgs = null;
    }

    private static MethodInfo _chatColour;

    internal static void OnGameMessage(string message)
    {
        if (string.IsNullOrEmpty(message) || !ArcadeHud.MessagesActive) return;
        Kind kind = _source;
        // Two alerts come from places that cannot be tagged (an async method and a sync-var
        // callback); they are fixed game strings, so they are recognised by text.
        if (kind == Kind.Other && (message.StartsWith("Nuclear weapon launched") || message.Contains(" has been captured by ")))
            kind = Kind.Update;

        switch (kind)
        {
            case Kind.Chat:
                QueueChat(message);
                break;
            case Kind.Server:
                ArcadeHud.QueueSubtitle("SERVER", new Color(0.55f, 0.8f, 1f), message, false);
                break;
            case Kind.Mission:
                foreach (string line in message.Split('\n'))
                    if (line.Trim().Length > 0) ArcadeHud.QueueSubtitle("", Color.white, line.Trim(), true);
                break;
            case Kind.Update:
                ArcadeHud.ShowBanner(message.Trim());
                break;
        }
    }

    // Chat arrives already formatted as "<link><color>(ally)Name:</color></link> text". Take the
    // speaker and colour from the sender instead, and the text from after the name.
    private static void QueueChat(string message)
    {
        if (_sourceArgs == null || _sourceArgs.Length < 4 || !(_sourceArgs[2] is Player player)) return;
        bool allChat = _sourceArgs[3] is bool b && b;
        string name = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard).ProfanityFilter();
        string body = message;
        int at = message.IndexOf(name + ":", System.StringComparison.Ordinal);
        if (at >= 0)
        {
            int space = message.IndexOf(' ', at + name.Length + 1);
            if (space >= 0) body = message.Substring(space + 1);
        }
        Color colour = Color.white;
        ChatManager chat = NetworkSceneSingleton<ChatManager>.i;
        _chatColour ??= AccessTools.Method(typeof(ChatManager), "GetTextColor");
        if (chat != null && _chatColour != null)
        {
            try { colour = (Color)_chatColour.Invoke(chat, new object[] { player.HQ, allChat }); }
            catch { colour = Color.white; }
        }
        ArcadeHud.QueueSubtitle(allChat ? name : "(ally) " + name, colour, body, false);
    }
}

internal static partial class ArcadeHud
{
    // ---- subtitle (top centre, Ace Combat radio style) ------------------------------------------

    private sealed class Subtitle
    {
        internal string Speaker, Body;
        internal Color SpeakerColour;
        internal float Duration;
        internal bool Mission;
        internal bool Voice;   // a WSO Yappinator voice line: lowest priority
    }

    private static readonly List<Subtitle> SubtitleQueue = new();
    private static Subtitle _current;
    private static float _currentShown;
    private static RectTransform _subtitle;
    private static TextMeshProUGUI _speaker, _line;
    private static CanvasGroup _subtitleGroup;
    private static Vector2 SubtitleBase = new(0f, -36f);   // set per style by ApplyStyleBases
    private static readonly Color Chevron = new(0.35f, 0.75f, 1f);

    internal static bool MessagesActive => _root != null && _root.activeSelf
        && (!_plus || (PlusSubtitles != null && PlusSubtitles.Value));

    internal static void QueueSubtitle(string speaker, Color colour, string body, bool mission)
    {
        float chars = body.Length;
        var item = new Subtitle
        {
            Speaker = speaker, SpeakerColour = colour, Body = body, Mission = mission,
            Duration = mission ? Mathf.Clamp(2.5f + chars * 0.055f, 3f, 9f) : Mathf.Clamp(2f + chars * 0.05f, 2.5f, 7f),
        };
        if (mission)
        {
            // Dialogue always gets shown; keep a short queue and drop the oldest waiting line.
            int waiting = 0;
            foreach (Subtitle s in SubtitleQueue) if (s.Mission) waiting++;
            if (waiting >= 4)
            {
                int oldest = SubtitleQueue.FindIndex(s => s.Mission);
                if (oldest >= 0) SubtitleQueue.RemoveAt(oldest);
            }
            // Mission dialogue takes over from waiting chat; chat stays in the native log.
            SubtitleQueue.RemoveAll(s => !s.Mission);
            SubtitleQueue.Add(item);
            return;
        }
        // Chat uses the subtitle only when no dialogue is showing or waiting, and never piles up:
        // a busy lobby keeps the newest line, older ones remain in the native log.
        if ((_current != null && _current.Mission) || SubtitleQueue.Exists(s => s.Mission)) return;
        SubtitleQueue.RemoveAll(s => !s.Mission);
        SubtitleQueue.Add(item);
    }

    /// <summary>
    /// A voice line (WSO Yappinator) as a subtitle. Lowest priority: shown at once, replacing an
    /// earlier voice line, but only when no chat or mission text is showing or waiting; any such
    /// text replaces it immediately.
    /// </summary>
    internal static void QueueVoice(string speaker, string body, float seconds)
    {
        if (!MessagesActive || _subtitle == null || string.IsNullOrEmpty(body)) return;
        float now = Time.unscaledTime;
        bool busy = SubtitleQueue.Count > 0
                    || (_current != null && !_current.Voice && now - _currentShown < _current.Duration);
        if (busy) return;
        ColorTheme theme = ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        _current = new Subtitle
        {
            Speaker = speaker, Body = body, Voice = true, Duration = Mathf.Max(1f, seconds) + 0.4f,
            SpeakerColour = theme != null ? theme.ChatFriendly : Chevron,
        };
        _currentShown = now;
        ShowSubtitleText(_current);
    }

    private static void BuildMessages()
    {
        TMP_FontAsset font = ChatFont();
        _subtitle = Rect("Subtitle", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), SubtitleBase,
            new Vector2(1000f, 80f));
        _subtitleGroup = _subtitle.gameObject.AddComponent<CanvasGroup>();
        _speaker = Text("Speaker", _subtitle, 20, TextAlignmentOptions.Center);
        _line = Text("Line", _subtitle, 22, TextAlignmentOptions.Center);
        foreach (TextMeshProUGUI t in new[] { _speaker, _line })
        {
            if (font != null) { t.font = font; t.fontSharedMaterial = font.material; }
            t.rectTransform.anchorMin = new Vector2(0f, 1f);
            t.rectTransform.anchorMax = new Vector2(1f, 1f);
            t.rectTransform.pivot = new Vector2(0.5f, 1f);
        }
        _speaker.rectTransform.sizeDelta = new Vector2(0f, 26f);
        _speaker.rectTransform.anchoredPosition = Vector2.zero;
        _line.enableWordWrapping = true;
        _line.color = Color.white;
        _line.rectTransform.sizeDelta = new Vector2(0f, 54f);
        _line.rectTransform.anchoredPosition = new Vector2(0f, -26f);
        AddShadow(_speaker);
        AddShadow(_line);
        _subtitleGroup.alpha = 0f;
        _current = null;
        SubtitleQueue.Clear();

        BuildBanner(font);
    }

    // The game's chat font reads better than the monospace HUD font for sentences.
    private static TMP_FontAsset ChatFont()
    {
        MessageUI ui = SceneSingleton<MessageUI>.i;
        var text = ui != null ? AccessTools.Field(typeof(MessageUI), "messageText")?.GetValue(ui) as TextMeshProUGUI : null;
        return text != null ? text.font : null;
    }

    private static void AddShadow(Graphic g)
    {
        var shadow = g.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private static bool _subtitlePreview, _bannerPreview;

    private static void ShowSubtitleText(Subtitle s)
    {
        _speaker.text = s.Speaker;
        _speaker.color = s.SpeakerColour;
        string c = "#" + ColorUtility.ToHtmlStringRGB(Chevron);
        _line.text = $"<color={c}><noparse><<</noparse></color> {s.Body} <color={c}><noparse>>></noparse></color>";
    }

    private static void UpdateSubtitle()
    {
        if (_subtitle == null) return;
        float now = Time.unscaledTime;
        // In the layout editor, show a sample so the subtitle can be placed.
        if (HudLayoutEditor.IsOpen)
        {
            if (!_subtitlePreview)
                ShowSubtitleText(new Subtitle { Speaker = "SPEAKER", SpeakerColour = Chevron, Body = "Subtitle preview" });
            _subtitlePreview = true;
            _subtitleGroup.alpha = 1f;
            return;
        }
        if (_subtitlePreview)
        {
            _subtitlePreview = false;
            if (_current != null) ShowSubtitleText(_current);
            else _subtitleGroup.alpha = 0f;
        }
        bool expired = _current == null || now - _currentShown > _current.Duration;
        // Dialogue may cut a chat line short (after a moment), as Ace Combat's radio does.
        bool cut = _current != null && !_current.Mission && SubtitleQueue.Count > 0
                   && (_current.Voice || (SubtitleQueue[0].Mission && now - _currentShown > 1.2f));
        if ((expired || cut) && SubtitleQueue.Count > 0)
        {
            _current = SubtitleQueue[0];
            SubtitleQueue.RemoveAt(0);
            _currentShown = now;
            ShowSubtitleText(_current);
        }
        else if (expired)
        {
            _current = null;
        }
        // Quick fade in and out.
        float target = _current != null && now - _currentShown < _current.Duration ? 1f : 0f;
        _subtitleGroup.alpha = Mathf.MoveTowards(_subtitleGroup.alpha, target, Time.unscaledDeltaTime * 6f);
    }

    // ---- MISSION UPDATE banner ----------------------------------------------------------------

    private static RectTransform _banner;
    private static Image _bannerBack;

    // MISSION UPDATE banner looks. Panel: a see-through panel in the HUD colour with dark text.
    // Outline: a thin box in the HUD colour with the text in the HUD colour (Project Wingman's
    // SYSTEM / MISSION START). Cutout: a solid panel with the letters cut out of it (kept for the
    // planned per-element options in the editor).
    internal enum BannerLook { Panel, Outline, Cutout }
    private static BannerLook _bannerLook;
    private static int _builtBannerSkin;
    private static TextMeshProUGUI _bannerHeader, _bannerBody;
    private static CanvasGroup _bannerGroup;
    private static readonly Queue<string> BannerQueue = new();
    private static string _bannerText;
    private static float _bannerShown, _bannerDuration;
    private static readonly Vector2 BannerBase = new(0f, 205f);

    internal static void ShowBanner(string text)
    {
        if (BannerQueue.Count >= 3) BannerQueue.Dequeue();
        BannerQueue.Enqueue(text);
    }

    private static void BuildBanner(TMP_FontAsset font)
    {
        _banner = Rect("MissionUpdate", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), BannerBase,
            new Vector2(420f, 70f));
        _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
        _builtBannerSkin = ElementLooks.SkinOf(BannerItem);
        _bannerLook = _builtBannerSkin switch
        {
            1 => BannerLook.Panel,
            2 => BannerLook.Outline,
            3 => BannerLook.Cutout,
            _ => _wingman ? BannerLook.Outline : BannerLook.Panel,
        };
        _bannerBack = null;
        if (_bannerLook == BannerLook.Panel)
        {
            _bannerBack = _banner.gameObject.AddComponent<Image>();
            _bannerBack.raycastTarget = false;
        }
        else if (_bannerLook == BannerLook.Outline) Frame(_banner, Green, 1.5f);
        // Near-black on the HUD-coloured panel; the HUD colour itself on an outline.
        Color ink = _bannerLook == BannerLook.Outline ? Green : new Color(0.02f, 0.03f, 0.03f, 1f);
        _bannerHeader = Text("Header", _banner, 14, TextAlignmentOptions.Center);
        _bannerHeader.text = "MISSION UPDATE";
        _bannerHeader.characterSpacing = 8f;
        _bannerHeader.color = ink;
        _bannerHeader.rectTransform.anchorMin = new Vector2(0f, 1f);
        _bannerHeader.rectTransform.anchorMax = new Vector2(1f, 1f);
        _bannerHeader.rectTransform.pivot = new Vector2(0.5f, 1f);
        _bannerHeader.rectTransform.sizeDelta = new Vector2(0f, 22f);
        _bannerHeader.rectTransform.anchoredPosition = new Vector2(0f, -4f);
        _bannerBody = Text("Body", _banner, 19, TextAlignmentOptions.Center);
        if (font != null) { _bannerBody.font = font; _bannerBody.fontSharedMaterial = font.material; }
        _bannerBody.color = ink;
        _bannerBody.enableWordWrapping = true;
        _bannerBody.rectTransform.anchorMin = new Vector2(0f, 0f);
        _bannerBody.rectTransform.anchorMax = new Vector2(1f, 1f);
        _bannerBody.rectTransform.offsetMin = new Vector2(16f, 8f);
        _bannerBody.rectTransform.offsetMax = new Vector2(-16f, -26f);
        // The HUD font's material is additive (it only brightens): dark text drawn with it is
        // invisible. Use the fonts' normal materials.
        if (_bannerHeader.font != null) _bannerHeader.fontSharedMaterial = _bannerHeader.font.material;
        if (_bannerBody.font != null) _bannerBody.fontSharedMaterial = _bannerBody.font.material;
        if (_bannerLook == BannerLook.Cutout) MakeBannerCutout();
        _bannerGroup.alpha = 0f;
        _bannerText = null;
        BannerQueue.Clear();
    }

    // Wingman's banner: a solid panel in the HUD colour with the words cut out of it, so the scene
    // shows through the letters. Done with the stencil buffer: the text writes a stencil mark
    // without drawing colour, the panel (drawn after it) skips marked pixels, and a last invisible
    // pass clears the mark so nothing else on screen is affected.
    private static void MakeBannerCutout()
    {
        foreach (TextMeshProUGUI t in new[] { _bannerHeader, _bannerBody })
        {
            Material m = StencilMaterial(t.fontSharedMaterial, CompareAlways, OpReplace, 0);
            m.EnableKeyword("UNITY_UI_ALPHACLIP");   // only the letters themselves, not their boxes
            t.fontSharedMaterial = m;
        }
        Material ui = Canvas.GetDefaultCanvasMaterial();
        _bannerBack = CutoutLayer("Panel", StencilMaterial(ui, CompareNotEqual, OpKeep, 15));
        CutoutLayer("Clear", StencilMaterial(ui, CompareAlways, OpZero, 0)).color = Color.white;
    }

    private const int CompareAlways = 8, CompareNotEqual = 6, OpKeep = 0, OpZero = 1, OpReplace = 2;

    private static Material StencilMaterial(Material source, int compare, int op, int colourMask)
    {
        var m = new Material(source);
        m.SetFloat("_Stencil", 1f);
        m.SetFloat("_StencilComp", compare);
        m.SetFloat("_StencilOp", op);
        m.SetFloat("_StencilReadMask", 255f);
        m.SetFloat("_StencilWriteMask", 255f);
        m.SetFloat("_ColorMask", colourMask);
        return m;
    }

    // A full-size image drawn after the texts (later sibling).
    private static Image CutoutLayer(string name, Material material)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_banner, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.transform.SetAsLastSibling();
        Image image = go.GetComponent<Image>();
        image.material = material;
        image.raycastTarget = false;
        return image;
    }

    // Size to the text: wide enough for short alerts on one line, wrapping long ones.
    private static void ShowBannerText(string text)
    {
        _bannerBody.text = text;
        float width = Mathf.Clamp(_bannerBody.GetPreferredValues(text).x + 40f, 360f, 900f);
        float height = _bannerBody.GetPreferredValues(text, width - 32f, 0f).y + 38f;
        _banner.sizeDelta = new Vector2(width, Mathf.Max(64f, height));
    }

    private static void UpdateBanner()
    {
        if (_banner == null) return;
        float now = Time.unscaledTime;
        // Wingman's cut-out panel is solid; the others are a see-through panel with dark text.
        if (_bannerBack != null)
            _bannerBack.color = new Color(Green.r, Green.g, Green.b, _bannerLook == BannerLook.Cutout ? 1f : 0.62f);
        if (HudLayoutEditor.IsOpen)
        {
            if (!_bannerPreview) ShowBannerText("Mission update preview");
            _bannerPreview = true;
            _bannerGroup.alpha = 1f;
            return;
        }
        if (_bannerPreview)
        {
            _bannerPreview = false;
            if (_bannerText != null) ShowBannerText(_bannerText);
            else _bannerGroup.alpha = 0f;
        }
        bool expired = _bannerText == null || now - _bannerShown > _bannerDuration;
        if (expired && BannerQueue.Count > 0)
        {
            _bannerText = BannerQueue.Dequeue();
            _bannerShown = now;
            _bannerDuration = Mathf.Clamp(5f + _bannerText.Length * 0.04f, 5f, 10f);
            ShowBannerText(_bannerText);
        }
        else if (expired)
        {
            _bannerText = null;
        }
        float target = _bannerText != null && now - _bannerShown < _bannerDuration ? 1f : 0f;
        _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, target, Time.unscaledDeltaTime * 5f);
    }

    // ---- native chat log: hidden in Arcade until chat is opened ----------------------------------

    private static CanvasGroup _nativeLogGroup;
    private static bool _ownsNativeLogGroup;

    private static void HideNativeLog(bool arcade)
    {
        MessageUI ui = SceneSingleton<MessageUI>.i;
        var box = ui != null ? AccessTools.Field(typeof(MessageUI), "messageBackground")?.GetValue(ui) as GameObject : null;
        var chat = ui != null ? AccessTools.Field(typeof(MessageUI), "chat")?.GetValue(ui) as Component : null;
        if (!arcade || box == null)
        {
            if (_nativeLogGroup != null)
            {
                if (_ownsNativeLogGroup) Object.Destroy(_nativeLogGroup);
                else _nativeLogGroup.alpha = 1f;
            }
            _nativeLogGroup = null;
            return;
        }
        if (_nativeLogGroup == null || _nativeLogGroup.gameObject != box)
        {
            _nativeLogGroup = box.GetComponent<CanvasGroup>();
            _ownsNativeLogGroup = _nativeLogGroup == null;
            if (_ownsNativeLogGroup) _nativeLogGroup = box.AddComponent<CanvasGroup>();
        }
        bool open = chat != null && chat.gameObject.activeInHierarchy;
        _nativeLogGroup.alpha = open ? 1f : 0f;
    }

    private static void UpdateMessages()
    {
        if (_plus && !(PlusSubtitles != null && PlusSubtitles.Value))
        {
            // Plus without subtitles: the game's own chat and messages, as normal.
            HideNativeLog(false);
            UpdateCallout();
            return;
        }
        // Plus keeps the game's event feed even when its optional subtitles are enabled.
        HideNativeLog(!_plus);
        UpdateSubtitle();
        UpdateBanner();
        UpdateCallout();
    }
}
