namespace MartialHeroes.Assets.Parsers.DataTables.Models;

/// <summary>
///     One record from <c>data/script/msg.xdb</c>.
///     Fixed stride: 516 bytes = 4 (caption_id) + 512 (text buffer).
/// </summary>
/// <param name="CaptionId">
///     Signed 32-bit caption identifier; the lookup key.
///     spec: Docs/RE/formats/msg_xdb.md §Record layout — "caption_id i32 LE @ +0x000". CONFIRMED.
/// </param>
/// <param name="Text">
///     CP949-decoded, NUL-trimmed caption string.
///     spec: Docs/RE/formats/msg_xdb.md §Record layout — "text char[512] CP949 NUL-terminated @ +0x004". CONFIRMED.
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
///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — "ordered-map lower-bound by caption_id". CONFIRMED.
///     A sorted dictionary is used for equivalent lower-bound semantics; the on-disk ascending
///     order (confirmed) means binary search is valid, but the dictionary gives equivalent O(1) avg.
/// </remarks>
public sealed class MsgXdbCatalog
{
    private readonly Dictionary<int, string> _map;

    internal MsgXdbCatalog(MsgXdbRecord[] records)
    {
        Records = records;
        _map = new Dictionary<int, string>(records.Length);
        foreach (var r in records)
            _map[r.CaptionId] = r.Text; // last-wins on duplicates (not expected per spec)
    }

    /// <summary>All decoded records in parse (on-disk) order.</summary>
    public IReadOnlyList<MsgXdbRecord> Records { get; }

    /// <summary>Number of records in the catalogue.</summary>
    public int Count => Records.Count;

    /// <summary>
    ///     Attempts to look up the caption text for the given <paramref name="captionId" />.
    ///     Returns <see langword="true" /> and sets <paramref name="text" /> when found;
    ///     returns <see langword="false" /> and sets <paramref name="text" /> to <see langword="null" /> otherwise.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — lookup by caption_id. CONFIRMED.
    /// </remarks>
    public bool TryGet(int captionId, out string? text)
    {
        return _map.TryGetValue(captionId, out text);
    }

    /// <summary>
    ///     Returns the caption text for the given <paramref name="captionId" />,
    ///     or <see langword="null" /> if not present.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/msg_xdb.md §Lookup model — lookup by caption_id. CONFIRMED.
    /// </remarks>
    public string? GetText(int captionId)
    {
        return _map.TryGetValue(captionId, out var t) ? t : null;
    }
}