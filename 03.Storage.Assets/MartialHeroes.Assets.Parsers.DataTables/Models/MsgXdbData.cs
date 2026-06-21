namespace MartialHeroes.Assets.Parsers.DataTables.Models;

/// <summary>
///     One record from <c>data/script/msg.xdb</c>.
///     Fixed stride: 516 bytes = 4 (caption_id) + 512 (text buffer).
/// </summary>
/// <param name="CaptionId">
///     Signed 32-bit caption identifier as stored on disk (i32 LE @ +0x000). CONFIRMED.
///     The runtime ordered-map uses UNSIGNED comparison — see <see cref="MsgXdbCatalog" />.
///     spec: Docs/RE/formats/msg_xdb.md §Record layout — "caption_id i32 LE @ +0x000". CONFIRMED.
///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED". CONFIRMED.
/// </param>
/// <param name="Text">
///     CP949-decoded, NUL-trimmed caption string.
///     Bytes after the first NUL terminator inside the 512-byte buffer are filled with <c>0xEE</c>
///     (NOT <c>0x00</c> — a decoder must stop at the first 0x00 and must not assume a zero-filled tail).
///     spec: Docs/RE/formats/msg_xdb.md §Record layout — "text char[512] CP949 NUL-terminated @ +0x004; 0xEE fill after
///     NUL". CONFIRMED.
/// </param>
/// <remarks>
///     spec: Docs/RE/formats/msg_xdb.md — flat headerless array, stride 516 bytes (0x204).
/// </remarks>
public sealed record MsgXdbRecord(int CaptionId, string Text);

/// <summary>
///     Decoded catalogue of all records from a <c>data/script/msg.xdb</c> file.
///     Provides O(1) average lookup by numeric <see cref="MsgXdbRecord.CaptionId" />.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "ordered-map lower-bound by caption_id; key comparison UNSIGNED".
///     CONFIRMED.
///     <para>
///         The runtime ordered-map uses <b>unsigned key comparison</b>. Although <c>caption_id</c> is stored as
///         i32 LE on disk, the runtime sorts and searches by its unsigned interpretation. Using a signed key would
///         mis-order any caption with the high bit set. The internal map uses <c>uint</c> keys so comparison
///         semantics match the original runtime exactly.
///         spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED (use uint-keyed sorted map)".
///         CONFIRMED.
///     </para>
/// </remarks>
public sealed class MsgXdbCatalog
{
    // Internal map uses uint keys for UNSIGNED key comparison — matches the runtime's ordered-map comparison.
    // spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED": CONFIRMED.
    private readonly Dictionary<uint, string> _map;

    internal MsgXdbCatalog(MsgXdbRecord[] records)
    {
        Records = records;
        _map = new Dictionary<uint, string>(records.Length);
        foreach (var r in records)
            _map[(uint)r.CaptionId] = r.Text; // cast i32→uint for unsigned ordering; last-wins on duplicates
    }

    /// <summary>All decoded records in parse (on-disk) order.</summary>
    public IReadOnlyList<MsgXdbRecord> Records { get; }

    /// <summary>Number of records in the catalogue.</summary>
    public int Count => Records.Count;

    /// <summary>
    ///     Attempts to look up the caption text for the given <paramref name="captionId" />.
    ///     Returns <see langword="true" /> and sets <paramref name="text" /> when found;
    ///     returns <see langword="false" /> and sets <paramref name="text" /> to <see langword="null" /> otherwise.
    ///     Unsigned key comparison — matches the runtime ordered-map semantics.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED". CONFIRMED.
    /// </remarks>
    public bool TryGet(int captionId, out string? text)
    {
        return _map.TryGetValue((uint)captionId, out text);
    }

    /// <summary>
    ///     Overload accepting an unsigned key directly (avoids the cast when the caller already holds a uint).
    ///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED". CONFIRMED.
    /// </summary>
    public bool TryGet(uint captionId, out string? text)
    {
        return _map.TryGetValue(captionId, out text);
    }

    /// <summary>
    ///     Returns the caption text for the given <paramref name="captionId" />,
    ///     or <see langword="null" /> if not present.
    ///     Unsigned key comparison — matches the runtime semantics.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED". CONFIRMED.
    /// </remarks>
    public string? GetText(int captionId)
    {
        return _map.TryGetValue((uint)captionId, out var t) ? t : null;
    }

    /// <summary>
    ///     Overload accepting an unsigned key directly.
    ///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "key comparison is UNSIGNED". CONFIRMED.
    /// </summary>
    public string? GetText(uint captionId)
    {
        return _map.TryGetValue(captionId, out var t) ? t : null;
    }
}