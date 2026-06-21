namespace MartialHeroes.Assets.Parsers.World.Models;

/// <summary>
///     One record from <c>regiontable&lt;area&gt;.bin</c>, representing a region-zone entry.
///     Record stride: 48 bytes. Table size: exactly 32 records = 1 536 bytes.
/// </summary>
/// <remarks>
///     <para>
///         The only field consumed by region-gating logic is <see cref="ZoneTypeRaw" /> at record offset
///         +40. All other bytes are opaque (UNVERIFIED) and are preserved here for completeness.
///         spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2.
///     </para>
///     <para>
///         The record is indexed directly by the region id (0..31) from the parallel grid file
///         (<c>region&lt;area&gt;.bin</c>). A region id ≥ 32 has no record.
///         spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "indexed directly by region id (0..31)": CONFIRMED.
///     </para>
///     <para>
///         <see cref="RegionTableParser" /> parses the same <c>regiontable&lt;area&gt;.bin</c>
///         file (48-byte stride, 32 records) and exposes both the <c>zoneName</c> CP949 string and
///         the <c>zoneType</c> field together. This type retains the opaque leading bytes verbatim
///         and exposes only <see cref="ZoneTypeRaw" /> for callers that exclusively need the gating
///         field.
///         spec: Docs/RE/formats/region_grid.md §regiontable — "Record stride: 48 bytes — CONFIRMED (RE-AFFIRMED)".
///     </para>
/// </remarks>
public sealed class RegionZoneRecord
{
    /// <summary>
    ///     Index of this record within the table (0..31), equal to the region id.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "indexed directly by region id": CONFIRMED.
    /// </summary>
    public required int RegionId { get; init; }

    /// <summary>
    ///     The raw zone-type u32 value read from record offset +40.
    ///     Callers should convert this to <c>ZoneType</c> (Shared.Kernel) for semantic use.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "zone type u32 @ +40": CONFIRMED (encoding).
    /// </summary>
    public required uint ZoneTypeRaw { get; init; }

    /// <summary>
    ///     The 40 opaque bytes at record offsets +0..+39 (not consumed by any region-gating path).
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+0 40 (opaque) unread bytes": UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> OpaqueLeading { get; init; }

    /// <summary>
    ///     The 4 opaque trailing bytes at record offsets +44..+47 (not consumed by any region-gating path).
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+44 4 (opaque) trailing bytes": UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> OpaqueTrailing { get; init; }
}