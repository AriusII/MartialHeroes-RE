// spec: Docs/RE/packets/cmsg_char_select.yaml — opcode 1/7 (0x10007), 2-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The (major:minor) routing is dispatch-table-confirmed; the field layout is a static inference
// (capture_verified: false). Both bytes are STATIC HIGH for both emitter fills per the spec:
// SlotIndex@0 is the chosen slot; Flag@1 distinguishes the two emit paths (1 = select-for-play,
// 0 = create-slot ack). No inbound reply opcode was attributed statically.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/7 — client slot select / ack on the character-select screen: a slot index plus a flag byte that
/// distinguishes the two emit paths (1 on the select-for-play path; 0 on the create-slot ack path).
/// Working model: 1/7 selects/pre-stages a slot, then 1/9 (cmsg_char_enter.yaml) drives world entry.
/// Two emitters in the select-window dispatcher. No inbound reply opcode attributed. Fixed 2 bytes.
/// spec: Docs/RE/packets/cmsg_char_select.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgSelectCharacter
{
    /// <summary>Packed opcode 0x10007 (1/7). spec: packets/cmsg_char_select.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgSelectCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/cmsg_char_select.yaml (size: 2).</summary>
    public const int WireSize = 2;

    /// <summary>0x00 — selected character slot index, range 0..4. STATIC HIGH. spec: packets/cmsg_char_select.yaml.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    /// 0x01 — select-confirm / play flag: 1 = select this slot to play (select-for-play path);
    /// 0 = ack / enter the created slot (create-slot ack path). STATIC HIGH for both emitter fills.
    /// spec: packets/cmsg_char_select.yaml.
    /// </summary>
    public readonly byte Flag;
}