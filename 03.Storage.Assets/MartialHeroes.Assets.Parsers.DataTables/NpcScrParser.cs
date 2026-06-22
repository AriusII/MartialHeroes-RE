using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parser for <c>data/script/npc.scr</c> — NPC description-text table.
///     Flat array of 404-byte records; no header. 2510 records.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/config_tables.md §2.17.3 npc.scr: SAMPLE-VERIFIED.
///     NPC_SCR_RECORD_BYTES = 404 (0x194).
///     spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404 bytes (0x194), 2510 records": SAMPLE-VERIFIED.
///     This file is DISTINCT from <c>npcs.scr</c> (stride 1916; §2.10); npc.scr holds description paragraphs.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class NpcScrParser
{
    // NPC_SCR_RECORD_BYTES = 404 (0x194).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride: 404 bytes (0x194)": SAMPLE-VERIFIED.
    private const int RecordStride = 404; // 0x194

    // Paragraph buffer boundaries (offsets and widths from spec).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 §npc.scr layout table — CONFIRMED (two-witness).
    // Layout: 20-byte header followed by six 64-byte CP949 string fields.
    // Field 0 @ +0x014 (20), field 1 @ +0x054 (84), field 2 @ +0x094 (148), each 64 bytes wide.
    // The create form reads string fields 0/1/2 for class description lines 1/2/3.
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — "offsets +0x14/+0x54/+0x94, 64 bytes each": CONFIRMED.
    private const int Paragraph0Offset = 0x014; // +20 dec — string field 0; spec: §2.17.3 +0x014
    private const int Paragraph0Width = 64; // 64 bytes — spec: §2.17.3 "six 64-byte CP949 string fields"

    private const int
        Paragraph1Offset = 0x054; // +84 dec — string field 1; spec: §2.17.3 +0x054 (was 0x050 — CORRECTED)

    private const int Paragraph1Width = 64; // 64 bytes — spec: §2.17.3

    private const int
        Paragraph2Offset = 0x094; // +148 dec — string field 2; spec: §2.17.3 +0x094 (was 0x090 — CORRECTED)

    private const int Paragraph2Width = 64; // 64 bytes — spec: §2.17.3

    static NpcScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    ///     Parses <c>data/script/npc.scr</c>.
    ///     Record count = file_size / 404 (must be exact multiple).
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Array of NPC description records in on-disk order.</returns>
    /// <exception cref="InvalidDataException">Buffer length is not a multiple of 404.</exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/config_tables.md §2.17.3 — "2510 records": SAMPLE-VERIFIED.
    ///     All strings are CP949, null-terminated.
    ///     spec: Docs/RE/formats/config_tables.md §2.1 — "All strings CP949 / EUC-KR, null-terminated": CONFIRMED.
    /// </remarks>
    public static NpcScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "record count = file_size / 404": SAMPLE-VERIFIED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"npc.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (NPC_SCR_RECORD_BYTES). " +
                "spec: Docs/RE/formats/config_tables.md §2.17.3.");

        var count = span.Length / RecordStride;
        var results = new NpcScrRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            // id u32LE @ 0x000. Sequential 1..2510. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id u32 @ 0x000: SAMPLE-VERIFIED".
            var id = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // id_mirror u32LE @ 0x004. Equals id in all records. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id_mirror u32 @ 0x004: SAMPLE-VERIFIED".
            var idMirror = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x004..]);

            // reserved 8 bytes @ 0x008 — zero in observed records. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "reserved 8 bytes @ 0x008 (zero): SAMPLE-VERIFIED".
            // (not exposed in the model; silently skipped)

            // paragraph_0 (string field 0) CP949 @ +0x014 (20 dec), 64 bytes wide. First class description line.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "string field 0 @ +0x014, 64 bytes: CONFIRMED".
            var para0 = ReadNullTerminatedCp949(rec.Slice(Paragraph0Offset, Paragraph0Width), cp949);

            // paragraph_1 (string field 1) CP949 @ +0x054 (84 dec), 64 bytes wide. Second class description line.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "string field 1 @ +0x054, 64 bytes: CONFIRMED".
            // CORRECTED: was 0x050 (off by 4); spec layout = 20-byte header + six 64-byte fields (0x14,0x54,0x94,0xD4,0x114,0x154).
            var para1 = ReadNullTerminatedCp949(rec.Slice(Paragraph1Offset, Paragraph1Width), cp949);

            // paragraph_2 (string field 2) CP949 @ +0x094 (148 dec), 64 bytes wide. Third class description line.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "string field 2 @ +0x094, 64 bytes: CONFIRMED".
            // CORRECTED: was 0x090 (off by 4); spec layout = 20-byte header + six 64-byte fields (0x14,0x54,0x94,0xD4,0x114,0x154).
            var para2 = ReadNullTerminatedCp949(rec.Slice(Paragraph2Offset, Paragraph2Width), cp949);

            results[i] = new NpcScrRecord
            {
                Id = id,
                IdMirror = idMirror,
                Paragraph0 = para0,
                Paragraph1 = para1,
                Paragraph2 = para2,
                Raw = data.Slice(recBase, RecordStride)
            };
        }

        return results;
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        // Also stop at 0xCC (MSVC debug-stack sentinel used as padding by the binary compiler).
        // spec: Docs/RE/formats/config_tables.md §2.1 — "0xCC MSVC debug-stack sentinel in unused bytes": CONFIRMED.
        var ccPos = field[..len].IndexOf((byte)0xCC);
        if (ccPos >= 0) len = ccPos;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}