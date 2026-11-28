using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = System.Random;

namespace UIPlus.Menu;

[HarmonyPatch(typeof(MainMenu), "Start")]
internal static class TitleBackgroundStart
{
    private static void Postfix(MainMenu __instance)
    {
        if (__instance.gameObject.scene.name != "MainMenu" || GameManager.IsHeadless) return;
        try { TitleBackground.EnsureHost(); }
        catch (Exception error) { Plugin.Logger.LogWarning($"UI Plus title backgrounds could not start: {error.Message}"); }
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.SetGameState))]
internal static class TitleBackgroundStateChange
{
    // Restore the music sources before the game notifies its state-change listeners.
    private static void Prefix(GameState gameState) => TitleBackground.OnGameStateChanging(gameState);
}

// The playlist can outlive a menu canvas or scene. The display layers cannot.
internal sealed class TitleBackground : MonoBehaviour
{
    private static TitleBackground _host;
    private readonly HashSet<string> _logged = new(StringComparer.OrdinalIgnoreCase);
    private readonly BackgroundDisplay _display = new();
    private CancellationTokenSource _scanToken, _playbackToken;
    private Task<string[]> _scan;
    private string[] _files;
    private BackgroundPlaylist _playlist;
    private BackgroundMedia _current, _next;
    private BackgroundMusic _music;
    private Scene _scene;
    private string _sceneName;
    private bool _enteredMenu, _failed, _leftMenu, _fading;
    private float _holdUntil, _fadeStarted, _fadeSeconds;
    private BackgroundTransition _transition;

    internal static int RejectedFileCount => _host != null
        ? _host._logged.Count - (_host._logged.Contains("") ? 1 : 0) : 0;
    internal static bool WasRejected(string path) => _host != null && _host._logged.Contains(path);

    internal static void EnsureHost()
    {
        if (_host != null) return;
        var root = new GameObject("UI Plus menu background player");
        try
        {
            DontDestroyOnLoad(root);
            _host = root.AddComponent<TitleBackground>();
        }
        catch
        {
            Destroy(root);
            throw;
        }
    }

    internal static void OnGameStateChanging(GameState gameState)
    {
        var host = _host;
        if (host == null) return;
        try { host.CheckSession(gameState, SceneManager.GetActiveScene().name); }
        catch (Exception error) { host.Fail(error); }
    }

    private void Awake()
    {
        _scene = SceneManager.GetActiveScene();
        _sceneName = _scene.name;
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    private void OnSceneChanged(Scene previous, Scene current)
    {
        try
        {
            _scene = current;
            _sceneName = current.name;
            // Remove scene references even while the playlist is between items.
            _display.Dispose();
            _music?.Invalidate();
            CheckSession(GameManager.gameState, _sceneName);
        }
        catch (Exception error) { Fail(error); }
    }

    private bool CheckSession(GameState state, string sceneName)
    {
        if (_leftMenu) return false;
        switch (BackgroundPlayback.SessionAction(_enteredMenu, state == GameState.Menu, sceneName))
        {
            case BackgroundSessionAction.Continue:
                _enteredMenu = true;
                return true;
            case BackgroundSessionAction.Stop:
                LeaveMenu();
                return false;
            default:
                return false;
        }
    }

    private void LateUpdate()
    {
        try
        {
            if (CheckSession(GameManager.gameState, _sceneName) && !_failed) Tick();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Fail(Exception error)
    {
        _failed = true;
        Warn("", error.Message);
        CleanUp();
    }

    private void Tick()
    {
        if (_files == null)
        {
            if (_scan == null)
            {
                if (_sceneName == "MainMenu" && MainMenu.State != MainMenu.LoadingState.Loaded) return;
                _scanToken = new CancellationTokenSource();
                CancellationToken token = _scanToken.Token;
                _scan = Task.Run(() => BackgroundFiles.Scan(token), token);
            }
            if (!_scan.IsCompleted) return;
            _files = _scan.GetAwaiter().GetResult();
            _scan = null;
            Cancel(ref _scanToken);
        }

        if (!MenuSettings.CustomBackground.Value)
        {
            if (_playlist != null) StopPlayback();
            return;
        }
        if (_files.Length == 0) return;
        if (_playlist == null)
        {
            _playlist = new BackgroundPlaylist(_files, new Random());
            _playbackToken = new CancellationTokenSource();
        }

        TickPlaylist();
        UpdateAudio();
        var alpha = _fading ? BackgroundPlayback.Fade(_transition, FadeProgress()) : default;
        _display.Present(_scene, _current, _fading ? _next : null, alpha.Incoming, alpha.Black);
    }

    private void UpdateAudio()
    {
        if (_music == null) return;
        _music.Refresh();
        _current?.RouteAudio(_music.Mixer);
        _next?.RouteAudio(_music.Mixer);
        bool requested = MenuSettings.BackgroundVideoAudio.Value;
        int muted = _music.SetVideoAudible(_current?.AllowSound(requested) == true);
        _current?.ReportAudio(requested, muted);
    }

    private void TickPlaylist()
    {
        if (_current != null && !Poll(_current))
        {
            _fading = false;
            Reject(ref _current);
            return;
        }
        if (_next != null && !Poll(_next))
        {
            _fading = false;
            Reject(ref _next);
            return;
        }
        if (_current == null && _next?.Ready == true)
        {
            PromoteNext();
            return;
        }
        if (_next == null && _playlist.ShouldQueue(_current != null))
        {
            QueueNext();
            return;
        }
        if (_current == null) return;

        if (_next?.Ready != true)
        {
            if (_current.IsVideo && _playlist.LoopsVideo) _current.KeepLooping();
            return;
        }

        float now = Time.unscaledTime;
        if (!_fading)
        {
            bool finished = _current.IsVideo ? _current.Ended : now >= _holdUntil;
            if (!finished) return;
            _fadeStarted = now;
            _fadeSeconds = MenuSettings.BackgroundFadeSeconds.Value;
            _transition = MenuSettings.BackgroundTransition.Value;
            _fading = true;
        }
        if (FadeProgress() >= 1f) PromoteNext();
    }

    private float FadeProgress() => (Time.unscaledTime - _fadeStarted) / _fadeSeconds;

    private void PromoteNext()
    {
        _current?.Dispose();
        _current = _next;
        _next = null;
        _fading = false;
        _current.Play(_playlist.LoopsVideo);
        _holdUntil = Time.unscaledTime + MenuSettings.BackgroundImageSeconds.Value;
    }

    private void QueueNext()
    {
        if (!_playlist.TryNext(_current?.FilePath, out string path)) return;
        _next = new BackgroundMedia(path);
        try
        {
            if (_next.IsVideo)
            {
                _music ??= new BackgroundMusic();
                _music.Refresh();
            }
            _next.Begin(transform, _music?.Mixer, MenuSettings.BackgroundVideoAudioOutput.Value, _playbackToken.Token);
        }
        catch (Exception error) when (MediaError(error))
        {
            Reject(ref _next, error.Message);
        }
    }

    private bool Poll(BackgroundMedia media)
    {
        try { media.Poll(); }
        catch (Exception error) when (MediaError(error))
        {
            media.Fail(error.Message);
        }
        return media.Error == null;
    }

    private static bool MediaError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or UriFormatException or UnityException;

    private void Reject(ref BackgroundMedia media, string error = null)
    {
        Warn(media.FilePath, error ?? media.Error);
        _playlist.Reject(media.FilePath);
        Release(ref media);
    }

    private void Warn(string path, string reason)
    {
        if (!_logged.Add(path)) return;
        string prefix = string.IsNullOrEmpty(path) ? "disabled for this menu visit" : $"skipping '{Path.GetFileName(path)}'";
        Plugin.Logger.LogWarning($"UI Plus title backgrounds: {prefix}. {reason}");
    }

    private void Release(ref BackgroundMedia media, bool duringCleanup = false)
    {
        var old = media;
        media = null;
        if (duringCleanup) DisposeSafely(old);
        else old?.Dispose();
    }

    private void DisposeSafely(IDisposable item)
    {
        try { item?.Dispose(); }
        catch (Exception error)
        {
            _failed = true;
            Warn("", error.Message);
        }
    }

    private static void Cancel(ref CancellationTokenSource token)
    {
        var old = token;
        token = null;
        if (old == null) return;
        try { old.Cancel(); }
        finally { old.Dispose(); }
    }

    private void StopPlayback()
    {
        _fading = false;
        _playlist = null;
        try { Cancel(ref _playbackToken); }
        finally
        {
            try { _music?.Restore(); }
            finally
            {
                _music = null;
                DisposeSafely(_display);
                Release(ref _current, duringCleanup: true);
                Release(ref _next, duringCleanup: true);
            }
        }
    }

    private void LeaveMenu()
    {
        if (_leftMenu) return;
        _leftMenu = true;
        CleanUp();
        if (_host == this) _host = null;
        Destroy(gameObject);
    }

    private void OnApplicationQuit() => LeaveMenu();

    private void OnDisable() => CleanUp();

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        if (_host == this) _host = null;
        CleanUp();
    }

    private void CleanUp()
    {
        _scan = null;
        _files = null;
        try
        {
            try { Cancel(ref _scanToken); }
            finally { StopPlayback(); }
        }
        catch (Exception error)
        {
            _failed = true;
            Warn("", error.Message);
        }
    }
}
