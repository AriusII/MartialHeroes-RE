// spec: Docs/RE/structs/skill.md — "SkillPointUpdate — variable-length wire packet (opcode 4/150),
// minimum 16 bytes".
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// THIS IS THE FIXED 16-BYTE HEADER ONLY. mode==2 paths read additional level-up data from a runtime
// singleton (NOT part of this packet's fixed prefix); the engineer must not model that tail. The
// 4/150 routing is dispatch-table-confirmed (opcodes.md); Valid/Mode/Value are CONFIRMED, IdKey is
// LIKELY per skill.md. No fixed-total WireSize: this packet is variable-length.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 4/150 — FIXED 16-byte header of the skill-point update. Mode==1 sets the total skill-point pool to
/// Value; Mode==2 is a level-up notice where Value is the new character level (and extra level-up data
/// comes from a runtime singleton, not this packet). The 255 display cap is UI-only; do not clamp the
/// wire value. spec: Docs/RE/structs/skill.md ("SkillPointUpdate"); opcode 4/150 per Docs/RE/opcodes.md.
/// CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(4, 150)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgSkillPointUpdateHeader
{
    /// <summary>Packed opcode 0x40096 (4/150). spec: Docs/RE/opcodes.md.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgSkillPointUpdate;

    /// <summary>Size of the FIXED header in bytes. spec: Docs/RE/structs/skill.md (minimum 16 bytes).</summary>
    public const int HeaderSize = 16;

    /// <summary>0x00 — must equal 1. CONFIRMED. spec: Docs/RE/structs/skill.md (valid).</summary>
    public readonly byte Valid;

    /// <summary>0x01 — alignment padding to 0x04. spec: Docs/RE/structs/skill.md (pad: 3).</summary>
    private readonly byte _pad0;

    private readonly byte _pad1;
    private readonly byte _pad2;

    /// <summary>0x04 — actor-id key, matched against the local player. spec: Docs/RE/structs/skill.md (idkey).</summary>
    public readonly int IdKey;

    /// <summary>0x08 — 1 = set total skill points; 2 = level-up notice. CONFIRMED. spec: skill.md (mode).</summary>
    public readonly uint Mode;

    /// <summary>0x0c — mode==1 → new skill-point pool; mode==2 → new character level. CONFIRMED. spec: skill.md (value).</summary>
    public readonly uint Value;
}