// spec: Docs/RE/structs/item.md — "EquipSlotBody — 36-byte item-slot state record (opcode 4/22)".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Read as a fixed 36-byte (0x24) body by the single-slot state-ack handler. The 4/22 routing is
// dispatch-table-confirmed (opcodes.md); the field meanings at +0x0C..+0x20 are inferred from local
// usage only and are UNVERIFIED per item.md.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 4/22 — server item-slot state acknowledgement (equip/unequip result plus stat/enchant fields).
/// Fixed 36-byte body. Distinct from the 16-byte <see cref="ItemSlotRecord"/>.
/// spec: Docs/RE/structs/item.md ("EquipSlotBody"); opcode 4/22 per Docs/RE/opcodes.md.
/// CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(4, 22)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemSlotStateAck
{
    /// <summary>Packed opcode 0x40016 (4/22). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgItemSlotStateAck;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/structs/item.md (36-byte / 0x24 body).</summary>
    public const int WireSize = 36;

    /// <summary>0x00 — guard / identity prefix bytes. spec: Docs/RE/structs/item.md (header: char[8]).</summary>
    public readonly HeaderBuffer Header;

    /// <summary>0x08 — result: 0 = error, 1 = ok. spec: Docs/RE/structs/item.md.</summary>
    public readonly byte Result;

    /// <summary>0x09 — padding. spec: Docs/RE/structs/item.md (pad_9).</summary>
    private readonly byte _pad9;

    /// <summary>0x0a — source slot index. spec: Docs/RE/structs/item.md (from_slot).</summary>
    public readonly byte FromSlot;

    /// <summary>0x0b — destination slot index. spec: Docs/RE/structs/item.md (to_slot).</summary>
    public readonly byte ToSlot;

    /// <summary>0x0c — flags. UNVERIFIED. spec: Docs/RE/structs/item.md (flag_c).</summary>
    public readonly uint FlagC;

    /// <summary>0x10 — flags. UNVERIFIED. spec: Docs/RE/structs/item.md (flag_10).</summary>
    public readonly uint Flag10;

    /// <summary>0x14 — unmapped 4 bytes (between flag_10 and bonus_1). spec: Docs/RE/structs/item.md (gap).</summary>
    private readonly uint _gap14;

    /// <summary>0x18 — bonus / stat field. UNVERIFIED. spec: Docs/RE/structs/item.md (bonus_field_1).</summary>
    public readonly int BonusField1;

    /// <summary>0x1c — bonus / stat field. UNVERIFIED. spec: Docs/RE/structs/item.md (bonus_field_2).</summary>
    public readonly int BonusField2;

    /// <summary>0x20 — bonus / durability / enchant field. UNVERIFIED. spec: Docs/RE/structs/item.md (bonus_field_3).</summary>
    public readonly int BonusField3;

    /// <summary>0x00 — 8-byte guard / identity prefix. spec: Docs/RE/structs/item.md (header).</summary>
    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }
}
