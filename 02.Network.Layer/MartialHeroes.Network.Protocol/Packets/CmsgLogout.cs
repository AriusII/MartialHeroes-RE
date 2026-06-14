// spec: Docs/RE/packets/cmsg_logout.yaml — opcode 1/0 (0x10000), header-only (0-byte payload).
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the "header-only, no payload" finding is
// a static inference (capture_verified: false in the spec). This is the only char-mgmt builder that
// writes NO payload bytes and does NOT set the select-window net-busy latch (single emitter, on the
// quit-from-in-game scene path).

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/0 — client logout / quit-from-in-game request. A header-only frame: the 8-byte frame header
/// (major=1, minor=0) carries the whole message and there is NO payload. Modelled as a zero-field
/// marker struct so the opcode is addressable through the same typed-view machinery; the send path
/// emits only the header.
/// spec: Docs/RE/packets/cmsg_logout.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 1)]
public readonly struct CmsgLogout
{
    /// <summary>Packed opcode 0x10000 (1/0). spec: packets/cmsg_logout.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgLogout;

    /// <summary>
    /// Declared wire PAYLOAD size in bytes: 0 (header-only). The struct itself carries a 1-byte
    /// layout floor (an empty Pack=1 struct cannot be 0 bytes), but no payload byte is read or sent.
    /// spec: packets/cmsg_logout.yaml (size: 0).
    /// </summary>
    public const int WireSize = 0;
}