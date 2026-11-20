using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIPlus.Settings;

/// <summary>
/// Adds a "UI PLUS" tab to the game's own settings menu (main menu and pause menu).
///
/// How the game's menu works: SettingsMenu has a row of SettingsMenuButton tabs; clicking one
/// instantiates that button's MenuPrefab as the page. So the tab is a copy of the default tab
/// button with our own page as its prefab. The page is a copy of the game's Gameplay page with
/// its rows removed and its own script (GameplayMenu) stripped, keeping one toggle, one slider
/// and one dropdown row to copy from; <see cref="SettingsPage"/> fills it when it opens.
/// If anything about the menu's shape is not what this expects, the tab is skipped and logged;
/// the settings stay available in Configuration Manager.
/// </summary>
[HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.Start))]
internal static class SettingsTab
{
    private static readonly FieldInfo DefaultMenu = AccessTools.Field(typeof(SettingsMenu), "defaultMenu");
    private static GameObject _template;
    private static bool _failed;

    private static void Prefix(SettingsMenu __instance)
    {
        if (_failed) return;
        try { AddTab(__instance); }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Logger.LogWarning($"UI Plus settings tab disabled (use Configuration Manager instead): {e}");
        }
    }

    private static void AddTab(SettingsMenu menu)
    {
        var first = DefaultMenu?.GetValue(menu) as SettingsMenuButton
            ?? throw new InvalidOperationException("the settings menu has no default tab");
        Transform bar = first.transform.parent;
        if (bar.Find("UIPlusTab") != null) return;
        if (_template == null) _template = SettingsPage.CreateTemplate(SourcePage(bar));

        SettingsMenuButton tab = Object.Instantiate(first, bar);
        tab.name = "UIPlusTab";
        tab.MenuPrefab = _template;
        tab.transform.SetAsLastSibling();
        foreach (Text text in tab.GetComponentsInChildren<Text>(true)) text.text = Caption(text.text);
        foreach (TMP_Text text in tab.GetComponentsInChildren<TMP_Text>(true)) text.text = Caption(text.text);
    }

    // Match the game's tab lettering (its tabs are all capitals).
    private static string Caption(string like) =>
        !string.IsNullOrEmpty(like) && like == like.ToUpperInvariant() ? "UI PLUS" : "UI Plus";

    // The Gameplay page: it has toggle, slider and dropdown rows to copy.
    private static GameObject SourcePage(Transform bar)
    {
        List<GameObject> pages = bar.GetComponentsInChildren<SettingsMenuButton>(true)
            .Select(b => b.MenuPrefab).Where(p => p != null).ToList();
        return pages.FirstOrDefault(p => p.GetComponentInChildren<GameplayMenu>(true) != null)
            ?? pages.FirstOrDefault(p => p.GetComponentInChildren<Toggle>(true) != null
                && p.GetComponentInChildren<Slider>(true) != null && p.GetComponentInChildren<Dropdown>(true) != null)
            ?? throw new InvalidOperationException("no settings page with toggle, slider and dropdown rows");
    }
}
