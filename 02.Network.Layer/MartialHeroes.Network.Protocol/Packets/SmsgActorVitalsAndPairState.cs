// spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml — opcode 5/53 (0x50035), 32-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The semantics of bytes 0x08/0x09 and the precise vital ordering are
// UNKNOWN per the spec.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/53 — server pushes an actor's current vitals (HP / stamina / a third vital) plus level/state,
/// and a pairing (couple) relationship when Sort marks a paired entity. Fixed 32-byte block.
/// spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 53)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorVitalsAndPairState
{
    /// <summary>Packed opcode 0x50035 (5/53). spec: packets/5-53_actor_vitals_and_pair_state.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgActorVitalsAndPairState;

    /// <summary>Declared wire size in bytes. spec: packets/5-53_actor_vitals_and_pair_state.yaml (size: 32).</summary>
    public const int WireSize = 32;

    /// <summary>0x00 — actor sort; the value 8 is normalised to 1 on read. spec: same.</summary>
    public readonly byte Sort;

    /// <summary>0x01 — alignment padding to the next dword. spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;
    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x04 — target actor id (LE u32). spec: packets/5-53_actor_vitals_and_pair_state.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — read into the record; meaning not decoded. spec: same.</summary>
    public readonly byte Byte08;

    /// <summary>0x09 — read into the record; meaning not decoded. spec: same.</summary>
    public readonly byte Byte09;

    /// <summary>0x0a — level / state byte. spec: packets/5-53_actor_vitals_and_pair_state.yaml.</summary>
    public readonly byte LevelOrState;

    /// <summary>0x0b — secondary state byte. spec: packets/5-53_actor_vitals_and_pair_state.yaml.</summary>
    public readonly byte StateByte;

    /// <summary>0x0c — partner / pair actor id (used when Sort == 2). spec: same.</summary>
    public readonly uint PartnerId;

    /// <summary>0x10 — current HP (LE u32). HIGH CONFIDENCE. spec: same.</summary>
    public readonly uint CurrentHp;

    /// <summary>0x14 — secondary vital (order unconfirmed). spec: same.</summary>
    public readonly uint VitalB;

    /// <summary>0x18 — current stamina (LE u32). HIGH CONFIDENCE. spec: same.</summary>
    public readonly uint Stamina;

    /// <summary>0x1c — third vital (MP / special); mirrored to the local-player global. spec: same.</summary>
    public readonly uint VitalC;

    /// <summary>Typed view over <see cref="Sort"/>. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)Sort;
}
