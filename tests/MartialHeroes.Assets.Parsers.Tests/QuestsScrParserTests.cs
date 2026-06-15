using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="QuestsScrParser"/> (quests.scr — quest template catalogue).
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/config_tables.md §2.17.1 quests.scr: SAMPLE-VERIFIED.
/// </summary>
public sealed class QuestsScrParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    // QUESTS_SCR_RECORD_BYTES = 3720 (0xE88).
    // spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride 3720 bytes (0xE88)": SAMPLE-VERIFIED.
    private const int RecordStride = 3720; // 0xE88

    private static Encoding Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    private static void WriteCp949(byte[] buf, int off, int fieldLen, string text)
    {
        byte[] enc = Cp949().GetBytes(text);
        int copyLen = Math.Min(enc.Length, fieldLen - 1);
        enc.AsSpan(0, copyLen).CopyTo(buf.AsSpan(off));
        buf[off + copyLen] = 0x00;
    }

    /// <summary>
    /// Builds a single 3720-byte quests.scr record.
    /// spec: Docs/RE/formats/config_tables.md §2.17.1 — Record layout: SAMPLE-VERIFIED.
    /// </summary>
    private static byte[] BuildRecord(ushort questId = 1, string questName = "")
    {
        byte[] buf = new byte[RecordStride];

        // quest_id u16LE @ 0x000. 0 = empty slot. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_id u16 @ 0x000 (1..617; 0=empty)": SAMPLE-VERIFIED.
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x000, 2), questId);

        // quest_name CP949 @ 0x002, null-terminated within ~62 bytes. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_name char[] @ 0x002, ends ~0x3F": SAMPLE-VERIFIED.
        if (questName.Length > 0)
            WriteCp949(buf, 0x002, 62, questName);

        return buf;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (byte[] p in parts) total += p.Length;
        byte[] buf = new byte[total];
        int pos = 0;
        foreach (byte[] p in parts)
        {
            p.CopyTo(buf, pos);
            pos += p.Length;
        }
        return buf;
    }

    // =========================================================================
    // 1. Stride validation
    // =========================================================================

    [Fact]
    public void Parse_EmptyBuffer_YieldsEmptyArray()
    {
        // 0 % 3720 == 0 — valid empty file.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "record count = file_size / 3720".
        QuestScrRecord[] result = QuestsScrParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NotMultipleOfStride_ThrowsInvalidDataException()
    {
        // 100 is not divisible by 3720.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride 3720 bytes (QUESTS_SCR_RECORD_BYTES)".
        byte[] buf = new byte[100];
        Assert.Throws<InvalidDataException>(() => QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    // =========================================================================
    // 2. Empty-slot filtering (quest_id == 0 → skip)
    // =========================================================================

    [Fact]
    public void Parse_AllEmptySlots_YieldsEmptyArray()
    {
        // quest_id == 0 means empty slot — these are skipped in the output.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "sparse flat array, quest_id 0 = empty slot": SAMPLE-VERIFIED.
        byte[] buf = new byte[RecordStride * 3]; // 3 zero-filled slots → all empty
        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_EmptySlotMixedWithOccupied_OnlyOccupiedReturned()
    {
        // Sparse array: slot 0 empty, slot 1 occupied, slot 2 empty.
        // Only slot 1 (questId != 0) must be in the output.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "sparse flat array; quest_id 0 = empty": SAMPLE-VERIFIED.
        byte[] empty = new byte[RecordStride]; // quest_id = 0
        byte[] occupied = BuildRecord(questId: 7, questName: "FindTheKey");
        byte[] buf = Concat(empty, occupied, empty);

        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal((ushort)7, result[0].QuestId);
    }

    // =========================================================================
    // 3. Field decoding
    // =========================================================================

    [Fact]
    public void Parse_QuestId_RoundTrips()
    {
        // quest_id u16LE @ 0x000. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_id u16 @ 0x000 (1..617; 0=empty)": SAMPLE-VERIFIED.
        byte[] buf = BuildRecord(questId: 42, questName: "Q");
        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal((ushort)42, result[0].QuestId);
    }

    [Fact]
    public void Parse_QuestName_DecodedFromOffset0x002()
    {
        // quest_name CP949 @ 0x002. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "quest_name char[] @ 0x002, ends ~0x3F": SAMPLE-VERIFIED.
        byte[] buf = BuildRecord(questId: 1, questName: "DragonSlayer");
        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal("DragonSlayer", result[0].QuestName);
    }

    [Fact]
    public void Parse_EmptyQuestName_YieldsEmptyString()
    {
        // A null byte at 0x002 means no name.
        byte[] buf = BuildRecord(questId: 5, questName: "");
        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(string.Empty, result[0].QuestName);
    }

    // =========================================================================
    // 4. Raw slice
    // =========================================================================

    [Fact]
    public void Parse_RawSlice_HasStrideLength()
    {
        // Raw slice must be exactly 3720 bytes.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "stride 3720 bytes (0xE88)": SAMPLE-VERIFIED.
        byte[] buf = BuildRecord(questId: 10, questName: "X");
        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(3720, result[0].Raw.Length);
    }

    // =========================================================================
    // 5. Multi-record walk
    // =========================================================================

    [Fact]
    public void Parse_TwoOccupiedSlots_BothDecoded_InOrder()
    {
        // Two occupied slots must appear in the output in on-disk order.
        // spec: Docs/RE/formats/config_tables.md §2.17.1 — "sparse flat array".
        byte[] r0 = BuildRecord(questId: 1, questName: "Quest1");
        byte[] r1 = BuildRecord(questId: 2, questName: "Quest2");
        byte[] buf = Concat(r0, r1);

        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal((ushort)1, result[0].QuestId);
        Assert.Equal("Quest1", result[0].QuestName);
        Assert.Equal((ushort)2, result[1].QuestId);
        Assert.Equal("Quest2", result[1].QuestName);
    }

    // =========================================================================
    // 6. MSVC 0xCC sentinel in quest name
    // =========================================================================

    [Fact]
    public void Parse_QuestName_WithCcSentinel_TruncatedAtSentinel()
    {
        // 0xCC = MSVC debug fill — must stop reading name before it.
        // spec: Docs/RE/formats/config_tables.md §2.1 — "0xCC MSVC debug fill after NUL": CONFIRMED.
        byte[] buf = BuildRecord(questId: 99, questName: "");
        // Write "Go" + 0xCC at offset 0x002 manually.
        buf[0x002] = (byte)'G';
        buf[0x003] = (byte)'o';
        buf[0x004] = 0xCC;

        QuestScrRecord[] result = QuestsScrParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal("Go", result[0].QuestName);
    }
}
