using UnityEngine;
using UIPlus.Helpers;
using UIPlus.Menu;

int checks = 0;

void Expect(bool actual, bool expected, string description)
{
    checks++;
    if (actual != expected)
        throw new Exception(description + $": expected {expected}, got {actual}");
}

void Near(float actual, float expected, string description)
{
    checks++;
    if (Math.Abs(actual - expected) > 0.00001f)
        throw new Exception(description + $": expected {expected}, got {actual}");
}

HudCameraView noseView = HudViewPolicy.Resolve(HudCameraView.Other, hudViewShowing: true);
Expect(HudViewPolicy.Shows(noseView, chase: false, orbit: false, cockpit: false, hudView: true), true,
    "ArcadeCam HUD view shows the HUD independently of the chase setting");
Expect(HudViewPolicy.AttachesToCamera(noseView, chase: false, hudView: true), true,
    "ArcadeCam HUD view uses camera attachment");
Expect(HudViewPolicy.Shows(noseView, chase: true, orbit: true, cockpit: true, hudView: false), false,
    "Turning HUD view off suppresses UI Plus even if the other views are enabled");
Expect(HudViewPolicy.AttachesToCamera(noseView, chase: true, hudView: false), false,
    "Turning HUD view off returns camera-attached widgets");
HudCameraView legacyChase = HudViewPolicy.Resolve(HudCameraView.Chase, hudViewShowing: false);
Expect(HudViewPolicy.AttachesToCamera(legacyChase, chase: true, hudView: false), true,
    "With no bridge flag, stock chase still attaches");
Expect(HudViewPolicy.Shows(legacyChase, chase: false, orbit: true, cockpit: true, hudView: true), false,
    "With no bridge flag, the HUD-view switch does not turn on disabled chase");
HudCameraView legacyOther = HudViewPolicy.Resolve(HudCameraView.Other, hudViewShowing: false);
Expect(HudViewPolicy.Shows(legacyOther, chase: true, orbit: true, cockpit: true, hudView: true), false,
    "An unknown camera does not get a HUD with older or absent ArcadeCam");
Expect(HudViewPolicy.Resolve(HudCameraView.Cockpit, hudViewShowing: true) == HudCameraView.HudView, true,
    "The ArcadeCam flag wins even if the nose camera shares a stock camera state");

Near(CapacitorReadout.WholeKj(0f), 0f, "An empty capacitor reads zero kJ");
Near(CapacitorReadout.WholeKj(411.49f), 411f, "Stored energy rounds down below half a kJ");
Near(CapacitorReadout.WholeKj(411.5f), 412f, "Stored energy rounds half a kJ up like the native readout");
Near(CapacitorReadout.WholeKj(900.51f), 901f, "Capacity also uses whole kJ");
float chargeRate = CapacitorReadout.SmoothRate(0f, 6f, 0.5f);
Near(chargeRate, 12f * (1f - MathF.Exp(-1f)), "The capacitor rate smooths over a half-second time constant");
float firstQuarter = CapacitorReadout.SmoothRate(0f, 3f, 0.25f);
Near(CapacitorReadout.SmoothRate(firstQuarter, 3f, 0.25f), chargeRate, "Smoothing is independent of the sample interval");
Near(CapacitorReadout.SmoothRate(chargeRate, 0f, 0f), chargeRate, "A paused physics clock keeps the last valid rate");
Near(CapacitorReadout.Direction(chargeRate), 1f, "Charging points up");
Near(CapacitorReadout.Direction(CapacitorReadout.SmoothRate(chargeRate, -15f, 0.5f)), -1f, "Draining points down");
Near(CapacitorReadout.Direction(CapacitorReadout.SmoothRate(chargeRate, 0f, 2f)), 0f, "The indicator fades out once the capacitor stops changing");
Near(CapacitorReadout.Direction(-0.99f), 0f, "Small negative rates are hidden too");

Rect nozzleRect = new(-50f, -50f, 100f, 100f);
var (nozzleFootprint, nozzleScale) = HudTrayLayout.Fit(HudTrayLayout.Measure(nozzleRect), 150f);
Near(nozzleScale, 1.5f, "The Vortex nozzle fits the same slot from its authored 100 by 100 rect");
Near(nozzleFootprint.width, 150f, "The fitted nozzle width uses the authored extent");
Near(nozzleFootprint.center.y, 0f, "The authored centre stays stationary throughout nozzle rotation");
Expect(HudTrayLayout.SameRect(nozzleRect, new Rect(-50f, -50f, 100.0001f, 100f)), true,
    "Subpixel differences from reparenting do not invalidate a tray measurement");
Expect(HudTrayLayout.SameRect(nozzleRect, new Rect(-60f, -50f, 120f, 100f)), false,
    "A real authored resize invalidates the tray measurement");
var (offsetFootprint, _) = HudTrayLayout.Fit(HudTrayLayout.Measure(new Rect(0f, -20f, 200f, 80f)), 150f);
Near(offsetFootprint.center.x, 75f, "The tray accounts for a gauge whose authored pivot is at an edge");
Near(offsetFootprint.height, 60f, "The tray preserves the aspect ratio of a non-square gauge");
Near(HudTrayLayout.Measure(new Rect(0f, 0f, 0f, 0f)).width, 100f,
    "A transform-only gauge gets a predictable footprint");

Expect(KillFeedFilter.Shows(KillFeedMode.All, KillFeedEvent.Kill, false, false, false), true,
    "The extra switches do not suppress ordinary kills");
Expect(KillFeedFilter.Shows(KillFeedMode.Mine, KillFeedEvent.Kill, false, true, true), false,
    "Mine hides unrelated kills");
Expect(KillFeedFilter.Shows(KillFeedMode.All, KillFeedEvent.Intercept, false, true, false), true,
    "All includes unrelated intercepts when enabled");
Expect(KillFeedFilter.Shows(KillFeedMode.Mine, KillFeedEvent.Intercept, false, true, true), false,
    "Mine hides unrelated intercepts");
Expect(KillFeedFilter.Shows(KillFeedMode.Mine, KillFeedEvent.Intercept, true, true, false), true,
    "Mine includes local intercepts");
Expect(KillFeedFilter.Shows(KillFeedMode.Mine, KillFeedEvent.Intercept, true, false, true), false,
    "The intercept switch hides even local intercepts");
Expect(KillFeedFilter.Shows(KillFeedMode.All, KillFeedEvent.Player, false, false, true), true,
    "All shows player notices independently of intercepts");
Expect(KillFeedFilter.Shows(KillFeedMode.Mine, KillFeedEvent.Player, false, false, true), true,
    "Mine still shows other players' join and leave notices");
Expect(KillFeedFilter.Shows(KillFeedMode.All, KillFeedEvent.Player, false, true, false), false,
    "The player switch hides join and leave notices");
foreach (KillFeedEvent kind in Enum.GetValues<KillFeedEvent>())
    Expect(KillFeedFilter.Shows(KillFeedMode.Off, kind, true, true, true), false, $"Off hides {kind} events");

Near(PusherThrottle.Signed(-0.2f), -1f, "Reverse clamps below the input range");
Near(PusherThrottle.Signed(0f), -1f, "Zero raw input is full reverse");
Near(PusherThrottle.Signed(0.235f), -0.5f, "Reverse uses its own half of the axis");
Near(PusherThrottle.Signed(0.47f), 0f, "The reverse dead-band edge is neutral");
Near(PusherThrottle.Signed(0.5f), 0f, "The native idle band stays neutral");
Near(PusherThrottle.Signed(0.53f), 0f, "The forward dead-band edge is neutral");
Near(PusherThrottle.Signed(0.765f), 0.5f, "Forward uses its own half of the axis");
Near(PusherThrottle.Signed(1f), 1f, "One raw input is full forward");
Near(PusherThrottle.Signed(1.2f), 1f, "Forward clamps above the input range");
Near(PusherThrottle.Signed(0.15f, 0.3f, 0.6f), -0.5f, "Other pusher aircraft can use their own reverse band");
Near(PusherThrottle.Signed(0.8f, 0.3f, 0.6f), 0.5f, "Other pusher aircraft can use their own forward band");
Expect(PusherThrottle.Source(PusherThrottle.Gauge(true, true, 0.5f), false) == PusherSource.Gauge, true,
    "A two-sided gauge with an interior neutral point detects a pusher even without a known engine");
Expect(PusherThrottle.Source(PusherThrottle.Gauge(true, false, 0.5f), true) == PusherSource.None, true,
    "An explicitly single-sided gauge takes precedence over the engine fallback");
Expect(PusherThrottle.Source(PusherThrottle.Gauge(true, true, 0f), false) == PusherSource.None, true,
    "The UFO anti-gravity gauge uses an endpoint split even with two image references");
Expect(PusherThrottle.Source(PusherThrottle.Gauge(true, true, 0.25f, 0.25f), false) == PusherSource.None, true,
    "A split with no usable reverse span is not a reversible gauge");
Expect(PusherThrottle.Source(PusherThrottle.Gauge(false, false, null), true) == PusherSource.Engine, true,
    "A known engine can detect a pusher where there is no custom-axis gauge");
Expect(PusherThrottle.Source(PusherThrottle.Gauge(true, true, null), false) == PusherSource.Gauge, true,
    "Two bars can detect a pusher if the band's fields are unavailable");
Expect(PusherThrottle.ShouldRefreshGauge(false, PusherGauge.Missing, false, true), false,
    "A missing gauge waits for the next poll");
Expect(PusherThrottle.ShouldRefreshGauge(false, PusherGauge.Missing, true, true), true,
    "A missing gauge is rechecked during the spawn window");
Expect(PusherThrottle.ShouldRefreshGauge(false, PusherGauge.SingleSided, true, true), true,
    "A single-sided gauge does not prevent discovery of a late reversible gauge");
Expect(PusherThrottle.ShouldRefreshGauge(false, PusherGauge.Missing, true, false), false,
    "Periodic discovery stops after the spawn window");
Expect(PusherThrottle.ShouldRefreshGauge(false, PusherGauge.Reversible, true, true), false,
    "A reversible gauge stays cached on the same HUD");
Expect(PusherThrottle.ShouldRefreshGauge(true, PusherGauge.Missing, false, false), true,
    "A HUD rebuild retries a negative result even after the spawn window");
Expect(PusherThrottle.ShouldRefreshGauge(true, PusherGauge.Reversible, false, false), true,
    "A HUD rebuild validates a previously detected gauge");

int[] petals = new int[WeaponStarLayout.Petals];
const int EmptyPetal = WeaponStarLayout.Empty, LinkPetal = WeaponStarLayout.Link, AutoPetal = WeaponStarLayout.Auto;
WeaponStarLayout.Assign(new[] { 10, 20, 30 }, 20, false, false, petals);
Expect(petals.SequenceEqual(new[] { 10, 20, 30, EmptyPetal, EmptyPetal, EmptyPetal, EmptyPetal, EmptyPetal }), true,
    "Weapons fill the petals clockwise from the top when neither toggle is eligible");
WeaponStarLayout.Assign(new[] { 0, 1, 2, 3, 4, 5 }, 3, true, true, petals);
Expect(petals.SequenceEqual(new[] { LinkPetal, 0, 1, 2, AutoPetal, 3, 4, 5 }), true,
    "LINK is top and AUTO bottom without changing weapon order");
WeaponStarLayout.Assign(new[] { 0, 1, 2, 3, 4, 5, 6 }, 3, false, true, petals);
Expect(petals.SequenceEqual(new[] { 0, 1, 2, 3, AutoPetal, 4, 5, 6 }), true,
    "When only AUTO is eligible the top petal is available to weapons");
WeaponStarLayout.Assign(new[] { 0, 1, 2, 3, 4 }, 3, true, false, petals);
Expect(petals.SequenceEqual(new[] { LinkPetal, 0, 1, 2, 3, 4, EmptyPetal, EmptyPetal }), true,
    "When only LINK is eligible the bottom petal is available to weapons");
WeaponStarLayout.Assign(Array.Empty<int>(), -1, true, false, petals);
Expect(petals[0] == LinkPetal && petals.Skip(1).All(p => p == EmptyPetal), true,
    "A plane with fixed guns and no weapon petals can still link guns");
WeaponStarLayout.Assign(Enumerable.Range(0, 7).ToArray(), 3, true, true, petals);
Expect(petals.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5, 6, EmptyPetal }), true,
    "Overflow drops the toggles and preserves the complete weapon order");
WeaponStarLayout.Assign(Enumerable.Range(0, 8).ToArray(), 3, true, false, petals);
Expect(petals.SequenceEqual(Enumerable.Range(0, 8)), true, "A full star gives every petal to a weapon");
WeaponStarLayout.Assign(Enumerable.Range(0, 10).ToArray(), 1, true, true, petals);
Expect(petals.SequenceEqual(new[] { 8, 9, 0, 1, 2, 3, 4, 5 }), true,
    "Large loadouts retain the same selected-centred weapon window");

Expect(MenuLayout.DefaultStyle == MenuStyle.BottomBar, true, "Bottom bar is the default title menu style");
Expect(MenuLayout.DefaultTips == MenuTips.AutoHide, true, "The default tips mode is Auto Hide");
Expect(MenuLayout.DefaultNews == MenuNews.Compact, true, "The default news mode is Compact");
Expect(MenuLayout.DefaultLogo == MenuLogo.Shown, true, "The default logo mode is Shown");
Expect(MenuLayout.DefaultAccent == MenuAccent.Green, true, "The default Side Rail accent is Green");
Expect(MenuLayout.StyleChange(null, MenuStyle.Vanilla, true) == MenuStyleChange.None, true, "Vanilla leaves the stock menu alone");
Expect(MenuLayout.StyleChange(null, MenuLayout.DefaultStyle, true) == MenuStyleChange.Build, true,
    "The default Bottom bar is built on a fresh title screen");
Expect(MenuLayout.StyleChange(null, MenuStyle.SideRail, true) == MenuStyleChange.Build, true,
    "A saved Side rail builds immediately on a fresh title screen");
Expect(MenuLayout.StyleChange(null, MenuStyle.SideRail, false) == MenuStyleChange.None, true,
    "A fresh title waits for game startup and modal readiness");
Expect(MenuLayout.StyleChange(MenuStyle.SideRail, MenuStyle.SideRail, true) == MenuStyleChange.None, true, "The active style is not rebuilt");
Expect(MenuLayout.StyleChange(MenuStyle.SideRail, MenuStyle.MinimalCorner, false) == MenuStyleChange.None, true, "A style switch waits until the title is ready and the modal closes");
Expect((int)MenuStyle.MinimalCorner == 2 && (int)MenuStyle.BottomBar == 3, true, "Bottom bar appends to the existing config values");
foreach (MenuStyle from in Enum.GetValues<MenuStyle>())
foreach (MenuStyle to in Enum.GetValues<MenuStyle>())
{
    if (from == to) continue;
    var firstChange = from == MenuStyle.Vanilla ? MenuStyleChange.Build : MenuStyleChange.Restore;
    MenuStyle? built = from == MenuStyle.Vanilla ? null : from;
    Expect(MenuLayout.StyleChange(built, to, true) == firstChange, true,
        $"The transition from {from} to {to} starts with the correct build or restore step");
    for (int frame = 0; frame < 2; frame++)
        built = MenuLayout.StyleChange(built, to, true) switch
        {
            MenuStyleChange.Restore => null,
            MenuStyleChange.Build => to,
            _ => built,
        };
    Expect((built ?? MenuStyle.Vanilla) == to && MenuLayout.StyleChange(built, to, true) == MenuStyleChange.None, true,
        $"The transition from {from} to {to} settles after at most two frames");
}

// The video arrives while the logo is parented under the layout. Teardown first rescues
// the logo by appending it, then uses the original following sibling to place it.
var titleSiblings = new List<string> { "background", "video", "LeftPanel", "NewLogo" };
for (int switchCount = 0; switchCount < 3; switchCount++)
{
    int current = titleSiblings.IndexOf("NewLogo");
    int target = MenuLayout.IndexBefore(current, titleSiblings.IndexOf("LeftPanel"), titleSiblings.Count);
    titleSiblings.RemoveAt(current);
    titleSiblings.Insert(target, "NewLogo");
    Expect(titleSiblings.SequenceEqual(new[] { "background", "video", "NewLogo", "LeftPanel" }), true,
        "Restoring the title preserves the late video layer behind the logo and controls");
    titleSiblings.Remove("NewLogo");
    titleSiblings.Add("NewLogo");
}
Expect(MenuLayout.IndexBefore(1, 3, 5) == 2, true, "Moving forward accounts for removing the item first");
Expect(MenuLayout.IndexBefore(1, -1, 5) == 4, true, "A rescued last child returns to the end when its anchors are gone");
Expect(MenuLayout.ChooseFocus(false, false, false, false) == MenuFocus.Keep, true, "An idle mouse leaves the menu unselected");
Expect(MenuLayout.ChooseFocus(true, true, false, false) == MenuFocus.Hovered, true, "Moving onto a row transfers the existing selection");
Expect(MenuLayout.ChooseFocus(true, false, false, false) == MenuFocus.Keep, true, "A stationary pointer or moving off a row leaves selection alone");
Expect(MenuLayout.ChooseFocus(false, false, false, true) == MenuFocus.First, true, "Navigation input can seed an empty selection");
Expect(MenuLayout.ChooseFocus(true, false, false, true) == MenuFocus.Keep, true, "Navigation keeps an existing selection");
Expect(MenuLayout.ChooseFocus(false, false, true, false) == MenuFocus.LastHovered, true, "A background press restores the last valid mouse selection");
Expect(MenuLayout.ChooseFocus(false, true, true, true) == MenuFocus.Hovered, true, "A new mouse target takes priority over a fallback");
int resume = MenuLayout.ButtonOrder("Continue", "");
int load = MenuLayout.ButtonOrder("Load Save", "");
int missions = MenuLayout.ButtonOrder("SinglePlayerButton", "");
Expect(resume < load && load < missions, true, "Save buttons lead the PLAY group in Continue then Load Save order");
Expect(MenuLayout.ButtonOrder("SomeSaveButton", "Continue") == resume, true, "A button caption can identify a save clone");
Expect(MenuLayout.IsLanRoot("StartLanButtonRoot") && MenuLayout.IsLanRoot("JoinLanButtonRoot"), true, "Both LAN roots are detected");
Expect(MenuLayout.Section(MenuLayout.ButtonOrder("JoinLanButtonRoot", "")) == MenuSection.Play, true, "LAN belongs to PLAY");
Expect(MenuLayout.Section(MenuLayout.ButtonOrder("OtherName", "Mission Editor")) == MenuSection.Create, true, "Mission Editor belongs to CREATE");
Expect(MenuLayout.Section(MenuLayout.ButtonOrder("WorkshopButton", "")) == MenuSection.Create, true, "Workshop belongs to CREATE");
Expect(MenuLayout.Section(MenuLayout.ButtonOrder("SettingsButton", "")) == MenuSection.More, true, "Settings belongs to MORE");
Expect(MenuLayout.Section(MenuLayout.ButtonOrder("UnrecognizedModButton", "")) == MenuSection.More, true, "Unrecognized mod buttons stay visible in MORE");

void ExpectMenuOrder(string[] discovered, string[] expected, string description)
{
    string[] ordered = discovered.OrderBy(name => MenuLayout.ButtonOrder(name, "")).ToArray();
    Expect(ordered.SequenceEqual(expected), true, description);
    int groups = 0;
    foreach (string name in ordered)
        groups |= 1 << (int)MenuLayout.Section(MenuLayout.ButtonOrder(name, ""));
    Expect(MenuLayout.HasGroupGap(groups, MenuSection.Play), false, "No gap leads into PLAY");
    Expect(MenuLayout.HasGroupGap(groups, MenuSection.Create), true, "One gap before CREATE");
    Expect(MenuLayout.HasGroupGap(groups, MenuSection.More), true, "One gap before MORE");
    var dividerBefore = new List<string>();
    int? previous = null;
    foreach (string name in ordered)
    {
        int order = MenuLayout.ButtonOrder(name, "");
        if (MenuLayout.BarDividerBefore(previous, order)) dividerBefore.Add(name);
        previous = order;
    }
    if (MenuLayout.BarDividerBefore(previous, MenuLayout.QuitOrder)) dividerBefore.Add("QUIT");
    Expect(dividerBefore.SequenceEqual(new[] { "MissionEditorButton", "SettingsButton", "QUIT" }), true,
        "Bottom bar divides PLAY, CREATE, MORE and QUIT without splitting save or LAN rows");
}

ExpectMenuOrder(
    new[] { "SettingsButton", "MissionEditorButton", "MultiplayerButton", "SinglePlayerButton" },
    new[] { "SinglePlayerButton", "MultiplayerButton", "MissionEditorButton", "SettingsButton" },
    "Stock buttons retain PLAY, CREATE, MORE order");
ExpectMenuOrder(
    new[] { "SettingsButton", "Load Save", "JoinLanButtonRoot", "MissionEditorButton", "SinglePlayerButton",
        "MultiplayerButton", "StartLanButtonRoot", "Continue" },
    new[] { "Continue", "Load Save", "SinglePlayerButton", "MultiplayerButton", "StartLanButtonRoot",
        "JoinLanButtonRoot", "MissionEditorButton", "SettingsButton" },
    "Save and LAN buttons stay within PLAY in order");
ExpectMenuOrder(
    new[] { "SettingsButton", "MissionEditorButton", "MultiplayerButton", "SinglePlayerButton", "Load Save" },
    new[] { "Load Save", "SinglePlayerButton", "MultiplayerButton", "MissionEditorButton", "SettingsButton" },
    "Load Save leads PLAY when Continue is unavailable");
Expect(MenuLayout.HasGroupGap(0b101, MenuSection.Create), false, "An empty group does not leave a spacer");
Expect(MenuLayout.HasGroupGap(0b101, MenuSection.More), true, "Separated visible groups still have one spacer");
Expect(MenuLayout.HasGroupGap(0b100, MenuSection.More), false, "A lone visible group has no leading spacer");
Near(MenuLayout.GroupGap, 14f, "Minimal corner uses a fourteen-unit group gap");

const float barWidth = 1920f - 2f * 44f;
const int fullBar = 11, barDividers = 3;
var stockFit = MenuLayout.FitBar(barWidth, 7, barDividers, 780f, 740f, 700f);
Expect(stockFit.FontSize == 17, true, "Stock rows use the regular font");
Near(stockFit.Padding, 18f, "Stock rows use full padding");
var paddedFit = MenuLayout.FitBar(barWidth, fullBar, barDividers, 1450f, 1400f, 1350f);
Expect(paddedFit.FontSize == 17, true, "The full bar shrinks padding before reducing type");
Near(paddedFit.Padding, 14.5f, "The full bar uses the available padding");
var font16 = MenuLayout.FitBar(barWidth, fullBar, barDividers, 1555f, 1500f, 1450f);
Expect(font16.FontSize == 16, true, "The next measured type size is used at minimum padding");
Near(font16.Padding, 10f, "Once type shrinks padding remains at ten units");
var font15 = MenuLayout.FitBar(barWidth, fullBar, barDividers, 1600f, 1550f, 1500f);
Expect(font15.FontSize == 15, true, "Fifteen is the last available font size");
Near(font15.WidthScale, 1f, "A fitting font keeps full label widths");
var overflow = MenuLayout.FitBar(barWidth, fullBar, barDividers, 2100f, 2050f, 2000f);
Expect(overflow.FontSize == 15, true, "Oversize labels keep the minimum font size");
Near(2000f * overflow.WidthScale + 2f * fullBar * overflow.Padding + barDividers * MenuLayout.BarDividerWidth,
    barWidth, "Oversize labels use only the remaining cell space");
Near(MenuLayout.TipsAboveBar(72f, 20f, 80f, false, 82f, 119f), 68f, "Bottom bar tips clear the strip by sixteen units");
Near(MenuLayout.TipsAboveBar(72f, 20f, 80f, true, 82f, 119f), 107f, "Tips with the same horizontal span clear the version label");
Near(MenuLayout.TipsAboveBar(72f, 145f, 205f, true, 82f, 119f), 0f, "Tips already above the version label stay in place");

// Expand keeps the reference width and grows the logical height at narrower aspect ratios.
foreach (float height in new[] { 1080f, 1200f, 1440f })
{
    float bottom = -height / 2f;
    Near(MenuLayout.TipsRightShift(-916f, -626f, bottom + 90f, bottom + 510f,
        -640f, 640f, bottom + 60f, bottom + 140f), 30f, "Overlapping tips move right with clearance");
    Near(MenuLayout.TipsRightShift(-916f, -626f, bottom + 90f, bottom + 510f,
        -600f, 600f, bottom + 60f, bottom + 140f), 0f, "Tips stay centred when horizontally clear");
    Near(MenuLayout.TipsRightShift(-916f, -626f, bottom + 90f, bottom + 510f,
        -640f, 640f, bottom + 20f, bottom + 80f), 0f, "Tips stay centred when below the last row");
}

Expect(MenuLayout.Reveal(true, true, true, true, true, true), false, "Hidden overrides reveal input");
Expect(MenuLayout.Reveal(false, false, false, false, false, false), true, "Shown stays visible without input");
Expect(MenuLayout.Reveal(false, true, false, false, false, false), false, "Auto Hide closes an idle panel");
Expect(MenuLayout.Reveal(false, true, true, false, false, false), true, "Gamepad reveals Auto Hide panels");
Expect(MenuLayout.Reveal(false, true, false, true, false, false), true, "The edge zone reveals the panel");
Expect(MenuLayout.Reveal(false, true, false, false, true, false), true, "Hover keeps the panel open");
Expect(MenuLayout.Reveal(false, true, false, false, false, true), true, "Expanded news stays open");

foreach (float scale in new[] { 0.5f, 1f, 1.5f })
{
    float barTop = 72f * scale;
    float zoneTop = barTop + 36f * scale;
    float hysteresis = 12f * scale;
    var restingTips = new MenuLayout.PanelBounds(400f * scale, 180f * scale, 900f * scale, 220f * scale);
    var tipsZone = new MenuLayout.PanelBounds(364f * scale, barTop, 936f * scale, zoneTop);

    bool TipsAt(bool shown, float y) =>
        MenuLayout.PointerRevealsPanel(shown, 500f * scale, y, restingTips, tipsZone, hysteresis, barTop);

    Expect(TipsAt(false, 36f * scale), false, "Hover over the bar never opens the tips");
    Expect(TipsAt(true, barTop - scale), false, "Moving into the bar closes the tips even within the hysteresis distance");
    Expect(TipsAt(false, barTop), true, "The reveal strip starts at the top of the bar");
    Expect(TipsAt(false, 200f * scale), true, "The fully shown tips rectangle can reveal the panel");
    Expect(TipsAt(true, zoneTop + 13f * scale), false, "Moving past the exit boundary closes the tips");

    bool shownAtEdge = false, shownInGap = true, hiddenInGap = false, shownBeyondGap = true;
    for (int frame = 0; frame < 20; frame++)
    {
        shownAtEdge = TipsAt(shownAtEdge, zoneTop);
        shownInGap = TipsAt(shownInGap, zoneTop + 8f * scale);
        hiddenInGap = TipsAt(hiddenInGap, zoneTop + 8f * scale);
        shownBeyondGap = TipsAt(shownBeyondGap, zoneTop + 13f * scale);
        Expect(shownAtEdge && shownInGap && !hiddenInGap && !shownBeyondGap, true,
            "A stationary pointer stays stable at the entry edge, within hysteresis, and beyond it");
    }
}

var restingLogo = new MenuLayout.PanelBounds(44f, 930f, 180f, 1036f);
var topZone = new MenuLayout.PanelBounds(float.NegativeInfinity, 1044f, 216f, float.PositiveInfinity);
Expect(MenuLayout.PointerRevealsPanel(false, 100f, 950f, restingLogo, topZone, 12f, float.NegativeInfinity),
    true, "Logo hover uses its resting rectangle");
Expect(MenuLayout.PointerRevealsPanel(true, 100f, 918f, restingLogo, topZone, 12f, float.NegativeInfinity),
    true, "Logo remains revealed at the hysteresis boundary");
var restingNews = new MenuLayout.PanelBounds(340f, 60f, 540f, 200f);
var stockBottomZone = new MenuLayout.PanelBounds(float.NegativeInfinity, float.NegativeInfinity, 576f, 36f);
Expect(MenuLayout.PointerRevealsPanel(false, 100f, 30f, restingNews, stockBottomZone, 12f, float.NegativeInfinity),
    true, "Bottom-left news retains the original screen-edge entry zone");

Near(MenuLayout.Slide(0f, true, 0.11f), 0.5f, "The slide reaches halfway on unscaled time");
Near(MenuLayout.Slide(0.5f, false, 0.055f), 0.25f, "A slide can reverse mid-animation");
Near(MenuLayout.Slide(0.9f, true, 1f), 1f, "A long frame clamps the panel fully shown");
Near(MenuLayout.Slide(0.1f, false, 1f), 0f, "A long frame clamps the panel fully hidden");
Near(MenuLayout.Ease(0.25f), 0.15625f, "The slide eases in near the edge");

string[] backgroundFiles = BackgroundPlayback.SortFiles(new[] { "z.WEBM", "README.txt", "B.JpG", "a.PNG", "movie.MP4", "C.jpeg", "c.gif" });
Expect(BackgroundPlayback.FolderStatus(true, 3, 1, 0) == "Found: 3 images, 1 video", true,
    "Background status counts the detected formats");
Expect(BackgroundPlayback.FolderStatus(true, 0, 0, 0) == "No files found — showing the stock background", true,
    "An empty folder shows the stock-background status");
Expect(BackgroundPlayback.FolderStatus(false, 3, 1, 1) == "Custom background is off", true,
    "The off status takes precedence over files and failures");
Expect(BackgroundPlayback.FolderStatus(true, 1, 2, 1) ==
    "Found: 1 image, 2 videos\n1 file could not be played — see the log", true,
    "Rejected backgrounds are reported separately from detected files");
Expect(BackgroundPlayback.FolderStatus(true, 0, 2, 2).Contains("2 files could not be played"), true,
    "The rejected count uses plural wording");
Expect(BackgroundPlayback.DefaultAudioOutput == BackgroundAudioOutput.Smooth, true, "Smooth audio output is the default");
Near(BackgroundPlayback.DirectVolume(0.75f, 0.75f), 0.5625f, "Direct audio multiplies the game's linear music and master gains");
Near(BackgroundPlayback.DirectVolume(0.75f, 0f), 0f, "Master volume zero mutes Direct audio");
Near(BackgroundPlayback.DirectVolume(0f, 0.75f), 0f, "Music volume zero mutes Direct audio");
Near(BackgroundPlayback.DirectVolume(0.5f, 0.8f, 0.5f), 0.2f, "Direct audio also respects global listener volume");
Near(BackgroundPlayback.DirectVolume(1.5f, 1.2f), 1f, "Out-of-range gains cannot amplify Direct audio");
Near(BackgroundPlayback.DirectVolume(float.NaN, 1f), 0f, "An invalid gain is silent");
Expect(BackgroundPlayback.SessionAction(false, false, "MainMenu") == BackgroundSessionAction.Wait,
    true, "The initial title waits for the game's Menu state");
Expect(BackgroundPlayback.SessionAction(false, true, "MainMenu") == BackgroundSessionAction.Continue,
    true, "A fresh title visit can start a playlist");
foreach (string scene in new[] { "MissionsMenu", "MultiplayerMenu", "MainMenu", "OtherMenu" })
    Expect(BackgroundPlayback.SessionAction(true, true, scene) == BackgroundSessionAction.Continue,
        true, $"Menu state keeps one background session through {scene}");
foreach (string scene in new[] { "Encyclopedia", "GameWorld" })
    Expect(BackgroundPlayback.SessionAction(true, true, scene) == BackgroundSessionAction.Stop,
        true, $"Entering {scene} releases playback even before the game state changes");
Expect(BackgroundPlayback.SessionAction(true, false, "MultiplayerMenu") == BackgroundSessionAction.Stop,
    true, "Starting a mission or leaving Menu state releases playback before the menu scene unloads");
Expect(backgroundFiles.SequenceEqual(new[] { "a.PNG", "B.JpG", "C.jpeg", "movie.MP4", "z.WEBM" }), true,
    "The folder scan filters and sorts images and videos case-insensitively");
Expect(BackgroundPlayback.Kind("movie.MP4") == BackgroundKind.Video && BackgroundPlayback.Kind("C.jpeg") == BackgroundKind.Image,
    true, "Mixed-case video and image extensions use the proper loader");
var emptyBackgrounds = new BackgroundPlaylist(Array.Empty<string>(), new System.Random(1));
Expect(emptyBackgrounds.ShouldQueue(false) || emptyBackgrounds.TryNext("", out _), false, "An empty folder keeps the stock background");
var singleBackground = new BackgroundPlaylist(new[] { "only.mp4" }, new System.Random(1));
Expect(singleBackground.ShouldQueue(false) && singleBackground.TryNext("", out string only) && only == "only.mp4",
    true, "The only file is queued initially");
Expect(singleBackground.LoopsVideo && !singleBackground.ShouldQueue(true), true, "One video loops without a second decoder");
var singleStill = new BackgroundPlaylist(new[] { "only.png" }, new System.Random(1));
Expect(singleStill.ShouldQueue(false) && !singleStill.ShouldQueue(true), true, "One image stays static after loading");
var shuffled = new BackgroundPlaylist(backgroundFiles, new System.Random(25));
string previousBackground = "";
for (int round = 0; round < 4; round++)
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < backgroundFiles.Length; i++)
    {
        Expect(shuffled.TryNext(previousBackground, out string next) && next != previousBackground && seen.Add(next), true,
            "Each shuffled cycle visits every file without an immediate repeat across the boundary");
        previousBackground = next;
    }
}
var retry = new BackgroundPlaylist(new[] { "good.png", "missing.webm" }, new System.Random(5));
Expect(retry.TryNext("good.png", out string bad) && bad == "missing.webm", true, "The next file differs from the current still");
retry.Reject("missing.webm");
Expect(retry.Count == 1 && !retry.ShouldQueue(true), true, "A failed file leaves the remaining still in place");
retry.Reject("good.png");
Expect(!retry.TryNext("", out _), true, "Rejecting every file exhausts the playlist");

Expect(BackgroundPlayback.ImageFileAllowed(64L * 1024 * 1024), true, "The exact image byte limit is allowed");
Expect(BackgroundPlayback.ImageFileAllowed(64L * 1024 * 1024 + 1), false, "Files larger than 64 MB are rejected");
Expect(BackgroundPlayback.ImageFileAllowed(0), false, "Empty files are rejected");
Expect(BackgroundPlayback.ImageDimensionsAllowed(8192, 4096), true, "The exact image dimension limit is allowed");
Expect(BackgroundPlayback.ImageDimensionsAllowed(1, 8193), false, "The long side limit applies in portrait");
Expect(BackgroundPlayback.DisplaySize(8192, 4096) == (4096, 2048), true, "Large stills downscale to 4096 without stretching");
Expect(BackgroundPlayback.DisplaySize(1536, 4096) == (1536, 4096), true, "A still already within the display limit is kept");
byte[] pngHeader = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0x20, 0, 0, 0, 0x10, 0 };
Expect(BackgroundPlayback.ImageDimensions(pngHeader) == (8192, 4096), true, "PNG dimensions are read from IHDR before decoding");
pngHeader[19]++;
var oversizePng = BackgroundPlayback.ImageDimensions(pngHeader);
Expect(BackgroundPlayback.ImageDimensionsAllowed(oversizePng.Width, oversizePng.Height), false, "An oversized PNG header is rejected");
byte[] jpegHeader = { 0xff, 0xd8, 0xff, 0xe0, 0, 4, 0, 0, 0xff, 0xc2, 0, 11, 8, 8, 0, 16, 0, 1, 1, 17, 0, 0xff, 0xd9 };
Expect(BackgroundPlayback.ImageDimensions(jpegHeader) == (4096, 2048), true, "Progressive JPEG dimensions are read from the start-of-frame marker");
Expect(BackgroundPlayback.ImageDimensions(new byte[] { 0xff, 0xd8, 0xff, 0xc0, 0, 1 }) == (0, 0), true,
    "Malformed JPEG segment lengths are rejected before decoding");

var wideBackground = BackgroundPlayback.CoverUv(2f, 1.5f);
Near(wideBackground.X, 0.125f, "Wide art crops equally from both sides");
Near(wideBackground.Width, 0.75f, "Wide art uses only the width needed to cover");
var tallBackground = BackgroundPlayback.CoverUv(1f, 2f);
Near(tallBackground.Y, 0.25f, "Tall art crops equally from top and bottom");
Near(tallBackground.Height, 0.5f, "Tall art uses only the height needed to cover");
Expect(BackgroundPlayback.CoverUv(1.5f, 1.5f) == (0f, 0f, 1f, 1f), true, "Matching aspect ratios use the entire image");
Expect(BackgroundPlayback.Fade(BackgroundTransition.Crossfade, 0.25f) == (0.25f, 0f), true, "Crossfade blends in the next item without a black overlay");
Expect(BackgroundPlayback.Fade(BackgroundTransition.FadeThroughBlack, 0.25f) == (0f, 0.5f), true, "The outgoing item fades to black in the first half");
Expect(BackgroundPlayback.Fade(BackgroundTransition.FadeThroughBlack, 0.5f) == (1f, 1f), true, "Items swap under fully opaque black");
Expect(BackgroundPlayback.Fade(BackgroundTransition.FadeThroughBlack, 0.75f) == (1f, 0.5f), true, "Black fades away over the incoming item");
Expect(BackgroundPlayback.Fade(BackgroundTransition.FadeThroughBlack, 1.2f) == (1f, 0f), true, "Late frames finish the transition fully visible");

checks++;
if (HudAttachmentMigration.Parse("True", HudAttachment.Hidden) != HudAttachment.Aircraft) throw new Exception("Old visible HUD settings must stay aircraft-attached");
checks++;
if (HudAttachmentMigration.Parse("False", HudAttachment.Aircraft) != HudAttachment.Hidden) throw new Exception("Old hidden HUD settings must remain hidden");
checks++;
if (HudAttachmentMigration.Parse("Camera", HudAttachment.Aircraft) != HudAttachment.Camera) throw new Exception("Camera attachment must survive restart");
checks++;
if (HudAttachmentMigration.Parse("hidden", HudAttachment.Aircraft) != HudAttachment.Hidden) throw new Exception("HUD mode parsing is case insensitive");
checks++;
if (HudAttachmentMigration.Parse("99", HudAttachment.Aircraft) != HudAttachment.Aircraft) throw new Exception("Invalid numeric HUD modes must fall back safely");
checks++;
if (HudAttachmentMigration.Parse("bad", HudAttachment.Aircraft) != HudAttachment.Aircraft) throw new Exception("Invalid HUD settings must fall back safely");
checks++;
if (Enum.GetNames<HudAttachment>()[2] != "Hidden") throw new Exception("Hidden must be the last dropdown option");

// HUD placement must be independent of the native HMD's screen-fixed readouts.
Expect(HudLayoutMath.IsFlightInstrument(0), true, "Real HUD instruments participate in attachment");
Expect(HudLayoutMath.IsFlightInstrument(1), false, "Helmet duplicates are excluded from aircraft frame calculations");
Expect(HudLayoutMath.IsFlightInstrument(2), false, "Cockpit MFDs are excluded from flight HUD attachment");
Vector3 top = new Vector3(0f, 100f, 0f);
Vector3 left = new Vector3(-100f, 0f, 0f);
float leftBank = HudLayoutMath.BankDegrees(Vector3.left, Vector3.up);
float rightBank = HudLayoutMath.BankDegrees(Vector3.right, Vector3.down);
Near(leftBank, 90f, "A left bank uses a positive UI bank angle");
Near(rightBank, -90f, "A right bank uses a negative UI bank angle");
Near(HudLayoutMath.PlaceOffset(top, HudAttachment.Aircraft, leftBank).x, -100f, "Aircraft compass moves left in a left knife-edge bank");
Near(HudLayoutMath.PlaceOffset(top, HudAttachment.Aircraft, rightBank).x, 100f, "Aircraft compass moves right in a right knife-edge bank");
Near(HudLayoutMath.PlaceOffset(left, HudAttachment.Aircraft, leftBank).y, -100f, "Aircraft speed cluster banks with the plane instead of counter-rotating");
foreach (float bank in new[] { -180f, -90f, -45f, 0f, 45f, 90f, 180f })
{
    Vector3 fixedOffset = HudLayoutMath.PlaceOffset(new Vector3(-120f, 30f, 0f), HudAttachment.Camera, bank);
    Near(fixedOffset.x, -120f, "Camera speed/radar layout never moves with aircraft bank");
    Near(fixedOffset.y, 30f, "Camera layout stays upright through inverted flight");
}
Near(HudLayoutMath.BankDegrees(Vector3.forward, Vector3.up), 90f, "Looking along aircraft up falls back to projected wing direction");
Near(HudLayoutMath.BankDegrees(new Vector3(-0.5f, 0.5f, 0.7f), Vector3.right), 45f, "Off-axis bank uses projected up, not Euler z subtraction");
Vector3 invertedTop = HudLayoutMath.PlaceOffset(top, HudAttachment.Aircraft, 180f);
Near(invertedTop.y, -100f, "Aircraft instruments follow an inverted aircraft");

// Picture-in-picture placement is clamped to the main camera viewport rather than
// assuming 16:9, full screen, or a particular HUD canvas scale.
var picture = TargetingScreenLayout.Calculate(1920f, 1080f, 100f, 25f, 24f, 1.8f);
Near(picture.Width, 460.8f, "Target screen width defaults to 24 percent of the viewport");
Near(picture.Height, 256f, "Target screen preserves the native feed aspect");
Near(picture.Left + picture.Width, 1908f, "Default panel sits against the right margin");
Expect(picture.Top > 150f && picture.Top < 300f, true, "Default panel leaves upper-right space for weapon selection");
foreach (var viewport in new[] { (1920f, 1080f), (1920f, 1200f), (1280f, 1024f), (3440f, 1440f), (640f, 480f) })
foreach (float x in new[] { -20f, 0f, 50f, 100f, 120f })
foreach (float y in new[] { -20f, 0f, 50f, 100f, 120f })
{
    var panel = TargetingScreenLayout.Calculate(viewport.Item1, viewport.Item2, x, y, 50f, 1.8f);
    Expect(panel.Left >= 0f && panel.Top >= 0f && panel.Left + panel.Width <= viewport.Item1 + 0.01f
        && panel.Top + panel.Height <= viewport.Item2 + 0.01f, true, "Movable target screen remains inside wide, tall and 16:10 viewports");
}
var portraitFeed = TargetingScreenLayout.Calculate(1920f, 1080f, 100f, 100f, 50f, 0.25f);
Expect(portraitFeed.Top + portraitFeed.Height <= 1080f && portraitFeed.Left + portraitFeed.Width <= 1920f, true, "An unusual tall feed is fitted without clipping");
Near(portraitFeed.Width / portraitFeed.Height, 0.25f, "Fitting an unusual feed does not stretch it");
var invalidAspect = TargetingScreenLayout.Calculate(1920f, 1080f, 0f, 0f, 24f, float.NaN);
Near(invalidAspect.Width / invalidAspect.Height, 1.8f, "An unavailable aspect uses the native default safely");
var rightBottom = TargetingScreenLayout.Calculate(1920f, 1080f, 100f, 100f, 24f, 1.8f);
Near(rightBottom.Top + rightBottom.Height, 1068f, "The bottom position slider respects the lower margin");
TargetingScreenLayout.PositionPercent(1920f, 1080f, 24f, 1.8f, picture.Left, picture.Top,
    out float draggedX, out float draggedY);
Near(draggedX, 100f, "Dragging back to the original screen position preserves horizontal alignment");
Near(draggedY, 25f, "Dragging back to the original screen position preserves vertical alignment");
TargetingScreenLayout.PositionPercent(1920f, 1080f, 40f, 1.8f, 400f, 200f, out draggedX, out draggedY);
var resizedPicture = TargetingScreenLayout.Calculate(1920f, 1080f, draggedX, draggedY, 40f, 1.8f);
Near(resizedPicture.Left, 400f, "Resizing an interior panel anchors its left edge");
Near(resizedPicture.Top, 200f, "Resizing an interior panel anchors its top edge");
TargetingScreenLayout.PositionPercent(1920f, 1080f, 40f, 1.8f, -100f, 2000f, out draggedX, out draggedY);
Near(draggedX, 0f, "Dragging off the left edge clamps the panel");
Near(draggedY, 100f, "Dragging off the bottom edge clamps the panel");
for (int bit = 0; bit < 9; bit++)
{
    // Disable each required condition in isolation, or enable landing mode.
    bool[] flags = { true, true, true, true, true, true, true, false, true };
    flags[bit] = !flags[bit];
    var unavailable = new TargetingScreenState(flags[0], flags[1], flags[2], flags[3], flags[4], flags[5], flags[6], flags[7], flags[8]);
    Expect(unavailable.Visible, false, "No stale feed is shown when an availability condition fails");
}
Expect(new TargetingScreenState(true, true, true, true, true, true, true, false, true).Visible, true,
    "A running native targeting feed appears on the local chase HUD");
var fittedImage = TargetingScreenLayout.FitImage(460.8f, 256f, 2f, 1.8f);
Near(fittedImage.Width / fittedImage.Height, 1.8f, "The border does not distort the actual video image");
Expect(fittedImage.Left >= 2f && fittedImage.Top >= 2f, true, "The image stays inside its frame padding");
var squareFrame = TargetingScreenLayout.FitImage(300f, 300f, 2f, 1.8f);
Near(squareFrame.Width / squareFrame.Height, 1.8f, "A square frame still displays an unstretched wide sensor image");
Expect(squareFrame.Top > 2f && squareFrame.Left >= 2f, true, "Unusual aspect ratios are centered inside the frame");

var feedReadiness = new TargetingFeedReadiness();
Expect(feedReadiness.CanShow(10), false, "An unobserved feed cannot be displayed");
feedReadiness.Observe(1, 2, 10);
Expect(feedReadiness.CanShow(10), false, "A created texture is not proof of a rendered sensor frame");
feedReadiness.Rendered(99, 2, 10);
Expect(feedReadiness.CanShow(11), false, "An unrelated camera cannot validate the texture");
feedReadiness.Rendered(1, 99, 10);
Expect(feedReadiness.CanShow(11), false, "Rendering a different texture cannot validate this feed");
feedReadiness.Rendered(1, 2, 10);
Expect(feedReadiness.CanShow(10), true, "A completed sensor render may be displayed later in the same frame");
feedReadiness.Observe(1, 2, 11);
Expect(feedReadiness.CanShow(11), true, "The first complete native sensor frame can be displayed");
Expect(feedReadiness.CanShow(12), true, "An active sensor does not blink when its render rate differs from the game frame rate");
feedReadiness.Rendered(1, 2, 12);
Expect(feedReadiness.CanShow(13), true, "A resumed native camera supplies a fresh frame");
feedReadiness.Observe(3, 2, 13);
Expect(feedReadiness.CanShow(13), false, "Aircraft/camera replacement invalidates a reused texture");
feedReadiness.Rendered(1, 2, 13);
Expect(feedReadiness.CanShow(14), false, "The previous aircraft cannot validate a new camera");
feedReadiness.Rendered(3, 2, 13);
Expect(feedReadiness.CanShow(14), true, "A new camera becomes ready after its own render");
feedReadiness.Observe(3, 4, 14);
Expect(feedReadiness.CanShow(14), false, "A resized/replaced texture waits for new content");
feedReadiness.Rendered(3, 4, 14);
Expect(feedReadiness.CanShow(15), true, "The replacement texture becomes ready after rendering");
feedReadiness.Reset();
Expect(feedReadiness.CanShow(15), false, "Leaving chase or clearing a target resets feed readiness");
feedReadiness.Observe(3, 4, 15);
Expect(feedReadiness.CanShow(16), false, "Reactivating cannot display the previous session's texture");
feedReadiness.Rendered(3, 4, 14);
Expect(feedReadiness.CanShow(16), false, "A render from before activation is rejected");
feedReadiness.Rendered(3, 4, 16);
Expect(feedReadiness.CanShow(16), true, "OnGUI can use this frame after the native render callback");
Expect(feedReadiness.CanShow(19), true, "Several non-rendering game frames do not hide a still-active native sensor");
Expect(feedReadiness.CanShow(15), false, "A future render cannot validate an earlier frame");
feedReadiness.Reset();
Expect(feedReadiness.CanShow(19), false, "Stopping a camera or losing context invalidates its last image immediately");

HudLayoutStamp LayoutStamp(int canvas = 1, float width = 1920f, float height = 1080f, float scale = 1f,
    HudAttachment left = HudAttachment.Camera, HudAttachment right = HudAttachment.Camera,
    HudAttachment compass = HudAttachment.Camera, HudAttachment waterline = HudAttachment.Aircraft) =>
    new(canvas, new Vector2(width, height), new Vector3(scale, scale, scale), left, right, compass, waterline);
HudLayoutStamp stableLayout = LayoutStamp();
Expect(stableLayout.Matches(LayoutStamp()), true, "Ordinary flight/camera frames retain the existing HUD attachments");
Expect(stableLayout.Matches(LayoutStamp(width: 1920.001f, scale: 1.00001f)), true, "Subpixel layout noise does not trigger reparenting");
Expect(stableLayout.Matches(LayoutStamp(canvas: 2)), false, "A new HUD canvas recaptures original layout");
Expect(stableLayout.Matches(LayoutStamp(width: 1600f)), false, "A changed resolution width rebuilds layout");
Expect(stableLayout.Matches(LayoutStamp(height: 1200f)), false, "A changed resolution height rebuilds layout");
Expect(stableLayout.Matches(LayoutStamp(scale: 1.2f)), false, "A changed HUD scale rebuilds layout");
Expect(stableLayout.Matches(LayoutStamp(left: HudAttachment.Aircraft)), false, "Changing left attachment restores its native layout");
Expect(stableLayout.Matches(LayoutStamp(right: HudAttachment.Hidden)), false, "Hiding the right group rebuilds visibility immediately");
Expect(stableLayout.Matches(LayoutStamp(compass: HudAttachment.Aircraft)), false, "Changing compass attachment rebuilds layout");
Expect(stableLayout.Matches(LayoutStamp(waterline: HudAttachment.Hidden)), false, "Changing waterline attachment rebuilds layout");

// Screen-space layout must survive a world-origin translation followed by Unity
// restoring the Canvas root. Exercise the production mapper with real Unity maths.
foreach (var size in new[] { new Vector2(1920f, 1200f), new Vector2(1920f, 1080f), new Vector2(1280f, 1024f) })
foreach (float uiScale in new[] { 0.5f, 1f, 1.5f, 2f })
foreach (float pivot in new[] { 0f, 0.5f, 1f })
{
    Rect pixels = new Rect(0f, 0f, size.x, size.y);
    Rect localRect = new Rect(-size.x * pivot / uiScale, -size.y * pivot / uiScale, size.x / uiScale, size.y / uiScale);
    Vector3 canvasRoot = new Vector3(size.x * pivot, size.y * pivot, 0f);
    foreach (var screenPoint in new[] { size * 0.5f, Vector2.zero, size, new Vector2(size.x * 0.2f, size.y * 0.65f) })
    {
        Expect(HudCanvasMath.TryScreenToLocal(screenPoint, pixels, localRect, out Vector3 localPoint), true,
            "Every tested screen size, HUD scale and canvas pivot maps into local UI coordinates");
        Vector3 expectedScreen = new Vector3(screenPoint.x, screenPoint.y, 0f);
        Expect((canvasRoot + localPoint * uiScale - expectedScreen).sqrMagnitude < 0.001f, true,
            "Canvas-local placement preserves the requested pixel position");
        foreach (Vector3 originShift in new[] { new Vector3(1024f, 0f, 0f), new Vector3(-2048f, 1024f, 0f), new Vector3(0f, -1024f, 0f) })
        {
            // The old world-position assignment compensates for the temporary
            // root translation. After root reset that becomes an unwanted jump.
            Vector3 shiftedRoot = canvasRoot - originShift;
            Vector3 oldLocal = (expectedScreen - shiftedRoot) / uiScale;
            Vector3 oldRendered = canvasRoot + oldLocal * uiScale;
            Expect((oldRendered - expectedScreen).sqrMagnitude > 100000f, true,
                "World placement has a demonstrable one-frame jump if the canvas root is reset after an origin shift");
            Expect((canvasRoot + localPoint * uiScale - expectedScreen).sqrMagnitude < 0.001f, true,
                "Canvas-local presentation remains correct after root reset without adding an origin compensation");
        }
    }
}
Expect(HudCanvasMath.TryScreenToLocal(new Vector2(700f, 500f), new Rect(0f, 0f, 1920f, 1200f),
    new Rect(-960f, -600f, 1920f, 1200f), out Vector3 subViewPoint), true,
    "A camera viewport center can be mapped into a larger HUD canvas");
Near(subViewPoint.x, -260f, "Sub-viewport positioning retains the requested horizontal screen coordinate");
Near(subViewPoint.y, -100f, "Sub-viewport positioning retains the requested vertical screen coordinate");
Expect(HudCanvasMath.TryScreenToLocal(new Vector2(1100f, 700f), new Rect(100f, 100f, 2000f, 1200f),
    new Rect(-500f, -300f, 1000f, 600f), out Vector3 shiftedPixelPoint), true,
    "Canvas pixel rectangles with a nonzero display origin are supported");
Near(shiftedPixelPoint.sqrMagnitude, 0f, "The center of a nonzero pixel rectangle maps to the local center");
Expect(HudCanvasMath.TryScreenToLocal(Vector2.zero, new Rect(0f, 0f, 0f, 1200f), new Rect(-960f, -600f, 1920f, 1200f), out _), false,
    "A minimized/zero-width viewport cannot generate invalid placement");
Expect(HudCanvasMath.TryScreenToLocal(new Vector2(float.NaN, 0f), new Rect(0f, 0f, 1920f, 1200f), new Rect(-960f, -600f, 1920f, 1200f), out _), false,
    "Invalid aircraft projection cannot poison the canvas position");
Expect(HudCanvasMath.TryScreenToLocal(new Vector2(float.PositiveInfinity, 0f), new Rect(0f, 0f, 1920f, 1200f), new Rect(-960f, -600f, 1920f, 1200f), out _), false,
    "Infinite near-plane projection is rejected");
Vector3 scaledNeutral = HudCanvasMath.RelativeOffset(new Vector3(-150f, 25f, 4f), new Vector3(2f, 3f, 1f), new Vector3(4f, 6f, 2f));
Near(scaledNeutral.x, -75f, "Neutral instrument offset uses scale relative to the canvas");
Near(scaledNeutral.y, 12.5f, "Nonuniform reference/canvas scale is preserved");
Near(scaledNeutral.z, 2f, "Neutral depth is expressed in canvas units too");
Expect(HudCanvasMath.HasPositionDrift(scaledNeutral, scaledNeutral), false, "A stable local pose does not produce a drift report");
Expect(HudCanvasMath.HasPositionDrift(scaledNeutral, scaledNeutral + new Vector3(0.005f, 0f, 0f)), false,
    "Sub-pixel numerical noise is below the presentation drift threshold");
Expect(HudCanvasMath.HasPositionDrift(scaledNeutral, scaledNeutral + Vector3.right), true,
    "A late layout shift of a widget is detected before rendering");
Expect(HudCanvasMath.HasPositionDrift(scaledNeutral, new Vector3(float.NaN, 0f, 0f)), true,
    "A nonfinite late-written widget position is detected for correction");

Console.WriteLine($"PASS: {checks} HUD layout, attachment, canvas, targeting-screen, capacitor and title-menu checks.");
