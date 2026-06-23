using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Parsers.Core;

public static class LenStrReader
{
    private const int PrefixBytes = 4;

    public static string Read(ReadOnlySpan<byte> span, ref int offset)
    {
        if (offset + PrefixBytes > span.Length)
            throw new InvalidDataException(
                $"LenStr prefix truncated: need {PrefixBytes} bytes at offset {offset}, " +
                $"but buffer length is {span.Length}.");

        var rawLength = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += PrefixBytes;

        if (rawLength > int.MaxValue)
            throw new InvalidDataException(
                $"LenStr: declared length {rawLength} exceeds addressable range at offset " +
                $"{offset - PrefixBytes}.");

        var length = (int)rawLength;

        if (offset + length > span.Length)
            throw new InvalidDataException(
                $"LenStr data truncated: declared length {length} at offset {offset - PrefixBytes}, " +
                $"but only {span.Length - offset} byte(s) remain in buffer.");

        var value = Encoding.ASCII.GetString(span.Slice(offset, length));
        offset += length;
        return value;
    }
}