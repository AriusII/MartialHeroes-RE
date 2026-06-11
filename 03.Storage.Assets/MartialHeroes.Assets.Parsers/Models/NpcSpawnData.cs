namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One NPC/monster spawn record from a <c>.arr</c> file.
/// Record stride: 28 bytes (0x1C).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/npc_spawns.md §Record layout — 28 bytes per record: CONFIRMED.
/// File has no header; record count = floor(file_size / 28).
/// spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
/// </remarks>
public sealed class NpcSpawnRecord
{
    /// <summary>
    /// NPC / mob template identifier (primary key).
    /// spec: Docs/RE/formats/npc_spawns.md — mob_id u16 @ +0: CONFIRMED.
    /// </summary>
    public required ushort MobId { get; init; }

    /// <summary>
    /// Field at offset +2. Purpose unknown. Observed value: 67.
    /// spec: Docs/RE/formats/npc_spawns.md — field_02 u16 @ +2: UNVERIFIED.
    /// </summary>
    public required ushort Field02 { get; init; }

    /// <summary>
    /// World-space X coordinate.
    /// spec: Docs/RE/formats/npc_spawns.md — world_x f32 @ +4: CONFIRMED.
    /// </summary>
    public required float WorldX { get; init; }

    /// <summary>
    /// World-space Z coordinate.
    /// Y (height) is absent; the terrain system resolves it at runtime.
    /// spec: Docs/RE/formats/npc_spawns.md — world_z f32 @ +8: CONFIRMED.
    /// </summary>
    public required float WorldZ { get; init; }

    /// <summary>
    /// Yaw / facing angle in radians (Y-axis rotation). Inference only — no runtime reader confirmed.
    /// spec: Docs/RE/formats/npc_spawns.md — rotation_y f32 @ +12: PARTIAL (sample inference only).
    /// </summary>
    public required float RotationY { get; init; }

    /// <summary>
    /// Spawn-group link ID and spawn-type modifier.
    /// Value 7 triggers elite/boss modifier.
    /// spec: Docs/RE/formats/npc_spawns.md — spawn_type u32 @ +16: CONFIRMED.
    /// </summary>
    public required uint SpawnType { get; init; }

    /// <summary>
    /// Unknown field at +20. Always zero in observed samples.
    /// Candidates: respawn delay (seconds), max simultaneous live count.
    /// spec: Docs/RE/formats/npc_spawns.md — unknown_20 u32 @ +20: UNVERIFIED.
    /// </summary>
    public required uint Unknown20 { get; init; }

    /// <summary>
    /// Unknown field at +24. Always zero in observed samples.
    /// Candidates: spawn radius, group size, reserved padding.
    /// spec: Docs/RE/formats/npc_spawns.md — unknown_24 u32 @ +24: UNVERIFIED.
    /// </summary>
    public required uint Unknown24 { get; init; }
}

/// <summary>
/// Decoded result of a <c>.arr</c> NPC/monster spawn array file.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
/// No file header; record count = floor(file_size / 28). Any trailing partial record is ignored.
/// At runtime, a sentinel null-record is prepended in memory (slot 0); this file returns
/// only the on-disk records (slots 1..N).
/// </remarks>
public sealed class NpcSpawnArray
{
    /// <summary>
    /// Spawn records in on-disk order.
    /// spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
    /// </summary>
    public required NpcSpawnRecord[] Records { get; init; }
}