using BepInEx.Configuration;

namespace UIPlus.Menu;

internal static class MenuSettings
{
    internal const string Section = "Main Menu";
    internal static ConfigEntry<MenuStyle> Style;
    internal static ConfigEntry<MenuTips> Tips;
    internal static ConfigEntry<MenuNews> News;
    internal static ConfigEntry<MenuLogo> Logo;
    internal static ConfigEntry<MenuAccent> Accent;
    internal static ConfigEntry<bool> CustomBackground, BackgroundVideoAudio;
    internal static ConfigEntry<float> BackgroundImageSeconds, BackgroundFadeSeconds;
    internal static ConfigEntry<BackgroundTransition> BackgroundTransition;
    internal static ConfigEntry<BackgroundAudioOutput> BackgroundVideoAudioOutput;

    internal static void Bind(ConfigFile config)
    {
        Style = config.Bind(Section, "MenuStyle", MenuLayout.DefaultStyle,
            "Title menu style. Changes apply on the title screen when the settings menu closes.");
        Tips = config.Bind(Section, "MenuTips", MenuLayout.DefaultTips, "Show, auto-hide, or hide the title menu tips in custom menu styles.");
        News = config.Bind(Section, "MenuNews", MenuLayout.DefaultNews, "Show the version and links, collapse them behind the version, or auto-hide them in custom menu styles.");
        Logo = config.Bind(Section, "MenuLogo", MenuLayout.DefaultLogo, "Show or auto-hide the title logo in custom menu styles.");
        Accent = config.Bind(Section, "MenuAccent", MenuLayout.DefaultAccent, "Highlight bar colour in custom menu styles. HUD colour follows the game's Accessibility theme.");
        CustomBackground = config.Bind(Section, "CustomBackground", true, "Use images and videos from BepInEx/plugins/UIPlus/Backgrounds across the title and supported menu screens.");
        BackgroundImageSeconds = config.Bind(Section, "BackgroundImageSeconds", 12f,
            new ConfigDescription("Seconds each image stays on screen.", new AcceptableValueRange<float>(3f, 120f)));
        BackgroundTransition = config.Bind(Section, "BackgroundTransition", Menu.BackgroundTransition.Crossfade,
            "How the title screen transitions between backgrounds.");
        BackgroundFadeSeconds = config.Bind(Section, "BackgroundFadeSeconds", 1f,
            new ConfigDescription("Duration of a background transition in seconds.", new AcceptableValueRange<float>(0.2f, 3f)));
        BackgroundVideoAudio = config.Bind(Section, "BackgroundVideoAudio", true,
            "Play title video sound at the game's music volume.");
        BackgroundVideoAudioOutput = config.Bind(Section, "BackgroundVideoAudioOutput", BackgroundPlayback.DefaultAudioOutput,
            "Smooth helps audio keep playing through loading hitches. Mixer routes it through the game's music channel. Applies when a video is prepared.");
    }
}
