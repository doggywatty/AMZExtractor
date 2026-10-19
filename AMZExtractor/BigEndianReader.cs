using System.Text;

namespace AMZExtractor;

/// <summary>
/// Sequential big-endian reader matching QuickBMS get VAR long/short with endian big.
/// </summary>
internal struct BigEndianReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    public BigEndianReader(Stream stream) => _stream = stream;

    public long Position => _stream.Position;

    public ushort ReadUInt16()
    {
        ReadExact(_buffer.AsSpan(0, 2));
        return (ushort)((_buffer[0] << 8) | _buffer[1]);
    }

    public uint ReadUInt32()
    {
        ReadExact(_buffer.AsSpan(0, 4));
        return ((uint)_buffer[0] << 24) | ((uint)_buffer[1] << 16) | ((uint)_buffer[2] << 8) | _buffer[3];
    }

    /// <summary>
    /// Equivalent of QuickBMS getdstring, reads the bytes as a NUL-delimited string
    /// </summary>
    public string ReadString(int length)
    {
        if (length <= 0)
            return string.Empty;

        byte[] data = new byte[length];
        ReadExact(data);

        int end = Array.IndexOf(data, (byte)0);
        if (end >= 0)
            length = end;

        return Encoding.UTF8.GetString(data, 0, length);
    }

    private void ReadExact(Span<byte> destination)
    {
        int done = 0;
        while (done < destination.Length)
        {
            int read = _stream.Read(destination[done..]);
            if (read <= 0)
                throw new EndOfStreamException($"unexpected end of archive at 0x{_stream.Position:X8}");
            done += read;
        }
    }
}