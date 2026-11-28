using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using UIPlus.Helpers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UIPlus.Menu;

[HarmonyPatch(typeof(MainMenu), "Start")]
internal static class TitleMenuStart
{
    private static bool _loggedConflict;

    private static void Postfix(MainMenu __instance)
    {
        if (__instance.gameObject.scene.name != "MainMenu") return;
        // MainMenu Replacer only swaps the background, so the layout still applies alongside it.
        const string layoutMod = "com.newmenulayout";
        if (Chainloader.PluginInfos.ContainsKey(layoutMod))
        {
            if (!_loggedConflict)
            {
                _loggedConflict = true;
                Plugin.Logger.LogInfo($"UI Plus is leaving the title menu layout to {layoutMod}.");
            }
            return;
        }
        __instance.gameObject.AddComponent<TitleMenu>();
    }
}

// Lives on the stock menu, so neither UI objects nor input state outlive the title scene.
internal sealed class TitleMenu : MonoBehaviour
{
    private static readonly FieldInfo FirstLoad = AccessTools.Field(typeof(MainMenu), "firstLoadOverlay");
    private static readonly FieldInfo Overlay = AccessTools.Field(typeof(MainMenu), "overlayMenuLayer");
    private static readonly FieldInfo Missions = AccessTools.Field(typeof(MainMenu), "missionsButton");
    private static readonly string[] ModalFields =
        { "unableToConnect", "disconnectNotification", "roadmapPanel", "controlChangesPanel", "inputSystemUpdatePanel", "openMissionEditorMenu" };

    private readonly List<GameObject> _modals = new();
    private readonly List<Selectable> _navigation = new();
    private int _barStart, _barEnd;
    private Selectable _lastBarItem;
    private GameObject _firstLoad;
    private Transform _overlay;
    private TitleMenuLayout _layout;
    private TitleMenuPanels _panels;
    private MenuSnapshot _snapshot;
    private MenuStyle _built = MenuLayout.DefaultStyle;
    private MenuStyle? _rejected;
    private Vector2 _lastMouse;
    private GameObject _lastHovered;
    private bool _blocked, _navigationDirty = true;
    private MenuAccent? _lastAccent;
    private bool _themeChanged;

    private void Awake()
    {
        var menu = GetComponent<MainMenu>();
        _firstLoad = FirstLoad?.GetValue(menu) as GameObject;
        _overlay = Overlay?.GetValue(menu) as Transform;
        FindModals(menu);
    }

    private void LateUpdate()
    {
        bool modal = ModalOpen();
        MenuStyle style = MenuSettings.Style?.Value ?? MenuLayout.DefaultStyle;
        if (_rejected != style) _rejected = null;
        bool ready = _built != style && _rejected != style && !modal && MainMenu.State == MainMenu.LoadingState.Loaded &&
            GameManager.gameState == GameState.Menu && (_firstLoad == null || !_firstLoad.activeInHierarchy);
        MenuStyleChange change = MenuLayout.StyleChange(_built, style, ready);
        if (change == MenuStyleChange.Restore)
        {
            RestoreLayout(captureLateRows: true);
            // Give Unity one frame to remove the old components before taking the next snapshot.
            return;
        }
        bool firstFrame = change == MenuStyleChange.Build;
        if (firstFrame)
        {
            try
            {
                BuildLayout(style);
                _built = style;
            }
            catch (Exception error)
            {
                RestoreLayout();
                _rejected = style;
                Plugin.Logger.LogWarning($"UI Plus could not build {style} for the title menu. Restored Vanilla. {error}");
                return;
            }
        }
        if (_layout == null) return;

        bool modalClosed = _blocked && !modal;
        var controller = GameManager.playerInput?.controllers.GetLastActiveController();
        bool gamepad = controller?.type == Rewired.ControllerType.Joystick;
        var events = EventSystem.current;
        Vector2 mouse = Input.mousePosition;
        bool mouseMoved = !firstFrame && mouse != _lastMouse;
        _lastMouse = mouse;
        if (!firstFrame) _navigationDirty |= _layout.RefreshRows();
        RefreshAccent();
        _navigationDirty |= _panels.Tick(mouse, gamepad, modal);
        if (_navigationDirty && !modal)
        {
            _navigationDirty = false;
            RebuildNavigation();
        }
        if (_blocked != modal)
        {
            _blocked = modal;
            _layout.SetInput(!modal);
            if (modal)
            {
                _lastHovered = null;
                if (events != null && OwnsFocus(events.currentSelectedGameObject)) events.SetSelectedGameObject(null);
            }
        }

        if (!modal && events != null)
            UpdateFocus(events, mouse, mouseMoved, firstFrame, modalClosed, controller?.type);
        GameObject selected = !modal && events != null ? events.currentSelectedGameObject : null;
        if (_layout.IsBar && selected != null) RememberBarItem(selected);
        _layout.Highlight(selected);
        _panels.Highlight(selected);
    }

    private void UpdateFocus(EventSystem events, Vector2 mouse, bool mouseMoved, bool firstFrame,
        bool modalClosed, Rewired.ControllerType? controller)
    {
        GameObject selected = events.currentSelectedGameObject;
        // Vanilla selects Singleplayer when it loads. Custom styles wait for input instead.
        if (OwnsFocus(selected) && (firstFrame || !IsNavigable(selected)))
        {
            events.SetSelectedGameObject(null);
            selected = null;
        }

        bool clicked = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool navigationKey = PressedNavigationKey();
        GameObject hovered = mouseMoved || clicked ? _panels.HoverTarget(mouse) ?? _layout.HoverTarget(mouse) : null;
        if (hovered != null && (mouseMoved && !navigationKey || clicked && hovered == selected))
            _lastHovered = hovered;

        // The input module can clear selection on a background press. Keep the last usable mouse target.
        bool restoreLastHovered = clicked && hovered == null && IsNavigable(_lastHovered);
        bool startNavigation = navigationKey || (!mouseMoved && !clicked &&
            (controller == Rewired.ControllerType.Joystick || (modalClosed && controller == Rewired.ControllerType.Keyboard)));
        switch (MenuLayout.ChooseFocus(selected != null, mouseMoved && !navigationKey && hovered != null,
            restoreLastHovered, startNavigation))
        {
            case MenuFocus.Hovered:
                if (selected != hovered) events.SetSelectedGameObject(hovered);
                break;
            case MenuFocus.LastHovered:
                events.SetSelectedGameObject(_lastHovered);
                break;
            case MenuFocus.First:
                _layout.SelectFirst();
                break;
        }
    }

    private bool IsNavigable(GameObject target) => target != null && target.activeInHierarchy &&
        target.TryGetComponent(out Selectable selectable) && selectable.IsInteractable() && _navigation.Contains(selectable);

    private static bool PressedNavigationKey() =>
        Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
        Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
        Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Return) ||
        Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);

    private void BuildLayout(MenuStyle style)
    {
        using var timing = new UiTiming("title layout build");
        var menu = GetComponent<MainMenu>();
        Transform canvas = transform.parent;
        var left = transform.Find("LeftPanel") as RectTransform;
        var container = left != null ? left.Find("Container") as RectTransform : null;
        var buttons = container != null ? container.Find("MenuButtonsPanel") as RectTransform : null;
        var quit = left != null ? left.Find("ExitButton")?.GetComponent<Button>() : null;
        var missions = Missions?.GetValue(menu) as Button;
        var name = left != null ? left.Find("playerNameInput")?.GetComponent<TMPro.TMP_InputField>() : null;
        var logo = transform.Find("NewLogo") as RectTransform;
        var news = canvas != null ? canvas.Find("NewsPanel") as RectTransform : null;
        var tips = canvas != null ? canvas.Find("HintPanel") as RectTransform : null;
        var version = news != null ? news.Find("VersionNumber")?.GetComponent<Text>() : null;
        if (left == null || buttons == null || quit == null || missions == null || name == null
            || logo == null || version == null || tips == null || _overlay == null)
            throw new InvalidOperationException("The stock title menu hierarchy was not found.");

        Canvas.ForceUpdateCanvases();
        _snapshot = new MenuSnapshot();
        _snapshot.CaptureTree(left);
        _snapshot.CaptureTree(logo);
        _snapshot.CaptureTree(news);
        _snapshot.CaptureTree(tips);
        _snapshot.Seal();
        _layout = new TitleMenuLayout(_snapshot, style, left, container, buttons, quit, missions, name, logo);
        // Keep the stock news panel clear of the horizontal hit areas when it is shown.
        float newsBottom = _layout.IsBar ? BottomBar.Height + 10f : 22f;
        _panels = new TitleMenuPanels(_snapshot, logo, news, tips, version, new Vector2(SideRail.Width + 22f, newsBottom), _layout);
        _layout.RefreshRows();
        _layout.ValidateGeometry(logo);
        ThemeManager.ThemeGroupChanged += OnThemeChanged;
    }

    private void FindModals(MainMenu menu)
    {
        foreach (string field in ModalFields)
        {
            object value = AccessTools.Field(typeof(MainMenu), field)?.GetValue(menu);
            if (value is GameObject panel) _modals.Add(panel);
            else if (value is Component component) _modals.Add(component.gameObject);
        }
        var quit = transform.Find("LeftPanel/ExitButton")?.GetComponent<Button>();
        if (quit != null)
            for (int i = 0; i < quit.onClick.GetPersistentEventCount(); i++)
                if (quit.onClick.GetPersistentMethodName(i) == "SetActive" &&
                    quit.onClick.GetPersistentTarget(i) is GameObject confirm)
                    _modals.Add(confirm);
        var confirmPanel = transform.parent?.Find("ConfirmLeave");
        if (confirmPanel != null && !_modals.Contains(confirmPanel.gameObject)) _modals.Add(confirmPanel.gameObject);
    }

    private void RestoreLayout(bool captureLateRows = false)
    {
        using var timing = new UiTiming("title layout restore");
        if (captureLateRows) _layout.RefreshRows(); // Capture buttons injected since the last update.
        ThemeManager.ThemeGroupChanged -= OnThemeChanged;
        var events = EventSystem.current;
        if (events != null && (_snapshot?.Owns(events.currentSelectedGameObject) == true ||
            OwnsFocus(events.currentSelectedGameObject)))
            events.SetSelectedGameObject(null);

        _panels?.Close();
        _snapshot?.Restore();
        _layout?.RestoreLateSaves();
        _snapshot = null;
        _layout = null;
        _panels = null;
        _built = MenuLayout.DefaultStyle;
        _navigation.Clear();
        _barStart = _barEnd = 0;
        _lastBarItem = null;
        _navigationDirty = true;
        _blocked = false;
        _lastHovered = null;
        _lastAccent = null;
        _themeChanged = false;
    }

    private void OnDestroy()
    {
        ThemeManager.ThemeGroupChanged -= OnThemeChanged;
        _snapshot?.ReleaseAssets();
    }

    private void OnThemeChanged() => _themeChanged = true;

    private void RefreshAccent()
    {
        MenuAccent choice = MenuSettings.Accent.Value;
        if (_lastAccent == choice && (choice != MenuAccent.HudColour || !_themeChanged)) return;
        _lastAccent = choice;
        _themeChanged = false;
        Color accent = choice switch
        {
            MenuAccent.Amber => new Color(0.94f, 0.62f, 0.15f),
            MenuAccent.White => new Color(0.92f, 0.95f, 1f),
            MenuAccent.HudColour when ArcadeHud.GameHudColour(out Color hud) => hud,
            _ => new Color(0.30f, 0.85f, 0.39f),
        };
        _layout.SetAccent(accent);
        _panels.SetAccent(accent);
    }

    private bool ModalOpen()
    {
        if (_overlay != null && _overlay.childCount > 0) return true;
        foreach (GameObject modal in _modals)
            if (modal != null && modal.activeInHierarchy) return true;
        return false;
    }

    private bool OwnsFocus(GameObject selected) => selected != null &&
        (_layout?.OwnsFocus(selected.transform) == true || _panels?.OwnsFocus(selected.transform) == true);

    private void RebuildNavigation()
    {
        _navigation.Clear();
        _layout.AddNavigation(_navigation);
        _barEnd = _navigation.Count;
        _barStart = _barEnd > 0 && _navigation[0] == _layout.Pilot ? 1 : 0;
        _panels.AddNavigation(_navigation);
        for (int i = 0; i < _navigation.Count; i++)
        {
            // Hidden panels and unavailable buttons stay out of this chain.
            var links = new Navigation { mode = Navigation.Mode.Explicit };
            links.selectOnUp = _navigation[(i + _navigation.Count - 1) % _navigation.Count];
            links.selectOnDown = _navigation[(i + 1) % _navigation.Count];
            links.selectOnLeft = links.selectOnUp;
            links.selectOnRight = links.selectOnDown;
            _navigation[i].navigation = links;
        }
        if (_layout.IsBar) LinkBottomNavigation();
    }

    private void LinkBottomNavigation()
    {
        if (_barStart >= _barEnd) return;
        Selectable version = _panels.Version;
        for (int i = _barStart; i < _barEnd; i++)
        {
            var nav = _navigation[i].navigation;
            if (i > _barStart) nav.selectOnLeft = _navigation[i - 1];
            else if (version != null) nav.selectOnLeft = version;
            if (i + 1 < _barEnd) nav.selectOnRight = _navigation[i + 1];
            else if (version != null) nav.selectOnRight = version;
            if (version != null) nav.selectOnUp = version;
            _navigation[i].navigation = nav;
        }
        if (version == null) return;

        var links = version.navigation;
        links.selectOnLeft = _navigation[_barEnd - 1];
        links.selectOnRight = _navigation[_barStart];
        if (_panels.FirstCompactLink is Button first) links.selectOnUp = first;
        else if (_barStart > 0) links.selectOnUp = _layout.Pilot;
        // A row may have gone away while focus was in the popup.
        int last = _navigation.IndexOf(_lastBarItem);
        if (last < _barStart || last >= _barEnd) _lastBarItem = _navigation[_barStart];
        links.selectOnDown = _lastBarItem;
        version.navigation = links;
    }

    private void RememberBarItem(GameObject selected)
    {
        if (_lastBarItem != null && _lastBarItem.gameObject == selected) return;
        for (int i = _barStart; i < _barEnd; i++)
        {
            Selectable item = _navigation[i];
            if (item == null || item.gameObject != selected) continue;
            _lastBarItem = item;
            var version = _panels.Version;
            if (version != null)
            {
                var nav = version.navigation;
                nav.selectOnDown = item;
                version.navigation = nav;
            }
            return;
        }
    }

    internal static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 at, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
    }
}
