// spec: Docs/RE/packets/lobby.yaml — the LOBBY (login-server) protocol wire records.
// Also documented in Docs/RE/opcodes.md "Appendix A — Lobby protocol".
//
// !!! NOT A (major:minor) OPCODE !!!
// The lobby is a SEPARATE synchronous TCP surface from the main game connection. It carries NO
// 8-byte (size,major,minor) opcode dispatch and NO byte cipher, and is NOT in the opcodes.md
// family table — so these structs carry NO [PacketOpcode] and are NOT registered in the router.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset below is a static inference (capture_verified: false in the spec). The widths and
// the 8-byte wrapper / 8-byte entry / 30-byte endpoint shapes are CODE-CONFIRMED; the field
// signedness and the endpoint token's internal delimiter are needs-capture.
//
// COMPRESSION: both lobby response PAYLOADS are LZ4-compressed (raw block) and NOT encrypted. The
// LZ4 decompression is the TRANSPORT / caller's job; the structs below map the DECOMPRESSED bytes.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// The common 8-byte lobby frame wrapper that prefixes BOTH lobby responses (server-list and
/// channel-endpoint queries), little-endian. On the server-list query the <see cref="Count"/> word
/// is the number of 8-byte <see cref="LobbyServerEntry"/> records that follow in the decompressed
/// payload. spec: Docs/RE/packets/lobby.yaml ("COMMON LOBBY FRAME WRAPPER"). CAPTURE-UNVERIFIED.
/// </summary>
/// <remarks>
/// This wrapper reuses the game frame's header shape but is dispatched by neither major nor minor:
/// the <see cref="Reserved"/> word (the game frame's "minor") is unused on this socket. The
/// 8-byte wrapper is read first, then <c>(Size - 8)</c> compressed payload bytes are received and
/// LZ4-decompressed by the caller. spec: Docs/RE/packets/lobby.yaml.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyFrameWrapper
{
    /// <summary>Size of the fixed wrapper in bytes. spec: lobby.yaml (8-byte wrapper).</summary>
    public const int WireSize = 8;

    /// <summary>
    /// 0x00 — total frame size in bytes = 8 (this wrapper) + compressed-payload length. The payload
    /// length to receive is <c>(Size - 8)</c>. CODE-CONFIRMED. spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly uint Size;

    /// <summary>
    /// 0x04 — on the SERVER-LIST query this is the RECORD COUNT (number of 8-byte server entries that
    /// follow). Reuses the game frame's "major" word as a count. CODE-CONFIRMED.
    /// spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly ushort Count;

    /// <summary>
    /// 0x06 — reuses the game frame's "minor" word; preserved through decompression but NOT read by
    /// either lobby thread (unused on this socket). CODE-CONFIRMED present, role = unused.
    /// spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly ushort Reserved;

    /// <summary>The compressed-payload byte length implied by <see cref="Size"/> (<c>Size - 8</c>).</summary>
    public int PayloadLength => (int)Size - WireSize;

    /// <summary>
    /// Reads the 8-byte lobby wrapper from the start of <paramref name="frame"/> (little-endian),
    /// no allocation. spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="frame"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LobbyFrameWrapper Read(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < WireSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Length, $"A lobby wrapper requires at least {WireSize} bytes.");
        }

        // spec: lobby.yaml — little-endian; size@+0 (u32), count@+4 (u16), reserved@+6 (u16).
        return MemoryMarshal.Read<LobbyFrameWrapper>(frame);
    }
}

/// <summary>
/// One 8-byte SERVER-LIST entry (record shape A), little-endian, from the lobby server-list query
/// (port 10000). The decompressed payload is <c>count</c> of these packed back-to-back; the count
/// lives in the <see cref="LobbyFrameWrapper"/>, not in the payload. spec: Docs/RE/packets/lobby.yaml
/// ("RECORD SHAPE A"). CAPTURE-UNVERIFIED.
/// </summary>
/// <remarks>
/// Signedness: the painter sign-extends some fields for a debug print but zero-extends them on the
/// branch reads; signedness is presentation-only and needs-capture. The spec documents the fields
/// as 16-bit, so they are modelled as signed <see cref="short"/> per the spec field types
/// (<c>i16</c>). spec: Docs/RE/packets/lobby.yaml.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyServerEntry
{
    /// <summary>Size of one server entry in bytes. spec: lobby.yaml (2 + 2 + 2 + 2 = 8).</summary>
    public const int WireSize = 8;

    /// <summary>
    /// 0x00 — server id / select-key (i16). Feeds the client-local server-name lookup, draws the
    /// remembered-default highlight, and the value <c>== 100</c> is the "available" gate that unlocks
    /// the per-row select buttons. CODE-CONFIRMED. spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly short Id;

    /// <summary>
    /// 0x02 — status / kind (i16): the per-row caption / branch selector (0 = derive a population
    /// label, 3 = special latency/24 branch, 1..39 = per-value caption array, else = fallback).
    /// CODE-CONFIRMED ids. spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly short Status;

    /// <summary>
    /// 0x04 — population / count value (i16); thresholded in the status==0 numeric mode and reused as
    /// the 6005 latency numerator / the ==24 special test. CODE-CONFIRMED.
    /// spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly short Population;

    /// <summary>
    /// 0x06 — mode flag (i16): nonzero ⇒ treat <see cref="Population"/> as a NUMERIC population
    /// (500/800/1200 thresholds); zero ⇒ treat it as a DISCRETE load level. CODE-CONFIRMED.
    /// spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly short Flag;
}

/// <summary>
/// The 30-byte CHANNEL-ENDPOINT token (record shape B) from the channel query
/// (port 10000 + selected channel offset). The channel thread zero-fills a 30-byte field then copies
/// the FIRST 30 bytes of the decompressed payload verbatim as a fixed ASCII endpoint token naming
/// the GAME server, consumed OPAQUELY (it is later spliced into the credential blob — this layer does
/// not parse host/port out of it). NOT guaranteed NUL-terminated within the field.
/// spec: Docs/RE/packets/lobby.yaml ("RECORD SHAPE B"). CAPTURE-UNVERIFIED.
/// </summary>
/// <remarks>
/// The exact internal format / delimiter of the token (host:port vs host port vs fixed sub-fields)
/// is needs-capture — no parse happens here. spec: Docs/RE/packets/lobby.yaml.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyChannelEndpointToken
{
    /// <summary>Size of the fixed endpoint token in bytes. spec: lobby.yaml (char[30]).</summary>
    public const int WireSize = 30;

    /// <summary>
    /// 0x00 — fixed 30-byte ASCII endpoint token naming the GAME server, consumed as one opaque
    /// string token (asciiz NOT guaranteed). spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly EndpointBuffer Endpoint;

    /// <summary>0x00 — 30-byte ASCII endpoint token. spec: Docs/RE/packets/lobby.yaml.</summary>
    [InlineArray(30)]
    public struct EndpointBuffer
    {
        private byte _element0;
    }
}

/// <summary>
/// Zero-allocation reader over a DECOMPRESSED lobby server-list reply. Wraps the leading 8-byte
/// <see cref="LobbyFrameWrapper"/> and exposes the trailing <c>count</c> packed 8-byte
/// <see cref="LobbyServerEntry"/> records as in-place <c>ref readonly</c> views — no copies, no
/// per-entry allocation. spec: Docs/RE/packets/lobby.yaml ("RECORD SHAPE A").
/// </summary>
public readonly ref struct LobbyServerListReader
{
    private readonly ReadOnlySpan<byte> _entries;

    /// <summary>The parsed 8-byte wrapper at the head of the reply. spec: Docs/RE/packets/lobby.yaml.</summary>
    public readonly LobbyFrameWrapper Wrapper;

    /// <summary>
    /// Parses the wrapper from the head of <paramref name="decompressed"/> and bounds the entry
    /// region to the smaller of the wrapper's <see cref="LobbyFrameWrapper.Count"/> and the actual
    /// bytes available after the wrapper. The input is the ALREADY-LZ4-DECOMPRESSED reply (the
    /// caller performs decompression). spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the input is shorter than the 8-byte wrapper.</exception>
    public LobbyServerListReader(ReadOnlySpan<byte> decompressed)
    {
        Wrapper = LobbyFrameWrapper.Read(decompressed);

        ReadOnlySpan<byte> tail = decompressed[LobbyFrameWrapper.WireSize..];
        int available = tail.Length / LobbyServerEntry.WireSize;
        int n = Wrapper.Count <= available ? Wrapper.Count : available;
        _entries = tail[..(n * LobbyServerEntry.WireSize)];
    }

    /// <summary>
    /// The number of 8-byte entries actually iterable (min of the wrapper count and the bytes
    /// available). spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public int Count => _entries.Length / LobbyServerEntry.WireSize;

    /// <summary>
    /// Returns an in-place <c>ref readonly</c> view of entry <paramref name="index"/> — zero copy.
    /// spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="index"/> is out of range.</exception>
    public ref readonly LobbyServerEntry this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Entry index out of range.");
            }

            ReadOnlySpan<byte> slot = _entries.Slice(index * LobbyServerEntry.WireSize, LobbyServerEntry.WireSize);
            return ref MemoryMarshal.AsRef<LobbyServerEntry>(slot);
        }
    }
}
