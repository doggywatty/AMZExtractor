namespace AMZExtractor;

/// <summary>
/// Turns paths into safe, creatable filesystem paths.
/// AMZ names are archive controlled data, so they get scrubbed of path separators, invalid characters and Windows device names.
/// </summary>
internal static class OutputPath
{
    private const int MaxSegmentLength = 200;

    private static readonly char[] Separators = ['/', '\\'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] InvalidChars = BuildInvalidChars();

    public static string Build(string root, params string[] parts)
    {
        List<string> segments = [];
        foreach (string part in parts)
        {
            foreach (string segment in part.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string cleaned = CleanSegment(segment);
                if (cleaned.Length > 0)
                    segments.Add(cleaned);
            }
        }

        if (segments.Count == 0)
            segments.Add("_");

        string combined = Path.Combine([root, .. segments]);

        // MAX_PATH is 260 on windows, the extended-length prefix lifts that
        return combined.Length >= 248 ? LongPath(combined) : combined;
    }

    private static string CleanSegment(string segment)
    {
        if (segment is "." or "..")
            return string.Empty;

        Span<char> buffer = segment.Length <= MaxSegmentLength ? stackalloc char[segment.Length] : new char[segment.Length];

        int length = 0;
        foreach (char c in segment)
        {
            buffer[length++] = Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c;
        }

        string cleaned = new string(buffer[..length]).TrimEnd(' ', '.');
        if (cleaned.Length > MaxSegmentLength)
            cleaned = cleaned[..MaxSegmentLength];
        if (cleaned.Length == 0)
            return string.Empty;

        string stem = Path.GetFileNameWithoutExtension(cleaned);
        if (ReservedNames.Contains(stem))
            cleaned = "_" + cleaned;

        return cleaned;
    }

    private static char[] BuildInvalidChars()
    {
        HashSet<char> chars = [.. Path.GetInvalidFileNameChars()];
        foreach (char c in Separators)
            chars.Add(c);
        chars.Add(':');
        return [.. chars];
    }

    public static void EnsureParent(string file)
    {
        string? directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    public static string DirectoryName(string file)
    {
        string? directory = Path.GetDirectoryName(file);
        return string.IsNullOrEmpty(directory) ? "." : directory;
    }

    private static string LongPath(string path)
    {
        string full = Path.GetFullPath(path);
        return full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full : @"\\?\" + full;
    }
}