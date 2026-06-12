// spec: Docs/RE/structs/item.md — "EquipItemResult — 16-byte equip/unequip result (opcode 4/12)".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// The visible header is 16 bytes (item.md); the (major:minor) 4/12 routing is dispatch-table-
// confirmed (opcodes.md). The apply path treats the underlying buffer as larger; only the 16-byte
// header is modelled here. Field meanings beyond the header are UNVERIFIED per item.md.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 4/12 — server equip/unequip result. Fixed 16-byte header. On success the equipment-slot / visual
/// update is applied; ToSlot value 15 triggers a title/gear visual rebuild.
/// spec: Docs/RE/structs/item.md ("EquipItemResult"); opcode 4/12 per Docs/RE/opcodes.md.
/// CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(4, 12)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgEquipItemResult
{
    /// <summary>Packed opcode 0x4000c (4/12). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgEquipItemResult;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/structs/item.md (16-byte header).</summary>
    public const int WireSize = 16;

    /// <summary>0x00 — validity guard; must be non-zero. spec: Docs/RE/structs/item.md.</summary>
    public readonly byte Guard;

    /// <summary>0x01 — alignment padding to the next dword. spec: Docs/RE/structs/item.md (pad: 3).</summary>
    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — actor identity / sort check (LE u32). spec: Docs/RE/structs/item.md (actor_sort).</summary>
    public readonly uint ActorSortKey;

    /// <summary>0x08 — result: 0 = error, 1 = ok, 2 = other. spec: Docs/RE/structs/item.md.</summary>
    public readonly byte Result;

    /// <summary>0x09 — unused. spec: Docs/RE/structs/item.md.</summary>
    private readonly byte _unused09;

    /// <summary>0x0a — source slot index. spec: Docs/RE/structs/item.md (from_slot).</summary>
    public readonly byte FromSlot;

    /// <summary>0x0b — from-sub-slot or related index. UNVERIFIED. spec: Docs/RE/structs/item.md (from_sub).</summary>
    public readonly byte FromSub;

    /// <summary>0x0c — destination slot index. 15 = visual gear refresh, 14 = special weapon slot. spec: item.md.</summary>
    public readonly byte ToSlot;

    /// <summary>0x0d — padding to 16 bytes. spec: Docs/RE/structs/item.md (pad_end: char[3]).</summary>
    private readonly byte _padEnd0;

    private readonly byte _padEnd1;
    private readonly byte _padEnd2;
}