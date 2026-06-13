using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/mapNNN/regiontableNNN.bin</c> — per-area sub-zone label table.
/// Flat array of 32-byte records; no header.
/// Known sample: 52 records per area (1 664 bytes).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §7.2 regiontableNNN.bin: SAMPLE-VERIFIED (stride and name field);
/// coordinate fields: PLAUSIBLE.
/// REGIONTABLE_RECORD_BYTES = 32.
/// spec: Docs/RE/formats/misc_data.md §7.2 — "flat array of fixed 32-byte records; no header": SAMPLE-VERIFIED.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class RegionTableParser
{
    // REGIONTABLE_RECORD_BYTES = 32 (0x20).
    // spec: Docs/RE/formats/misc_data.md §7.2 — "stride 32 bytes": SAMPLE-VERIFIED.
    private const int RecordStride = 32; // 0x20

    static RegionTableParser()
    {
        // Register CP949 provider once for the process (idempotent).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Parses a <c>regiontableNNN.bin</c> file.
    /// Record count = file_size / 32 (must be exact multiple).
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Array of sub-zone records in on-disk order.</returns>
    /// <exception cref="InvalidDataException">Buffer length is not a multiple of 32.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §7.2 — "file_size / 32 = 52 records (1 664 bytes sample)": SAMPLE-VERIFIED.
    /// Note: some records may carry garbage coordinate values at offsets 0x00/0x04 (two-sub-type issue);
    /// validate CenterX/CenterZ against the area bounding box before use.
    /// spec: Docs/RE/formats/misc_data.md §7.2 — "Two sub-types under one stride (open)": UNKNOWN discriminator.
    /// </remarks>
    public static RegionTableRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // spec: Docs/RE/formats/misc_data.md §7.2 — "file size must be exact multiple of 32": SAMPLE-VERIFIED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"regiontable*.bin parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (REGIONTABLE_RECORD_BYTES). " +
                "spec: Docs/RE/formats/misc_data.md §7.2.");

        int count = span.Length / RecordStride;
        var results = new RegionTableRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (int i = 0; i < count; i++)
        {
            int recBase = i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, RecordStride);

            // center_x f32LE @ 0x00. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.2 — "center_x f32 @ 0x00: PLAUSIBLE".
            float centerX = BinaryPrimitives.ReadSingleLittleEndian(rec[0x00..]);

            // center_z f32LE @ 0x04. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.2 — "center_z f32 @ 0x04: PLAUSIBLE".
            float centerZ = BinaryPrimitives.ReadSingleLittleEndian(rec[0x04..]);

            // unknown_0x08 u8[8] @ 0x08. UNKNOWN. Zero in all observed records.
            // spec: Docs/RE/formats/misc_data.md §7.2 — "unknown_0x08 u8[8] @ 0x08: UNKNOWN".
            var unknown08 = data.Slice(recBase + 0x08, 8);

            // sub_zone_name char[16] CP949 @ 0x10. PLAUSIBLE.
            // spec: Docs/RE/formats/misc_data.md §7.2 — "sub_zone_name char[16] CP949 @ 0x10: PLAUSIBLE".
            string subZoneName = ReadNullTerminatedCp949(rec.Slice(0x10, 16), cp949);

            results[i] = new RegionTableRecord
            {
                CenterX = centerX,
                CenterZ = centerZ,
                Unknown0x08 = unknown08,
                SubZoneName = subZoneName,
            };
        }

        return results;
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        int len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}