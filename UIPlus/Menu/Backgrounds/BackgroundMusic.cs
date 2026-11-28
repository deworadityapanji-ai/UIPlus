using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace UIPlus.Menu;

// Borrow the game's music bus and keep the previous mute state of each source we touch.
internal sealed class BackgroundMusic
{
    private static readonly FieldInfo CurrentSource = AccessTools.Field(typeof(MusicManager), "currentSource");
    private static readonly FieldInfo FadeSource = AccessTools.Field(typeof(MusicManager), "fadeSource");
    private readonly Dictionary<AudioSource, bool> _savedMute = new();
    private AudioSource _source, _fade;
    private bool _refreshPending = true;
    private float _retryAt;

    internal AudioMixerGroup Mixer { get; private set; }

    internal void Invalidate()
    {
        _refreshPending = true;
        _retryAt = 0f;
    }

    internal void Refresh()
    {
        // CrossFade exchanges the same two sources. Both stay in the cache regardless of order.
        if (!_refreshPending && _source != null && _fade != null && Mixer != null) return;
        float now = Time.unscaledTime;
        if (now < _retryAt) return;
        _retryAt = now + 0.5f;
        _refreshPending = false;
        SoundManager sound = SoundManager.i;
        var manager = sound != null ? sound.Music : null;
        _source = manager != null ? CurrentSource?.GetValue(manager) as AudioSource : null;
        _fade = manager != null ? FadeSource?.GetValue(manager) as AudioSource : null;
        Mixer = sound != null ? sound.MusicMixer : null;
        if (Mixer == null && _source != null) Mixer = _source.outputAudioMixerGroup;
        if (Mixer == null && _fade != null) Mixer = _fade.outputAudioMixerGroup;
    }

    internal int SetVideoAudible(bool audible)
    {
        if (!audible)
        {
            Restore();
            return 0;
        }
        int muted = MutePlaying(_source) ? 1 : 0;
        if (_fade != _source && MutePlaying(_fade)) muted++;
        return muted;
    }

    private bool MutePlaying(AudioSource source)
    {
        if (source == null || !source.isPlaying) return false;
        if (!_savedMute.ContainsKey(source)) _savedMute.Add(source, source.mute);
        if (!source.mute) source.mute = true;
        return true;
    }

    internal void Restore()
    {
        foreach (var entry in _savedMute)
            if (entry.Key != null) entry.Key.mute = entry.Value;
        _savedMute.Clear();
    }
}
