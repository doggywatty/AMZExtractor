namespace AMZExtractor;

/// <summary>
/// Parses the big-endian AMZ header described in order_chaos_online.bmz.
/// </summary>
internal sealed class AmzArchive
{
    private const string VersionMarker = "version:";
    private const string SplitMarker = ".split_";

    private readonly List<AmzEntry> _entries = [];

    public IReadOnlyList<AmzEntry> Entries => _entries;

    /// <summary>
    /// Offset every entry offset is relative to BASE_OFF.
    /// </summary>
    public long BaseOffset { get; private set; }

    /// <summary>
    /// Position right after the header table, trailing data starts here
    /// </summary>
    public long HeaderEnd { get; private set; }

    public string? Warning { get; private set; }

    public static AmzArchive Parse(Stream stream)
    {
        var reader = new BigEndianReader(stream);
        var archive = new AmzArchive();
        long infoSize = reader.ReadUInt32();
        archive.BaseOffset = reader.Position + infoSize;

        while (true)
        {
            if (reader.Position == archive.BaseOffset)
                break;

            // quickbms only breaks on an exact match and would keep walking into garbage until it reads past EOF. Bail out instead, but say so.
            if (reader.Position > archive.BaseOffset)
            {
                archive.Warning = $"header table overran BASE_OFF (0x{archive.BaseOffset:X}) by 0x{reader.Position - archive.BaseOffset:X} bytes, stopped there";
                break;
            }

            string name = reader.ReadString(reader.ReadUInt16());

            string path;
            if (name.Contains(VersionMarker, StringComparison.OrdinalIgnoreCase))
            {
                reader.ReadUInt32(); // ALIGN
                path = reader.ReadString(reader.ReadUInt16());
            }
            else
            {
                // Some AMZ files start straight with the path.
                path = name;
            }

            uint fileCount = reader.ReadUInt32();
            for (uint i = 0; i < fileCount; i++)
                archive._entries.Add(ReadEntry(ref reader, path, archive.BaseOffset));
        }

        archive.HeaderEnd = reader.Position;
        return archive;
    }

    private static AmzEntry ReadEntry(ref BigEndianReader reader, string archivePath, long baseOffset)
    {
        long offset = reader.ReadUInt32() + baseOffset;
        long size = reader.ReadUInt32();
        reader.ReadUInt32(); // ZERO
        long crc = reader.ReadUInt32();

        reader.ReadString(reader.ReadUInt16()); // first name, immediately overwritten

        long zipSize = reader.ReadUInt32();
        if (zipSize == 0)
        {
            reader.ReadUInt32(); // DUMMY
            zipSize = reader.ReadUInt32();
        }

        string name = reader.ReadString(reader.ReadUInt16());

        // quickbms >> operator
        int split = name.LastIndexOf(SplitMarker, StringComparison.OrdinalIgnoreCase);
        bool isSplit = split >= 0;
        string outputName = isSplit ? name[..split] : FullBaseName(name);

        if (outputName.Length == 0)
            outputName = name;

        return new AmzEntry
        {
            ArchivePath = archivePath,
            HeaderName = name,
            OutputName = outputName,
            Offset = offset,
            Size = size,
            ZipSize = zipSize,
            Crc = crc,
            IsSplit = isSplit,
        };
    }

    /// <summary>
    /// QuickBMS fullbasename: drop directories, then drop from the last dot
    /// </summary>
    private static string FullBaseName(string name)
    {
        int separator = name.LastIndexOfAny(['/', '\\']);
        string fileName = separator >= 0 ? name[(separator + 1)..] : name;

        int dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName[..dot] : fileName;
    }
}