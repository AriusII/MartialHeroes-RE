namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// One record from <c>data/script/msg.xdb</c>.
/// Stride: 516 bytes = 4 (id) + 512 (text buffer).
/// </summary>
/// <param name="Id">
/// Unique message identifier; runtime lookup key.
/// spec: Docs/RE/formats/misc_data.md §6 — "id u32LE @ 0x000: CODE-CONFIRMED".
/// </param>
/// <param name="Text">
/// CP949-decoded, NUL-stripped message string.
/// spec: Docs/RE/formats/misc_data.md §6 — "text u8[512] CP949 NUL-terminated @ 0x004: CODE-CONFIRMED".
/// </param>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §6 msg.xdb.
/// Verification: CODE-CONFIRMED (loader + stride); SAMPLE-UNVERIFIED (content).
/// </remarks>
public sealed record MsgXdbRecord(uint Id, string Text);

/// <summary>
/// Decoded catalogue of all records from a <c>msg.xdb</c> file,
/// providing O(log n) lookup by numeric ID.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §6 — "global red-black tree keyed on id": CODE-CONFIRMED.
/// The runtime uses a red-black tree; here we use a sorted dictionary for equivalent semantics.
/// </remarks>
public sealed class MsgXdbCatalog
{
    private readonly Dictionary<uint, MsgXdbRecord> _map;

    /// <summary>All decoded records in parse order.</summary>
    public IReadOnlyList<MsgXdbRecord> Records { get; }

    /// <summary>Number of records in the catalogue.</summary>
    public int Count => Records.Count;

    internal MsgXdbCatalog(MsgXdbRecord[] records)
    {
        Records = records;
        _map = new Dictionary<uint, MsgXdbRecord>(records.Length);
        foreach (var r in records)
            _map[r.Id] = r; // last-wins on duplicate (not expected per spec)
    }

    /// <summary>
    /// Returns the text string for the given <paramref name="id"/>,
    /// or <see langword="null"/> if not present.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §6 Load and lookup model — "keyed on id": CODE-CONFIRMED.
    /// </remarks>
    public string? GetText(uint id) =>
        _map.TryGetValue(id, out MsgXdbRecord? r) ? r.Text : null;

    /// <summary>
    /// Attempts to get the record for the given <paramref name="id"/>.
    /// Returns <see langword="true"/> when found.
    /// </summary>
    public bool TryGetRecord(uint id, out MsgXdbRecord? record) =>
        _map.TryGetValue(id, out record);
}