// spec: Docs/RE/packets/2-13_move_request.yaml — opcode 2/13 (0x2000d), 16-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The internal byte split of ModeFlags is UNKNOWN per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 2/13 — client click-to-move / position-sync request: a computed heading, a target XZ point,
/// and a packed move-mode/run region. Fixed 16-byte payload (XZ-plane; world Y never sent).
/// The server re-broadcasts the result via S2C 5/13 ActorMovementUpdate.
/// spec: Docs/RE/packets/2-13_move_request.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(2, 13)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgMoveRequest
{
    /// <summary>Packed opcode 0x2000d (2/13). spec: packets/2-13_move_request.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgMoveRequest;

    /// <summary>Declared wire size in bytes. spec: packets/2-13_move_request.yaml (size: 16).</summary>
    public const int WireSize = 16;

    /// <summary>0x00 — facing/heading angle from (target - current) delta (LE f32). spec: same.</summary>
    public readonly float Heading;

    /// <summary>0x04 — requested target X in world coords (LE f32). spec: packets/2-13_move_request.yaml.</summary>
    public readonly float TargetX;

    /// <summary>0x08 — requested target Z in world coords (LE f32); world Y not sent. spec: same.</summary>
    public readonly float TargetZ;

    /// <summary>
    /// 0x0c — packed move-mode + run-flag region (LE u32). Low byte is a move-mode selector
    /// (click-to-move passes 1); the sub-byte split is unconfirmed, so it stays a single u32 to be
    /// masked once a capture pins it down. spec: packets/2-13_move_request.yaml.
    /// </summary>
    public readonly uint ModeFlags;
}