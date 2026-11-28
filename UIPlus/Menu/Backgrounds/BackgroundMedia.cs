using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace UIPlus.Menu;

// Owns either one decoded still or a streaming decoder with one screen-sized output.
internal sealed class BackgroundMedia : IDisposable
{
    private const float PrepareTimeout = 10f;
    internal readonly string FilePath;
    internal readonly bool IsVideo;
    internal Texture Texture { get; private set; }
    internal bool Ready { get; private set; }
    internal bool Ended { get; private set; }
    internal string Error { get; private set; }
    private GameObject _host;
    private VideoPlayer _video;
    private AudioSource _audio;
    private AudioMixerGroup _mixer;
    private Task<(byte[] Bytes, string Error)> _image;
    private RenderTexture _target;
    private int _videoWidth, _videoHeight;
    private bool _prepared, _priming, _played, _stoppedDuringPrime;
    private bool _audioReported, _missingAudioReported;
    private bool _direct, _directMuted;
    private float _directVolume = -1f;
    private int _frameReady = -1;
    private float _deadline;

    internal BackgroundMedia(string path)
    {
        FilePath = path;
        IsVideo = BackgroundPlayback.Kind(path) == BackgroundKind.Video;
    }

    internal void Begin(Transform parent, AudioMixerGroup mixer, BackgroundAudioOutput output, CancellationToken token)
    {
        using var timing = new UiTiming(IsVideo ? "background video begin/prepare" : "background image begin");
        if (!IsVideo)
        {
            _image = Task.Run(() =>
            {
                try { return (BackgroundFiles.ReadImage(FilePath, token), (string)null); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    return ((byte[])null, error.Message);
                }
            }, token);
            return;
        }

        _host = new GameObject("UI Plus title video", typeof(VideoPlayer));
        _host.transform.SetParent(parent, false);
        _mixer = mixer;

        _video = _host.GetComponent<VideoPlayer>();
        _video.playOnAwake = false;
        _video.source = VideoSource.Url;
        _video.url = new Uri(System.IO.Path.GetFullPath(FilePath), UriKind.Absolute).AbsoluteUri;
        _video.renderMode = VideoRenderMode.RenderTexture;
        _video.aspectRatio = VideoAspectRatio.FitOutside;
        _video.skipOnDrop = true;
        _video.waitForFirstFrame = true;
        _video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        _video.prepareCompleted += Prepared;
        _video.errorReceived += DecodeError;
        _video.loopPointReached += LoopPoint;
        _video.frameReady += FrameReady;
        PrepareVideo(output == BackgroundAudioOutput.Smooth);
    }

    private void PrepareVideo(bool direct)
    {
        _prepared = false;
        _direct = direct;
        _video.audioOutputMode = direct ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.AudioSource;
        _video.controlledAudioTrackCount = 1;
        // URL playback needs the track enabled and routed when Unity prepares the decoder.
        _video.EnableAudioTrack(0, true);
        if (!direct)
        {
            _audio = _host.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _audio.outputAudioMixerGroup = _mixer;
            _audio.volume = 0f;
            _video.SetTargetAudioSource(0, _audio);
        }
        _deadline = Time.realtimeSinceStartup + PrepareTimeout;
        _video.Prepare();
    }

    internal void Poll()
    {
        if (Error != null) return;
        if (IsVideo) PollVideo();
        else if (!Ready && _image.IsCompleted)
        {
            var result = _image.GetAwaiter().GetResult();
            _image = null;
            Error = result.Error;
            if (Error == null) DecodeImage(result.Bytes);
        }
    }

    private void DecodeImage(byte[] bytes)
    {
        using var timing = new UiTiming("background image decode");
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        Texture = image;
        if (!ImageConversion.LoadImage(image, bytes, markNonReadable: true) ||
            !BackgroundPlayback.ImageDimensionsAllowed(image.width, image.height))
        {
            Error = "Unity could not decode the PNG/JPEG within the image limits.";
            return;
        }
        var (width, height) = BackgroundPlayback.DisplaySize(image.width, image.height);
        if (width != image.width || height != image.height)
        {
            // Keep the downscaled GPU copy. Reading it back to CPU memory would stall rendering.
            _target = AllocateTarget(width, height);
            Graphics.Blit(image, _target);
            Texture = _target;
            Object.Destroy(image);
        }
        Ready = true;
    }

    private void PollVideo()
    {
        if (_prepared) ResizeVideoOutput();
        if (Ready) return;
        if (Time.realtimeSinceStartup >= _deadline)
        {
            Error = "Timed out preparing the video or decoding its first frame.";
            return;
        }
        if (!_prepared) return;
        if (!_priming)
        {
            using var timing = new UiTiming("background video first frame start");
            if (_direct && _video.audioTrackCount > 0)
            {
                // Capability is backend-dependent. Fall back before any unattenuated sound can play.
                if (!_video.canSetDirectAudioVolume)
                {
                    Plugin.Logger.LogWarning($"UI Plus background: '{Path.GetFileName(FilePath)}' cannot set Direct audio volume. Using Mixer.");
                    _video.Stop();
                    PrepareVideo(direct: false);
                    return;
                }
                // The next item is primed in the background and must stay silent until promoted.
                _video.SetDirectAudioMute(0, true);
                _directMuted = true;
            }
            _priming = true;
            _video.sendFrameReadyEvents = true;
            _video.Play();
        }
        // Prepare completes before anything is drawn. Give the first decoded frame time
        // to reach the render texture, then leave it there until the fade completes.
        if (_frameReady >= 0 && Time.frameCount > _frameReady)
        {
            _video.Pause();
            _video.sendFrameReadyEvents = false;
            Ready = true;
        }
    }

    private void ResizeVideoOutput()
    {
        int width = Screen.width, height = Screen.height;
        if (width < 1 || height < 1 || (_videoWidth == width && _videoHeight == height)) return;
        using var timing = new UiTiming("background video output allocation");
        var replacement = AllocateTarget(width, height);
        var previous = _target;
        try
        {
            if (previous != null)
            {
                var uv = BackgroundPlayback.CoverUv((float)previous.width / previous.height, (float)width / height);
                Graphics.Blit(previous, replacement, new Vector2(uv.Width, uv.Height), new Vector2(uv.X, uv.Y));
            }
            _video.targetTexture = replacement;
        }
        catch
        {
            ReleaseTarget(replacement);
            throw;
        }
        Texture = _target = replacement;
        _videoWidth = width;
        _videoHeight = height;
        if (previous != null) ReleaseTarget(previous);
    }

    private static RenderTexture AllocateTarget(int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            useMipMap = false,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        try
        {
            if (!texture.Create()) throw new InvalidDataException($"Could not allocate a {width}×{height} render texture.");
            return texture;
        }
        catch
        {
            ReleaseTarget(texture);
            throw;
        }
    }

    internal void Play(bool loop)
    {
        if (!IsVideo) return;
        if (_stoppedDuringPrime || Ended) _video.time = 0;
        _played = true;
        Ended = false;
        _video.isLooping = loop;
        _video.Play();
    }

    internal void KeepLooping()
    {
        if (Ended) Play(loop: true);
        else if (!_video.isLooping) _video.isLooping = true;
    }

    internal void Fail(string error) => Error = error;

    internal void RouteAudio(AudioMixerGroup mixer)
    {
        if (_mixer == mixer) return;
        _mixer = mixer;
        if (_audio != null) _audio.outputAudioMixerGroup = mixer;
    }

    internal bool AllowSound(bool enabled)
    {
        if (_video == null || !_played || _video.audioTrackCount == 0) return false;
        bool playing = enabled && !Ended && _video.isPlaying;
        if (_direct)
        {
            // The game stores linear gains and its music sources ignore listener pause.
            // Direct bypasses the mixer, so apply the two slider gains and the listener volume here.
            float gain = playing ? BackgroundPlayback.DirectVolume(AudioMixerVolume.GetPref(AudioMixerVolume.Music),
                AudioMixerVolume.GetPref(AudioMixerVolume.Master), AudioListener.volume) : 0f;
            if (gain > 0f && gain != _directVolume)
            {
                _video.SetDirectAudioVolume(0, gain);
                _directVolume = gain;
            }
            bool muted = gain <= 0f;
            if (muted != _directMuted)
            {
                _video.SetDirectAudioMute(0, muted);
                _directMuted = muted;
            }
            return !muted;
        }

        float volume = 0f;
        if (playing)
            volume = _mixer != null ? 1f : Mathf.Clamp01(AudioMixerVolume.GetPref(AudioMixerVolume.Music));
        if (_audio.volume != volume) _audio.volume = volume;
        return volume > 0f;
    }

    internal void ReportAudio(bool requested, int mutedSources)
    {
        if (_video == null || !_played || (!_video.isPlaying && !Ended)) return;
        if (_audioReported && (!requested || _missingAudioReported || _video.audioTrackCount > 0)) return;
        string file = Path.GetFileName(FilePath);
        int tracks = _video.audioTrackCount;
        if (!_audioReported)
        {
            _audioReported = true;
            string mode = _direct ? "Smooth (Direct)" : "Mixer";
            string volume = _direct ? "MusicVolume × MasterVolume (linear game sliders) × listener"
                : _mixer != null ? _mixer.name : "MusicVolume (linear game slider, no mixer)";
            Plugin.Logger.LogInfo($"UI Plus background: {file} audio tracks={tracks}, output={mode}, volume={volume}, stock music muted={mutedSources} sources");
        }
        if (requested && tracks == 0 && !_missingAudioReported)
        {
            _missingAudioReported = true;
            Plugin.Logger.LogWarning($"UI Plus background: '{file}' has no audio track reported by Unity.");
        }
    }

    private void Prepared(VideoPlayer player) => _prepared = true;
    private void DecodeError(VideoPlayer player, string error) => Error = string.IsNullOrEmpty(error) ? "Video decoding failed." : error;
    private void FrameReady(VideoPlayer player, long frame)
    {
        if (_frameReady < 0) _frameReady = Time.frameCount;
    }

    private void LoopPoint(VideoPlayer player)
    {
        if (!_played) _stoppedDuringPrime = true;
        else if (!player.isLooping) Ended = true;
    }

    private static void ReleaseTarget(RenderTexture texture)
    {
        texture.Release();
        Object.Destroy(texture);
    }

    public void Dispose()
    {
        try
        {
            if (_video != null)
            {
                _video.prepareCompleted -= Prepared;
                _video.errorReceived -= DecodeError;
                _video.loopPointReached -= LoopPoint;
                _video.frameReady -= FrameReady;
                _video.Stop();
                _video.targetTexture = null;
            }
        }
        finally
        {
            if (_host != null)
            {
                _host.SetActive(false);
                Object.Destroy(_host);
            }
            if (Texture is Texture2D image) Object.Destroy(image);
            if (_target != null) ReleaseTarget(_target);
            Texture = null;
            _target = null;
            _image = null;
            _host = null;
            _video = null;
        }
    }
}
