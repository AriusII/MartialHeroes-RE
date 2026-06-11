// spec: Docs/RE/packets/3-1_character_list.yaml — opcode 3/1 (0x30001), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it.
//
// THIS IS THE FIXED 3-BYTE HEADER ONLY. After it, for each set bit in SlotMask (LSB-first, ~8
// slots), one 981-byte per-slot record follows = 880-byte SpawnDescriptor + 96-byte stat block +
// 1 flag byte + 4-byte timestamp. Those variable per-slot records are NOT modelled as a struct;
// the caller (Client.Application) hand-codes the repeat loop. No fixed WireSize / size assertion.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 3/1 — FIXED 3-byte header of the character-select list. The <see cref="SlotMask"/> bitmask drives a
/// per-slot loop that reads one 981-byte record (SpawnDescriptor + stat block + flag + timestamp) per
/// set bit; that variable tail is hand-coded by the caller.
/// spec: Docs/RE/packets/3-1_character_list.yaml. CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(3, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharacterListHeader
{
    /// <summary>Packed opcode 0x30001 (3/1). spec: packets/3-1_character_list.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgCharacterList;

    /// <summary>Size of the FIXED header in bytes. spec: packets/3-1_character_list.yaml (3-byte header).</summary>
    public const int HeaderSize = 3;

    /// <summary>Size of one per-slot record that follows the header. spec: same (880 + 96 + 1 + 4 = 981).</summary>
    public const int SlotRecordSize = 981;

    /// <summary>0x00 — server id / list context byte. spec: packets/3-1_character_list.yaml.</summary>
    public readonly byte ServerId;

    /// <summary>0x01 — channel id / context byte. spec: packets/3-1_character_list.yaml.</summary>
    public readonly byte ChannelId;

    /// <summary>
    /// 0x02 — slot bitmask: bit i set =&gt; slot i record follows (LSB-first, ~8 slots).
    /// spec: packets/3-1_character_list.yaml.
    /// </summary>
    public readonly byte SlotMask;
}
