using System.IO.Compression;
using System.Text;

namespace AMZExtractor;

internal sealed class MiniZipHeader
{
    public required ushort Version { get; init; }
    public required ushort Flags { get; init; }
    public required ushort Method { get; init; }
    public required uint Crc { get; init; }
    public required long CompressedSize { get; init; }
    public required long UncompressedSize { get; init; }
    public required string InnerName { get; init; }
    public required long DataOffset { get; init; }

    /// <summary>True when the sizes had to be recovered from the AMZ record itself.</summary>
    public required bool SizeRecoveredFromArchive { get; init; }
}

/// <summary>
/// Port of the MINI_ZIP_ONE_FILE function, every AMZ entry is a small single file ZIP blob
/// (little-endian local file header plus a deflate or store payload).
/// </summary>
internal static class MiniZip
{
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const int FixedHeaderSize = 30;
    private const int CopyBufferSize = 1 << 20;

    public static MiniZipHeader ReadHeader(Stream source, long entryOffset, long archiveSize, long archiveZipSize)
    {
        source.Position = entryOffset;

        Span<byte> fixedHeader = stackalloc byte[FixedHeaderSize];
        ReadExact(source, fixedHeader);

        uint signature = BitConverter.ToUInt32(fixedHeader);
        if (signature != LocalFileHeaderSignature)
        {
            throw new InvalidDataException($"expected a ZIP local file header (PK\\x03\\x04) at 0x{entryOffset:X8}, got 0x{signature:X8}");
        }

        var version = BitConverter.ToUInt16(fixedHeader[4..]);
        var flags = BitConverter.ToUInt16(fixedHeader[6..]);
        var method = BitConverter.ToUInt16(fixedHeader[8..]);
        var crc = BitConverter.ToUInt32(fixedHeader[14..]);
        long compressedSize = BitConverter.ToUInt32(fixedHeader[18..]);
        long uncompressedSize = BitConverter.ToUInt32(fixedHeader[22..]);

        int nameLength = BitConverter.ToUInt16(fixedHeader[26..]);
        int extraLength = BitConverter.ToUInt16(fixedHeader[28..]);

        string innerName = ReadAndDiscard(source, nameLength);
        source.Position += extraLength;

        long dataOffset = entryOffset + FixedHeaderSize + nameLength + extraLength;

        // some writers stream the local header with zeroed sizes, recover them from the AMZ record exactly like the script does.
        bool recovered = false;
        if (compressedSize == 0 && uncompressedSize == 0)
        {
            compressedSize = archiveZipSize - (dataOffset - entryOffset);
            uncompressedSize = archiveSize;
            recovered = true;
        }

        return new MiniZipHeader
        {
            Version = version,
            Flags = flags,
            Method = method,
            Crc = crc,
            CompressedSize = compressedSize,
            UncompressedSize = uncompressedSize,
            InnerName = innerName,
            DataOffset = dataOffset,
            SizeRecoveredFromArchive = recovered,
        };
    }

    public static long Write(Stream source, Stream destination, MiniZipHeader header)
    {
        source.Position = header.DataOffset;

        return header.Method switch
        {
            0 => Copy(source, destination, header.CompressedSize),
            8 => Inflate(source, destination),
            _ => throw new NotSupportedException($"unsupported compression method {header.Method} for \"{header.InnerName}\""),
        };
    }

    private static long Inflate(Stream source, Stream destination)
    {
        using var deflate = new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true);

        byte[] buffer = new byte[CopyBufferSize];
        long total = 0;
        int read;
        while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            total += read;
        }

        return total;
    }

    private static long Copy(Stream source, Stream destination, long count)
    {
        byte[] buffer = new byte[CopyBufferSize];
        long total = 0;
        while (total < count)
        {
            int want = (int)Math.Min(buffer.Length, count - total);
            int read = source.Read(buffer, 0, want);
            if (read <= 0)
                break;
            destination.Write(buffer, 0, read);
            total += read;
        }

        return total;
    }

    private static string ReadAndDiscard(Stream source, int count)
    {
        if (count <= 0)
            return string.Empty;

        byte[] data = new byte[count];
        ReadExact(source, data);

        int end = Array.IndexOf(data, (byte)0);
        if (end >= 0)
            count = end;

        return Encoding.UTF8.GetString(data, 0, count);
    }

    private static void ReadExact(Stream source, Span<byte> destination)
    {
        int done = 0;
        while (done < destination.Length)
        {
            int read = source.Read(destination[done..]);
            if (read <= 0)
                throw new EndOfStreamException($"unexpected end of archive at 0x{source.Position:X8}");
            done += read;
        }
    }
}