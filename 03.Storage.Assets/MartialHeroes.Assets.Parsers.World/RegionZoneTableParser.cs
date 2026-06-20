using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

/// <summary>
///     Parser for <c>regiontable&lt;area&gt;.bin</c> as a zone-type table.
///     Reads exactly 32 records × 48 bytes = 1 536 bytes; extracts the zone-type field at +40.
/// </summary>
/// <remarks>
///     <para>
///         This parser interprets <c>regiontable&lt;area&gt;.bin</c> through the <b>region-gating lens</b>
///         documented in <c>Docs/RE/specs/world_systems.md Ch. 16 §16.2</c>: 32 fixed 48-byte records,
///         indexed directly by region id (0..31), with the only consumed field being the u32le zone-type
///         value at record offset +40.
///     </para>
///     <para>
///         This is <b>distinct</b> from <see cref="RegionTableParser" />, which interprets the same VFS
///         file through the 32-byte-stride sub-zone-label lens (spec:
///         <c>
///             Docs/RE/formats/misc_data.md
///             §7.2
///         </c>
///         ). The two parsers expose different views of the same file for different consumers:
///         one for the minimap label chain, one for the PvP/movement gating chain. Both views are
///         documented in their respective committed specs.
///     </para>
///     <para>
///         Record count: fixed 32 (one per possible region id 0..31).
///         Record stride: 48 bytes.
///         Total table size: 1 536 bytes (32 × 48).
///         spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "32 records × 48 bytes = 1 536 bytes": CONFIRMED.
///     </para>
///     <para>
///         ZERO rendering/engine dependencies.
///     </para>
/// </remarks>
public static class RegionZoneTableParser
{
    // ── constants ────────────────────────────────────────────────────────────

    // Record count: fixed 32.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "fixed 32 records": CONFIRMED.
    public const int RecordCount = 32;

    // Record stride: 48 bytes.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "Record stride: 48 bytes": CONFIRMED.
    public const int RecordStride = 48;

    // Expected total table size = RecordCount × RecordStride = 1 536 bytes.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "32 records × 48 bytes = 1 536 bytes": CONFIRMED.
    public const int ExpectedTableSize = RecordCount * RecordStride; // 1 536

    // Opaque leading bytes per record: +0 .. +39 (40 bytes, UNVERIFIED meaning).
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+0 40 (opaque) unread bytes": UNVERIFIED.
    private const int OpaqueLeadingSize = 40;

    // Zone-type u32le at record offset +40.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+40 4 u32 zone type": CONFIRMED (encoding).
    private const int ZoneTypeOffset = 40;

    // Opaque trailing bytes per record: +44 .. +47 (4 bytes, UNVERIFIED meaning).
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+44 4 (opaque) trailing bytes": UNVERIFIED.
    private const int OpaqueTrailingOffset = 44;
    private const int OpaqueTrailingSize = 4;

    // ── public API ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Parses <c>regiontable&lt;area&gt;.bin</c> as a 32-entry zone-type table.
    ///     Returns an array of 32 <see cref="RegionZoneRecord" /> objects indexed by region id.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS (zero-copy slice).</param>
    /// <returns>
    ///     Array of 32 records in region-id order (index 0 = region id 0, …, index 31 = region id 31).
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     Buffer is shorter than the expected 1 536 bytes (32 records × 48-byte stride).
    /// </exception>
    /// <remarks>
    ///     If the buffer is <b>larger</b> than 1 536 bytes the extra bytes are silently ignored —
    ///     only the first 32 records are consumed, consistent with the fixed-record-count contract.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "record count fixed 32": CONFIRMED.
    /// </remarks>
    public static RegionZoneRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Validate: buffer must contain at least 32 × 48 = 1 536 bytes.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "32 records × 48 bytes = 1 536 bytes": CONFIRMED.
        if (span.Length < ExpectedTableSize)
            throw new InvalidDataException(
                $"regiontable*.bin zone-type parse error: buffer length {span.Length} is too " +
                $"short for 32 × 48-byte records (expected ≥ {ExpectedTableSize} bytes). " +
                "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2.");

        var records = new RegionZoneRecord[RecordCount];

        for (var regionId = 0; regionId < RecordCount; regionId++)
        {
            var recBase = regionId * RecordStride;

            // Opaque leading bytes at +0..+39 (preserved but not interpreted).
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+0 40 (opaque)": UNVERIFIED.
            var opaqueLeading = data.Slice(recBase, OpaqueLeadingSize);

            // Zone-type u32le at +40 — the only field consumed by region-gating logic.
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+40 4 u32 zone type": CONFIRMED (encoding).
            var zoneTypeRaw = BinaryPrimitives.ReadUInt32LittleEndian(
                span.Slice(recBase + ZoneTypeOffset, 4));

            // Opaque trailing bytes at +44..+47 (preserved but not interpreted).
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "+44 4 (opaque) trailing bytes": UNVERIFIED.
            var opaqueTrailing = data.Slice(recBase + OpaqueTrailingOffset, OpaqueTrailingSize);

            records[regionId] = new RegionZoneRecord
            {
                RegionId = regionId,
                ZoneTypeRaw = zoneTypeRaw,
                OpaqueLeading = opaqueLeading,
                OpaqueTrailing = opaqueTrailing
            };
        }

        return records;
    }
}