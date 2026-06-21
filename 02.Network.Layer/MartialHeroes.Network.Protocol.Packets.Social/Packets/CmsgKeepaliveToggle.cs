// spec: Docs/RE/opcodes.md — opcode 2/112 (0x20070), 1-byte fixed payload.
// (No packets/2-112_*.yaml exists; opcodes.md is the authoritative entry for this send.)
//
// !!! CAPTURE-UNVERIFIED VALUE SEMANTICS !!!
// The (major:minor) routing and the 1-byte send size are control-flow-confirmed; the on-wire cadence
// and the exact toggle semantics are capture/debugger-pending per opcodes.md.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

/// <summary>
///     2/112 — on-demand keepalive enable/toggle. A single-byte body: the send site appends a constant 1.
///     DISTINCT from the timer-driven 2/10000 keepalive (4-byte zero body); the client has TWO
///     keepalive-family C2S messages on major 2. Gated by a master-enable flag (set on world-enter,
///     cleared on leave); emitted from the leave-to-logout scene path, the game-state-tick handler, and
///     WinMain. Fixed 1-byte payload. spec: Docs/RE/opcodes.md (2/112). CAPTURE-UNVERIFIED cadence.
/// </summary>
[PacketOpcode(2, 112)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgKeepaliveToggle
{
    /// <summary>Packed opcode 0x20070 (2/112). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.CmsgKeepaliveToggle;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/opcodes.md (2/112 — 1-byte body).</summary>
    public const int WireSize = 1;

    /// <summary>
    ///     0x00 — the toggle flag the send appends; the builder writes a constant 1. spec: Docs/RE/opcodes.md.
    /// </summary>
    public readonly byte Flag;
}