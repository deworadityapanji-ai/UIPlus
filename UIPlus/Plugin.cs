using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UIPlus.Helpers;

namespace UIPlus;

// UI Plus: the HUD half of what used to be ArcadeCam. HUD styles, the layout editor, the HUD in
// chase view, the targeting screen, callouts, kill feed, subtitles and the mod integrations.
// Works on its own with the game's chase camera; with ArcadeCam installed it also hides the
// targeting screen during the weapon camera and shows ArcadeCam's HIT / MISS / DESTROYED callouts.
[BepInDependency(ArcadeCamBridge.Guid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static Plugin Instance;
    private ChaseHudUpdater _hudUpdater;

    // Section and key names match the old ArcadeCam ones, so settings carry over (ConfigMigration).
    internal const string SectionChaseHud = "Chase HUD";

    /// <summary>Kept for the moved code's checks; UI Plus has no master switch of its own.</summary>
    internal static bool IsActive => true;

    public static ConfigEntry<bool> FlightHud;
    public static ConfigEntry<bool> Map;
    public static ConfigEntry<bool> PitchLadder;
    public static ConfigEntry<HudAttachment> Waterline;
    public static ConfigEntry<HudAttachment> LeftInstruments;
    public static ConfigEntry<HudAttachment> RightInstruments;
    public static ConfigEntry<HudAttachment> Compass;
    public static ConfigEntry<bool> VirtualJoystickIndicator;
    public static ConfigEntry<bool> TargetingScreenEnabled;
    public static ConfigEntry<float> TargetingScreenXPercent;
    public static ConfigEntry<float> TargetingScreenYPercent;
    public static ConfigEntry<float> TargetingScreenWidthPercent;
    public static ConfigEntry<bool> KillCallouts;
    public static ConfigEntry<bool> MatchMissileIndicators;
    public static ConfigEntry<bool> FixTargetArrow;
    internal static ConfigEntry<TextHalo.Strength> TextContrast;

    // The action keeps its old ArcadeCam name: its id comes from the name, and the id is what
    // Rewired saves the player's binding under, so an existing binding keeps working.
    public const string ACTION_MAP_ZOOM = "ArcadeCam::MapZoom";
    public static int MapZoomActionId = -1;
    public const string ACTION_HIDE_HUD = "UIPlus::HideHud";
    public static int HideHudActionId = -1;

    private void Awake()
    {
        Logger = base.Logger;
        Instance = this;
        ConfigMigration.ImportFromArcadeCam(Config);

        FlightHud = Config.Bind(SectionChaseHud, "FlightHud", true,
            "Keep the flight HUD on in chase view. Vanilla switches it off on entering chase.");
        Map = Config.Bind(SectionChaseHud, "Map", true,
            "Keep the minimap alive in chase view. Needs FlightHud.");
        PitchLadder = Config.Bind(SectionChaseHud, "PitchLadder", false,
            "Show the pitch ladder. Its rungs and its horizon line are one scrolling texture, so they come and go together.");
        Waterline = BindHudAttachment("Waterline", HudAttachment.Aircraft,
            "Placement of the aircraft datum marker: Aircraft, Camera (screen-fixed), or Hidden. Camera placement is an arcade view marker, not the actual nose direction.");
        LeftInstruments = BindHudAttachment("LeftInstruments", HudAttachment.Aircraft,
            "Placement of airspeed, AoA, Mach, g and fuel: Aircraft, Camera (screen-fixed), or Hidden.");
        RightInstruments = BindHudAttachment("RightInstruments", HudAttachment.Aircraft,
            "Placement of throttle, RPM, altitude and climb rate: Aircraft, Camera (screen-fixed), or Hidden.");
        Compass = BindHudAttachment("Compass", HudAttachment.Aircraft,
            "Placement of the heading ribbon and bearing readout: Aircraft, Camera (screen-fixed), or Hidden. Readings remain aircraft heading.");
        VirtualJoystickIndicator = Config.Bind(SectionChaseHud, "VirtualJoystickIndicator", false,
            "Show the virtual joystick's stretching line and the dot on its end. Drawing only — the stick keeps working either way.");

        TargetingScreenEnabled = Config.Bind("Targeting Screen", "TargetingScreenEnabled", true,
            "Show the aircraft's native cockpit targeting-camera feed on the chase HUD when a target is selected. Uses the same sensor and overlay; hides when the native camera is unavailable.");
        TargetingScreenXPercent = Config.Bind("Targeting Screen", "TargetingScreenXPercent", 100f,
            new ConfigDescription("Horizontal panel position: 0 is the left edge, 100 the right edge. The full panel stays inside the view.", new AcceptableValueRange<float>(0f, 100f)));
        TargetingScreenYPercent = Config.Bind("Targeting Screen", "TargetingScreenYPercent", 25f,
            new ConfigDescription("Vertical panel position: 0 is the top, 100 the bottom. Default places it in the upper right, below weapon selection.", new AcceptableValueRange<float>(0f, 100f)));
        TargetingScreenWidthPercent = Config.Bind("Targeting Screen", "TargetingScreenWidthPercent", 24f,
            new ConfigDescription("Panel width as a percentage of the view. The native feed's aspect ratio is preserved.", new AcceptableValueRange<float>(10f, 50f)));

        KillCallouts = Config.Bind("HUD Layout", "KillCallouts", true,
            "Arcade HUD style: show DESTROYED for your kills, HIT when your missile damages its target, MISS when it does not. Needs ArcadeCam, which judges the hits.");
        Integrations.Bind(Config);
        MatchMissileIndicators = Integrations.MissileIndicators.Enabled;
        FixTargetArrow = Config.Bind("Compatibility", "FixTargetArrow", true,
            "Fix a game bug: the HUD's target arrow (and other off-screen markers) pointed to the wrong side when the target was behind you (more than 90 degrees off the nose). Off restores the game's own behaviour.");
        TextContrast = Config.Bind("HUD Layout", "TextContrast", TextHalo.Strength.Soft,
            "High contrast: a soft dark halo behind the HUD text so it stays readable over bright sky and clouds, without changing its look. Soft is barely visible on a dark background; Strong for very bright scenes. Arcade, Wingman and Thunder (Dashboard's strip is already dark).");
        HudViews.Bind(Config);
        HudLayoutEditor.Bind(Config, SectionChaseHud);
        HudElementPositions.Bind(Config);
        MinimapZoom.Bind(Config);

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        HudElements.Initialize();
        _hudUpdater = gameObject.AddComponent<ChaseHudUpdater>();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private ConfigEntry<HudAttachment> BindHudAttachment(string key, HudAttachment fallback, string description)
    {
        var definition = new ConfigDefinition(SectionChaseHud, key);
        bool save = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            // Very old configs held booleans here: true reads as Aircraft, false as Hidden.
            string previous = Config.Bind(definition, fallback.ToString(), new ConfigDescription(description)).Value;
            HudAttachment mode = HudAttachmentMigration.Parse(previous, fallback);
            Config.Remove(definition);
            return Config.Bind(definition, mode, new ConfigDescription(description));
        }
        finally
        {
            Config.SaveOnConfigSet = save;
            if (save) Config.Save();
        }
    }

    private void OnGUI()
    {
        if (HudLayoutEditor.IsOpen) HudLayoutEditor.Draw();
        else TargetingScreenOverlay.Draw();
    }

    private void Update()
    {
        HudLayoutEditor.Update();
        HudHide.Poll();
        ArcadeCamBridge.TryConnect();
    }

    private void LateUpdate() => HudLayoutEditor.RefreshCursor();

    private void OnDestroy()
    {
        HudLayoutEditor.Close(false);
        ArcadeCamBridge.Disconnect();
        HudHide.Reset();
        if (_hudUpdater != null) Destroy(_hudUpdater);
        HudElements.Dispose();
        TargetingScreenOverlay.Dispose();
    }
}
