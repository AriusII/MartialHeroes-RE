namespace MartialHeroes.Assets.Parsers.World.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  regiontableNNN.bin  Per-area sub-zone label table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>data/mapNNN/regiontableNNN.bin</c>.
///     Stride: <b>48 bytes</b> (0x30). Fixed 32 records = 1,536 bytes total.
/// </summary>
/// <remarks>
///     <para>
///         Record layout (48 bytes):
///         <code>
///   +0x00  char[40]  zoneName   NUL-terminated CP949 zone display-name (minimap sub-zone caption)
///   +0x28  u32 LE    zoneType   Zone-type enum {0=Safe, 1=OpenPvP, 2=Closed}
///   +0x2C  u32 LE    _tail      No reader found; UNVERIFIED meaning
/// </code>
///     </para>
///     <para>
///         <b>Stride correction:</b> an earlier revision of this record used a 32-byte stride. That
///         figure is REFUTED. The confirmed stride is <b>48 bytes</b>, which is the only value that
///         reconciles 32 records to the 1,536-byte table block.
///         spec: Docs/RE/formats/region_grid.md §regiontable — "stride 48 bytes — CONFIRMED (RE-AFFIRMED)".
///         spec: Docs/RE/formats/region_grid.md §regiontable — "Stride is 48 not 32 (conflation note)".
///     </para>
/// </remarks>
public sealed class RegionTableRecord
{
    /// <summary>
    ///     Index of this record in the table (0..31), equal to the region id.
    ///     spec: Docs/RE/formats/region_grid.md §regiontable — "indexed directly by region id (0..31)".
    /// </summary>
    public required int RegionId { get; init; }

    /// <summary>
    ///     NUL-terminated CP949 zone display-name string (minimap sub-zone caption).
    ///     Read from the 40-byte field starting at record offset +0x00.
    ///     spec: Docs/RE/formats/region_grid.md §regiontable — "zoneName char[40] @ +0x00": HIGH.
    /// </summary>
    public required string ZoneName { get; init; }

    /// <summary>
    ///     Zone-type enum value: 0 = Safe, 1 = OpenPvP, 2 = Closed.
    ///     Read from record offset +0x28 (= +40).
    ///     The only numeric field any region-gating path reads.
    ///     spec: Docs/RE/formats/region_grid.md §regiontable zoneType enum — CONFIRMED.
    /// </summary>
    public required uint ZoneType { get; init; }

    /// <summary>
    ///     Opaque trailing dword at record offset +0x2C (= +44). No reader found.
    ///     spec: Docs/RE/formats/region_grid.md §regiontable — "_tail u32 @ +0x2C: UNVERIFIED".
    /// </summary>
    public required uint TailOpaque { get; init; }
}