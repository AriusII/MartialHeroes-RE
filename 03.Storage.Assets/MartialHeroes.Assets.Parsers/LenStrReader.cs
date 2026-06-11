using System.Text;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Reads a length-prefixed byte string (LenStr) from a binary span.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/mesh.md §String encoding (LenStr) — used in .skn and .bnd
/// <para>
/// UNVERIFIED: The exact prefix width (1 byte vs. 2 bytes) and whether a null terminator
/// follows the character data are sample-unverified.  This implementation uses a 1-byte
/// length prefix with no null terminator, which matches the most common observed pattern in
/// D3D9-era Asian MMO clients.  The implementation is deliberately isolated behind this single
/// helper so the encoding width can be changed in one place when a sample confirms it.
/// </para>
/// <para>
/// Isolation rationale: the helper is the only site that reads the prefix byte; all three
/// parsers (.skn header name, .bnd header actor_name) call this helper rather than inlining
/// the logic.  Changing prefix width requires editing only this file.
/// </para>
/// </remarks>
internal static class LenStrReader
{
    // UNVERIFIED: 1-byte prefix assumed; change to 2 when confirmed by sample.
    // spec: Docs/RE/formats/mesh.md §String encoding (LenStr): "support at least a
    // 1-byte-prefixed byte string until a sample confirms the encoding width."
    private const int PrefixBytes = 1; // UNVERIFIED — see class remarks

    /// <summary>
    /// Reads a LenStr from <paramref name="span"/> at position <paramref name="offset"/>,
    /// decodes it as ASCII, and advances <paramref name="offset"/> past the string.
    /// </summary>
    /// <param name="span">The source byte span.</param>
    /// <param name="offset">
    /// On entry: the byte index of the length-prefix byte.
    /// On exit: the byte index immediately after the string data.
    /// </param>
    /// <returns>The decoded string.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the buffer is too short to hold the declared string length.
    /// </exception>
    public static string Read(ReadOnlySpan<byte> span, ref int offset)
    {
        // Guard: need at least PrefixBytes remaining.
        if (offset + PrefixBytes > span.Length)
            throw new InvalidDataException(
                $"LenStr prefix truncated: need {PrefixBytes} byte(s) at offset {offset}, " +
                $"but buffer length is {span.Length}.");

        // Read the length prefix.
        // UNVERIFIED: 1-byte prefix assumed.
        // spec: Docs/RE/formats/mesh.md §String encoding (LenStr): UNVERIFIED.
        int length = span[offset]; // single-byte prefix
        offset += PrefixBytes;

        // Guard: need 'length' more bytes.
        if (offset + length > span.Length)
            throw new InvalidDataException(
                $"LenStr data truncated: declared length {length} at offset {offset - PrefixBytes}, " +
                $"but only {span.Length - offset} byte(s) remain in buffer.");

        string value = Encoding.ASCII.GetString(span.Slice(offset, length));
        offset += length;
        return value;
    }
}
