using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UIPlus.Helpers;

/// <summary>
/// Hides whole groups of flight instruments, leaving the parts of the HUD that are about the world
/// rather than about the aircraft — the reticles, the velocity vector, the free-look diamond, unit
/// markers and threat warnings.
///
/// Groups are matched on the **component type** rather than on object names, because the names are
/// scene data and vary by airframe while the types are code. The proof is in the game's own data: the
/// same throttle arc is called <c>throttleGauge_AB</c> on a fighter and <c>throttleGauge</c> on a
/// helicopter, and both carry a <c>ThrottleGauge</c>. Matching by name would have hidden it on one
/// aircraft and silently missed it on the other.
///
/// Type names are compared as strings rather than through <c>typeof</c> so that a game update which
/// renames or drops one of these classes costs that single element instead of failing to load the
/// mod. Nothing here is required for the mod to work.
///
/// Warnings are deliberately left out of every group. The stall and RPM warnings, the radar warning
/// receiver, the gear indicator and the helicopter's hover indicator all live in the same containers,
/// and tidying the frame is not worth being told about a stall too late.
/// </summary>
internal static partial class HudElements
{
    /// <summary>
    /// Airspeed, angle of attack, Mach, g and fuel — the cluster on the left of the frame.
    /// Fighter-only entries are simply absent on a helicopter and cost nothing.
    /// </summary>
    private static readonly string[] LeftTypes =
    {
        "SpeedGauge", "AoADisplay", "AoAIndexer", "MachIndicator", "GIndicators", "FuelGauge"
    };

    /// <summary>
    /// Throttle or collective, rotor RPM, altitude, radar altitude and climb rate — the cluster on
    /// the right.
    /// </summary>
    private static readonly string[] RightTypes =
    {
        "ThrottleGauge", "RPMGauge", "Altitude", "Climbrate"
    };

    /// <summary>The heading ribbon's degree readout. The ribbon itself is a field on the HUD.</summary>
    private static readonly string[] CompassTypes = { "Bearing" };

    private static readonly List<GameObject> Left = new();
    private static readonly List<GameObject> Right = new();
    private static readonly List<GameObject> Compass = new();
    private static readonly Dictionary<GameObject, bool> HudViewVisibility = new();

    private sealed class AttachmentPose
    {
        internal Transform Node;
        internal Transform Parent;
        internal Vector3 LocalPosition;
        internal Quaternion LocalRotation;
        internal Vector3 Offset;
        internal Vector3 PresentationPosition;
        internal Quaternion PresentationRotation;
        internal Quaternion LayoutRotation;
        internal HudAttachment Mode;
        internal int Group = -1; // 0 left, 1 right, 2 compass; -1 not user-movable
        internal bool WasActive;
        internal Vector3 LocalScale;
        internal int Sibling;
        internal Vector2 AnchorMin, AnchorMax, Pivot, SizeDelta;
        internal Vector3 AnchoredPosition;
        internal UnityEngine.UI.LayoutElement Layout;
        internal bool AddedLayout, LayoutEnabled, IgnoreLayout, PresentationReady;
    }

    private static readonly List<AttachmentPose> Attached = new();
    private static int _attachedCount;
    private static HudLayoutStamp? _layoutStamp;
    private static bool _initialized, _renderListening, _inRenderCallback;
    private static Canvas _presentationCanvas;
    private static int _presentationFrame = -1, _reportedCanvas, _driftReports, _originReports, _canvasMoveReports;
    private static int _lastCanvasMoveFrame = -1;
    private static Vector3 _canvasAtPlacement, _originAtPlacement, _lastOrigin;
    private static bool _originKnown;
    private static float _lastDriftReport = -100f;
    private static readonly List<UnityEngine.UI.LayoutElement> OwnedLayouts = new();

    // The Arcade HUD style draws its own speed/altitude readouts, so the native groups are hidden.
    internal static HudAttachment LeftMode =>
        ArcadeHud.NativeInstrumentsHidden ? HudAttachment.Hidden : Plugin.LeftInstruments.Value;
    internal static HudAttachment RightMode =>
        ArcadeHud.NativeInstrumentsHidden ? HudAttachment.Hidden : Plugin.RightInstruments.Value;

    internal static void Initialize()
    {
        if (_initialized) return;
        PlayerSettings.OnApplyOptions += RestoreAttachments;
        _initialized = true;
    }

    internal static void Dispose()
    {
        if (_initialized) PlayerSettings.OnApplyOptions -= RestoreAttachments;
        _initialized = false;
        if (_renderListening) Canvas.willRenderCanvases -= BeforeCanvasRender;
        _renderListening = false;
        ShowAll();
        foreach (var layout in OwnedLayouts) if (layout != null) Object.Destroy(layout);
        OwnedLayouts.Clear();
    }
    private static FieldInfo _canvasField;
    private static FieldInfo _waterlineField;

    internal static bool IsCameraAttached(Transform node)
    {
        for (int i = 0; i < _attachedCount; i++)
            if (Attached[i].Node == node && Attached[i].Mode == HudAttachment.Camera) return true;
        return false;
    }

    private static readonly IComparer<AttachmentPose> RestoreOrder = Comparer<AttachmentPose>.Create((a, b) => {
        int parent = (a.Parent == null ? 0 : a.Parent.GetInstanceID()).CompareTo(b.Parent == null ? 0 : b.Parent.GetInstanceID());
        return parent != 0 ? parent : a.Sibling.CompareTo(b.Sibling);
    });

    internal static void RestoreAttachments()
    {
        RestoreStockRoll();
        _layoutStamp = null;
        _presentationFrame = -1; _presentationCanvas = null;
        Attached.Sort(0, _attachedCount, RestoreOrder);
        for (int i = 0; i < _attachedCount; i++)
        {
            AttachmentPose pose = Attached[i];
            if (pose.Node == null) continue;
            if (pose.Parent == null)
            {
                // A detached gauge must not survive destruction of its aircraft HUD.
                Object.Destroy(pose.Node.gameObject);
                continue;
            }
            if (pose.Layout != null)
            {
                if (pose.AddedLayout)
                {
                    pose.Layout.enabled = false;
                    pose.Layout.ignoreLayout = false;
                }
                else
                {
                    pose.Layout.ignoreLayout = pose.IgnoreLayout;
                    pose.Layout.enabled = pose.LayoutEnabled;
                }
                pose.Layout = null;
            }
            pose.Node.SetParent(pose.Parent, false);

            if (pose.Node is RectTransform rect)
            {
                rect.anchorMin = pose.AnchorMin;
                rect.anchorMax = pose.AnchorMax;
                rect.pivot = pose.Pivot;
                rect.sizeDelta = pose.SizeDelta;
                rect.anchoredPosition3D = pose.AnchoredPosition;
            }
            pose.Node.localScale = pose.LocalScale;
            pose.Node.localPosition = pose.LocalPosition;
            pose.Node.localRotation = pose.LocalRotation;
            if (pose.Node.gameObject.activeSelf != pose.WasActive) pose.Node.gameObject.SetActive(pose.WasActive);
        }
        for (int i = 0; i < _attachedCount; i++)
            if (Attached[i].Node != null && Attached[i].Parent != null)
                Attached[i].Node.SetSiblingIndex(Attached[i].Sibling);
        _attachedCount = 0;
    }

    internal static void AttachToCamera(CameraStateManager cam, bool hudView)
    {
        FlightHud hud = SceneSingleton<FlightHud>.i;
        if (hud == null || cam.mainCamera == null || cam.followingUnit == null) return;
        if (_root != hud.transform || IsStale()) Rescan(hud);

        _canvasField ??= AccessTools.Field(typeof(FlightHud), "canvas");
        if (!(_canvasField?.GetValue(hud) is Canvas canvas) || !canvas.gameObject.activeInHierarchy)
            return;
        if (!(canvas.transform is RectTransform canvasRect)) return;
        var stamp = new HudLayoutStamp(canvas.GetInstanceID(), canvasRect.rect.size, canvasRect.lossyScale,
            LeftMode, RightMode, Plugin.Compass.Value, Plugin.Waterline.Value);
        if (_layoutStamp.HasValue && !_layoutStamp.Value.Matches(stamp)) RestoreAttachments();
        _layoutStamp = stamp;
        SuppressHelmetDuplicates();
        // Reapply visibility after restoration, so selecting Hidden cannot reveal
        // the just-restored gauge for a frame.
        Apply(LeftMode, RightMode, Plugin.Compass.Value, hudView);
        Transform center = hud.GetHUDCenter();
        if (center == null) return;
        if (!TryCanvasPoint(canvas, canvasRect, cam.mainCamera.pixelRect.center, out Vector3 screenCenter))
            return;

        Transform aircraft = cam.followingUnit is Aircraft airframe && airframe.cockpit != null
            ? airframe.cockpit.transform : cam.followingUnit.transform;
        Vector3 projected = cam.mainCamera.WorldToScreenPoint(aircraft.position + aircraft.forward * 4000f);
        bool mapped = TryCanvasPoint(canvas, canvasRect, new Vector2(projected.x, projected.y), out Vector3 aircraftCenter);
        bool noseVisible = projected.z > 0f && mapped;
        if (!mapped) aircraftCenter = screenCenter;
        Transform view = cam.mainCamera.transform;
        float bank = HudLayoutMath.BankDegrees(view.InverseTransformDirection(aircraft.up),
            view.InverseTransformDirection(aircraft.right));

        // Only real flight-HUD widgets are in these groups. Helmet duplicates are
        // screen-fixed by the game and cannot be unrotated using HUDCenter.
        Queue(Left, center, canvasRect, screenCenter, LeftMode, 0);
        Queue(Right, center, canvasRect, screenCenter, RightMode, 1);
        if (RightMode == HudAttachment.Camera && PusherAircraft.HasPusher(_scannedAircraft))
        {
            CustomAxis1Gauge pusher = PusherStockHud.ReversibleGauge;
            if (pusher != null)
                Queue(pusher.transform, center, canvasRect, screenCenter, HudAttachment.Camera, groupId: 1);
        }
        Queue(Compass, center, canvasRect, screenCenter, Plugin.Compass.Value, 2);
        // User offsets per group, in canvas-local units (pixels divided by the canvas scale).
        float canvasScale = canvasRect.lossyScale.x > 0.0001f ? canvasRect.lossyScale.x : 1f;
        Vector3 groupOffset0 = (Vector3)(HudElementPositions.PixelOffset(HudElementPositions.LeftInstruments) / canvasScale);
        Vector3 groupOffset1 = (Vector3)(HudElementPositions.PixelOffset(HudElementPositions.RightInstruments) / canvasScale);
        Vector3 groupOffset2 = (Vector3)(HudElementPositions.PixelOffset(HudElementPositions.Compass) / canvasScale);
        Vector3 groupOffset3 = (Vector3)(HudElementPositions.PixelOffset(HudElementPositions.Waterline) / canvasScale);
        if (Plugin.Waterline.Value != HudAttachment.Hidden)
        {
            _waterlineField ??= AccessTools.Field(typeof(FlightHud), "waterline");
            if (_waterlineField?.GetValue(hud) is Component marker)
                Queue(marker.transform, center, canvasRect, screenCenter, Plugin.Waterline.Value, true, 3);
        }


        for (int i = 0; i < _attachedCount; i++)
        {
            AttachmentPose pose = Attached[i];
            bool followsAircraft = pose.Mode == HudAttachment.Aircraft;
            if (pose.Node == null || pose.Parent == null)
            {
                RestoreAttachments();
                _root = null;
                return;
            }
            // Detach once per layout/session, not on every Update/LateUpdate pair.
            // Native HUDApps retain their registered references and keep refreshing.
            if (!pose.PresentationReady || pose.Node.parent != canvasRect)
            {
                Vector2 resolvedSize = pose.Node is RectTransform original ? original.rect.size : Vector2.zero;
                pose.Node.SetParent(canvasRect, true);
                if (pose.Node is RectTransform detached)
                {
                    // Native stretch anchors describe a small aircraft-HUD parent.
                    // Freeze the resolved size before moving it onto the full canvas.
                    detached.anchorMin = detached.anchorMax = canvasRect.pivot;
                    detached.sizeDelta = resolvedSize;
                }
                pose.Layout = pose.Node.GetComponent<UnityEngine.UI.LayoutElement>();
                pose.AddedLayout = pose.Layout == null || OwnedLayouts.Contains(pose.Layout);
                if (pose.Layout == null)
                {
                    pose.Layout = pose.Node.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    OwnedLayouts.Add(pose.Layout);
                }
                pose.LayoutEnabled = pose.Layout.enabled;
                pose.IgnoreLayout = pose.Layout.ignoreLayout;
                pose.Layout.enabled = true;
                pose.Layout.ignoreLayout = true;
                pose.PresentationReady = true;
            }
            Vector3 origin = followsAircraft ? aircraftCenter : screenCenter;
            pose.PresentationPosition = origin + HudLayoutMath.PlaceOffset(pose.Offset, pose.Mode, bank)
                + (pose.Group == 0 ? groupOffset0 : pose.Group == 1 ? groupOffset1 : pose.Group == 2 ? groupOffset2 : pose.Group == 3 ? groupOffset3 : Vector3.zero);
            pose.PresentationRotation = Quaternion.AngleAxis(followsAircraft ? bank : 0f, Vector3.forward)
                                        * pose.LayoutRotation;
            pose.Node.localPosition = pose.PresentationPosition;
            pose.Node.localRotation = pose.PresentationRotation;
            if (followsAircraft && !noseVisible) pose.Node.gameObject.SetActive(false);
        }
        _presentationCanvas = canvas; _presentationFrame = Time.frameCount;
        CorrectStockRoll(canvas);
        _canvasAtPlacement = canvasRect.position; _originAtPlacement = Datum.originPosition;
        if (_reportedCanvas != canvas.GetInstanceID())
        {
            _reportedCanvas = canvas.GetInstanceID();
            Plugin.Logger.LogInfo("HUD presentation: canvas-local coordinates, mode=" + canvas.renderMode);
        }
        if (_originKnown && _lastOrigin != Datum.originPosition && _originReports++ < 4)
            Plugin.Logger.LogInfo($"HUD presentation: world origin shifted at frame {Time.frameCount}; delta={Datum.originPosition - _lastOrigin}");
        _lastOrigin = Datum.originPosition; _originKnown = true;
        if (!_renderListening)
        {
            // Register after Unity's layout registry, so our final pose check runs
            // after its normal canvas/layout rebuild. Never force a canvas rebuild.
            UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingLayout();
            Canvas.willRenderCanvases += BeforeCanvasRender;
            _renderListening = true;
        }
    }

    internal static bool TryCanvasPoint(Canvas canvas, RectTransform rect, Vector2 screen, out Vector3 local)
    {
        if (canvas.renderMode != RenderMode.WorldSpace)
            return HudCanvasMath.TryScreenToLocal(screen, canvas.pixelRect, rect.rect, out local);
        bool mapped = RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, canvas.worldCamera, out Vector2 point);
        local = new Vector3(point.x, point.y, 0f);
        return mapped && !float.IsNaN(point.x) && !float.IsNaN(point.y)
                      && !float.IsInfinity(point.x) && !float.IsInfinity(point.y);
    }

    private static void BeforeCanvasRender()
    {
        if (_inRenderCallback || _presentationFrame != Time.frameCount || _presentationCanvas == null
            || !_presentationCanvas.isActiveAndEnabled || !Plugin.IsActive || DynamicMap.mapMaximized)
            return;
        CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
        if (!HudViews.CameraAttached(cam) || cam.followingUnit == null) return;
        _inRenderCallback = true;
        try
        {
            // Screen-space Canvas roots can move after LateUpdate. Local widget
            // positions must survive that movement without compensating for it.
            if (_presentationCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                && (_presentationCanvas.transform.position - _canvasAtPlacement).sqrMagnitude > 1f)
            {
                if (_canvasMoveReports < 4 && _lastCanvasMoveFrame != Time.frameCount)
                {
                    _canvasMoveReports++; _lastCanvasMoveFrame = Time.frameCount;
                    Plugin.Logger.LogInfo($"HUD presentation: canvas moved after placement; local widget positions retained; frame={Time.frameCount}, worldDelta={_presentationCanvas.transform.position - _canvasAtPlacement}");
                }
            }
            for (int i = 0; i < _attachedCount; i++)
            {
                AttachmentPose pose = Attached[i];
                if (pose.Node == null || !pose.PresentationReady) continue;
                if (pose.Node.parent != _presentationCanvas.transform)
                {
                    ReportDrift("widget was reparented: " + pose.Node.name);
                    continue;
                }
                bool moved = HudCanvasMath.HasPositionDrift(pose.PresentationPosition, pose.Node.localPosition);
                bool rotated = Quaternion.Angle(pose.PresentationRotation, pose.Node.localRotation) > 0.1f;
                if (moved || rotated)
                {
                    ReportDrift($"corrected {pose.Node.name} ({pose.Mode}), localDelta={pose.Node.localPosition - pose.PresentationPosition}, rotated={rotated}");
                    pose.Node.localPosition = pose.PresentationPosition;
                    pose.Node.localRotation = pose.PresentationRotation;
                }
            }
            // Native HMD duplicates are managed separately from flight widgets.
            CorrectStockRoll(_presentationCanvas);
            // Do not overwrite the visibility snapshot when checking another render.
            foreach (HelmetVisibility readout in HelmetReadouts)
                if (readout.Node != null && readout.Node.activeSelf) readout.Node.SetActive(false);
        }
        finally { _inRenderCallback = false; }
    }

    private static void ReportDrift(string detail)
    {
        if (_driftReports >= 8 || Time.unscaledTime - _lastDriftReport < 2f) return;
        _driftReports++; _lastDriftReport = Time.unscaledTime;
        Plugin.Logger.LogWarning($"HUD presentation: {detail}; frame={Time.frameCount}, canvas={_presentationCanvas.renderMode}, originDelta={Datum.originPosition - _originAtPlacement}");
    }

    private static void Queue(List<GameObject> group, Transform center, Transform canvas, Vector3 screenCenter, HudAttachment mode, int groupId)
    {
        foreach (GameObject node in group)
            if (node != null && mode != HudAttachment.Hidden) Queue(node.transform, center, canvas, screenCenter, mode, false, groupId);
    }

    // On-screen bounds (IMGUI coordinates) of one instrument group's placed widgets.
    internal static bool TryGetGroupScreenRect(int group, out Rect rect)
    {
        rect = default;
        bool any = false;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        var corners = new Vector3[4];
        for (int i = 0; i < _attachedCount; i++)
        {
            AttachmentPose pose = Attached[i];
            if (pose.Group != group || pose.Node == null || !pose.PresentationReady
                || !pose.Node.gameObject.activeInHierarchy || !(pose.Node is RectTransform r)) continue;
            r.GetWorldCorners(corners);
            foreach (Vector3 c in corners)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
            }
            any = true;
        }
        if (!any) return false;
        rect = new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
        return rect.width > 4f && rect.height > 4f;
    }

    private static void Queue(Transform node, Transform center, Transform canvas, Vector3 screenCenter, HudAttachment mode, bool centerMarker = false, int groupId = -1)
    {
        if (node == null || !node.gameObject.activeInHierarchy || node == center || center.IsChildOf(node))
            return;
        for (int i = 0; i < _attachedCount; i++)
            if (Attached[i].Node == node || node.IsChildOf(Attached[i].Node)) return;

        AttachmentPose pose;
        if (_attachedCount < Attached.Count) pose = Attached[_attachedCount];
        else { pose = new AttachmentPose(); Attached.Add(pose); }
        pose.Node = node;
        pose.Group = groupId;
        pose.PresentationReady = false;
        pose.Parent = node.parent;
        pose.Mode = mode;
        pose.WasActive = node.gameObject.activeSelf;
        pose.LocalScale = node.localScale;
        pose.Sibling = node.GetSiblingIndex();
        if (node is RectTransform rect)
        {
            pose.AnchorMin = rect.anchorMin;
            pose.AnchorMax = rect.anchorMax;
            pose.Pivot = rect.pivot;
            pose.SizeDelta = rect.sizeDelta;
            pose.AnchoredPosition = rect.anchoredPosition3D;
        }
        pose.LocalPosition = node.localPosition;
        pose.LocalRotation = node.localRotation;
        // Remove only the rotation of the widget's actual native reference frame.
        // Some native widgets (notably the ribbon) are anchored outside HUDCenter.
        Transform reference = node.IsChildOf(center) ? center : canvas;
        pose.Offset = centerMarker ? Vector3.zero : reference == center
            ? HudCanvasMath.RelativeOffset(NativeLocalPoint(node, center), center.lossyScale, canvas.lossyScale)
            : NativeLocalPoint(node, canvas) - screenCenter;
        pose.LayoutRotation = centerMarker ? Quaternion.identity : Quaternion.Inverse(reference.rotation) * node.rotation;
        _attachedCount++;
    }
    internal static Vector3 NativeLocalPoint(Transform node, Transform reference)
    {
        // Use the original local layout instead of subtracting two projected
        // world positions, which lose precision when HUDCenter is far off-screen.
        Vector3 point = Vector3.zero;
        for (Transform current = node; current != null && current != reference; current = current.parent)
            point = current.localPosition + current.localRotation * Vector3.Scale(current.localScale, point);
        return point;
    }
    private static Transform _root;
    private static FieldInfo _compassField;

    /// <summary>
    /// Applies the three groups. Called every frame from the chase postfix, like the rest of the HUD
    /// repair, so switching aircraft or camera mode is picked up without anything having to announce
    /// it. <c>SetActive</c> is only called when a group is actually in the wrong state.
    /// </summary>
    internal static void Apply(HudAttachment left, HudAttachment right, HudAttachment compass, bool hudView = false)
    {
        FlightHud hud = SceneSingleton<FlightHud>.i;
        if (hud == null)
        {
            RestoreAttachments();
            _root = null;
            return;
        }

        if (_root != hud.transform || IsStale())
            Rescan(hud);

        Show(Left, left != HudAttachment.Hidden, hudView);
        Show(Right, right != HudAttachment.Hidden, hudView);
        Show(Compass, compass != HudAttachment.Hidden, hudView);

    }

    internal static void RestoreHudViewVisibility()
    {
        RestoreAttachments();
        RestoreHelmetDuplicates();
        foreach (var saved in HudViewVisibility)
            if (saved.Key != null && saved.Key.activeSelf != saved.Value) saved.Key.SetActive(saved.Value);
        HudViewVisibility.Clear();
    }

    /// <summary>Puts every group back, for leaving chase.</summary>
    internal static void ShowAll()
    {
        RestoreAttachments();
        RestoreHelmetDuplicates();
        if (_root == null)
            return;

        Show(Left, true);
        Show(Right, true);
        Show(Compass, true);
    }

    private static void Show(List<GameObject> group, bool show, bool hudView = false)
    {
        foreach (GameObject element in group)
        {
            if (element != null && element.activeSelf != show)
            {
                if (hudView && !HudViewVisibility.ContainsKey(element)) HudViewVisibility.Add(element, element.activeSelf);
                element.SetActive(show);
            }
        }
    }

    /// <summary>
    /// True once anything collected has been destroyed — which is what changing aircraft does, since
    /// the instrument cluster is a per-airframe prefab (<c>Fighter1_HUDExtras</c>,
    /// <c>AttackHelo1_HUDExtras</c>, and so on) instantiated under the shared canvas.
    /// </summary>
    // The aircraft the groups were collected for. A new aircraft brings new gauges, and a scan made
    // between aircraft (after a death, when the HUD holds none) finds nothing to watch for, so both
    // the aircraft changing and an empty scan count as stale (empty ones retried twice a second).
    private static Aircraft _scannedAircraft;
    private static float _nextEmptyRescan;

    private static bool IsStale()
    {
        Aircraft aircraft = SceneSingleton<CombatHUD>.i != null ? SceneSingleton<CombatHUD>.i.aircraft : null;
        if (aircraft != _scannedAircraft) return true;
        if (Left.Count == 0 && Right.Count == 0 && aircraft != null && Time.unscaledTime >= _nextEmptyRescan) return true;

        foreach (GameObject element in Left)
            if (element == null) return true;

        foreach (GameObject element in Right)
            if (element == null) return true;

        foreach (GameObject element in Compass)
            if (element == null) return true;

        return false;
    }

    private static void Rescan(FlightHud hud)
    {
        RestoreAttachments();
        _root = hud.transform;
        OwnedLayouts.RemoveAll(layout => layout == null);

        Left.Clear();
        Right.Clear();
        Compass.Clear();
        _scannedAircraft = SceneSingleton<CombatHUD>.i != null ? SceneSingleton<CombatHUD>.i.aircraft : null;
        _nativeFuel = _nativeThrottle = null;
        _nextEmptyRescan = Time.unscaledTime + 0.5f;

        // Inactive children included: a gauge the game has switched off for this airframe must still
        // be collected, or turning the group back on would leave it behind.
        foreach (Transform node in _root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            foreach (Component component in node.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (component is HUDApp app && (!HudLayoutMath.IsFlightInstrument(AppType(app)) || IsHelmetReadout(app)))
                    continue;
                string type = component.GetType().Name;

                if (component is FuelGauge) _nativeFuel = component;
                else if (component is ThrottleGauge) _nativeThrottle = component;

                if (Contains(LeftTypes, type))
                    AddInstrument(Left, component);
                else if (Contains(RightTypes, type))
                    AddInstrument(Right, component);
                else if (Contains(CompassTypes, type))
                    AddInstrument(Compass, component);
            }
        }

        // The ribbon has no component of its own — it is a RawImage held in a private field on the
        // HUD, the same shape as the waterline and the pitch ladder. Taking the GameObject rather
        // than the RawImage's `enabled` flag means its heading marker goes with it.
        _compassField ??= AccessTools.Field(typeof(FlightHud), "compass");
        if (_compassField?.GetValue(hud) is Component ribbon)
            Add(Compass, ribbon.gameObject);

        Plugin.Logger.LogInfo(
            $"HUD element groups resolved: {Left.Count} left, {Right.Count} right, "
            + $"{Compass.Count} compass.");
    }

    private static readonly FieldInfo AppTypeField = AccessTools.Field(typeof(HUDApp), "type");
    private static int AppType(HUDApp app) => AppTypeField == null ? 0 : System.Convert.ToInt32(AppTypeField.GetValue(app));

    private sealed class HelmetVisibility { internal GameObject Node; internal bool Active; }
    private static readonly List<HelmetVisibility> HelmetReadouts = new();
    private static HeadMountedDisplay _helmet;
    private static readonly string[] HelmetFields = { "speed", "altitude", "bearing" };
    private static readonly FieldInfo[] HelmetReferences = System.Array.ConvertAll(HelmetFields,
        name => AccessTools.Field(typeof(HeadMountedDisplay), name));

    private static bool IsHelmetReadout(Component candidate)
    {
        HeadMountedDisplay helmet = SceneSingleton<HeadMountedDisplay>.i;
        if (helmet == null) return false;
        foreach (FieldInfo field in HelmetReferences)
            if (field?.GetValue(helmet) is Component readout
                && (candidate == readout || candidate.transform.IsChildOf(readout.transform)))
                return true;
        return false;
    }

    // The helmet display switches its readouts on and off itself every frame (they show when you
    // look away from the HUD), so they are faded with a CanvasGroup rather than deactivated. Only
    // the four readouts: other HUD parts (the minimap among them) can sit under the same object.
    private static readonly FieldInfo[] HelmetFadeFields = System.Array.ConvertAll(
        new[] { "speed", "altitude", "bearing", "horizon" }, name => AccessTools.Field(typeof(HeadMountedDisplay), name));
    private static readonly List<CanvasGroup> _helmetFades = new();
    private static HeadMountedDisplay _helmetFaded;

    /// <summary>Hide (or show again) the helmet's speed, altitude, heading and horizon readouts.</summary>
    internal static void FadeHelmet(bool hide)
    {
        HeadMountedDisplay helmet = SceneSingleton<HeadMountedDisplay>.i;
        if (helmet != _helmetFaded)
        {
            _helmetFades.Clear();
            _helmetFaded = helmet;
            if (helmet == null || !hide) { _helmetFaded = null; return; }
            foreach (FieldInfo field in HelmetFadeFields)
                if (field?.GetValue(helmet) is Component readout)
                    _helmetFades.Add(readout.GetComponent<CanvasGroup>() ?? readout.gameObject.AddComponent<CanvasGroup>());
        }
        float alpha = hide ? 0f : 1f;
        foreach (CanvasGroup fade in _helmetFades)
            if (fade != null && fade.alpha != alpha) fade.alpha = alpha;
    }

    private static void SuppressHelmetDuplicates()
    {
        HeadMountedDisplay helmet = SceneSingleton<HeadMountedDisplay>.i;
        if (_helmet != helmet)
        {
            RestoreHelmetDuplicates();
            HelmetReadouts.Clear();
            _helmet = helmet;
            if (helmet != null)
                foreach (FieldInfo field in HelmetReferences)
                    if (field?.GetValue(helmet) is Component readout)
                        HelmetReadouts.Add(new HelmetVisibility { Node = readout.gameObject });
        }
        foreach (HelmetVisibility readout in HelmetReadouts)
        {
            if (readout.Node == null) continue;
            readout.Active = readout.Node.activeSelf;
            if (readout.Active) readout.Node.SetActive(false);
        }
    }

    private static void RestoreHelmetDuplicates()
    {
        foreach (HelmetVisibility readout in HelmetReadouts)
            if (readout.Node != null && readout.Node.activeSelf != readout.Active)
                readout.Node.SetActive(readout.Active);
    }

    private static void AddInstrument(List<GameObject> group, Component instrument)
    {
        Add(group, instrument.gameObject);
        // Prefabs may store readouts beside, rather than underneath, their HUDApp.
        // In particular include SpeedGauge's airspeedDisplay and Altitude's radarAlt.
        foreach (FieldInfo field in instrument.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!typeof(Component).IsAssignableFrom(field.FieldType)) continue;
            if (field.GetValue(instrument) is Component display && display.transform.IsChildOf(_root)
                && display.transform != _root && display.transform is RectTransform
                && !display.transform.IsChildOf(instrument.transform))
                Add(group, display.gameObject);
        }
        // Ensure parents are queued first so children inherit the relocated widget once.
        group.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));
    }

    private static int Depth(Transform node)
    {
        int depth = 0;
        while (node.parent != null) { depth++; node = node.parent; }
        return depth;
    }

    private static void Add(List<GameObject> group, GameObject element)
    {
        if (!group.Contains(element))
            group.Add(element);
    }

    private static bool Contains(string[] types, string name)
    {
        foreach (string type in types)
            if (type == name) return true;

        return false;
    }
}
