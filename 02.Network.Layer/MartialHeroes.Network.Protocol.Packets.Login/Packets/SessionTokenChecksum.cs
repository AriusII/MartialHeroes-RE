// spec: Docs/RE/packets/cmsg_char_enter.yaml — SessionToken @0x01, 33 bytes.
//
// CmsgEnterGameRequest.SessionToken is the lowercase-hex MD5 digest of the client's OWN executable
// file (argv[0] path fed through an MD5-of-file hasher), stored as 32 lowercase-hex ASCII chars
// followed by a NUL terminator = 33 bytes total. It is a BUILD-INTEGRITY SELF-CHECKSUM, NOT a
// launcher/login session token. spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
//
// NOTE — MD5 is used here as a build-integrity checksum (matching the original binary's algorithm),
// NOT for cryptographic strength. This is a faithful re-implementation of what doida.exe does.
// spec: Docs/RE/packets/cmsg_char_enter.yaml ("lowercase-hex MD5 digest of the client's OWN exe").
//
// CAPTURE-PENDING: a clean-room client cannot reproduce MD5(original-exe) byte-identically because
// the executable bytes differ. If the server validates the shape (a present 32-char lowercase-hex
// string + NUL), a valid-shaped self-checksum of OUR binary is strictly better than 33 zero bytes.

using System.Security.Cryptography;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     Writes the 33-byte self-checksum <c>SessionToken</c> field of
///     <see cref="CmsgEnterGameRequest" /> into a caller-supplied span.
///     <para>
///         The token is the <b>lowercase-hex MD5 digest of the running executable file</b>
///         (32 ASCII hex chars) followed by a NUL terminator, totalling
///         <see cref="SessionTokenLength" /> = 33 bytes.
///         spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
///     </para>
///     <para>
///         A clean-room client cannot reproduce <c>MD5(original-exe)</c> byte-identically; it
///         instead hashes ITS OWN running assembly, matching the algorithm and shape.  If
///         <see cref="System.Environment.ProcessPath" /> is null or the file cannot be read, the
///         destination is zero-filled (shape-valid field of all zeros + NUL) so the send path
///         never crashes.
///         spec: Docs/RE/packets/cmsg_char_enter.yaml notes ("capture-pending … valid-shaped token").
///     </para>
/// </summary>
public static class SessionTokenChecksum
{
    /// <summary>
    ///     Total byte width of the SessionToken field: 32 lowercase-hex chars + 1 NUL terminator.
    ///     spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01, "bytes[33]".
    /// </summary>
    public const int SessionTokenLength = 33; // spec: Docs/RE/packets/cmsg_char_enter.yaml

    /// <summary>
    ///     Number of hex characters in the MD5 digest string (16 digest bytes × 2 nibbles).
    ///     spec: Docs/RE/packets/cmsg_char_enter.yaml ("32 hex chars + NUL").
    /// </summary>
    private const int HexCharCount = 32; // spec: Docs/RE/packets/cmsg_char_enter.yaml

    /// <summary>
    ///     Writes the 33-byte self-checksum token into <paramref name="destination33" />.
    ///     <list type="bullet">
    ///         <item>Resolves the running executable path via <see cref="System.Environment.ProcessPath" /> (argv[0]).</item>
    ///         <item>Reads the file and computes its MD5 digest (16 bytes).</item>
    ///         <item>Formats the digest as 32 lowercase-hex ASCII bytes written directly into destination[0..31].</item>
    ///         <item>Writes NUL (0x00) into destination[32].</item>
    ///         <item>Falls back to 33 zero bytes if ProcessPath is null or the file cannot be read/hashed.</item>
    ///     </list>
    ///     spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
    /// </summary>
    /// <param name="destination33">
    ///     A span of exactly <see cref="SessionTokenLength" /> (33) bytes. The caller is responsible
    ///     for passing the correct slice — typically a <c>MemoryMarshal.CreateSpan</c> view over the
    ///     <see cref="CmsgEnterGameRequest.SessionTokenBuffer" /> inline-array field.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="destination33" />.Length != <see cref="SessionTokenLength" />.
    /// </exception>
    public static void WriteSelfChecksum(Span<byte> destination33)
    {
        if (destination33.Length != SessionTokenLength) // spec: Docs/RE/packets/cmsg_char_enter.yaml — "bytes[33]"
            throw new ArgumentException(
                $"destination33 must be exactly {SessionTokenLength} bytes (SessionToken field width).",
                nameof(destination33));

        // Zero the entire field first; guarantees the trailing NUL and is the fallback for any
        // failure path below.  spec: Docs/RE/packets/cmsg_char_enter.yaml notes ("zero-fills a 40-byte buffer").
        destination33.Clear();

        var exePath = Environment.ProcessPath; // argv[0] — the running executable's path.
        // spec: Docs/RE/packets/cmsg_char_enter.yaml ("argv[0] path run through an MD5-of-file hasher").

        if (exePath is null)
            return; // Fallback: zero-filled field. See type-level note on capture-pending caveat.

        byte[]? digest;
        try
        {
            // Read the executable and hash it.  This is a once-per-session low-rate call (fired
            // once at enter-world), not a hot path.  File.ReadAllBytes is acceptable here.
            // spec: Docs/RE/packets/cmsg_char_enter.yaml ("MD5-of-file hasher").
            // MD5 is a build-integrity checksum here, NOT used for security.
            // spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
            var exeBytes = File.ReadAllBytes(exePath);
            digest = MD5.HashData(exeBytes); // 16 bytes
        }
        catch
        {
            // File unreadable (locked, missing, permission denied, etc.) — fall back to zeros.
            // A shape-valid self-checksum is strictly better than zeros, but a missing exe is
            // non-fatal.  spec: Docs/RE/packets/cmsg_char_enter.yaml notes (capture-pending).
            return;
        }

        // Write the 32 lowercase-hex chars directly into the destination span — no intermediate
        // managed string allocated for the on-wire bytes.
        // spec: Docs/RE/packets/cmsg_char_enter.yaml ("32 lowercase-hex chars").
        WriteHexLower(digest, destination33);

        // destination33[32] is already 0x00 from the Clear() above — NUL terminator guaranteed.
        // spec: Docs/RE/packets/cmsg_char_enter.yaml ("32 hex chars + NUL terminator = 33 bytes").
    }

    /// <summary>
    ///     Writes <paramref name="source" /> (16 bytes) as 32 lowercase-hex ASCII chars into the
    ///     first 32 bytes of <paramref name="destination" />.  No heap allocation.
    /// </summary>
    private static void WriteHexLower(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        // Each byte produces two nibble characters.
        // 'a'-'f' = 0x61-0x66; '0'-'9' = 0x30-0x39.
        // spec: Docs/RE/packets/cmsg_char_enter.yaml ("lowercase-hex").
        var hexTable =
            "0123456789abcdef"u8; // lowercase per spec

        for (var i = 0; i < source.Length; i++)
        {
            var b = source[i];
            destination[i * 2] = hexTable[b >> 4]; // high nibble
            destination[i * 2 + 1] = hexTable[b & 0x0F]; // low nibble
        }

        // The HexCharCount (32) chars fill destination[0..31]; destination[32] stays 0x00 (NUL).
        // spec: Docs/RE/packets/cmsg_char_enter.yaml field SessionToken @0x01.
        _ = HexCharCount; // suppress unused-private-member warning; value is structural documentation.
    }
}