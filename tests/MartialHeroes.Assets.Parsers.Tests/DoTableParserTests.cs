using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="DoTableParser"/>.
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/config_tables.md §3 — .do files.
/// </summary>
public sealed class DoTableParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

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

    // =========================================================================
    // textcommand.do — stride 52 bytes
    // spec: Docs/RE/formats/config_tables.md §3.1 textcommand.do — "stride: 52 bytes": CONFIRMED.
    // =========================================================================

    private const int TextCmdStride = 52; // spec: §3.1 — CONFIRMED.

    private static byte[] BuildTextCommandRecord(
        uint commandId = 1u,
        string commandName = "test",
        byte argFlag = 0,
        uint subCommandId = 0u)
    {
        byte[] buf = new byte[TextCmdStride];

        // +0 u32 Command ID. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.1 — "+0 u32 Command ID: CONFIRMED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), commandId);

        // +4 char[36] Command name CP949. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.1 — "+4 char[36] Command name CP949: CONFIRMED".
        WriteCp949(buf, 4, 36, commandName);

        // +44 u8 Argument flag. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.1 — "+44 u8 Argument flag: CONFIRMED (value pattern)".
        buf[44] = argFlag;

        // +48 u32 Sub-command ID. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.1 — "+48 u32 Sub-command ID: CONFIRMED (value pattern)".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(48, 4), subCommandId);

        return buf;
    }

    [Fact]
    public void ParseTextCommandDo_Empty_YieldsNoRecords()
    {
        // 0 % 52 == 0 — empty file is valid.
        // spec: Docs/RE/formats/config_tables.md §3.1 — record count = file_size / 52.
        TextCommandRecord[] result = DoTableParser.ParseTextCommandDo(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseTextCommandDo_NotMultipleOfStride_Throws()
    {
        // 30 is not divisible by 52.
        // spec: Docs/RE/formats/config_tables.md §3.1 — "stride: 52 bytes; exact multiple": CONFIRMED.
        byte[] buf = new byte[30];
        Assert.Throws<InvalidDataException>(() => DoTableParser.ParseTextCommandDo(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseTextCommandDo_OneRecord_AllFieldsDecoded()
    {
        // spec: Docs/RE/formats/config_tables.md §3.1 — all fields CONFIRMED.
        byte[] buf = BuildTextCommandRecord(commandId: 5u, commandName: "/help", argFlag: 0, subCommandId: 99u);
        TextCommandRecord[] result = DoTableParser.ParseTextCommandDo(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(5u, result[0].CommandId);
        Assert.Equal("/help", result[0].CommandName);
        Assert.Equal((byte)0, result[0].ArgumentFlag);
        Assert.Equal(99u, result[0].SubCommandId);
    }

    [Fact]
    public void ParseTextCommandDo_TwoRecords_IndependentDecoding()
    {
        // Two records must decode without cross-record bleed.
        // spec: Docs/RE/formats/config_tables.md §3.1 — stride 52 bytes.
        byte[] r0 = BuildTextCommandRecord(commandId: 1u, commandName: "/chat");
        byte[] r1 = BuildTextCommandRecord(commandId: 2u, commandName: "/trade", argFlag: 1);
        byte[] buf = new byte[r0.Length + r1.Length];
        r0.CopyTo(buf, 0);
        r1.CopyTo(buf, r0.Length);

        TextCommandRecord[] result = DoTableParser.ParseTextCommandDo(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal(1u, result[0].CommandId);
        Assert.Equal("/chat", result[0].CommandName);
        Assert.Equal(2u, result[1].CommandId);
        Assert.Equal("/trade", result[1].CommandName);
        Assert.Equal((byte)1, result[1].ArgumentFlag);
    }

    [Fact]
    public void ParseTextCommandDo_RawSlice_HasStrideLength()
    {
        // Raw slice must be exactly 52 bytes.
        // spec: Docs/RE/formats/config_tables.md §3.1 — stride 52 bytes: CONFIRMED.
        byte[] buf = BuildTextCommandRecord();
        TextCommandRecord[] result = DoTableParser.ParseTextCommandDo(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(52, result[0].Raw.Length);
    }

    // =========================================================================
    // emoticon.do — stride 40 bytes
    // spec: Docs/RE/formats/config_tables.md §3.2 emoticon.do — "stride: 40 bytes": CONFIRMED.
    // =========================================================================

    private const int EmoticonStride = 40; // spec: §3.2 — CONFIRMED.

    private static byte[] BuildEmoticonRecord(
        uint emoteId = 1u,
        byte categoryFlag = 0,
        uint secondaryKey = 0u,
        uint actionLink = 0u)
    {
        byte[] buf = new byte[EmoticonStride];

        // +0 u32 Emote ID. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.2 — "+0 u32 Emote ID: CONFIRMED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), emoteId);

        // +4 u8 Category flag. CONFIRMED (pattern).
        // spec: Docs/RE/formats/config_tables.md §3.2 — "+4 u8 Category flag: CONFIRMED (pattern)".
        buf[4] = categoryFlag;

        // +8 u32 Secondary key. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.2 — "+8 u32 Secondary key: CONFIRMED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), secondaryKey);

        // +12 u32 Action link. CONFIRMED (pattern).
        // spec: Docs/RE/formats/config_tables.md §3.2 — "+12 u32 Action link: CONFIRMED (pattern)".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), actionLink);

        return buf;
    }

    [Fact]
    public void ParseEmoticonDo_Empty_YieldsNoRecords()
    {
        // spec: Docs/RE/formats/config_tables.md §3.2 — record count = file_size / 40.
        EmoticonRecord[] result = DoTableParser.ParseEmoticonDo(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseEmoticonDo_NotMultipleOfStride_Throws()
    {
        // 20 is not divisible by 40.
        byte[] buf = new byte[20];
        Assert.Throws<InvalidDataException>(() => DoTableParser.ParseEmoticonDo(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseEmoticonDo_OneRecord_AllFieldsDecoded()
    {
        // spec: Docs/RE/formats/config_tables.md §3.2 — all fields CONFIRMED.
        byte[] buf = BuildEmoticonRecord(emoteId: 3u, categoryFlag: 1, secondaryKey: 2u, actionLink: 7u);
        EmoticonRecord[] result = DoTableParser.ParseEmoticonDo(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(3u, result[0].EmoteId);
        Assert.Equal((byte)1, result[0].CategoryFlag);
        Assert.Equal(2u, result[0].SecondaryKey);
        Assert.Equal(7u, result[0].ActionLink);
    }

    [Fact]
    public void ParseEmoticonDo_RawSlice_HasStrideLength()
    {
        // spec: Docs/RE/formats/config_tables.md §3.2 — stride 40 bytes: CONFIRMED.
        byte[] buf = BuildEmoticonRecord();
        EmoticonRecord[] result = DoTableParser.ParseEmoticonDo(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(40, result[0].Raw.Length);
    }

    // =========================================================================
    // msginfo.do — stride 128 bytes
    // spec: Docs/RE/formats/config_tables.md §3.3 msginfo.do — "stride: 128 bytes": CONFIRMED.
    // =========================================================================

    private const int MsgInfoStride = 128; // spec: §3.3 — CONFIRMED.

    private static byte[] BuildMsgInfoRecord(
        uint msgId = 1u,
        uint dialogFlag = 0u,
        string textLine1 = "Line1",
        string textLine2 = "Line2")
    {
        byte[] buf = new byte[MsgInfoStride];

        // +0 u32 Message ID. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.3 — "+0 u32 Message ID: CONFIRMED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), msgId);

        // +4 u32 Dialog flag. CONFIRMED (pattern).
        // spec: Docs/RE/formats/config_tables.md §3.3 — "+4 u32 Dialog flag: CONFIRMED (pattern)".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), dialogFlag);

        // +8 char[60] Text line 1 CP949. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.3 — "+8 char[60] Text line 1 CP949: CONFIRMED".
        WriteCp949(buf, 8, 60, textLine1);

        // +68 char[60] Text line 2 CP949. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.3 — "+68 char[60] Text line 2 CP949: CONFIRMED".
        WriteCp949(buf, 68, 60, textLine2);

        return buf;
    }

    [Fact]
    public void ParseMsgInfoDo_Empty_YieldsNoRecords()
    {
        // spec: Docs/RE/formats/config_tables.md §3.3 — record count = file_size / 128.
        MsgInfoRecord[] result = DoTableParser.ParseMsgInfoDo(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseMsgInfoDo_NotMultipleOfStride_Throws()
    {
        // 100 is not divisible by 128.
        byte[] buf = new byte[100];
        Assert.Throws<InvalidDataException>(() => DoTableParser.ParseMsgInfoDo(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseMsgInfoDo_OneRecord_AllFieldsDecoded()
    {
        // spec: Docs/RE/formats/config_tables.md §3.3 — all fields CONFIRMED.
        byte[] buf = BuildMsgInfoRecord(msgId: 100u, dialogFlag: 1u, textLine1: "Confirm?", textLine2: "Yes/No");
        MsgInfoRecord[] result = DoTableParser.ParseMsgInfoDo(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(100u, result[0].MessageId);
        Assert.Equal(1u, result[0].DialogFlag);
        Assert.Equal("Confirm?", result[0].TextLine1);
        Assert.Equal("Yes/No", result[0].TextLine2);
    }

    [Fact]
    public void ParseMsgInfoDo_RawSlice_HasStrideLength()
    {
        // spec: Docs/RE/formats/config_tables.md §3.3 — stride 128 bytes: CONFIRMED.
        byte[] buf = BuildMsgInfoRecord();
        MsgInfoRecord[] result = DoTableParser.ParseMsgInfoDo(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(128, result[0].Raw.Length);
    }

    // =========================================================================
    // items_extra.do — stride 48 bytes
    // spec: Docs/RE/formats/config_tables.md §3.4 items_extra.do — "stride: 48 bytes": CONFIRMED.
    // =========================================================================

    private const int ItemsExtraStride = 48; // spec: §3.4 — CONFIRMED.

    private static byte[] BuildItemsExtraRecord(
        uint itemId = 1u,
        float animScale = 1.0f,
        int attachFieldA = 0,
        int attachFieldB = 8)
    {
        byte[] buf = new byte[ItemsExtraStride];

        // +0 u32 Item ID. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.4 — "+0 u32 Item ID: CONFIRMED".
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), itemId);

        // +4 f32 Animation speed scale. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.4 — "+4 f32 Animation speed scale: CONFIRMED".
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(4, 4), animScale);

        // +8 i32 Attachment field A. CONFIRMED (range).
        // spec: Docs/RE/formats/config_tables.md §3.4 — "+8 i32 Attachment field A (range 0..3): CONFIRMED".
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), attachFieldA);

        // +12 i32 Attachment field B. CONFIRMED (range).
        // spec: Docs/RE/formats/config_tables.md §3.4 — "+12 i32 Attachment field B (range 8..48): CONFIRMED".
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12, 4), attachFieldB);

        return buf;
    }

    [Fact]
    public void ParseItemsExtraDo_Empty_YieldsNoRecords()
    {
        // spec: Docs/RE/formats/config_tables.md §3.4 — record count = file_size / 48.
        ItemsExtraRecord[] result = DoTableParser.ParseItemsExtraDo(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseItemsExtraDo_NotMultipleOfStride_Throws()
    {
        // 30 is not divisible by 48.
        byte[] buf = new byte[30];
        Assert.Throws<InvalidDataException>(() => DoTableParser.ParseItemsExtraDo(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void ParseItemsExtraDo_OneRecord_AllFieldsDecoded()
    {
        // spec: Docs/RE/formats/config_tables.md §3.4 — all confirmed fields.
        byte[] buf = BuildItemsExtraRecord(itemId: 42u, animScale: 1.5f, attachFieldA: 2, attachFieldB: 16);
        ItemsExtraRecord[] result = DoTableParser.ParseItemsExtraDo(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(42u, result[0].ItemId);
        Assert.Equal(1.5f, result[0].AnimScale);
        Assert.Equal(2, result[0].AttachFieldA);
        Assert.Equal(16, result[0].AttachFieldB);
    }

    [Fact]
    public void ParseItemsExtraDo_SentinelId_FlaggedCorrectly()
    {
        // Sentinel item ID = 0x7FFFFFFF. CONFIRMED.
        // spec: Docs/RE/formats/config_tables.md §3.4 — "sentinel ID = 0x7FFFFFFF: CONFIRMED".
        byte[] buf = BuildItemsExtraRecord(itemId: 0x7FFFFFFF);
        ItemsExtraRecord[] result = DoTableParser.ParseItemsExtraDo(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        Assert.Equal(0x7FFFFFFFu, result[0].ItemId);
        Assert.True(result[0].IsSentinel);
    }

    [Fact]
    public void ParseItemsExtraDo_NormalId_NotSentinel()
    {
        // A non-sentinel ID must not be flagged.
        // spec: Docs/RE/formats/config_tables.md §3.4 — "sentinel ID = 0x7FFFFFFF: CONFIRMED".
        byte[] buf = BuildItemsExtraRecord(itemId: 1000u);
        ItemsExtraRecord[] result = DoTableParser.ParseItemsExtraDo(new ReadOnlyMemory<byte>(buf));

        Assert.False(result[0].IsSentinel);
    }
}