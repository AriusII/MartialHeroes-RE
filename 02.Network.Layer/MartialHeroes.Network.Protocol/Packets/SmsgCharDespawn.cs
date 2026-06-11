// spec: Docs/RE/packets/5-0_char_despawn.yaml — opcode 5/0 (0x50000), 12-byte fixed block.
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. Do NOT treat byte offsets here as ground truth.

using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/0 — server tells the client an actor has left the world. Fixed 12-byte payload block.
/// spec: Docs/RE/packets/5-0_char_despawn.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
[PacketOpcode(5, 0)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharDespawn
{
    /// <summary>Packed opcode 0x50000 (5/0). spec: packets/5-0_char_despawn.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgCharDespawn;

    /// <summary>Declared wire size in bytes. spec: packets/5-0_char_despawn.yaml (size: 12).</summary>
    public const int WireSize = 12;

    /// <summary>
    /// 0x00 — actor category, read as a full LE u32 here (low byte is the real sort: 1=PC, 2=Mob,
    /// 3=NPC). spec: packets/5-0_char_despawn.yaml.
    /// </summary>
    public readonly uint Sort;

    /// <summary>0x04 — actor id (LE u32). spec: packets/5-0_char_despawn.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x08 — flags; bit0 =&gt; play a "left" SFX + chat line. spec: packets/5-0_char_despawn.yaml.</summary>
    public readonly byte Flags;

    /// <summary>0x09 — padding to 12 bytes. spec: packets/5-0_char_despawn.yaml (Pad0: bytes[3]).</summary>
    private readonly byte _pad0;
    private readonly byte _pad1;
    private readonly byte _pad2;

    /// <summary>Typed view over the low byte of <see cref="Sort"/>. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)(byte)Sort;
}
