// spec: Docs/RE/packets/lobby.yaml — RECORD SHAPE A (SERVER-LIST entry, port 10000).
//
// The 8-byte, little-endian server-list wire record. This is the BLITTABLE Pack=1 view of the
// bytes on the wire; LobbyServerRecord (sibling file) is the decoded, type-safe DTO that the
// application layer consumes. The transport reinterprets the decompressed payload as a packed
// array of these structs in place (zero copy, zero allocation) and projects each into the DTO.
//
// !!! CAPTURE-UNVERIFIED ON-WIRE VALUES !!!
// Field ORDER, SIZE and SIGNEDNESS are static-CONFIRMED (doida.exe IDB 263bd994, CYCLE 9 Phase 1 +
// the 2026-06-23 server-list re-derive). The literal on-wire byte VALUES remain capture-pending
// (capture_verified: false). spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
///     One 8-byte little-endian server-list record as a <c>Pack=1</c> wire struct (lobby
///     RECORD SHAPE A). All four fields are signed <c>i16</c> — the legacy consumer sign-extends
///     every load and the commit gate (<c>Load &lt; 2400</c>) is a signed branch. The decompressed
///     server-list payload is <c>record_count</c> of these packed back to back with no padding
///     (allocation = copy = stride = 8). spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.
/// </summary>
/// <remarks>
///     <para>
///         This is the raw wire layout; <see cref="LobbyServerRecord" /> is the decoded DTO that
///         crosses the layer boundary. Use <see cref="ToRecord" /> to project. The transport never
///         hand-reads the four <c>i16</c>s with <c>BinaryPrimitives</c>; it reinterprets the payload
///         span as a <c>ReadOnlySpan&lt;LobbyServerRecordWire&gt;</c> and reads each field directly
///         (the protocol is little-endian throughout, matching x86 host order, so a blittable
///         reinterpret is byte-exact). spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.
///     </para>
///     <para>
///         Field map (all LE <c>i16</c>): <c>+0 server_id</c>, <c>+2 status_code</c>, <c>+4 load</c>,
///         <c>+6 open_time</c>. Field order and the commit gate are static-CONFIRMED — the earlier
///         "+0 may be open_time" caveat is REFUTED. spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LobbyServerRecordWire
{
    /// <summary>Wire size of one server-list record in bytes (2+2+2+2). spec: lobby.yaml RECORD SHAPE A.</summary>
    public const int WireSize = 8; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A (per-entry total = 8)

    /// <summary>
    ///     +0x00 — server id / select-key (LE i16, range 1..40). Read sign-extended by both the
    ///     painter and the plate-pick commit. NOT a selectability gate (the <c>== 100</c> literal is
    ///     display-only). spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A (+0 server_id, i16).
    /// </summary>
    public readonly short ServerId; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A offset +0x00

    /// <summary>
    ///     +0x02 — availability / caption selector (LE i16). <c>== 0</c> is the active/selectable
    ///     state (the commit gate equality-tests this for 0); <c>== 3</c> is the scheduled-open
    ///     branch. spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A (+2 status_code, i16).
    /// </summary>
    public readonly short StatusCode; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A offset +0x02

    /// <summary>
    ///     +0x04 — population / load gauge (LE i16). Commit gate requires <c>Load &lt; 2400</c>
    ///     (0x960, signed strict less-than). Reused as the scheduled-open HOUR when
    ///     <see cref="StatusCode" /> == 3. spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A (+4 load, i16).
    /// </summary>
    public readonly short Load; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A offset +0x04

    /// <summary>
    ///     +0x06 — scheduled-open MINUTE value (LE i16) — a time component, NOT a flag/bitfield.
    ///     Read only in the <see cref="StatusCode" /> == 3 branch (combined with <see cref="Load" />
    ///     for an HH:MM caption). spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A (+6 open_time, i16).
    /// </summary>
    public readonly short OpenTime; // spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A offset +0x06

    /// <summary>
    ///     Projects this blittable wire record into the decoded <see cref="LobbyServerRecord" /> DTO
    ///     that crosses the layer boundary. Pure field copy — no allocation beyond the value struct.
    ///     spec: Docs/RE/packets/lobby.yaml RECORD SHAPE A.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LobbyServerRecord ToRecord()
    {
        return new LobbyServerRecord(ServerId, StatusCode, Load, OpenTime);
    }
}
