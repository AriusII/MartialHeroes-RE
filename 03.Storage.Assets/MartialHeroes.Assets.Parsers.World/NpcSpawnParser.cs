using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

/// <summary>
///     Parser for <c>.arr</c> NPC/monster spawn array files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/npc_spawns.md
///     ZERO rendering/engine dependencies.
///     File has no header. Record count = floor(file_size / 28).
///     spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
/// </remarks>
public static class NpcSpawnParser
{
    // Record stride: 28 bytes (0x1C).
    // spec: Docs/RE/formats/npc_spawns.md §Record layout — 28 bytes per record: CONFIRMED.
    private const int RecordStride = 28;

    /// <summary>
    ///     Parses the raw bytes of a <c>.arr</c> spawn file.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded spawn array.</returns>
    /// <remarks>
    ///     Any trailing bytes that do not fill a complete 28-byte record are silently ignored,
    ///     consistent with runtime behaviour.
    ///     spec: Docs/RE/formats/npc_spawns.md §Container structure — floor(file_size/28): CONFIRMED.
    /// </remarks>
    public static NpcSpawnArray Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})" />
    public static NpcSpawnArray Parse(ReadOnlySpan<byte> span)
    {
        // record_count = floor(file_size / 28). Trailing bytes ignored.
        // spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
        var recordCount = span.Length / RecordStride;
        var records = new NpcSpawnRecord[recordCount];

        for (var i = 0; i < recordCount; i++)
        {
            var offset = i * RecordStride;
            var rec = span.Slice(offset, RecordStride);

            // mob_id u16le @ +0. Primary key resolves mob template.
            // spec: Docs/RE/formats/npc_spawns.md — mob_id u16 @ +0: CONFIRMED.
            var mobId = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            // field_02 u16le @ +2. INERT — present but unconsumed by the spawn loader.
            // No runtime consumer accesses this offset. Read past it to maintain stride.
            // spec: Docs/RE/formats/npc_spawns.md — field_02 u16 @ +2: CONFIRMED inert.
            var field02Inert = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // world_x f32le @ +4.
            // spec: Docs/RE/formats/npc_spawns.md — world_x f32 @ +4: CONFIRMED.
            var worldX = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // world_z f32le @ +8.
            // spec: Docs/RE/formats/npc_spawns.md — world_z f32 @ +8: CONFIRMED.
            var worldZ = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            // facing f32le @ +12. Base orientation in radians.
            // IMPORTANT: the runtime applies π/2 − stored_value as the entity's facing.
            // Applied facing = π/2 − stored_value. Simply adding π/2 would mirror the orientation.
            // spec: Docs/RE/formats/npc_spawns.md — facing f32 @ +12: CONFIRMED.
            // spec: Docs/RE/formats/npc_spawns.md §Record layout — facing: "runtime applies π/2 − value".
            var facing = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);

            // spawn_type u32le @ +16.
            // spec: Docs/RE/formats/npc_spawns.md — spawn_type u32 @ +16: CONFIRMED.
            var spawnType = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            // field_20 u32le @ +20. INERT — present but unconsumed by the spawn loader.
            // No runtime consumer accesses this offset. Read past it to maintain stride.
            // spec: Docs/RE/formats/npc_spawns.md — field_20 u32 @ +20: CONFIRMED inert.
            var field20Inert = BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]);

            // field_24 u32le @ +24. INERT — present but unconsumed by the spawn loader.
            // No runtime consumer accesses this offset. Read past it to maintain stride.
            // spec: Docs/RE/formats/npc_spawns.md — field_24 u32 @ +24: CONFIRMED inert.
            var field24Inert = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);

            records[i] = new NpcSpawnRecord
            {
                MobId = mobId,
                Field02Inert = field02Inert,
                WorldX = worldX,
                WorldZ = worldZ,
                Facing = facing,
                SpawnType = spawnType,
                Field20Inert = field20Inert,
                Field24Inert = field24Inert
            };
        }

        return new NpcSpawnArray { Records = records };
    }
}