namespace MartialHeroes.Assets.Parsers.DataTables.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  curse.txt / cursechat.txt — Chat-filter substitution tables
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One entry from <c>data/cursor/curse.txt</c> or <c>data/cursor/cursechat.txt</c>.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/text_tables.md §3.1 — "2-column TAB-separated, CP949": HIGH.
///     No join to other tables; consumed by the chat-filter subsystem at load time.
/// </remarks>
public sealed class ChatFilterEntry
{
    /// <summary>
    ///     The profanity / prohibited word to match.
    ///     spec: Docs/RE/formats/text_tables.md §3.1 — col0 "bad word to filter" CP949: HIGH.
    /// </summary>
    public required string BadWord { get; init; }

    /// <summary>
    ///     The clean replacement string to substitute when <see cref="BadWord" /> is matched.
    ///     spec: Docs/RE/formats/text_tables.md §3.1 — col1 "clean replacement to substitute" CP949: HIGH.
    /// </summary>
    public required string Replacement { get; init; }
}