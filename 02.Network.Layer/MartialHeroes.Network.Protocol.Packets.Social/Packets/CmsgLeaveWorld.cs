// spec: Docs/RE/packets/2-0_leave_world.yaml — opcode 2/0 (0x20000), header-only (0-byte payload).
// spec: Docs/RE/specs/world_exit.md §1 — 2/0 CmsgLeaveWorld is the guarded leave-world transition;
//   the routine disarms the 2/112 keepalive toggle BEFORE sending this frame, then performs the more
//   elaborate UI/object teardown. Header-only: the 8-byte frame header is the entire message.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed (opcodes.md status: confirmed); the
// "header-only, no payload" finding is a static inference (capture_verified: false in the spec).
// This is the heavier guarded exit path; the lighter quit path is 1/0 CmsgLogout.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

/// <summary>
///     2/0 — client leave-world / logout-to-lobby request. A header-only frame: the 8-byte frame header
///     (major=2, minor=0) carries the whole message and there is NO payload. Modelled as a zero-field
///     marker struct so the opcode is addressable through the same typed-view machinery; the send path
///     emits only the header.
///     The guarded routine disarms the 2/112 keepalive toggle before sending this packet, then tears
///     down the in-world UI/objects (heavier path than 1/0 CmsgLogout).
///     spec: Docs/RE/packets/2-0_leave_world.yaml; Docs/RE/specs/world_exit.md §1.
///     CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(2, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
public readonly struct CmsgLeaveWorld
{
    /// <summary>Packed opcode 0x20000 (2/0). spec: packets/2-0_leave_world.yaml; Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.CmsgLeaveWorld;

    /// <summary>
    ///     Declared wire PAYLOAD size in bytes: 0 (header-only). The struct itself carries a 1-byte
    ///     layout floor (an empty Pack=1 struct cannot be 0 bytes), but no payload byte is read or sent.
    ///     spec: packets/2-0_leave_world.yaml (size: 0); Docs/RE/specs/world_exit.md §1.
    /// </summary>
    public const int WireSize = 0;
}