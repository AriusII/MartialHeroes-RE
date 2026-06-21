using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.World.Models;

namespace MartialHeroes.Assets.Parsers.World;

/// <summary>
///     Parser for <c>data/mapNNN/regiontableNNN.bin</c> — per-area zone label + zone-type record table.
///     Flat array of fixed <b>48-byte</b> records with no header; exactly 32 records = 1,536 bytes.
/// </summary>
/// <remarks>
///     <para>
///         Record layout (48 bytes per record):
///         <code>
///   +0x00  char[40]  zoneName   NUL-terminated CP949 zone display-name (minimap sub-zone caption)
///   +0x28  u32 LE    zoneType   Zone-type enum {0=Safe, 1=OpenPvP, 2=Closed} — region-gating
///   +0x2C  u32 LE    _tail      Trailing dword — no reader found; UNVERIFIED meaning
/// </code>
///         spec: Docs/RE/formats/region_grid.md §regiontable — "Record stride: 48 bytes — CONFIRMED (RE-AFFIRMED)".
///         spec: Docs/RE/formats/region_grid.md §regiontable — zoneName char[40] @ +0x00: HIGH.
///         spec: Docs/RE/formats/region_grid.md §regiontable — zoneType u32 @ +0x28: CONFIRMED.
///         spec: Docs/RE/formats/region_grid.md §regiontable — _tail u32 @ +0x2C: UNVERIFIED.
///     </para>
///     <para>
///         <b>Stride is 48, NOT 32.</b> An earlier interim reading proposed 32 bytes; that figure was
///         REFUTED by the same loader that walks the 28-byte npc.arr spawn record on the same path —
///         the "32" was a conflation of the two adjacent structures. The only stride that reconciles the
///         table to exactly 32 × 48 = 1,536 bytes is 48.
///         spec: Docs/RE/formats/region_grid.md §regiontable — "Stride is 48 not 32 (note on the conflation)":
///         RE-AFFIRMED.
///     </para>
///     <para>
///         The same file is consumed by <see cref="RegionZoneTableParser" /> for the zone-type gating path;
///         this parser surfaces both the zone name and zone type together with the opaque tail.
///     </para>
///     <para>
///         Any bytes present beyond offset 1536 (end of record 31) are silently ignored.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class RegionTableParser
{
    // Record stride: 48 bytes (0x30).
    // spec: Docs/RE/formats/region_grid.md §regiontable — "Record stride: 48 bytes": CONFIRMED (RE-AFFIRMED).
    // CORRECTION: was previously 32 (REFUTED). The "32" was a conflation with the adjacent 28-byte
    // npc.arr record on the same loader path. 32 × 48 = 1,536 bytes total is the correct table size.
    private const int RecordStride = 48; // 0x30

    // Fixed record count: 32 slots (region ids 0..31).
    // spec: Docs/RE/formats/region_grid.md §regiontable — "A fixed 32 records × 48 bytes = 1,536 bytes".
    private const int RecordCount = 32;

    // Expected minimum buffer size: 32 × 48 = 1,536 bytes.
    private const int ExpectedMinSize = RecordCount * RecordStride; // 1,536

    // zoneName char[40] @ +0x00 — NUL-terminated CP949 zone display-name (minimap sub-zone caption).
    // spec: Docs/RE/formats/region_grid.md §regiontable — "zoneName char[40] @ +0x00": HIGH.
    private const int ZoneNameOffset = 0x00;
    private const int ZoneNameSize = 40;

    // zoneType u32le @ +0x28 — zone-type enum {0=Safe, 1=OpenPvP, 2=Closed}.
    // spec: Docs/RE/formats/region_grid.md §regiontable — "zoneType u32 @ +0x28": CONFIRMED.
    private const int ZoneTypeOffset = 0x28; // 40

    // _tail u32 @ +0x2C — no reader found; UNVERIFIED meaning.
    // spec: Docs/RE/formats/region_grid.md §regiontable — "_tail u32 @ +0x2C: UNVERIFIED".
    private const int TailOffset = 0x2C; // 44

    static RegionTableParser()
    {
        // Register CP949 provider once for the process (idempotent).
        // spec: Docs/RE/formats/region_grid.md §regiontable — "The game text encoding is CP949".
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    ///     Parses a <c>regiontableNNN.bin</c> file.
    ///     Record count = file_size / 48, capped at 32 (extra bytes beyond 1 536 are ignored).
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Array of up to 32 zone records in region-id order (index = region id).</returns>
    /// <exception cref="InvalidDataException">Buffer is shorter than 1 536 bytes.</exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/region_grid.md §regiontable — "32 records × 48 bytes = 1,536 bytes": CONFIRMED.
    /// </remarks>
    public static RegionTableRecord[] Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // Must contain at least 32 × 48 = 1,536 bytes.
        // spec: Docs/RE/formats/region_grid.md §regiontable — "fixed 32 records × 48 bytes": CONFIRMED.
        if (span.Length < ExpectedMinSize)
            throw new InvalidDataException(
                $"regiontable*.bin parse error: buffer length {span.Length} is too short for " +
                $"{RecordCount} × {RecordStride}-byte records (expected ≥ {ExpectedMinSize} bytes). " +
                "spec: Docs/RE/formats/region_grid.md §regiontable.");

        var cp949 = Encoding.GetEncoding(949);
        var results = new RegionTableRecord[RecordCount];

        for (var i = 0; i < RecordCount; i++)
        {
            var recBase = i * RecordStride;
            var rec = span.Slice(recBase, RecordStride);

            // zoneName char[40] CP949 @ +0x00.
            // spec: Docs/RE/formats/region_grid.md §regiontable — "zoneName char[40] @ +0x00 CP949": HIGH.
            var zoneName = ReadNullTerminatedCp949(rec.Slice(ZoneNameOffset, ZoneNameSize), cp949);

            // zoneType u32le @ +0x28 (= +40). Zone-type enum {0=Safe, 1=OpenPvP, 2=Closed}.
            // spec: Docs/RE/formats/region_grid.md §regiontable zoneType enum — CONFIRMED.
            var zoneType = BinaryPrimitives.ReadUInt32LittleEndian(rec[ZoneTypeOffset..]);

            // _tail u32 @ +0x2C (= +44). No reader found; UNVERIFIED.
            // spec: Docs/RE/formats/region_grid.md §regiontable — "_tail u32 @ +0x2C: UNVERIFIED".
            var tail = BinaryPrimitives.ReadUInt32LittleEndian(rec[TailOffset..]);

            results[i] = new RegionTableRecord
            {
                RegionId = i,
                ZoneName = zoneName,
                ZoneType = zoneType,
                TailOpaque = tail
            };
        }

        return results;
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static string ReadNullTerminatedCp949(ReadOnlySpan<byte> field, Encoding cp949)
    {
        var len = field.IndexOf((byte)0);
        if (len < 0) len = field.Length;
        if (len == 0) return string.Empty;
        return cp949.GetString(field[..len]);
    }
}