using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/npc.scr</c> — NPC description-text table.
/// Flat array of 404-byte records; no header. 2510 records.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.17.3 npc.scr: SAMPLE-VERIFIED.
/// NPC_SCR_RECORD_BYTES = 404 (0x194).
/// spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride 404 bytes (0x194), 2510 records": SAMPLE-VERIFIED.
/// This file is DISTINCT from <c>npcs.scr</c> (stride 1916; §2.10); npc.scr holds description paragraphs.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class NpcScrParser
{
    // NPC_SCR_RECORD_BYTES = 404 (0x194).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — "stride: 404 bytes (0x194)": SAMPLE-VERIFIED.
    private const int RecordStride = 404; // 0x194

    // Paragraph buffer boundaries (offsets and widths from spec).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — layout table: SAMPLE-VERIFIED.
    private const int Paragraph0Offset = 0x014; // ≤36 bytes
    private const int Paragraph0Width = 60; // conservative; buffer ends before 0x050
    private const int Paragraph1Offset = 0x050; // ≤28 bytes
    private const int Paragraph1Width = 64; // conservative; buffer ends before 0x090
    private const int Paragraph2Offset = 0x090; // ≤28 bytes
    private const int Paragraph2Width = 64; // conservative; buffer ends before 0x0D0

    static NpcScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Parses <c>data/script/npc.scr</c>.
    /// Record count = file_size / 404 (must be exact multiple).
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Array of NPC description records in on-disk order.</returns>
    /// <exception cref="InvalidDataException">Buffer length is not a multiple of 404.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — "2510 records": SAMPLE-VERIFIED.
    /// All strings are CP949, null-terminated.
    /// spec: Docs/RE/formats/config_tables.md §2.1 — "All strings CP949 / EUC-KR, null-terminated": CONFIRMED.
    /// </remarks>
    public static NpcScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.17.3 — "record count = file_size / 404": SAMPLE-VERIFIED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"npc.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (NPC_SCR_RECORD_BYTES). " +
                "spec: Docs/RE/formats/config_tables.md §2.17.3.");

        int count = span.Length / RecordStride;
        var results = new NpcScrRecord[count];
        var cp949 = Encoding.GetEncoding(949);

        for (int i = 0; i < count; i++)
        {
            int recBase = i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, RecordStride);

            // id u32LE @ 0x000. Sequential 1..2510. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id u32 @ 0x000: SAMPLE-VERIFIED".
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x000..]);

            // id_mirror u32LE @ 0x004. Equals id in all records. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "id_mirror u32 @ 0x004: SAMPLE-VERIFIED".
            uint idMirror = BinaryPrimitives.ReadUInt32LittleEndian(rec[0x004..]);

            // reserved 8 bytes @ 0x008 — zero in observed records. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "reserved 8 bytes @ 0x008 (zero): SAMPLE-VERIFIED".
            // (not exposed in the model; silently skipped)

            // paragraph_0 CP949 @ 0x014. First archetype paragraph. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_0 char[] @ 0x014: SAMPLE-VERIFIED".
            string para0 = ReadNullTerminatedCp949(rec.Slice(Paragraph0Offset, Paragraph0Width), cp949);

            // paragraph_1 CP949 @ 0x050. Second paragraph. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_1 char[] @ 0x050: SAMPLE-VERIFIED".
            string para1 = ReadNullTerminatedCp949(rec.Slice(Paragraph1Offset, Paragraph1Width), cp949);

            // paragraph_2 CP949 @ 0x090. Third paragraph. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.3 — "paragraph_2 char[] @ 0x090: SAMPLE-VERIFIED".
            string para2 = ReadNullTerminatedCp949(rec.Slice(Paragraph2Offset, Paragraph2Width), cp949);

            results[i] = new NpcScrRecord
            {
                Id = id,
                IdMirror = idMirror,
                Paragraph0 = para0,
                Paragraph1 = para1,
                Paragraph2 = para2,
                Raw = data.Slice(recBase, RecordStride),
            };
        }

        return results;
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        int len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        // Also stop at 0xCC (MSVC debug-stack sentinel used as padding by the binary compiler).
        // spec: Docs/RE/formats/config_tables.md §2.1 — "0xCC MSVC debug-stack sentinel in unused bytes": CONFIRMED.
        int ccPos = field[..len].IndexOf((byte)0xCC);
        if (ccPos >= 0) len = ccPos;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}