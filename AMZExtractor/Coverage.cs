namespace AMZExtractor;

/// <summary>
/// Works out how much of the archive the file table actually accounts for
/// Entries should tile the payload region with no gaps and no overlaps, anything left over is data the archive carries without a name for it.
/// </summary>
internal sealed class Coverage
{
    public required long PayloadStart { get; init; }
    public required long PayloadBytes { get; init; }
    public required long ClaimedBytes { get; init; }
    public required int SpanCount { get; init; }
    public required int OverlapCount { get; init; }
    public required long TrailingBytes { get; init; }
    public required IReadOnlyList<(long Offset, long Length)> Gaps { get; init; }

    public bool IsFullyCovered => Gaps.Count == 0 && OverlapCount == 0 && TrailingBytes == 0;

    public static Coverage Analyse(AmzArchive amz, long fileLength)
    {
        List<(long Offset, long Length)> gaps = [];
        long claimed = 0;
        int spans = 0;
        int overlaps = 0;

        long cursor = amz.BaseOffset;

        foreach (AmzEntry entry in amz.Entries.OrderBy(e => e.Offset))
        {
            if (entry.Offset > cursor)
                gaps.Add((cursor, entry.Offset - cursor));
            else if (entry.Offset < cursor)
                overlaps++;

            long end = entry.Offset + entry.ZipSize;

            if (end > cursor)
            {
                if (entry.Offset >= cursor)
                    claimed += entry.ZipSize;

                spans++;
                cursor = end;
            }
        }

        long trailing = Math.Max(0, fileLength - cursor);

        return new Coverage
        {
            PayloadStart = amz.BaseOffset,
            PayloadBytes = Math.Max(0, fileLength - amz.BaseOffset),
            ClaimedBytes = claimed,
            SpanCount = spans,
            OverlapCount = overlaps,
            TrailingBytes = trailing,
            Gaps = gaps,
        };
    }
}