// spec: Docs/RE/packets/2-52_use_skill.yaml — opcode 2/52 (0x20034), VARIABLE-LENGTH packet.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it.
//
// THIS IS THE FIXED 24-BYTE HEADER ONLY. The variable tail (two count-prefixed u32 target-id
// arrays, length = (CountA + CountB) * 4 bytes) is NOT modelled as a struct — the caller
// (Client.Application) hand-codes the trailing loop driven by CountA / CountB. Do not add a
// WireSize / size assertion: this packet has no fixed size.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 2/52 — FIXED 24-byte header of the client skill-activate / battle-action request (skill slot +
/// aim). Followed on the wire by two optional count-prefixed u32 target-id arrays which the caller
/// reads by hand (see <see cref="CountA"/> / <see cref="CountB"/>). The server replies with S2C
/// 5/52 ActorSkillAction.
/// spec: Docs/RE/packets/2-52_use_skill.yaml. CAPTURE-UNVERIFIED layout. VARIABLE-LENGTH packet.
/// </summary>
[PacketOpcode(2, 52)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CmsgUseSkillHeader
{
    /// <summary>Packed opcode 0x20034 (2/52). spec: packets/2-52_use_skill.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.CmsgUseSkill;

    /// <summary>
    /// Size of the FIXED header in bytes. Total payload is HeaderSize + (CountA + CountB) * 4.
    /// spec: packets/2-52_use_skill.yaml (24-byte header).
    /// </summary>
    public const int HeaderSize = 24;

    /// <summary>0x00 — skill slot index; 0xFF = basic attack. spec: packets/2-52_use_skill.yaml.</summary>
    public readonly byte SkillSlot;

    /// <summary>0x01 — padding / unused (zeroed in the short form). spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — aim mode selector (LE u32). spec: packets/2-52_use_skill.yaml.</summary>
    public readonly uint AimMode;

    /// <summary>0x08 — aim scale / radius factor (LE f32). spec: packets/2-52_use_skill.yaml.</summary>
    public readonly float AimScale;

    /// <summary>0x0c — aim point X in world coords (LE f32). spec: packets/2-52_use_skill.yaml.</summary>
    public readonly float AimX;

    /// <summary>0x10 — aim point Z in world coords (LE f32). spec: packets/2-52_use_skill.yaml.</summary>
    public readonly float AimZ;

    /// <summary>
    /// 0x14 — number of entries in target array A (each a 4-byte u32 actor id). The array follows
    /// the header on the wire; the caller reads it by hand. spec: packets/2-52_use_skill.yaml.
    /// </summary>
    public readonly ushort CountA;

    /// <summary>
    /// 0x16 — number of entries in target array B (each a 4-byte u32 actor id), appended after
    /// array A. The caller reads it by hand. spec: packets/2-52_use_skill.yaml.
    /// </summary>
    public readonly ushort CountB;
}