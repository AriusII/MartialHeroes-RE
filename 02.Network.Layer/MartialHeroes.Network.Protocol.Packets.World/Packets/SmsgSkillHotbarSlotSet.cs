// spec: Docs/RE/structs/skill.md — "SkillHotbarSlotSet — 20-byte wire packet (opcode 5/33)".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Field widths sum to 20 per skill.md. The 5/33 routing is dispatch-table-confirmed (opcodes.md);
// HotbarSlot, SkillId, and SkillPoints are CONFIRMED in skill.md; the (sort, actor_id) order is
// LIKELY. The min-fixed-payload of 20 (0x14) agrees with handlers.md §4.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/33 — authoritative server overwrite of one skill-hotbar slot for the local player. Fixed 20-byte
///     block. Writes an 8-byte hotbar entry {SkillId, SkillPoints} at offset 8 * HotbarSlot in the 240-slot
///     hotbar table. spec: Docs/RE/structs/skill.md ("SkillHotbarSlotSet"); opcode 5/33 per opcodes.md.
///     CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 33)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillHotbarSlotSet
{
    /// <summary>Packed opcode 0x50021 (5/33). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.SmsgSkillHotbarSlotSet;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/structs/skill.md (20-byte packet).</summary>
    public const int WireSize = 20;

    /// <summary>Number of hotbar slots; HotbarSlot must be &lt; this. spec: Docs/RE/structs/skill.md (240 / 0xF0).</summary>
    public const int HotbarSlotCount = 240;

    /// <summary>0x00 — actor sort (category in the low byte). spec: Docs/RE/structs/skill.md (sort).</summary>
    public readonly int Sort;

    /// <summary>0x04 — actor id (identity check vs. local player). spec: Docs/RE/structs/skill.md (actor_id).</summary>
    public readonly int ActorId;

    /// <summary>0x08 — hotbar slot index (0..239). CONFIRMED. spec: Docs/RE/structs/skill.md (hotbar_slot).</summary>
    public readonly byte HotbarSlot;

    /// <summary>0x09 — alignment padding. spec: Docs/RE/structs/skill.md (pad: char[3]).</summary>
    private readonly byte _pad0;

    private readonly byte _pad1;
    private readonly byte _pad2;

    /// <summary>0x0c — skill id to assign to this slot. CONFIRMED. spec: Docs/RE/structs/skill.md (skill_id).</summary>
    public readonly int SkillId;

    /// <summary>0x10 — skill point allocation / rank. CONFIRMED. spec: Docs/RE/structs/skill.md (skill_points).</summary>
    public readonly short SkillPoints;

    /// <summary>0x12 — padding to 20 bytes. spec: Docs/RE/structs/skill.md (pad_end: char[2]).</summary>
    private readonly byte _padEnd0;

    private readonly byte _padEnd1;

    /// <summary>Typed view over the low byte of <see cref="Sort" />. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)(byte)Sort;
}