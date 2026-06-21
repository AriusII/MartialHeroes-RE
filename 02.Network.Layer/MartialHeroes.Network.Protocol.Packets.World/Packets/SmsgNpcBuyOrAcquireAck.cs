// spec: Docs/RE/structs/item.md — "NpcBuy / inventory-acquire ack (opcode 4/19) — 56-byte fixed body".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Fixed 56-byte body. The 4/19 routing is dispatch-table-confirmed (opcodes.md). Several field
// meanings (gold_hi, repair_val_*, item_quad_a/c/d) are UNVERIFIED per item.md.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     4/19 — NPC buy / inventory-acquire acknowledgement. Fixed 56-byte body. On success with a valid
///     ItemQuadB actor id, the item is applied to BagSlotIndex; result==0 &amp;&amp; reason==1 shows an
///     item-shop-expired message. spec: Docs/RE/structs/item.md ("NpcBuy / inventory-acquire ack");
///     opcode 4/19 per Docs/RE/opcodes.md. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(4, 19)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgNpcBuyOrAcquireAck
{
    /// <summary>Packed opcode 0x40013 (4/19). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.SmsgNpcBuyOrAcquireAck;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/structs/item.md (56-byte fixed body).</summary>
    public const int WireSize = 56;

    /// <summary>0x00 — packet prefix. spec: Docs/RE/structs/item.md (header: char[4]).</summary>
    public readonly HeaderBuffer Header;

    /// <summary>0x04 — target actor id. spec: Docs/RE/structs/item.md (actor_id).</summary>
    public readonly int ActorId;

    /// <summary>0x08 — gold cost (low) or amount. spec: Docs/RE/structs/item.md (gold_lo).</summary>
    public readonly int GoldLo;

    /// <summary>0x0c — gold cost (high) or secondary currency. UNVERIFIED. spec: Docs/RE/structs/item.md (gold_hi).</summary>
    public readonly int GoldHi;

    /// <summary>0x10 — result: 0 = error, 1 = ok. spec: Docs/RE/structs/item.md.</summary>
    public readonly byte Result;

    /// <summary>0x11 — error reason; selects a localized string. spec: Docs/RE/structs/item.md (reason_code).</summary>
    public readonly byte ReasonCode;

    /// <summary>0x12 — destination bag slot. spec: Docs/RE/structs/item.md (bag_slot_index).</summary>
    public readonly byte BagSlotIndex;

    /// <summary>0x13 — unmapped to 0x20. spec: Docs/RE/structs/item.md (gap_13: char[13]).</summary>
    public readonly Gap13Buffer Gap13;

    /// <summary>0x20 — repair-related value; non-zero shows a repair-completion countdown. UNVERIFIED. spec: item.md.</summary>
    public readonly uint RepairVal1;

    /// <summary>0x24 — repair-related value. UNVERIFIED. spec: Docs/RE/structs/item.md (repair_val_2).</summary>
    public readonly uint RepairVal2;

    /// <summary>0x28 — item descriptor word A. UNVERIFIED. spec: Docs/RE/structs/item.md (item_quad_a).</summary>
    public readonly int ItemQuadA;

    /// <summary>0x2c — item descriptor word B = item actor id. spec: Docs/RE/structs/item.md (item_quad_b).</summary>
    public readonly int ItemQuadB;

    /// <summary>0x30 — item descriptor word C = quantity / stack. UNVERIFIED. spec: item.md (item_quad_c).</summary>
    public readonly int ItemQuadC;

    /// <summary>0x34 — item descriptor word D. UNVERIFIED. spec: Docs/RE/structs/item.md (item_quad_d).</summary>
    public readonly int ItemQuadD;

    /// <summary>0x00 — 4-byte packet prefix. spec: Docs/RE/structs/item.md (header).</summary>
    [InlineArray(4)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    /// <summary>0x13 — 13-byte unmapped gap to 0x20. spec: Docs/RE/structs/item.md (gap_13).</summary>
    [InlineArray(13)]
    public struct Gap13Buffer
    {
        private byte _element0;
    }
}