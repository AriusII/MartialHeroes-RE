namespace MartialHeroes.Assets.Parsers.World.Models;

/// <summary>
///     One monster spawn record decoded from a <c>mob{NNN}.arr</c> file.
///     Record stride: 20 bytes (0x14).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/npc_spawns.md §Companion formats with NO client loader —
///     20-byte mob.arr record layout: MobId u16 @0, pad u16 @2,
///     WorldX f32 @4, WorldZ f32 @8, FieldC f32 @12, Field10 f32 @16.
///     The file has no header; record_count = floor(file_size / 20).
///     Distinct from the 28-byte NPC format parsed by <see cref="NpcSpawnArray" />
///     (spec: Docs/RE/formats/npc_spawns.md — "Record size: 28 bytes (0x1C): CONFIRMED").
/// </remarks>
public sealed class MobSpawnRecord
{
    /// <summary>
    ///     Monster template identifier.  Primary key used to look up the mob's skin chain via
    ///     <c>actormotion.txt</c>: actormotion row where col1 == MobId gives col2 = skin_class.
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr u16 MobId @0: sample-verified.
    /// </summary>
    public required ushort MobId { get; init; }

    /// <summary>
    ///     Opaque content-tool field at offset +2.
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr has no client loader; semantics pending.
    /// </summary>
    public required ushort Pad { get; init; }

    /// <summary>
    ///     World-space X coordinate (horizontal plane).
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr f32 WorldX @4: sample-verified.
    /// </summary>
    public required float WorldX { get; init; }

    /// <summary>
    ///     World-space Z coordinate (horizontal plane).  Y is absent; the terrain system resolves it.
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr f32 WorldZ @8: sample-verified.
    /// </summary>
    public required float WorldZ { get; init; }

    /// <summary>
    ///     Field at offset +12. Semantic out-of-client-scope / DBG-pending.
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr has no client loader.
    /// </summary>
    public required float FieldC { get; init; }

    /// <summary>
    ///     Field at offset +16. Semantic out-of-client-scope / DBG-pending.
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr has no client loader.
    /// </summary>
    public required float Field10 { get; init; }
}