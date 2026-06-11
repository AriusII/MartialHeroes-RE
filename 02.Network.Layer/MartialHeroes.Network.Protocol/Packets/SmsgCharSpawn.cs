// spec: Docs/RE/packets/5-3_char_spawn.yaml — opcode 5/3 (0x50003), 908-byte fixed block.
// SpawnDescriptor sub-field layout: Docs/RE/structs/actor.md (decode is a Domain/Application
// concern; here it is an opaque 880-byte buffer).
//
// !!! CAPTURE-UNVERIFIED STATIC LAYOUT !!!
// Every offset/size below is a static inference (capture_verified: false in the spec). The
// (major:minor) routing is dispatch-table-confirmed; the field layout is a hypothesis until a
// live capture confirms it. The world-coordinate offset and trailer contents are UNKNOWN per spec.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

/// <summary>
/// 5/3 — server spawns an actor into the world; the payload carries an embedded 880-byte
/// SpawnDescriptor record plus a 20-byte trailer. Fixed 908-byte payload block.
/// spec: Docs/RE/packets/5-3_char_spawn.yaml. CAPTURE-UNVERIFIED layout.
/// </summary>
/// <remarks>
/// The 880-byte <see cref="SpawnDescriptor"/> is intentionally opaque on this layer: its internal
/// fields (name, vitals, world X/Z, class, etc.) are documented in Docs/RE/structs/actor.md and
/// are decoded by the Domain/Application layer, not by Network.Protocol.
/// </remarks>
[PacketOpcode(5, 3)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharSpawn
{
    /// <summary>Packed opcode 0x50003 (5/3). spec: packets/5-3_char_spawn.yaml.</summary>
    public const uint OpcodeId = Opcodes.Opcodes.SmsgCharSpawn;

    /// <summary>Declared wire size in bytes. spec: packets/5-3_char_spawn.yaml (size: 908).</summary>
    public const int WireSize = 908;

    /// <summary>0x000 — actor category (1=PC, 2=Mob/NPC); low byte is the real sort. spec: same.</summary>
    public readonly uint Sort;

    /// <summary>0x004 — actor id (LE u32). spec: packets/5-3_char_spawn.yaml.</summary>
    public readonly uint ActorId;

    /// <summary>
    /// 0x008 — embedded 880-byte SpawnDescriptor (opaque here; decode per structs/actor.md).
    /// spec: packets/5-3_char_spawn.yaml.
    /// </summary>
    public readonly SpawnDescriptorBuffer SpawnDescriptor;

    /// <summary>0x378 — 20-byte trailer; purpose unconfirmed. spec: same (Trailer: bytes[20]).</summary>
    public readonly TrailerBuffer Trailer;

    /// <summary>Typed view over the low byte of <see cref="Sort"/>. spec: Docs/RE/structs/actor.md.</summary>
    public ActorSort SortKind => (ActorSort)(byte)Sort;

    /// <summary>
    /// 0x008 — opaque 880-byte (0x370) SpawnDescriptor record. Sub-fields per Docs/RE/structs/actor.md.
    /// spec: packets/5-3_char_spawn.yaml.
    /// </summary>
    [InlineArray(880)]
    public struct SpawnDescriptorBuffer
    {
        private byte _element0;
    }

    /// <summary>0x378 — 20-byte trailer. spec: packets/5-3_char_spawn.yaml.</summary>
    [InlineArray(20)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}
