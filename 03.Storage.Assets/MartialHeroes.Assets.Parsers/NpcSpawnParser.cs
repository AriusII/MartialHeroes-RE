using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>.arr</c> NPC/monster spawn array files.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/npc_spawns.md
/// ZERO rendering/engine dependencies.
/// File has no header. Record count = floor(file_size / 28).
/// spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
/// </remarks>
public static class NpcSpawnParser
{
    // Record stride: 28 bytes (0x1C).
    // spec: Docs/RE/formats/npc_spawns.md §Record layout — 28 bytes per record: CONFIRMED.
    private const int RecordStride = 28;

    /// <summary>
    /// Parses the raw bytes of a <c>.arr</c> spawn file.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Decoded spawn array.</returns>
    /// <remarks>
    /// Any trailing bytes that do not fill a complete 28-byte record are silently ignored,
    /// consistent with runtime behaviour.
    /// spec: Docs/RE/formats/npc_spawns.md §Container structure — floor(file_size/28): CONFIRMED.
    /// </remarks>
    public static NpcSpawnArray Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static NpcSpawnArray Parse(ReadOnlySpan<byte> span)
    {
        // record_count = floor(file_size / 28). Trailing bytes ignored.
        // spec: Docs/RE/formats/npc_spawns.md §Container structure: CONFIRMED.
        int recordCount = span.Length / RecordStride;
        var records = new NpcSpawnRecord[recordCount];

        for (int i = 0; i < recordCount; i++)
        {
            int offset = i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, RecordStride);

            // mob_id u16le @ +0.
            // spec: Docs/RE/formats/npc_spawns.md — mob_id u16 @ +0: CONFIRMED.
            ushort mobId = BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]);

            // field_02 u16le @ +2. Purpose unknown; value 67 in all examined samples.
            // spec: Docs/RE/formats/npc_spawns.md — field_02 u16 @ +2: UNVERIFIED.
            ushort field02 = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // world_x f32le @ +4.
            // spec: Docs/RE/formats/npc_spawns.md — world_x f32 @ +4: CONFIRMED.
            float worldX = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // world_z f32le @ +8.
            // spec: Docs/RE/formats/npc_spawns.md — world_z f32 @ +8: CONFIRMED.
            float worldZ = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            // rotation_y f32le @ +12. Yaw in radians, sample-inference only.
            // spec: Docs/RE/formats/npc_spawns.md — rotation_y f32 @ +12: PARTIAL.
            float rotationY = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);

            // spawn_type u32le @ +16.
            // spec: Docs/RE/formats/npc_spawns.md — spawn_type u32 @ +16: CONFIRMED.
            uint spawnType = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);

            // unknown_20 u32le @ +20. Always zero in observed samples.
            // spec: Docs/RE/formats/npc_spawns.md — unknown_20 u32 @ +20: UNVERIFIED.
            uint unknown20 = BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]);

            // unknown_24 u32le @ +24. Always zero in observed samples.
            // spec: Docs/RE/formats/npc_spawns.md — unknown_24 u32 @ +24: UNVERIFIED.
            uint unknown24 = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);

            records[i] = new NpcSpawnRecord
            {
                MobId = mobId,
                Field02 = field02,
                WorldX = worldX,
                WorldZ = worldZ,
                RotationY = rotationY,
                SpawnType = spawnType,
                Unknown20 = unknown20,
                Unknown24 = unknown24,
            };
        }

        return new NpcSpawnArray { Records = records };
    }
}
