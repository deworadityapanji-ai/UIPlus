using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UIPlus.Menu;

internal enum BackgroundTransition { Crossfade, FadeThroughBlack }
internal enum BackgroundAudioOutput { Smooth, Mixer }
internal enum BackgroundKind { Unsupported, Image, Video }
internal enum BackgroundSessionAction { Wait, Continue, Stop }

internal static class BackgroundPlayback
{
    internal const long ImageByteLimit = 64L * 1024 * 1024;
    internal const int ImageDimensionLimit = 8192, DisplayDimensionLimit = 4096;
    internal const BackgroundAudioOutput DefaultAudioOutput = BackgroundAudioOutput.Smooth;

    internal static float DirectVolume(float music, float master, float listener = 1f) =>
        LinearGain(music) * LinearGain(master) * LinearGain(listener);

    private static float LinearGain(float volume) => volume > 0f ? Math.Min(1f, volume) : 0f;

    internal static string FolderStatus(bool enabled, int images, int videos, int rejected)
    {
        if (!enabled) return "Custom background is off";
        if (images + videos == 0) return "No files found — showing the stock background";
        string found = $"Found: {images} {(images == 1 ? "image" : "images")}, {videos} {(videos == 1 ? "video" : "videos")}";
        return rejected == 0 ? found : $"{found}\n{rejected} {(rejected == 1 ? "file" : "files")} could not be played — see the log";
    }

    internal static BackgroundSessionAction SessionAction(bool enteredMenu, bool isMenu, string sceneName)
    {
        // Scene loading and SetGameState can happen in either order.
        if (sceneName is "Encyclopedia" or "GameWorld") return BackgroundSessionAction.Stop;
        if (isMenu) return BackgroundSessionAction.Continue;
        return enteredMenu ? BackgroundSessionAction.Stop : BackgroundSessionAction.Wait;
    }

    internal static BackgroundKind Kind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" => BackgroundKind.Image,
        ".mp4" or ".webm" => BackgroundKind.Video,
        _ => BackgroundKind.Unsupported,
    };

    internal static string[] SortFiles(IEnumerable<string> files) => files
        .Where(path => Kind(path) != BackgroundKind.Unsupported)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ThenBy(path => path, StringComparer.Ordinal)
        .ToArray();

    internal static bool ImageFileAllowed(long bytes) => bytes > 0 && bytes <= ImageByteLimit;
    internal static bool ImageDimensionsAllowed(int width, int height) =>
        width > 0 && height > 0 && Math.Max(width, height) <= ImageDimensionLimit;

    internal static (int Width, int Height) DisplaySize(int width, int height)
    {
        int longest = Math.Max(width, height);
        if (longest <= DisplayDimensionLimit) return (width, height);
        double scale = (double)DisplayDimensionLimit / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    // Reject oversized headers before LoadImage can allocate decoded pixel storage.
    internal static (int Width, int Height) ImageDimensions(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 &&
            bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10 &&
            bytes[12] == 'I' && bytes[13] == 'H' && bytes[14] == 'D' && bytes[15] == 'R')
            return (BigEndianInt(bytes, 16), BigEndianInt(bytes, 20));
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) return default;

        int offset = 2;
        while (offset < bytes.Length && bytes[offset++] == 0xff)
        {
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset == bytes.Length) break;
            byte marker = bytes[offset++];
            if (marker is 0xda or 0xd9 || offset > bytes.Length - 2) break;
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            int length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || length > bytes.Length - offset) break;
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc) && length >= 8)
                return ((bytes[offset + 5] << 8) | bytes[offset + 6], (bytes[offset + 3] << 8) | bytes[offset + 4]);
            offset += length;
        }
        return default;
    }

    private static int BigEndianInt(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    internal static (float X, float Y, float Width, float Height) CoverUv(float sourceAspect, float viewAspect)
    {
        if (!(sourceAspect > 0f) || !(viewAspect > 0f)) return (0f, 0f, 1f, 1f);
        float width = Math.Min(1f, viewAspect / sourceAspect);
        float height = Math.Min(1f, sourceAspect / viewAspect);
        return ((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
    }

    internal static (float Incoming, float Black) Fade(BackgroundTransition transition, float progress)
    {
        float t = Math.Max(0f, Math.Min(1f, progress));
        return transition == BackgroundTransition.FadeThroughBlack
            ? (t < 0.5f ? 0f : 1f, 1f - Math.Abs(2f * t - 1f))
            : (t, 0f);
    }
}

internal sealed class BackgroundPlaylist
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private readonly List<string> _available, _remaining = new();
    private readonly Random _random;

    internal BackgroundPlaylist(IEnumerable<string> paths, Random random)
    {
        _available = paths.Distinct(Comparer).ToList();
        _random = random;
    }

    internal int Count => _available.Count;
    internal bool ShouldQueue(bool hasCurrent) => Count > 0 && (!hasCurrent || Count > 1);
    internal bool LoopsVideo => Count == 1;

    internal bool TryNext(string previous, out string path)
    {
        path = "";
        if (Count == 0) return false;
        if (_remaining.Count == 0 || (_remaining.Count == 1 && Count > 1 && Comparer.Equals(_remaining[0], previous)))
            Refill();
        int last = _remaining.Count - 1;
        int pick = last;
        if (last > 0 && Comparer.Equals(_remaining[pick], previous)) pick--;
        path = _remaining[pick];
        _remaining[pick] = _remaining[last];
        _remaining.RemoveAt(last);
        return true;
    }

    internal void Reject(string path)
    {
        _available.RemoveAll(p => Comparer.Equals(p, path));
        _remaining.RemoveAll(p => Comparer.Equals(p, path));
    }

    private void Refill()
    {
        _remaining.Clear();
        _remaining.AddRange(_available);
        for (int i = _remaining.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            string swap = _remaining[i];
            _remaining[i] = _remaining[j];
            _remaining[j] = swap;
        }
    }
}
