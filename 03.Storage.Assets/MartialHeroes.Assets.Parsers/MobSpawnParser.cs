using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>mob{NNN}.arr</c> monster spawn array files.
/// </summary>
/// <remarks>
/// <para>
/// Format: headerless flat array of fixed 20-byte records.
/// <c>record_count = floor(file_size / 20)</c>.
/// Any trailing bytes that do not fill a complete 20-byte record are silently ignored.
/// </para>
/// <para>
/// This is DISTINCT from the 28-byte NPC spawn format (<see cref="NpcSpawnParser"/>).
/// spec: MISSION B — 20-byte mob record layout.
/// spec: Docs/RE/formats/npc_spawns.md — NPC format stride 28 bytes: CONFIRMED (contrast).
/// </para>
/// <para>
/// Record layout (all little-endian, x86 client):
/// <list type="table">
/// <item><term>@0  u16</term><description>MobId — monster template identifier</description></item>
/// <item><term>@2  u16</term><description>Pad   — padding / unknown</description></item>
/// <item><term>@4  f32</term><description>WorldX — world-space X coordinate</description></item>
/// <item><term>@8  f32</term><description>WorldZ — world-space Z coordinate</description></item>
/// <item><term>@12 f32</term><description>FieldC — unverified (possibly rotation/type)</description></item>
/// <item><term>@16 f32</term><description>Field10 — unverified</description></item>
/// </list>
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class MobSpawnParser
{
    // Record stride: 20 bytes (0x14).
    // spec: MISSION B — 20-byte mob record layout.
    private const int RecordStride = 20;

    /// <summary>
    /// Parses the raw bytes of a <c>mob{NNN}.arr</c> spawn file.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of decoded spawn records.  Empty array when the input is shorter than one record.</returns>
    /// <remarks>
    /// Consecutive records with the same (MobId, WorldX, WorldZ) are de-duplicated: only the
    /// first occurrence is kept.  This reduces visual clutter when stacked spawns share a position.
    /// spec: MISSION B — "Optionally de-dupe consecutive identical (MobId,X,Z)."
    /// </remarks>
    public static MobSpawnRecord[] Parse(ReadOnlyMemory<byte> data)
        => Parse(data.Span);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static MobSpawnRecord[] Parse(ReadOnlySpan<byte> span)
    {
        // record_count = floor(file_size / 20).  Trailing bytes ignored.
        // spec: MISSION B — "count = length/20".
        int recordCount = span.Length / RecordStride;
        if (recordCount == 0)
            return [];

        var results = new List<MobSpawnRecord>(recordCount);

        // De-duplication: track (MobId, WorldX, WorldZ) tuples seen so far so that
        // stacked/duplicate spawn entries collapse to a single visual instance.
        // spec: MISSION B — "Optionally de-dupe consecutive identical (MobId,X,Z)."
        var seen = new HashSet<(ushort, float, float)>(recordCount);

        for (int i = 0; i < recordCount; i++)
        {
            int offset = i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, RecordStride);

            // MobId u16le @ +0.
            // spec: MISSION B — u16 MobId @0.
            ushort mobId = BinaryPrimitives.ReadUInt16LittleEndian(rec[0..]);

            // Pad u16le @ +2.
            // spec: MISSION B — u16 pad @2.
            ushort pad = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // WorldX f32le @ +4.
            // spec: MISSION B — f32 WorldX @4; Docs/RE/formats/npc_spawns.md — world_x: CONFIRMED.
            float worldX = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // WorldZ f32le @ +8.
            // spec: MISSION B — f32 WorldZ @8; Docs/RE/formats/npc_spawns.md — world_z: CONFIRMED.
            float worldZ = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            // FieldC f32le @ +12.  Semantic UNVERIFIED.
            // spec: MISSION B — f32 FieldC @12.
            float fieldC = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);

            // Field10 f32le @ +16.  Semantic UNVERIFIED.
            // spec: MISSION B — f32 Field10 @16.
            float field10 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]);

            // Skip records with MobId == 0 (sentinel / unused entries).
            if (mobId == 0) continue;

            // De-duplicate by (MobId, WorldX, WorldZ).
            // spec: MISSION B — dedupe consecutive identical (MobId,X,Z).
            if (!seen.Add((mobId, worldX, worldZ))) continue;

            results.Add(new MobSpawnRecord
            {
                MobId = mobId,
                Pad = pad,
                WorldX = worldX,
                WorldZ = worldZ,
                FieldC = fieldC,
                Field10 = field10,
            });
        }

        return results.ToArray();
    }
}