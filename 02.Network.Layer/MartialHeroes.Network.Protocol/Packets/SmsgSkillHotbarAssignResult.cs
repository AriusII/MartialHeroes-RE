// spec: Docs/RE/structs/skill.md — "SkillHotbarAssignResult — 24-byte wire packet (opcode 4/41)".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Field widths sum to 24 per skill.md. The 4/41 routing is dispatch-table-confirmed (opcodes.md);
// Gate is CONFIRMED, the remaining fields are LIKELY per skill.md.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 4/41 — result of a client-initiated hotbar assignment. Fixed 24-byte block. Gate==1 refreshes the
/// hotbar + skill UI; Gate==0 clears the slot and shows the error string.
/// spec: Docs/RE/structs/skill.md ("SkillHotbarAssignResult"); opcode 4/41 per Docs/RE/opcodes.md.
/// CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(4, 41)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillHotbarAssignResult
{
    /// <summary>Packed opcode 0x40029 (4/41). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgSkillHotbarAssignResult;

    /// <summary>Declared wire size in bytes. spec: Docs/RE/structs/skill.md (24-byte packet).</summary>
    public const int WireSize = 24;

    /// <summary>0x00 — packet prefix (value 1 expected). spec: Docs/RE/structs/skill.md (header).</summary>
    public readonly uint Header;

    /// <summary>0x04 — actor id. spec: Docs/RE/structs/skill.md (actor_id).</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — gate: 1 = apply/ok, 0 = error. CONFIRMED. spec: Docs/RE/structs/skill.md (gate).</summary>
    public readonly byte Gate;

    /// <summary>0x09 — error reason (1..8) → localized strings. spec: Docs/RE/structs/skill.md (result_code).</summary>
    public readonly byte ResultCode;

    /// <summary>0x0a — alignment padding. spec: Docs/RE/structs/skill.md (pad: char[2]).</summary>
    private readonly byte _pad0;

    private readonly byte _pad1;

    /// <summary>0x0c — echo of the requested hotbar slot. spec: Docs/RE/structs/skill.md (hotbar_slot_echo).</summary>
    public readonly int HotbarSlotEcho;

    /// <summary>0x10 — echo of the requested skill id. spec: Docs/RE/structs/skill.md (skill_id_echo).</summary>
    public readonly int SkillIdEcho;

    /// <summary>0x14 — remaining skill points after assignment. spec: Docs/RE/structs/skill.md (skill_point_pool).</summary>
    public readonly uint SkillPointPool;
}
