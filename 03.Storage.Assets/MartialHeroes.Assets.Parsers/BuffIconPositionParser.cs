using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// High-level parser for <c>data/script/buff_icon_position.xdb</c>.
/// Decodes the flat 12-byte record array and exposes a fast O(1) lookup by <c>buff_id</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb: CODE-CONFIRMED + SAMPLE-VERIFIED.
/// <para>
/// Wire consumer: the active-buff array in response 4/102 (<c>SkillWindowStateUpdate</c>) contains up to
/// 30 slots; each non-zero <c>buff_id</c> is looked up here to obtain <c>(atlas_x, atlas_y)</c> for
/// blitting from <c>data/ui/skillicon/stateicon.dds</c>.
/// spec: Docs/RE/formats/misc_data.md §1.6 — buff-bar render contract: CODE-CONFIRMED.
/// </para>
/// <para>
/// SPEC CORRECTION 2026-06-13: <c>atlas_x</c> / <c>atlas_y</c> are <b>signed i32LE</b>, not u32.
/// spec: Docs/RE/formats/misc_data.md §1.3 — "(corrected 2026-06-13)".
/// </para>
/// ZERO rendering/engine dependencies.
/// </remarks>
public sealed class BuffIconPositionTable
{
    // Stride: 12 bytes. CONFIRMED (1608 bytes = 134 records in known sample).
    // spec: Docs/RE/formats/misc_data.md §1.3 — "stride 12 bytes": CONFIRMED.

    private readonly Dictionary<uint, BuffIconPositionRecord> _byBuffId;

    /// <summary>All decoded records in on-disk order.</summary>
    public IReadOnlyList<BuffIconPositionRecord> Records { get; }

    private BuffIconPositionTable(BuffIconPositionRecord[] records)
    {
        Records = records;
        // Build O(1) lookup. dict capacity pre-sized to avoid rehash.
        _byBuffId = new Dictionary<uint, BuffIconPositionRecord>(records.Length);
        foreach (var r in records)
        {
            // Last record wins if duplicate keys exist (spec does not mention duplicates).
            _byBuffId[r.BuffId] = r;
        }
    }

    /// <summary>
    /// Parses <c>data/script/buff_icon_position.xdb</c> and builds a lookup table.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Length must be a multiple of 12.</param>
    /// <returns>A <see cref="BuffIconPositionTable"/> ready for lookup.</returns>
    /// <exception cref="InvalidDataException">Buffer length is not a multiple of 12.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §1.3 — "record count = file_size / 12 (exact multiple)": CONFIRMED.
    /// </remarks>
    public static BuffIconPositionTable Parse(ReadOnlyMemory<byte> data)
    {
        // Delegate raw decode to XdbParser (hot path, zero extra alloc).
        BuffIconPositionRecord[] records = XdbParser.ParseBuffIconPositionXdb(data);
        return new BuffIconPositionTable(records);
    }

    /// <summary>
    /// Looks up the atlas coordinates for a given <paramref name="buffId"/>.
    /// Returns <see langword="null"/> when the id is absent (per spec: runtime returns (0,0) when absent).
    /// spec: Docs/RE/formats/misc_data.md §1.3 — "lookup returns (atlas_x, atlas_y) or (0,0) when absent".
    /// </summary>
    /// <param name="buffId">The buff/state catalogue id from the 4/102 wire payload.</param>
    public BuffIconPositionRecord? TryGetById(uint buffId) =>
        _byBuffId.TryGetValue(buffId, out var r) ? r : null;
}

/// <summary>
/// Stateless entry-point for <c>buff_icon_position.xdb</c> parsing.
/// Prefer <see cref="BuffIconPositionTable"/> for repeated lookup.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class BuffIconPositionParser
{
    // BUFF_ICON_POS_RECORD_BYTES = 12.
    // spec: Docs/RE/formats/misc_data.md §1.3 — "stride 12 bytes = BUFF_ICON_POS_RECORD_BYTES": CONFIRMED.

    /// <inheritdoc cref="BuffIconPositionTable.Parse"/>
    public static BuffIconPositionTable Parse(ReadOnlyMemory<byte> data) =>
        BuffIconPositionTable.Parse(data);
}