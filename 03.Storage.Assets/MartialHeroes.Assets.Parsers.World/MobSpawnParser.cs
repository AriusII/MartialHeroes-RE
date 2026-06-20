using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

/// <summary>
///     Mechanical decoder for <c>mob{NNN}.arr</c> content-tool spawn array files.
/// </summary>
/// <remarks>
///     <para>
///         Format: headerless flat array of fixed 20-byte records.
///         <c>record_count = floor(file_size / 20)</c>.
///         Any trailing bytes that do not fill a complete 20-byte record are silently ignored.
///     </para>
///     <para>
///         This is DISTINCT from the 28-byte NPC spawn format (<see cref="NpcSpawnParser" />).
///         The shipped client has no runtime loader for <c>mob*.arr</c>; it is retained here only
///         as a neutral, spec-cited mechanical decoder for VFS diagnostics and chain existence checks.
///         spec: Docs/RE/formats/npc_spawns.md §Companion formats with NO client loader — <c>mob.arr</c>
///         fixed 20-byte records: sample-verified.
///     </para>
///     <para>
///         Record layout (all little-endian, x86 client):
///         <list type="table">
///             <item>
///                 <term>@0  u16</term><description>MobId — monster template identifier</description>
///             </item>
///             <item>
///                 <term>@2  u16</term><description>Pad   — opaque content-tool data</description>
///             </item>
///             <item>
///                 <term>@4  f32</term><description>WorldX — world-space X coordinate</description>
///             </item>
///             <item>
///                 <term>@8  f32</term><description>WorldZ — world-space Z coordinate</description>
///             </item>
///             <item>
///                 <term>@12 f32</term><description>FieldC — semantic out-of-client-scope / DBG-pending</description>
///             </item>
///             <item>
///                 <term>@16 f32</term><description>Field10 — semantic out-of-client-scope / DBG-pending</description>
///             </item>
///         </list>
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class MobSpawnParser
{
    // Record stride: 20 bytes (0x14).
    // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr 20-byte stride: sample-verified.
    private const int RecordStride = 20;

    /// <summary>
    ///     Parses the raw bytes of a <c>mob{NNN}.arr</c> spawn file.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>Array of decoded spawn records.  Empty array when the input is shorter than one record.</returns>
    /// <remarks>
    ///     This decoder preserves every complete 20-byte record in file order. It does not skip
    ///     <c>MobId == 0</c> and does not de-duplicate; the spec only verifies the content-tool
    ///     stride, not runtime semantics.
    ///     spec: Docs/RE/formats/npc_spawns.md §Companion formats — no client loader; semantics pending.
    /// </remarks>
    public static MobSpawnRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})" />
    public static MobSpawnRecord[] Parse(ReadOnlySpan<byte> span)
    {
        // record_count = floor(file_size / 20).  Trailing bytes ignored.
        // spec: Docs/RE/formats/npc_spawns.md §Companion formats — "mob.arr" record_count = floor(file_size / 20).
        var recordCount = span.Length / RecordStride;
        if (recordCount == 0)
            return [];

        var results = new MobSpawnRecord[recordCount];

        for (var i = 0; i < recordCount; i++)
        {
            var offset = i * RecordStride;
            var rec = span.Slice(offset, RecordStride);

            // MobId u16le @ +0.
            // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr u16 MobId @0.
            var mobId = BinaryPrimitives.ReadUInt16LittleEndian(rec[..]);

            // Pad u16le @ +2.
            // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr u16 opaque field @2.
            var pad = BinaryPrimitives.ReadUInt16LittleEndian(rec[2..]);

            // WorldX f32le @ +4.
            // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr f32 WorldX @4: sample-verified.
            var worldX = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // WorldZ f32le @ +8.
            // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr f32 WorldZ @8: sample-verified.
            var worldZ = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            // FieldC f32le @ +12.  Semantic UNVERIFIED.
            // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr field semantics out-of-client-scope.
            var fieldC = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);

            // Field10 f32le @ +16.  Semantic UNVERIFIED.
            // spec: Docs/RE/formats/npc_spawns.md §Companion formats — mob.arr field semantics out-of-client-scope.
            var field10 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]);

            results[i] = new MobSpawnRecord
            {
                MobId = mobId,
                Pad = pad,
                WorldX = worldX,
                WorldZ = worldZ,
                FieldC = fieldC,
                Field10 = field10
            };
        }

        return results;
    }
}