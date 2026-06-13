using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/quests.scr</c> — quest template catalogue.
/// Sparse flat array: 488 total slots of 3720 bytes each; 122 slots are occupied.
/// A slot is empty when its leading <c>quest_id</c> (u16 @ 0x000) is 0.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §2.17.1 quests.scr: SAMPLE-VERIFIED.
/// QUESTS_SCR_RECORD_BYTES = 3720 (0xE88). 488 slots; 122 occupied.
/// spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride 3720 bytes (0xE88), 488 slots, 122 occupied": SAMPLE-VERIFIED.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class QuestsScrParser
{
    // QUESTS_SCR_RECORD_BYTES = 3720 (0xE88).
    // spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride: 3720 bytes (0xE88)": SAMPLE-VERIFIED.
    private const int RecordStride = 3720; // 0xE88

    // Name buffer: offset 0x002, ends by ~0x03F (≤62 bytes).
    // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_name char[] @ 0x002, buffer ends ~0x3F": SAMPLE-VERIFIED.
    private const int NameOffset = 0x002;
    private const int NameWidth = 62; // conservative upper bound

    static QuestsScrParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Parses <c>data/script/quests.scr</c> and returns only the <b>occupied</b> slots
    /// (i.e. records with <c>quest_id != 0</c>).
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>
    /// Array of occupied quest records. Known: 122 of 488 slots are occupied.
    /// </returns>
    /// <exception cref="InvalidDataException">Buffer length is not a multiple of 3720.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/config_tables.md §2.17.1 — "sparse flat array, quest_id 0 = empty slot": SAMPLE-VERIFIED.
    /// spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_id u16 @ 0x000: SAMPLE-VERIFIED".
    /// All strings CP949, null-terminated.
    /// spec: Docs/RE/formats/config_tables.md §2.1 — "All strings CP949 / EUC-KR, null-terminated": CONFIRMED.
    /// </remarks>
    public static QuestScrRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "record count = file_size / 3720": SAMPLE-VERIFIED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"quests.scr parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {RecordStride} (QUESTS_SCR_RECORD_BYTES). " +
                "spec: Docs/RE/formats/config_tables.md §2.17.1.");

        int totalSlots = span.Length / RecordStride;
        var cp949 = Encoding.GetEncoding(949);

        // Pre-size list for the expected 122 occupied slots (avoids repeated realloc).
        var results = new List<QuestScrRecord>(Math.Min(totalSlots, 128));

        for (int i = 0; i < totalSlots; i++)
        {
            int recBase = i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, RecordStride);

            // quest_id u16LE @ 0x000. 0 = empty slot (skip). SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_id u16 @ 0x000 (1..617; 0=empty)": SAMPLE-VERIFIED.
            ushort questId = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x000..]);
            if (questId == 0)
                continue; // empty slot — spec says skip

            // quest_name CP949 @ 0x002. Null-terminated within ~62-byte buffer. SAMPLE-VERIFIED.
            // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_name char[] @ 0x002, ends ~0x3F": SAMPLE-VERIFIED.
            string questName = ReadNullTerminatedCp949(rec.Slice(NameOffset, NameWidth), cp949);

            results.Add(new QuestScrRecord
            {
                QuestId = questId,
                QuestName = questName,
                Raw = data.Slice(recBase, RecordStride),
            });
        }

        return results.ToArray();
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        int len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        // 0xCC = MSVC debug-stack sentinel, acts as padding after NUL in some records.
        // spec: Docs/RE/formats/config_tables.md §2.1 — "0xCC MSVC debug fill after NUL": CONFIRMED.
        int ccPos = field[..len].IndexOf((byte)0xCC);
        if (ccPos >= 0) len = ccPos;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}