namespace AMZExtractor;

internal sealed class AmzEntry
{
    /// <summary>
    /// Group directory as stored in the header (may be empty)
    /// </summary>
    public required string ArchivePath { get; init; }

    /// <summary>
    /// File name exactly as stored in the header.
    /// </summary>
    public required string HeaderName { get; init; }

    /// <summary>
    /// Name after the .split_ / fullbasename rules of the script
    /// </summary>
    public required string OutputName { get; init; }

    public required long Offset { get; init; }
    public required long Size { get; init; }
    public required long ZipSize { get; init; }
    public required long Crc { get; init; }
    public required bool IsSplit { get; init; }
}