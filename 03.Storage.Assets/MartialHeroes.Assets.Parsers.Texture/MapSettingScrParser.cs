using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

/// <summary>
///     Parser for <c>data/script/mapsetting.scr</c> — zone bounding-box and fog table.
///     Flat array of 84-byte records; no header.
///     Known sample: 52 records (4 368 bytes).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §7.1 mapsetting.scr: SAMPLE-VERIFIED.
///     MAPSETTING_RECORD_BYTES = 84 (0x54).
///     spec: Docs/RE/formats/misc_data.md §7.1 — "flat array of fixed 84-byte records; no header": SAMPLE-VERIFIED.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class MapSettingScrParser
{
    // MAPSETTING_RECORD_BYTES = 84 (0x54). 52 zones expected.
    // spec: Docs/RE/formats/misc_data.md §7.1 — "stride 84 bytes": SAMPLE-VERIFIED.
    private const int RecordStride = 84; // 0x54

    static MapSettingScrParser()
    {
        // Register CP949 provider once for the process (idempotent).
        // spec: Docs/RE/formats/misc_data.md §7.1 — "zone_name CP949": SAMPLE-VERIFIED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    ///     Parses <c>data/script/mapsetting.scr</c>.
    ///     Record count = file_size / 84 (must be exact multiple).
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Array of zone records in on-disk order.</returns>
    /// <exception cref="InvalidDataException">Buffer length is not a multiple of 84.</exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — "file_size / 84 = 52 records (4 368 bytes sample)": SAMPLE-VERIFIED.
    /// </remarks>
    public static MapZoneRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // spec: Docs/RE/formats/misc_data.md §7.1 — "file size must be exact multiple of 84": SAMPLE-VERIFIED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"mapsetting.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (MAPSETTING_RECORD_BYTES). " +
                "spec: Docs/RE/formats/misc_data.md §7.1.");

        var count = span.Length / RecordStride;
        var results = new MapZoneRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            // zone_id i32LE @ 0x00. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "zone_id i32 @ 0x00: SAMPLE-VERIFIED".
            var zoneId = BinaryPrimitives.ReadInt32LittleEndian(rec[..]);

            // zone_name char[36] CP949 @ 0x04. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "zone_name char[36] CP949 @ 0x04: SAMPLE-VERIFIED".
            var zoneName = ReadNullTerminatedCp949(rec.Slice(0x04, 36), cp949);

            // world_min_x i32LE @ 0x28. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "world_min_x i32 @ 0x28: PLAUSIBLE".
            var worldMinX = BinaryPrimitives.ReadInt32LittleEndian(rec[0x28..]);

            // world_min_z i32LE @ 0x2C. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "world_min_z i32 @ 0x2C: PLAUSIBLE".
            var worldMinZ = BinaryPrimitives.ReadInt32LittleEndian(rec[0x2C..]);

            // world_max_x i32LE @ 0x30. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "world_max_x i32 @ 0x30: PLAUSIBLE".
            var worldMaxX = BinaryPrimitives.ReadInt32LittleEndian(rec[0x30..]);

            // world_max_z i32LE @ 0x34. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "world_max_z i32 @ 0x34: PLAUSIBLE".
            var worldMaxZ = BinaryPrimitives.ReadInt32LittleEndian(rec[0x34..]);

            // flags_a i32LE @ 0x38. UNKNOWN. 0x012C0001 in 50/52 records.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "flags_a i32 @ 0x38: UNKNOWN".
            var flagsA = BinaryPrimitives.ReadInt32LittleEndian(rec[0x38..]);

            // flags_b i32LE @ 0x3C. UNKNOWN. Usually 0x00000001.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "flags_b i32 @ 0x3C: UNKNOWN".
            var flagsB = BinaryPrimitives.ReadInt32LittleEndian(rec[0x3C..]);

            // fog_density f32LE @ 0x40. PLAUSIBLE. Observed: 1.30, 1.50, 1.70.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "fog_density f32 @ 0x40: PLAUSIBLE".
            var fogDensity = BinaryPrimitives.ReadSingleLittleEndian(rec[0x40..]);

            // unknown_0x44 i32LE @ 0x44. UNKNOWN. First record=1, all others=0.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "unknown_0x44 i32 @ 0x44: UNKNOWN".
            var unknown44 = BinaryPrimitives.ReadInt32LittleEndian(rec[0x44..]);

            // unknown_0x48 i32LE @ 0x48. UNKNOWN. Typically 0 or -1.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "unknown_0x48 i32 @ 0x48: UNKNOWN".
            var unknown48 = BinaryPrimitives.ReadInt32LittleEndian(rec[0x48..]);

            // unknown_0x4C i32LE @ 0x4C. UNKNOWN. High byte 0x64; low 24 bits vary.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "unknown_0x4C i32 @ 0x4C: UNKNOWN".
            var unknown4C = BinaryPrimitives.ReadInt32LittleEndian(rec[0x4C..]);

            // unknown_0x50 i32LE @ 0x50. UNKNOWN. Always 0 in 52 records.
            // spec: Docs/RE/formats/misc_data.md §7.1 — "unknown_0x50 i32 @ 0x50: UNKNOWN".
            var unknown50 = BinaryPrimitives.ReadInt32LittleEndian(rec[0x50..]);

            results[i] = new MapZoneRecord
            {
                ZoneId = zoneId,
                ZoneName = zoneName,
                WorldMinX = worldMinX,
                WorldMinZ = worldMinZ,
                WorldMaxX = worldMaxX,
                WorldMaxZ = worldMaxZ,
                FlagsA = flagsA,
                FlagsB = flagsB,
                FogDensity = fogDensity,
                Unknown0x44 = unknown44,
                Unknown0x48 = unknown48,
                Unknown0x4C = unknown4C,
                Unknown0x50 = unknown50
            };
        }

        return results;
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        // Scan for NUL terminator; decode only the live bytes.
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}