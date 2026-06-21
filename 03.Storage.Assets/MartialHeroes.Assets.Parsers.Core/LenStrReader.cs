using System.Buffers.Binary;
using System.Text;

namespace MartialHeroes.Assets.Parsers.Core;

/// <summary>
///     Reads a length-prefixed byte string (LenStr) from a binary span.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mesh.md §String encoding (LenStr) — used in .skn and .bnd
///     <para>
///         Wire format: [u32 LE — byte length of body (4 bytes)][char[length] — string body, no null
///         terminator on disk].  Both .skn and .bnd use this encoding throughout.
///     </para>
///     <para>
///         spec: Docs/RE/formats/mesh.md §String encoding (LenStr) — CORRECTION:
///         "The prefix is a 4-byte little-endian u32. Any parser that reads these files must consume
///         4 bytes for the length prefix, not 1 byte." CONFIRMED.
///     </para>
/// </remarks>
public static class LenStrReader
{
    // Length prefix is 4 bytes (u32 LE).
    // spec: Docs/RE/formats/mesh.md §String encoding (LenStr):
    //   "The length field is always a full 4-byte unsigned integer read in little-endian order."
    //   CONFIRMED — resolved by direct analysis of the string-read primitive in binary mode.
    private const int PrefixBytes = 4;

    /// <summary>
    ///     Reads a LenStr from <paramref name="span" /> at position <paramref name="offset" />,
    ///     decodes it as ASCII, and advances <paramref name="offset" /> past the string.
    /// </summary>
    /// <param name="span">The source byte span.</param>
    /// <param name="offset">
    ///     On entry: the byte index of the 4-byte u32 LE length prefix.
    ///     On exit: the byte index immediately after the string body.
    /// </param>
    /// <returns>The decoded string.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown if the buffer is too short to hold the declared string length.
    /// </exception>
    public static string Read(ReadOnlySpan<byte> span, ref int offset)
    {
        // Guard: need at least 4 bytes for the length prefix.
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr) — 4-byte prefix: CONFIRMED.
        if (offset + PrefixBytes > span.Length)
            throw new InvalidDataException(
                $"LenStr prefix truncated: need {PrefixBytes} bytes at offset {offset}, " +
                $"but buffer length is {span.Length}.");

        // Read the 4-byte little-endian u32 length prefix.
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr):
        //   "The wire format is: [u32 LE — byte length of the body (4 bytes)]
        //    [char[length] — string body, no null terminator on disk]." CONFIRMED.
        var rawLength = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
        offset += PrefixBytes;

        // Guard against pathological length values before allocating.
        if (rawLength > int.MaxValue)
            throw new InvalidDataException(
                $"LenStr: declared length {rawLength} exceeds addressable range at offset " +
                $"{offset - PrefixBytes}.");

        var length = (int)rawLength;

        // Guard: need 'length' more bytes for the string body.
        if (offset + length > span.Length)
            throw new InvalidDataException(
                $"LenStr data truncated: declared length {length} at offset {offset - PrefixBytes}, " +
                $"but only {span.Length - offset} byte(s) remain in buffer.");

        var value = Encoding.ASCII.GetString(span.Slice(offset, length));
        offset += length;
        return value;
    }
}