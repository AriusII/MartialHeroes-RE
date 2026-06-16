using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/autoquestion_cl.scr</c> — the NPC anti-bot captcha Q&amp;A table.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/events_scr.md §2 autoquestion_cl.scr — sample_verified.
/// <para>
/// Format: no file header; fixed stride 92 bytes (0x5C); record count = file_size / 92.
/// Known sample: 92 × 1300 = 119,600 bytes, exact.
/// spec: Docs/RE/formats/events_scr.md §2.1 — "Record stride: 92 bytes (0x5C)": CONFIRMED.
/// </para>
/// <para>
/// Text encoding: CP949 (EUC-KR). The 84-byte text block at +0x04 contains two consecutive
/// null-terminated CP949 strings: the question text, then the answer-prompt instruction.
/// spec: Docs/RE/formats/events_scr.md §2.2 — "Split within text_block: boundary is the first null": HIGH.
/// </para>
/// <para>
/// The correct answer is NOT in this file; it is validated server-side.
/// spec: Docs/RE/formats/events_scr.md §2.4 — "correct answer NOT stored in client file": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class AutoQuestionParser
{
    // Record stride: 92 bytes (0x5C). CONFIRMED (sample: 119,600 / 92 = 1,300, exact).
    // spec: Docs/RE/formats/events_scr.md §2.1 — "Record stride: 92 bytes (0x5C)": CONFIRMED.
    private const int RecordStride = 92; // 0x5C

    // Field offsets within a record.
    // spec: Docs/RE/formats/events_scr.md §2.2 Record layout.

    // question_id u32LE @ 0x00. HIGH.
    // spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
    private const int OffQuestionId = 0x00;

    // text_block CP949 @ 0x04, 84-byte block (0x04–0x57). HIGH.
    // Two consecutive null-terminated CP949 strings within this block.
    // spec: Docs/RE/formats/events_scr.md §2.2 — text_block char[] CP949 @ 0x04 (84 bytes): HIGH.
    private const int OffTextBlock = 0x04;
    private const int TextBlockLen = 84; // 84-byte block — CONFIRMED (0x04..0x57 = 84 bytes)

    // record_padding u8[4] @ 0x58. HIGH (zero).
    // spec: Docs/RE/formats/events_scr.md §2.2 — record_padding u8[4] @ 0x58: HIGH.
    // Not stored.

    /// <summary>
    /// Parses <c>data/script/autoquestion_cl.scr</c> into an array of <see cref="AutoQuestionRecord"/>.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Length must be an exact multiple of 92.</param>
    /// <returns>All 1300 captcha records in on-disk order.</returns>
    /// <exception cref="InvalidDataException">
    /// Buffer length is not an exact multiple of 92 bytes.
    /// spec: Docs/RE/formats/events_scr.md §2.1 — "record count = file_size / 92 (exact)".
    /// </exception>
    public static AutoQuestionRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Validate exact stride divisibility.
        // spec: Docs/RE/formats/events_scr.md §2.1 — "stride 92 bytes; no remainder": CONFIRMED.
        if (span.Length % RecordStride != 0)
            throw new InvalidDataException(
                $"autoquestion_cl.scr parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {RecordStride}. " +
                "spec: Docs/RE/formats/events_scr.md §2.1.");

        int count = span.Length / RecordStride;

        // Register CP949 provider. Idempotent; safe to call multiple times.
        // spec: Docs/RE/formats/events_scr.md §2.1 — "Encoding: CP949 (EUC-KR)": CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949); // spec: Docs/RE/formats/events_scr.md §2.1 — CP949.

        var records = new AutoQuestionRecord[count];

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> rec = span.Slice(i * RecordStride, RecordStride);

            // question_id u32LE @ 0x00. 1-based sequential. HIGH.
            // spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
            uint questionId = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffQuestionId..]);

            // text_block CP949 @ 0x04, 84-byte block. Two consecutive null-terminated strings.
            // spec: Docs/RE/formats/events_scr.md §2.2 — text_block CP949 @ 0x04; split at first null: HIGH.
            ReadOnlySpan<byte> block = rec.Slice(OffTextBlock, TextBlockLen);

            // First string: question text — read until first null.
            int firstNul = block.IndexOf((byte)0);
            string questionText;
            string answerPrompt;

            if (firstNul < 0)
            {
                // No null found; treat the entire block as the question with no prompt.
                questionText = cp949.GetString(block);
                answerPrompt = string.Empty;
            }
            else
            {
                questionText = cp949.GetString(block[..firstNul]);

                // Second string: answer-prompt — starts right after the first null.
                ReadOnlySpan<byte> remaining = block[(firstNul + 1)..];
                int secondNul = remaining.IndexOf((byte)0);
                answerPrompt = secondNul >= 0
                    ? cp949.GetString(remaining[..secondNul])
                    : cp949.GetString(remaining);
            }

            records[i] = new AutoQuestionRecord
            {
                QuestionId = questionId,
                QuestionText = questionText,
                AnswerPrompt = answerPrompt,
            };
        }

        return records;
    }
}