using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Unit tests for <see cref="AutoQuestionParser"/>.
/// All buffers are hand-built in-memory; no real VFS file is required.
/// spec: Docs/RE/formats/events_scr.md §2 autoquestion_cl.scr — sample_verified.
/// </summary>
public sealed class AutoQuestionParserTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    // Record stride: 92 bytes (0x5C). CONFIRMED.
    // spec: Docs/RE/formats/events_scr.md §2.1 — "Record stride: 92 bytes (0x5C)": CONFIRMED.
    private const int RecordStride = 92; // 0x5C

    private static Encoding Cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    /// <summary>
    /// Builds one 92-byte autoquestion_cl.scr record.
    /// spec: Docs/RE/formats/events_scr.md §2.2 — Record layout: HIGH.
    /// </summary>
    private static byte[] BuildRecord(
        uint questionId = 1u,
        string questionText = "Q?",
        string answerPrompt = "Enter answer:")
    {
        // spec: Docs/RE/formats/events_scr.md §2.1 — "stride 92 bytes (0x5C)": CONFIRMED.
        byte[] buf = new byte[RecordStride];
        var enc = Cp949();

        // question_id u32LE @ 0x00. HIGH.
        // spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00, 4), questionId);

        // text_block CP949[84] @ 0x04. Two consecutive null-terminated strings. HIGH.
        // spec: Docs/RE/formats/events_scr.md §2.2 — text_block CP949 @ 0x04 (84 bytes): HIGH.
        // spec: Docs/RE/formats/events_scr.md §2.2 — "Split within text_block: boundary is the first null": HIGH.
        byte[] qBytes = enc.GetBytes(questionText);
        byte[] aBytes = enc.GetBytes(answerPrompt);

        int qLen = Math.Min(qBytes.Length, 83);
        qBytes.AsSpan(0, qLen).CopyTo(buf.AsSpan(0x04));
        buf[0x04 + qLen] = 0x00; // null terminator for question

        // Answer prompt follows immediately after the null.
        int aStart = 0x04 + qLen + 1;
        int remaining = 84 - qLen - 1; // remaining bytes in text_block
        if (remaining > 0)
        {
            int aLen = Math.Min(aBytes.Length, remaining - 1);
            aBytes.AsSpan(0, aLen).CopyTo(buf.AsSpan(aStart));
            if (aStart + aLen < 0x04 + 84)
                buf[aStart + aLen] = 0x00;
        }

        // record_padding u8[4] @ 0x58. HIGH (zero). Not asserted here — just left zero.
        // spec: Docs/RE/formats/events_scr.md §2.2 — record_padding u8[4] @ 0x58: HIGH.

        return buf;
    }

    // =========================================================================
    // 1. Stride validation
    // =========================================================================

    [Fact]
    public void Parse_EmptyBuffer_YieldsEmptyArray()
    {
        // 0 % 92 == 0. Zero records is valid.
        // spec: Docs/RE/formats/events_scr.md §2.1 — "record count = file_size / 92 (exact)".
        AutoQuestionRecord[] result = AutoQuestionParser.Parse(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NotMultipleOfStride_ThrowsInvalidDataException()
    {
        // 50 is not divisible by 92.
        // spec: Docs/RE/formats/events_scr.md §2.1 — stride 92 bytes; no remainder.
        byte[] buf = new byte[50];
        Assert.Throws<InvalidDataException>(() => AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf)));
    }

    [Fact]
    public void Parse_ExactlyOneRecord_DecodesSingleEntry()
    {
        // 92 bytes = exactly one record.
        // spec: Docs/RE/formats/events_scr.md §2.1 — "Record stride: 92 bytes (0x5C)": CONFIRMED.
        byte[] buf = BuildRecord();
        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Single(result);
    }

    // =========================================================================
    // 2. Field decoding
    // =========================================================================

    [Fact]
    public void Parse_QuestionId_RoundTrips()
    {
        // question_id u32LE @ 0x00. HIGH.
        // spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
        byte[] buf = BuildRecord(questionId: 42u);
        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal(42u, result[0].QuestionId);
    }

    [Fact]
    public void Parse_QuestionText_DecodedFromFirstNullTerminatedString()
    {
        // The question text is the first null-terminated CP949 string in text_block @ 0x04.
        // spec: Docs/RE/formats/events_scr.md §2.2 — "first null-terminated string = question text": HIGH.
        byte[] buf = BuildRecord(questionText: "What is 2+2?");
        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("What is 2+2?", result[0].QuestionText);
    }

    [Fact]
    public void Parse_AnswerPrompt_DecodedFromSecondNullTerminatedString()
    {
        // The answer prompt is the second null-terminated string immediately following the first null.
        // spec: Docs/RE/formats/events_scr.md §2.2 — "second null-terminated string = answer prompt": HIGH.
        byte[] buf = BuildRecord(questionText: "Q", answerPrompt: "A:");
        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));
        Assert.Equal("A:", result[0].AnswerPrompt);
    }

    [Fact]
    public void Parse_CorrectAnswerNotStoredInFile_AnswerPromptIsNotTheAnswer()
    {
        // The correct answer is validated server-side; only a prompt string appears client-side.
        // spec: Docs/RE/formats/events_scr.md §2.4 — "correct answer NOT stored in client file": CONFIRMED.
        // The test documents this constraint: AnswerPrompt is a UI prompt, not the answer itself.
        byte[] buf = BuildRecord(questionText: "X?", answerPrompt: "Enter:");
        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));
        // AnswerPrompt carries the UI instruction text, not the correct answer — by design.
        Assert.Equal("Enter:", result[0].AnswerPrompt);
    }

    // =========================================================================
    // 3. Multi-record walk
    // =========================================================================

    [Fact]
    public void Parse_TwoRecords_BothDecoded_InOrder()
    {
        // Two consecutive 92-byte records must decode independently without cross-record bleed.
        // spec: Docs/RE/formats/events_scr.md §2.1 — "no header; record count = file_size / 92".
        byte[] r0 = BuildRecord(questionId: 1u, questionText: "Q1");
        byte[] r1 = BuildRecord(questionId: 2u, questionText: "Q2");
        byte[] buf = new byte[r0.Length + r1.Length];
        r0.CopyTo(buf, 0);
        r1.CopyTo(buf, r0.Length);

        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Equal(2, result.Length);
        Assert.Equal(1u, result[0].QuestionId);
        Assert.Equal("Q1", result[0].QuestionText);
        Assert.Equal(2u, result[1].QuestionId);
        Assert.Equal("Q2", result[1].QuestionText);
    }

    // =========================================================================
    // 4. Edge: entire text_block has no null (fallback path)
    // =========================================================================

    [Fact]
    public void Parse_NoNullInTextBlock_YieldsEntireBlockAsQuestionAndEmptyPrompt()
    {
        // If no null byte exists in the 84-byte text block, the whole block is the question text.
        // spec: Docs/RE/formats/events_scr.md §2.2 — "split at first null": HIGH (fallback: no-null → full block).
        byte[] buf = new byte[RecordStride];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00, 4), 99u); // question_id
        // Fill text_block with 'A' (0x41) — no null byte in the block.
        for (int i = 0x04; i < 0x04 + 84; i++)
            buf[i] = (byte)'A';

        AutoQuestionRecord[] result = AutoQuestionParser.Parse(new ReadOnlyMemory<byte>(buf));

        Assert.Single(result);
        // The question text is the 84 'A' characters; no second null so prompt is empty.
        Assert.Equal(84, result[0].QuestionText.Length);
        Assert.Equal(string.Empty, result[0].AnswerPrompt);
    }
}
