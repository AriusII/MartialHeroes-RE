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
    /// Layout: [+0 u16 size][+2 u16 unused][+4 u16 major][+6 u16 minor][+8 payload...]
    /// </summary>
    internal const int HeaderSize = 8;

    /// <summary>
    /// Byte offset within the header at which the little-endian u16 total-frame-size field lives.
    /// spec: Docs/RE/opcodes.md — "Offset +0, Size u16, Field size: Total frame size in bytes,
    /// including this 8-byte header."
    /// </summary>
    internal const int SizeFieldOffset = 0;

    /// <summary>
    /// Byte offset of the major opcode (u16, little-endian).
    /// spec: Docs/RE/opcodes.md — "Offset +4, Size u16, Field major."
    /// </summary>
    internal const int MajorOpcodeOffset = 4;

    /// <summary>
    /// Byte offset of the minor opcode (u16, little-endian).
    /// spec: Docs/RE/opcodes.md — "Offset +6, Size u16, Field minor."
    /// </summary>
    internal const int MinorOpcodeOffset = 6;

    /// <summary>
    /// Maximum legal total frame size (inclusive). The size field is u16, so 65 535 bytes is the
    /// absolute wire-protocol ceiling. Any frame whose size field is below <see cref="HeaderSize"/>
    /// or above this value is malformed.
    /// spec: Docs/RE/opcodes.md — size field is u16.
    /// </summary>
    internal const int MaxFrameSize = ushort.MaxValue; // 65535

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