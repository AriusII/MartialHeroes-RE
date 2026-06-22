// spec: Docs/RE/packets/3-4_scene_entity_update.yaml — opcode 3/4 (0x30004), VARIABLE-LENGTH.
// REWRITTEN in correct namespace; supersedes the earlier wrong-namespace codegen stub.
// ida_anchor: 263bd994   evidence: [static-ida]   capture_verified: false
//
// The handler reads a 3-byte header (ServerId + ChannelId + SlotMask), then for each set bit in
// SlotMask (LSB-first, EXACTLY 5 slots scanned) reads one 981-byte per-slot record. The fixed
// 3-byte header is modelled here; the variable tail is decoded by SmsgCharacterListReader since
// 3/4 shares the exact per-slot record format with 3/1 SmsgCharacterList.
//
// Width of the fixed header = 1 + 1 + 1 = 3 bytes. spec: Docs/RE/packets/3-4_scene_entity_update.yaml.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.Login.Packets;

/// <summary>
///     3/4 — lobby scene and character-slot update. VARIABLE-LENGTH. Models the FIXED 3-byte header
///     only: ServerId + ChannelId + SlotMask. The variable per-slot records (each 981 bytes: 880-byte
///     SpawnDescriptor + 96-byte StatBlock + 1-byte SlotFlag + 4-byte FlagsWord) are decoded by the caller
///     (identical record format to 3/1 SmsgCharacterList; use <see cref="SmsgCharacterListReader" />).
///     spec: Docs/RE/packets/3-4_scene_entity_update.yaml. CAPTURE-UNVERIFIED value semantics.
/// </summary>
[PacketOpcode(3, 4)] // spec: Docs/RE/packets/3-4_scene_entity_update.yaml (opcode 3/4 = 0x30004)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSceneEntityUpdate
{
    /// <summary>Packed opcode 0x30004 (3/4). spec: Docs/RE/opcodes.md; packets/3-4_scene_entity_update.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgSceneEntityUpdate; // spec: Docs/RE/opcodes.md row 3/4

    /// <summary>
    ///     Size of the FIXED header in bytes (3). The variable per-slot records follow.
    ///     spec: Docs/RE/packets/3-4_scene_entity_update.yaml (3-byte header).
    /// </summary>
    public const int HeaderSize = 3; // spec: Docs/RE/packets/3-4_scene_entity_update.yaml

    /// <summary>
    ///     0x00 — inbound server id indicator. spec: Docs/RE/packets/3-4_scene_entity_update.yaml (ServerId @0x00).
    ///     If ServerId != 1, the handler delegates to the select-window sub-handler.
    /// </summary>
    public readonly byte ServerId; // spec: Docs/RE/packets/3-4_scene_entity_update.yaml offset 0x00

    /// <summary>0x01 — inbound channel id indicator. spec: Docs/RE/packets/3-4_scene_entity_update.yaml (ChannelId @0x01).</summary>
    public readonly byte ChannelId; // spec: Docs/RE/packets/3-4_scene_entity_update.yaml offset 0x01

    /// <summary>
    ///     0x02 — slot bitmask: bit i set =&gt; slot i record follows (LSB-first, EXACTLY 5 slots scanned,
    ///     bit indices 0..4). spec: Docs/RE/packets/3-4_scene_entity_update.yaml (SlotMask @0x02).
    /// </summary>
    public readonly byte SlotMask; // spec: Docs/RE/packets/3-4_scene_entity_update.yaml offset 0x02
}
