using UnityEngine;
using UIPlus.Helpers;

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

Console.WriteLine($"PASS: {checks} HUD layout, attachment, canvas and targeting-screen checks.");
