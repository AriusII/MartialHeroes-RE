namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  autoquestion_cl.scr — NPC anti-bot captcha Q&A table (client-side)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/autoquestion_cl.scr</c>. Stride: 92 bytes (0x5C).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/events_scr.md §2 autoquestion_cl.scr — sample_verified.
/// No file header; record count = file_size / 92 (must be exact). Known: 1300 records (119,600 bytes).
/// <para>
/// The <c>_cl</c> suffix marks this as the <b>client-side</b> half of the captcha table:
/// it holds only the display text. The correct answer is NOT present in this file.
/// spec: Docs/RE/formats/events_scr.md §2.4 — "correct answer NOT stored in client file": CONFIRMED.
/// </para>
/// </remarks>
public sealed class AutoQuestionRecord
{
    /// <summary>
    /// 1-based sequential identifier (1, 2, 3, …).
    /// spec: Docs/RE/formats/events_scr.md §2.2 — question_id u32LE @ 0x00: HIGH.
    /// </summary>
    public required uint QuestionId { get; init; }

    /// <summary>
    /// The arithmetic question text (CP949 Korean), null-terminated within the 84-byte block.
    /// spec: Docs/RE/formats/events_scr.md §2.2 — text_block CP949 @ 0x04; first null-terminated string: HIGH.
    /// </summary>
    public required string QuestionText { get; init; }

    /// <summary>
    /// The constant answer-prompt instruction text (CP949), starts immediately after the
    /// question text's null terminator. Observed identical across sampled records.
    /// spec: Docs/RE/formats/events_scr.md §2.2 — text_block CP949 @ 0x04; second null-terminated string: HIGH.
    /// Note: only verified constant for first 5 records.
    /// spec: Docs/RE/formats/events_scr.md §2.4 — "second string observed constant across sampled records".
    /// </summary>
    public required string AnswerPrompt { get; init; }
}