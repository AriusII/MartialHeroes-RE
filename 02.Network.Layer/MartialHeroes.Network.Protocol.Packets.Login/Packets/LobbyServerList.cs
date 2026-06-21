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
// the 8-byte wrapper / 8-byte entry / 30-byte endpoint shapes are CODE-CONFIRMED.
// SIGNEDNESS: all four fields of LobbyServerEntry (Id, StatusCode, Load, OpenTime) are signed i16 —
// CONFIRMED CYCLE 9 (binary-won, 263bd994). The earlier "needs-capture" signedness flag is RESOLVED.
// ENDPOINT DELIMITER: single SPACE (0x20) — CONFIRMED CYCLE 9 (binary-won, 263bd994); see §2.2.
//
// COMPRESSION: both lobby response PAYLOADS are LZ4-compressed (raw block) and NOT encrypted. The
// LZ4 decompression is the TRANSPORT / caller's job; the structs below map the DECOMPRESSED bytes.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     The common 8-byte lobby frame wrapper that prefixes BOTH lobby responses (server-list and
///     channel-endpoint queries), little-endian. On the server-list query the <see cref="Count" /> word
///     is the number of 8-byte <see cref="LobbyServerEntry" /> records that follow in the decompressed
///     payload. spec: Docs/RE/packets/lobby.yaml ("COMMON LOBBY FRAME WRAPPER"). CAPTURE-UNVERIFIED.
/// </summary>
/// <remarks>
///     This wrapper reuses the game frame's header shape but is dispatched by neither major nor minor:
///     the <see cref="Reserved" /> word (the game frame's "minor") is unused on this socket. The
///     8-byte wrapper is read first, then <c>(Size - 8)</c> compressed payload bytes are received and
///     LZ4-decompressed by the caller. spec: Docs/RE/packets/lobby.yaml.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyFrameWrapper
{
    /// <summary>Size of the fixed wrapper in bytes. spec: lobby.yaml (8-byte wrapper).</summary>
    public const int WireSize = 8;

    /// <summary>
    ///     0x00 — total frame size in bytes = 8 (this wrapper) + compressed-payload length. The payload
    ///     length to receive is <c>(Size - 8)</c>. CODE-CONFIRMED. spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly uint Size;

    /// <summary>
    ///     0x04 — on the SERVER-LIST query this is the RECORD COUNT (number of 8-byte server entries that
    ///     follow). Reuses the game frame's "major" word as a count. CODE-CONFIRMED.
    ///     spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly ushort Count;

    /// <summary>
    ///     0x06 — reuses the game frame's "minor" word; preserved through decompression but NOT read by
    ///     either lobby thread (unused on this socket). CODE-CONFIRMED present, role = unused.
    ///     spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public readonly ushort Reserved;

    /// <summary>The compressed-payload byte length implied by <see cref="Size" /> (<c>Size - 8</c>).</summary>
    public int PayloadLength => (int)Size - WireSize;

    /// <summary>
    ///     Reads the 8-byte lobby wrapper from the start of <paramref name="frame" /> (little-endian),
    ///     no allocation. spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="frame" /> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LobbyFrameWrapper Read(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < WireSize)
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Length, $"A lobby wrapper requires at least {WireSize} bytes.");

        // spec: lobby.yaml — little-endian; size@+0 (u32), count@+4 (u16), reserved@+6 (u16).
        return MemoryMarshal.Read<LobbyFrameWrapper>(frame);
    }
}

/// <summary>
///     One 8-byte SERVER-LIST entry (record shape A), little-endian, from the lobby server-list query
///     (port 10000). The decompressed payload is <c>count</c> of these packed back-to-back; the count
///     lives in the <see cref="LobbyFrameWrapper" />, not in the payload. spec: Docs/RE/packets/lobby.yaml
///     ("RECORD SHAPE A"). CAPTURE-UNVERIFIED.
/// </summary>
/// <remarks>
///     Signedness: all four fields are <b>signed <c>i16</c></b> — CONFIRMED CYCLE 9
///     (binary-won, build 263bd994). The binary reads every field with a sign-extending load, and
///     the commit gate's <c>load &lt; 2400</c> is a signed branch — the decisive evidence.
///     An earlier "needs-capture" note on signedness is RESOLVED; the field types are definitive.
///     spec: Docs/RE/specs/login_flow.md §2.1 (Signedness CONFLICT corrected, CYCLE 9, 263bd994).
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyServerEntry
{
    /// <summary>Size of one server entry in bytes. spec: lobby.yaml (2 + 2 + 2 + 2 = 8).</summary>
    public const int WireSize = 8;

    /// <summary>
    ///     0x00 — server id / select-key (i16, range 1..40). Read by BOTH consumers (the painter and the
    ///     plate-pick commit) as the server id, sign-extended; feeds the client-local server-name lookup
    ///     and draws the remembered-default highlight. The <c>== 100</c> literal is display-only (the
    ///     "new server" label reposition), NOT a selectability gate. CODE-CONFIRMED.
    ///     Signed i16 — CONFIRMED CYCLE 9 (binary-won, 263bd994); all four record fields are signed.
    ///     spec: Docs/RE/specs/login_flow.md §2.1 Record Shape A, offset +0 (i16).
    /// </summary>
    public readonly short Id;

    /// <summary>
    ///     0x02 — status code (i16): availability / caption selector. <c>== 0</c> is the active/selectable
    ///     state (the commit gate equality-tests this field for 0); <c>== 3</c> is the scheduled-open branch
    ///     that builds the HH:MM caption (see <see cref="Load" /> / <see cref="OpenTime" />). CODE-CONFIRMED.
    ///     spec: Docs/RE/packets/lobby.yaml Record Shape A.
    /// </summary>
    public readonly short StatusCode;

    /// <summary>
    ///     0x04 — load / population gauge (i16). The commit gate requires <c>load &lt; 2400</c> (0x960,
    ///     signed strict less-than). When <see cref="StatusCode" /> == 3 the painter reuses this field as the
    ///     scheduled-open HOUR (combined with <see cref="OpenTime" /> for an HH:MM caption). CODE-CONFIRMED.
    ///     spec: Docs/RE/packets/lobby.yaml Record Shape A.
    /// </summary>
    public readonly short Load;

    /// <summary>
    ///     0x06 — open time (i16): the scheduled-open MINUTE value (a time component, NOT a flag/bitfield).
    ///     Read ONLY in the <see cref="StatusCode" /> == 3 branch, where it supplies the minute digits of an
    ///     HH:MM caption (hour from <see cref="Load" />). CODE-CONFIRMED.
    ///     spec: Docs/RE/packets/lobby.yaml Record Shape A.
    /// </summary>
    public readonly short OpenTime;
}

/// <summary>
///     The 30-byte CHANNEL-ENDPOINT token (record shape B) from the channel query
///     (port 10000 + selected channel offset). The channel thread zero-fills a 30-byte field then copies
///     UP TO 30 bytes of the decompressed payload as a NUL-terminated ASCII "&lt;host&gt; &lt;port&gt;" token
///     naming the GAME server (single-space delimiter, port via decimal atol). 30 is a copy CAP, not a
///     minimum; it is a SINGLE endpoint (no trailing array).
///     spec: Docs/RE/specs/login_flow.md §2.2; Docs/RE/packets/lobby.yaml ("RECORD SHAPE B").
/// </summary>
/// <remarks>
///     Token format binary-confirmed CYCLE 9 Phase 1: "&lt;host&gt; &lt;port&gt;", single space (0x20),
///     NUL-terminated, up-to-30-byte copy cap, single endpoint. spec: Docs/RE/specs/login_flow.md §2.2.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyChannelEndpointToken
{
    /// <summary>Size of the fixed endpoint token in bytes. spec: lobby.yaml (char[30]).</summary>
    public const int WireSize = 30;

    /// <summary>
    ///     0x00 — fixed 30-byte ASCII endpoint token naming the GAME server, consumed as one opaque
    ///     string token (asciiz NOT guaranteed). spec: Docs/RE/packets/lobby.yaml.
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
///     Zero-allocation reader over a DECOMPRESSED lobby server-list reply. Wraps the leading 8-byte
///     <see cref="LobbyFrameWrapper" /> and exposes the trailing <c>count</c> packed 8-byte
///     <see cref="LobbyServerEntry" /> records as in-place <c>ref readonly</c> views — no copies, no
///     per-entry allocation. spec: Docs/RE/packets/lobby.yaml ("RECORD SHAPE A").
/// </summary>
public readonly ref struct LobbyServerListReader
{
    private readonly ReadOnlySpan<byte> _entries;

    /// <summary>The parsed 8-byte wrapper at the head of the reply. spec: Docs/RE/packets/lobby.yaml.</summary>
    public readonly LobbyFrameWrapper Wrapper;

    /// <summary>
    ///     Parses the wrapper from the head of <paramref name="decompressed" /> and bounds the entry
    ///     region to the smaller of the wrapper's <see cref="LobbyFrameWrapper.Count" /> and the actual
    ///     bytes available after the wrapper. The input is the ALREADY-LZ4-DECOMPRESSED reply (the
    ///     caller performs decompression). spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the input is shorter than the 8-byte wrapper.</exception>
    public LobbyServerListReader(ReadOnlySpan<byte> decompressed)
    {
        Wrapper = LobbyFrameWrapper.Read(decompressed);

        var tail = decompressed[LobbyFrameWrapper.WireSize..];
        var available = tail.Length / LobbyServerEntry.WireSize;
        var n = Wrapper.Count <= available ? Wrapper.Count : available;
        _entries = tail[..(n * LobbyServerEntry.WireSize)];
    }

    /// <summary>
    ///     The number of 8-byte entries actually iterable (min of the wrapper count and the bytes
    ///     available). spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    public int Count => _entries.Length / LobbyServerEntry.WireSize;

    /// <summary>
    ///     Returns an in-place <c>ref readonly</c> view of entry <paramref name="index" /> — zero copy.
    ///     spec: Docs/RE/packets/lobby.yaml.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="index" /> is out of range.</exception>
    public ref readonly LobbyServerEntry this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Entry index out of range.");

            var slot = _entries.Slice(index * LobbyServerEntry.WireSize, LobbyServerEntry.WireSize);
            return ref MemoryMarshal.AsRef<LobbyServerEntry>(slot);
        }
    }
}