// spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml — opcode 5/1 (0x50001), 912-byte fixed block.
// SpawnDescriptor sub-field layout: Docs/RE/structs/spawn_descriptor.md (decode is a
// Domain/Application concern; here it is an opaque 880-byte buffer).
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The per-byte meaning of the 20-byte trailer is UNKNOWN per the spec.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

/// <summary>
///     5/1 — server pushes an extended actor spawn (player, mob/NPC, or ground item): a 12-byte prefix,
///     an embedded 880-byte SpawnDescriptor record, and a 20-byte trailer of visual/combat flags.
///     Fixed 912-byte payload block.
///     spec: Docs/RE/packets/5-1_actor_spawn_extended.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
/// <remarks>
///     The 880-byte <see cref="SpawnDescriptor" /> is intentionally opaque on this layer: its internal
///     fields (name, vitals, world X/Z, class, etc.) are documented in Docs/RE/structs/spawn_descriptor.md
///     and are decoded by the Domain/Application layer, not by Network.Protocol.
/// </remarks>
[PacketOpcode(5, 1)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorSpawnExtended
{
    /// <summary>Packed opcode 0x50001 (5/1). spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    public const uint OpcodeId = Opcodes.SmsgActorSpawnExtended;

    /// <summary>Declared wire size in bytes. spec: packets/5-1_actor_spawn_extended.yaml (size: 912).</summary>
    public const int WireSize = 912;

    /// <summary>0x000 — actor sort: 1=player, 2=mob/NPC, 3=ground item. HIGH CONFIDENCE. spec: same.</summary>
    public readonly byte Sort;

    /// <summary>0x001 — alignment padding to the next dword. spec: same (Pad0: bytes[3]).</summary>
    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    /// <summary>0x004 — actor id (LE u32). spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>0x008 — title / state byte. spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly byte TitleState;

    /// <summary>0x009 — title slot / secondary flag. spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly byte TitleSlot;

    /// <summary>0x00a — guild / relation flag. spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    public readonly byte RelationFlag;

    /// <summary>0x00b — padding byte. spec: same (Pad1: u8).</summary>
    private readonly byte _pad1;

    /// <summary>
    ///     0x00c — embedded 880-byte SpawnDescriptor (opaque here; decode per structs/spawn_descriptor.md).
    ///     spec: packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    public readonly SpawnDescriptorBuffer SpawnDescriptor;

    /// <summary>0x37c — 20-byte trailer: combat flag + visual/stealth bytes. spec: same (Trailer: bytes[20]).</summary>
    public readonly TrailerBuffer Trailer;

    /// <summary>Typed view over <see cref="Sort" />. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)Sort;

    /// <summary>
    ///     0x00c — opaque 880-byte (0x370) SpawnDescriptor record. Sub-fields per
    ///     Docs/RE/structs/spawn_descriptor.md. spec: packets/5-1_actor_spawn_extended.yaml.
    /// </summary>
    [InlineArray(880)]
    public struct SpawnDescriptorBuffer
    {
        private byte _element0;
    }

    /// <summary>0x37c — 20-byte trailer. spec: packets/5-1_actor_spawn_extended.yaml.</summary>
    [InlineArray(20)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}