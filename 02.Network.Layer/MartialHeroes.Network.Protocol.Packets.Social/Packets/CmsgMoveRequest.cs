// spec: Docs/RE/packets/2-13_move_request.yaml — opcode 2/13 (0x2000d), 16-byte fixed block.
//
// Control-flow-confirmed on IDB SHA 263bd994 (CYCLE 12 / Phase 3): size (16 bytes), the first
// three floats (Heading@0x00, TargetX@0x04, TargetZ@0x08), and the ModeFlags sub-byte split
// (ModeByte@0x0c = constant 3; RunFlag@0x0d = run-flag bool; Reserved0E@0x0e undefined) are
// confirmed. Wire VALUE semantics (Heading angular unit, cross-caller mode byte range) are
// CAPTURE/DEBUGGER-PENDING.
//
// CORRECTED (CYCLE 12 / Phase 3): the 0x0c..0x0f region splits into discrete bytes, NOT a
// single u32. ModeByte@0x0c is the constant 3 (hardcoded in the single shared body builder,
// even though every caller passes a mode argument that the builder ignores — statically-
// decidable value space is the singleton {3}). RunFlag@0x0d is the run-flag bool. Reserved0E
// (@0x0e, 2 bytes) is not written; treat as reserved/padding.
// spec: Docs/RE/packets/2-13_move_request.yaml (ModeFlags sub-layout refined to @0x0c=3 / @0x0d run-flag split).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

/// <summary>
///     2/13 — client click-to-move / position-sync request: a computed heading, a target XZ point,
///     and a movement-mode/run region. Fixed 16-byte payload (XZ-plane; world Y never sent).
///     The server re-broadcasts the result via S2C 5/13 ActorMovementUpdate.
///     <para>
///         Width sum: 4 + 4 + 4 + 1 + 1 + 2 = 16. ✓
///     </para>
///     spec: Docs/RE/packets/2-13_move_request.yaml.
/// </summary>
[PacketOpcode(2, 13)] // spec: Docs/RE/packets/2-13_move_request.yaml (opcode 0x2000d = major 2, minor 13)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgMoveRequest
{
    /// <summary>Packed opcode 0x2000d (2/13). spec: packets/2-13_move_request.yaml.</summary>
    public const uint OpcodeId = Opcodes.CmsgMoveRequest;

    /// <summary>Declared wire size in bytes. spec: packets/2-13_move_request.yaml (size: 16).</summary>
    public const int WireSize = 16;

    /// <summary>
    ///     0x00 — facing/heading angle from (target - current) delta (LE f32).
    ///     Angular unit capture-pending (radians-ish).
    ///     spec: Docs/RE/packets/2-13_move_request.yaml (Heading, offset 0).
    /// </summary>
    public readonly float Heading; // spec: 2-13_move_request.yaml +0x00 (f32)

    /// <summary>
    ///     0x04 — requested target X in world coords (LE f32).
    ///     spec: Docs/RE/packets/2-13_move_request.yaml (TargetX, offset 4, CONFIRMED).
    /// </summary>
    public readonly float TargetX; // spec: 2-13_move_request.yaml +0x04 (f32, confirmed)

    /// <summary>
    ///     0x08 — requested target Z in world coords (LE f32); world Y not sent.
    ///     spec: Docs/RE/packets/2-13_move_request.yaml (TargetZ, offset 8, CONFIRMED).
    /// </summary>
    public readonly float TargetZ; // spec: 2-13_move_request.yaml +0x08 (f32, confirmed)

    /// <summary>
    ///     0x0c (u8) Move-mode byte. CONTROL-FLOW CONFIRMED to be the constant 3 at this send site.
    ///     The body builder is a single shared sender that writes 3 unconditionally (hardcoded
    ///     literal), even though every caller passes a mode argument that the builder ignores.
    ///     Statically-decidable value space = the singleton {3}. Any other server-side interpretation
    ///     is capture/debugger-pending.
    ///     spec: Docs/RE/packets/2-13_move_request.yaml (ModeByte, offset 0x0c; constant 3 CONFIRMED).
    /// </summary>
    public readonly byte ModeByte; // spec: 2-13_move_request.yaml +0x0c (u8, constant 3 at this send site)

    /// <summary>
    ///     0x0d (u8) Run/walk flag — 1 when the local player run state is 1 (running).
    ///     CONTROL-FLOW CONFIRMED as a distinct byte at @0x0d.
    ///     spec: Docs/RE/packets/2-13_move_request.yaml (RunFlag, offset 0x0d, CONFIRMED).
    /// </summary>
    public readonly byte RunFlag; // spec: 2-13_move_request.yaml +0x0d (u8, run-flag bool confirmed)

    /// <summary>
    ///     0x0e (u8[2]) Trailing reserved bytes — not written by the sender; treat as reserved/padding.
    ///     spec: Docs/RE/packets/2-13_move_request.yaml (Reserved0E, offset 0x0e, bytes[2]).
    /// </summary>
    private readonly byte _reserved0E_0; // spec: 2-13_move_request.yaml +0x0e..+0x0f (2 bytes reserved/undefined)

    private readonly byte _reserved0E_1;
}