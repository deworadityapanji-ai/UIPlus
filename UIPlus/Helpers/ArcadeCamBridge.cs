using System;
using System.Reflection;

namespace UIPlus.Helpers;

/// <summary>
/// The optional link to ArcadeCam. UI Plus never references ArcadeCam's DLL, so either mod works
/// without the other. When ArcadeCam is loaded, this finds its public <c>ArcadeCam.ArcadeCamApi</c>
/// class by name and reads from it:
///   WeaponCameraShowing  the weapon camera is on screen (the targeting screen hides)
///   Callout              ArcadeCam judged a HIT, MISS or DESTROYED (shown on the Arcade HUD)
/// </summary>
internal static class ArcadeCamBridge
{
    internal const string Guid = "ArcadeCam";
    private static bool _tried;
    private static Func<bool> _weaponCameraShowing;
    private static EventInfo _calloutEvent;
    private static Delegate _calloutHandler;

    internal static bool Connected => _weaponCameraShowing != null;

    /// <summary>True while ArcadeCam's weapon camera is on screen. False without ArcadeCam.</summary>
    internal static bool WeaponCameraShowing
    {
        get
        {
            try { return _weaponCameraShowing != null && _weaponCameraShowing(); }
            catch { return false; }
        }
    }

    // Called every frame until it has looked once; ArcadeCam may load after UI Plus.
    internal static void TryConnect()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            Type api = Type.GetType("ArcadeCam.ArcadeCamApi, ArcadeCam");
            if (api == null) { Plugin.Logger.LogInfo("ArcadeCam not found; running on its own."); return; }
            PropertyInfo showing = api.GetProperty("WeaponCameraShowing", BindingFlags.Public | BindingFlags.Static);
            if (showing != null)
                _weaponCameraShowing = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), showing.GetGetMethod());
            _calloutEvent = api.GetEvent("Callout", BindingFlags.Public | BindingFlags.Static);
            if (_calloutEvent != null)
            {
                _calloutHandler = new Action<string, string, bool>(OnCallout);
                _calloutEvent.AddEventHandler(null, _calloutHandler);
            }
            Plugin.Logger.LogInfo("Connected to ArcadeCam.");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not connect to ArcadeCam: {e.Message}");
        }
    }

    internal static void Disconnect()
    {
        try { if (_calloutEvent != null && _calloutHandler != null) _calloutEvent.RemoveEventHandler(null, _calloutHandler); }
        catch { }
        _calloutEvent = null;
        _calloutHandler = null;
        _weaponCameraShowing = null;
    }

    private static void OnCallout(string title, string name, bool alert)
    {
        if (Plugin.KillCallouts.Value) ArcadeHud.ShowCallout(title, name, alert);
    }
}
