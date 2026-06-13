// spec: Docs/RE/packets/1-7_select_character.yaml — opcode 1/7 (0x10007), 2-byte fixed payload.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. SlotIndex is MEDIUM confidence; StateFlag's meaning is LOWER
// confidence (lock / relation / detail-request) per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 1/7 — client select-character pre-step on the character-select screen: a slot index plus an
/// accompanying state/lock flag. Believed to lock or pre-stage the chosen slot before the
/// enter-world request (1/9). No inbound reply opcode was attributed to 1/7. Fixed 2-byte payload.
/// spec: Docs/RE/packets/1-7_select_character.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(1, 7)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgSelectCharacter
{
    /// <summary>Packed opcode 0x10007 (1/7). spec: packets/1-7_select_character.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgSelectCharacter;

    /// <summary>Declared wire size in bytes. spec: packets/1-7_select_character.yaml (size: 2).</summary>
    public const int WireSize = 2;

    /// <summary>0x00 — selected character slot index, range 0..4. MEDIUM CONFIDENCE. spec: same.</summary>
    public readonly byte SlotIndex;

    /// <summary>
    /// 0x01 — accompanying state/lock flag byte (lock vs relation vs detail-request undecoded).
    /// LOWER CONFIDENCE. spec: packets/1-7_select_character.yaml.
    /// </summary>
    public readonly byte StateFlag;
}
