namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// Wire-level framing constants derived from the clean-room spec.
/// spec: Docs/RE/opcodes.md (Wire frame header)
/// </summary>
internal static class FramingConstants
{
    /// <summary>
    /// Size in bytes of the fixed frame header.
    /// spec: Docs/RE/opcodes.md — "Minimum 8 bytes are required to parse a header."
    /// Layout: [+0 u32 size][+4 u16 major][+6 u16 minor][+8 payload...]
    /// </summary>
    internal const int HeaderSize = 8;

    /// <summary>
    /// Byte offset of the major opcode (u16, little-endian).
    /// spec: Docs/RE/opcodes.md — "Offset +4, Size u16, Field major."
    /// Equals the width of the preceding u32 size field at +0.
    /// </summary>
    internal const int MajorOpcodeOffset = 4;

    /// <summary>
    /// Maximum legal total frame size (inclusive), used as a sanity bound. The size field is a true
    /// u32, but a single inbound payload never exceeds the client's fixed LZ4 decompress capacity of
    /// <c>0x2DA0</c> = 11680 bytes (spec: Docs/RE/specs/crypto.md §3/§5 — "Inbound max decompressed
    /// size 0x2DA0 = 11680 bytes"); the full frame (8-byte header + that capacity) is the documented
    /// ceiling. Any frame whose size field is below <see cref="HeaderSize"/> or above this value is
    /// treated as malformed. spec: Docs/RE/opcodes.md (size u32) + crypto.md §2/§3/§5 (0x2DA0 cap).
    /// </summary>
    internal const int MaxFrameSize = HeaderSize + 0x2DA0; // 8 + 11680 = 11688

    /// <summary>
    /// Default <see cref="System.IO.Pipelines.PipeOptions"/> pause-writer threshold (bytes).
    /// Once this many bytes are buffered in the receive pipe the socket-read loop pauses
    /// until the consumer catches up (backpressure).
    /// </summary>
    internal const long PipeResumeThreshold = 64 * 1024; // 64 KiB

    /// <summary>
    /// Default <see cref="System.IO.Pipelines.PipeOptions"/> resume-writer threshold (bytes).
    /// </summary>
    internal const long PipePauseThreshold = 128 * 1024; // 128 KiB

    /// <summary>
    /// Minimum buffer size requested from the <see cref="System.IO.Pipelines.PipeWriter"/>
    /// when soliciting space for a socket receive.
    /// </summary>
    internal const int MinReceiveBufferSize = 4096;
}