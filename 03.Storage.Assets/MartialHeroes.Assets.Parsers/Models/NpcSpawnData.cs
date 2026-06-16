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
    /// Field at offset +2. INERT — present but unconsumed by the spawn loader.
    /// No runtime consumer accesses this offset; retained for stride purposes only.
    /// spec: Docs/RE/formats/npc_spawns.md — field_02 u16 @ +2: CONFIRMED inert.
    /// </summary>
    public required ushort Field02Inert { get; init; }

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
    /// Facing / orientation value in radians (base orientation stored on disk).
    /// The runtime applies <c>π/2 − Facing</c> as the entity's on-screen facing;
    /// the stored value is *subtracted from* a quarter-turn, NOT simply added to it.
    /// Simply adding π/2 would mirror the orientation.
    /// Applied facing = <c>Math.PI / 2 - Facing</c>.
    /// spec: Docs/RE/formats/npc_spawns.md — facing f32 @ +12: CONFIRMED.
    /// spec: Docs/RE/formats/npc_spawns.md §Record layout — "runtime applies π/2 − value": sample-verified.
    /// </summary>
    public required float Facing { get; init; }

    /// <summary>
    /// The entity's on-screen facing angle in radians, after applying the runtime transform.
    /// The runtime computes <c>π/2 − Facing</c> (NOT <c>π/2 + Facing</c>).
    /// Simply adding π/2 would mirror the orientation.
    /// Engine consumers MUST use this property instead of <see cref="Facing"/> directly.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/npc_spawns.md §Record layout — "runtime applies π/2 − value": sample-verified.
    /// </remarks>
    public float AppliedFacing => MathF.PI / 2f - Facing;

    /// <summary>
    /// Spawn-group link ID and spawn-type modifier.
    /// Value 7 triggers elite/boss modifier (10% bonus multiplier).
    /// spec: Docs/RE/formats/npc_spawns.md — spawn_type u32 @ +16: CONFIRMED.
    /// </summary>
    public required uint SpawnType { get; init; }

    /// <summary>
    /// Field at offset +20. INERT — present but unconsumed by the spawn loader.
    /// No runtime consumer accesses this offset; retained for stride purposes only.
    /// spec: Docs/RE/formats/npc_spawns.md — field_20 u32 @ +20: CONFIRMED inert.
    /// </summary>
    public required uint Field20Inert { get; init; }

    /// <summary>
    /// Field at offset +24. INERT — present but unconsumed by the spawn loader.
    /// No runtime consumer accesses this offset; retained for stride purposes only.
    /// spec: Docs/RE/formats/npc_spawns.md — field_24 u32 @ +24: CONFIRMED inert.
    /// </summary>
    public required uint Field24Inert { get; init; }
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