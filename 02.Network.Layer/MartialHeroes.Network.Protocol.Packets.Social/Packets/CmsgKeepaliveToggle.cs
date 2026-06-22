// spec: Docs/RE/packets/2-112_keepalive_toggle.yaml — opcode 2/112 (0x20070), 1-byte fixed payload.
// spec: Docs/RE/opcodes.md (2/112 row).
//
// CONTROL-FLOW CONFIRMED (static IDA, IDB SHA 263bd994):
//   routing+size: confirmed — builder writes header 2/112 + appends a SINGLE 1-byte body, value 1.
//   enable gate:  confirmed — gated by a master keepalive-enable flag (Block-A keepalive anchor):
//     one argument SETS the flag (armed on world-enter), another CLEARS it (on leave-to-logout),
//     otherwise the toggle is sent only while the flag is set.
//   callers:      confirmed — the leave-world-to-logout scene path, the world-state-tick handler (4/1),
//     and WinMain.
//   wire VALUE semantics: capture/debugger-pending (non-blocking).
//
// DISTINCTION FROM 2/10000: the major-2 keepalive family has TWO distinct C2S messages:
//   - 2/10000 (packets/2-10000_keepalive.yaml): timer-driven, 4-byte zero body, ~20000 ms cadence.
//   - 2/112 (this spec):  on-demand 1-byte enable/ping gated by the master-enable flag.
// spec: Docs/RE/packets/2-112_keepalive_toggle.yaml.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Social.Packets;

/// <summary>
///     2/112 — on-demand keepalive enable/toggle. A single-byte body: the send site appends the constant
///     value 1. DISTINCT from the timer-driven 2/10000 keepalive (4-byte zero body); the client has TWO
///     keepalive-family C2S messages on major 2. Gated by a master-enable flag: ENABLE on world entry
///     (armed from the world-state-tick / WinMain path), DISABLE on logout/leave-world (the leave-world
///     path disarms the flag before sending 2/0 CmsgLeaveWorld). Fixed 1-byte payload.
///     spec: Docs/RE/packets/2-112_keepalive_toggle.yaml. CONTROL-FLOW CONFIRMED (IDB SHA 263bd994).
///     Wire VALUE semantics (cadence, whether body byte is ever != 1) capture/debugger-pending.
/// </summary>
[PacketOpcode(2, 112)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgKeepaliveToggle
{
    /// <summary>
    ///     Packed opcode 0x20070 = (2 &lt;&lt; 16) | 112 (2/112).
    ///     spec: Docs/RE/packets/2-112_keepalive_toggle.yaml (opcode: 0x20070).
    /// </summary>
    public const uint OpcodeId = Opcodes.CmsgKeepaliveToggle; // spec: Docs/RE/packets/2-112_keepalive_toggle.yaml

    /// <summary>
    ///     Declared wire size in bytes = 1 (the builder appends exactly one byte, value 1).
    ///     spec: Docs/RE/packets/2-112_keepalive_toggle.yaml (size: 1). Σ-verified.
    /// </summary>
    public const int WireSize = 1; // spec: Docs/RE/packets/2-112_keepalive_toggle.yaml — single 1-byte body

    /// <summary>
    ///     0x00 — the toggle flag byte; the builder writes the constant 1. Whether the server
    ///     interprets it as an enable/ping toggle is capture/debugger-pending (non-blocking).
    ///     spec: Docs/RE/packets/2-112_keepalive_toggle.yaml (Flag @0x00, u8, value 1 confirmed at send-site).
    /// </summary>
    public readonly byte Flag; // spec: Docs/RE/packets/2-112_keepalive_toggle.yaml @0x00 Flag u8
}