namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One monster spawn record decoded from a <c>mob{NNN}.arr</c> file.
/// Record stride: 20 bytes (0x14).
/// </summary>
/// <remarks>
/// spec: MISSION B — 20-byte mob record layout: MobId u16 @0, pad u16 @2,
///       WorldX f32 @4, WorldZ f32 @8, FieldC f32 @12, Field10 f32 @16.
/// The file has no header; record_count = floor(file_size / 20).
/// Distinct from the 28-byte NPC format parsed by <see cref="NpcSpawnArray"/>
/// (spec: Docs/RE/formats/npc_spawns.md — "Record size: 28 bytes (0x1C): CONFIRMED").
/// </remarks>
public sealed class MobSpawnRecord
{
    /// <summary>
    /// Monster template identifier.  Primary key used to look up the mob's skin chain via
    /// <c>actormotion.txt</c>: actormotion row where col1 == MobId gives col2 = skin_class.
    /// spec: MISSION B — u16 MobId @0; Docs/RE/formats/npc_spawns.md — mob_id field: CONFIRMED.
    /// </summary>
    public required ushort MobId { get; init; }

    /// <summary>
    /// Padding / unknown field at offset +2.  Always treated as padding.
    /// spec: MISSION B — u16 pad @2.
    /// </summary>
    public required ushort Pad { get; init; }

    /// <summary>
    /// World-space X coordinate (horizontal plane).
    /// spec: MISSION B — f32 WorldX @4; Docs/RE/formats/npc_spawns.md — world_x: CONFIRMED.
    /// </summary>
    public required float WorldX { get; init; }

    /// <summary>
    /// World-space Z coordinate (horizontal plane).  Y is absent; the terrain system resolves it.
    /// spec: MISSION B — f32 WorldZ @8; Docs/RE/formats/npc_spawns.md — world_z: CONFIRMED.
    /// </summary>
    public required float WorldZ { get; init; }

    /// <summary>
    /// Field at offset +12.  Possibly a rotation angle or additional spawn parameter.
    /// spec: MISSION B — f32 FieldC @12.  Semantic UNVERIFIED.
    /// </summary>
    public required float FieldC { get; init; }

    /// <summary>
    /// Field at offset +16.  Semantic UNVERIFIED.
    /// spec: MISSION B — f32 Field10 @16.
    /// </summary>
    public required float Field10 { get; init; }
}