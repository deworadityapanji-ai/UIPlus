using System.IO;
using System.Threading;
using BepInEx;

namespace UIPlus.Menu;

internal static class BackgroundFiles
{
    internal const string RelativeDirectory = "BepInEx/plugins/UIPlus/Backgrounds";

    private static string DirectoryPath => Path.Combine(Paths.PluginPath, "UIPlus", "Backgrounds");

    internal static string EnsureDirectory()
    {
        string directory = DirectoryPath;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            string readme = Path.Combine(directory, "README.txt");
            try
            {
                using var output = new StreamWriter(new FileStream(readme, FileMode.CreateNew, FileAccess.Write));
                output.WriteLine("Drop PNG, JPG, JPEG, MP4 or WebM files here for UI Plus title-screen backgrounds.");
                output.WriteLine("For best video compatibility use H.264/AAC MP4. An empty folder uses the stock art.");
            }
            catch (IOException) when (File.Exists(readme)) { } // A user file always wins.
        }
        return directory;
    }

    internal static string[] Scan(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        EnsureDirectory();
        token.ThrowIfCancellationRequested();
        return FindFiles();
    }

    internal static string[] FindFiles()
    {
        try { return BackgroundPlayback.SortFiles(Directory.EnumerateFiles(DirectoryPath)); }
        catch (DirectoryNotFoundException) { return System.Array.Empty<string>(); }
    }

    internal static byte[] ReadImage(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        if (!BackgroundPlayback.ImageFileAllowed(length))
            throw new InvalidDataException("Image must be nonempty and at most 64 MB on disk.");
        var bytes = new byte[(int)length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            int count = stream.Read(bytes, offset, System.Math.Min(256 * 1024, bytes.Length - offset));
            if (count == 0) throw new EndOfStreamException("The image file ended while it was being read.");
            offset += count;
        }
        var (width, height) = BackgroundPlayback.ImageDimensions(bytes);
        if (!BackgroundPlayback.ImageDimensionsAllowed(width, height))
            throw new InvalidDataException("Image has an invalid PNG/JPEG header or exceeds 8192 pixels on one side.");
        return bytes;
    }
}
