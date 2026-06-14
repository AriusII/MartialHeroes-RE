using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

// spec: Docs/RE/formats/mi.md

/// <summary>
/// Parser for <c>.mi</c> UI-panel descriptor files (the single VFS instance is
/// <c>data/ui/mobinfo.mi</c>).
/// </summary>
/// <remarks>
/// <para>
/// Container layout (no magic, no version, no compression):
/// <code>
///   +0x00   u32le  recordCount   (number of widget records)
///   +0x04   28-byte records[recordCount]   (each record = 7 x u32le)
///   Total size = 4 + recordCount x 28 bytes
/// </code>
/// spec: Docs/RE/formats/mi.md §Container structure -- "4 + recordCount x 28 = file size": HIGH.
/// spec: Docs/RE/formats/mi.md §Identification -- "Magic: none. Version: none. Endianness: little-endian.": HIGH.
/// </para>
/// <para>
/// Field semantics within each record are PLAUSIBLE working hypotheses derived from a full
/// re-parse of all 21 sample records; the dedicated loader was not located in the static pass
/// so meanings are pending live-debugger confirmation.
/// PLAUSIBLE groupings: field0=sequential ordinal; field1+2=caption +-1 couple;
/// field4+5=decimal-packed icon-id pair; field3+6=kind/link couple; 0xFFFFFFFF=none sentinel.
/// spec: Docs/RE/formats/mi.md §Status -- "record_field_semantics: PLAUSIBLE; loader: UNRESOLVED (static) -- LIVE-DEBUGGER-PENDING".
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class MobInfoPanelParser
{
    // recordCount header size.
    // spec: Docs/RE/formats/mi.md §Header layout -- "recordCount u32le @ 0x00": HIGH.
    private const int HeaderSize = 4;

    // Per-record stride: 28 bytes = 7 x u32.
    // spec: Docs/RE/formats/mi.md §Record layout -- "Record stride: 28 bytes": HIGH.
    private const int RecordStride = MiPanelData.RecordStride; // 28

    // Per-field offsets within a 28-byte record.
    // Stride and field boundaries are SAMPLE-VERIFIED. Semantics are PLAUSIBLE (see model XML docs).
    // spec: Docs/RE/formats/mi.md §Record layout (7 x u32le, little-endian): HIGH (stride); PLAUSIBLE (semantics).
    private const int
        OffWidgetId =
            0x00; // spec: Docs/RE/formats/mi.md §Record layout field 0 @ +0x00 -- PLAUSIBLE sequential ordinal.

    private const int
        OffFieldA0 =
            0x04; // spec: Docs/RE/formats/mi.md §Record layout field 1 @ +0x04 -- PLAUSIBLE caption id (primary of +-1 couple).

    private const int
        OffFieldA1 =
            0x08; // spec: Docs/RE/formats/mi.md §Record layout field 2 @ +0x08 -- PLAUSIBLE caption id sibling (= FieldA0 - 1).

    private const int
        OffFieldKind =
            0x0C; // spec: Docs/RE/formats/mi.md §Record layout field 3 @ +0x0C -- PLAUSIBLE kind/link id (co-varies with field 6; kind-vs-link UNRESOLVED).

    private const int
        OffFieldB0 =
            0x10; // spec: Docs/RE/formats/mi.md §Record layout field 4 @ +0x10 -- PLAUSIBLE decimal-packed icon id (primary of pair; NOT a pointer).

    private const int
        OffFieldB1 =
            0x14; // spec: Docs/RE/formats/mi.md §Record layout field 5 @ +0x14 -- PLAUSIBLE decimal-packed icon id sibling (= FieldB0 + 1), or 0xFFFFFFFF.

    private const int
        OffFieldLink =
            0x18; // spec: Docs/RE/formats/mi.md §Record layout field 6 @ +0x18 -- PLAUSIBLE kind/link id (co-varies with field 3), or 0xFFFFFFFF.

    /// <summary>
    /// Parses the raw bytes of a <c>.mi</c> file into a <see cref="MiPanelData"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS.</param>
    /// <returns>The decoded panel descriptor with all widget records.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the buffer is too short to hold the header or the declared record array.
    /// spec: Docs/RE/formats/mi.md §Container structure -- "4 + count * 28 == length".
    /// </exception>
    public static MiPanelData Parse(ReadOnlyMemory<byte> data) => Parse(data.Span);

    /// <summary>
    /// Parses a span of bytes into a <see cref="MiPanelData"/>.
    /// </summary>
    /// <param name="span">Raw bytes from the VFS.</param>
    /// <returns>The decoded panel descriptor.</returns>
    /// <exception cref="InvalidDataException">Buffer is truncated or length is inconsistent.</exception>
    public static MiPanelData Parse(ReadOnlySpan<byte> span)
    {
        // Must have at least the 4-byte header.
        // spec: Docs/RE/formats/mi.md §Header layout -- "recordCount u32le @ 0x00": HIGH.
        if (span.Length < HeaderSize)
            throw new InvalidDataException(
                $".mi parse error: buffer length {span.Length} is too short for the 4-byte header. " +
                "spec: Docs/RE/formats/mi.md §Header layout.");

        // recordCount u32le @ 0x00.
        // spec: Docs/RE/formats/mi.md §Header layout -- "recordCount u32le @ 0x00 -- number of widget records": HIGH.
        uint recordCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0x00..]);

        // Validate: 4 + recordCount * 28 must equal the buffer length.
        // spec: Docs/RE/formats/mi.md §Container structure -- "4 + recordCount x 28 = file size": HIGH.
        long expectedLength = HeaderSize + (long)recordCount * RecordStride;
        if (span.Length != expectedLength)
            throw new InvalidDataException(
                $".mi parse error: buffer length {span.Length} does not match expected " +
                $"4 + {recordCount} x 28 = {expectedLength} bytes. " +
                "spec: Docs/RE/formats/mi.md §Container structure.");

        // Decode each 28-byte record.
        // spec: Docs/RE/formats/mi.md §Record layout -- "28 bytes per record (7 x u32le)": HIGH.
        var records = new MiWidgetRecord[(int)recordCount];
        for (int i = 0; i < (int)recordCount; i++)
        {
            int recBase = HeaderSize + i * RecordStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, RecordStride);

            // Fields +0x00..+0x18 (7 x u32le). Stride SAMPLE-VERIFIED; semantics PLAUSIBLE.
            // spec: Docs/RE/formats/mi.md §Record layout -- offsets HIGH; meanings PLAUSIBLE.
            // 0xFFFFFFFF = none-sentinel (HIGH: observed in fields 1,2,5,6; never in field 0).
            // spec: Docs/RE/formats/mi.md §Record layout -- "None sentinel: 0xFFFFFFFF": HIGH (value); PLAUSIBLE (field set).
            records[i] = new MiWidgetRecord
            {
                // PLAUSIBLE: sequential ordinal -- strictly increasing, no gaps.
                // spec: Docs/RE/formats/mi.md §Record layout field 0 @ +0x00: PLAUSIBLE.
                WidgetId = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffWidgetId..]),

                // PLAUSIBLE: caption / text id (primary of +-1 couple with FieldA1), or 0xFFFFFFFF.
                // spec: Docs/RE/formats/mi.md §Record layout field 1 @ +0x04: PLAUSIBLE.
                FieldA0 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldA0..]),

                // PLAUSIBLE: caption / text id sibling (= FieldA0 - 1), or 0xFFFFFFFF.
                // spec: Docs/RE/formats/mi.md §Record layout field 2 @ +0x08: PLAUSIBLE.
                FieldA1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldA1..]),

                // PLAUSIBLE: small kind / link id (co-varies with field 6; kind-vs-link UNRESOLVED).
                // spec: Docs/RE/formats/mi.md §Record layout field 3 @ +0x0C: PLAUSIBLE / kind-vs-link UNRESOLVED.
                FieldKind = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldKind..]),

                // PLAUSIBLE: decimal-packed icon / sprite id (primary of pair; NOT a pointer).
                // spec: Docs/RE/formats/mi.md §Record layout field 4 @ +0x10: PLAUSIBLE.
                FieldB0 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldB0..]),

                // PLAUSIBLE: decimal-packed icon / sprite id sibling (= FieldB0 + 1), or 0xFFFFFFFF.
                // spec: Docs/RE/formats/mi.md §Record layout field 5 @ +0x14: PLAUSIBLE.
                FieldB1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldB1..]),

                // PLAUSIBLE: small kind / link id (co-varies with field 3), or 0xFFFFFFFF; kind-vs-link UNRESOLVED.
                // spec: Docs/RE/formats/mi.md §Record layout field 6 @ +0x18: PLAUSIBLE / kind-vs-link UNRESOLVED.
                FieldLink = BinaryPrimitives.ReadUInt32LittleEndian(rec[OffFieldLink..]),
            };
        }

        return new MiPanelData
        {
            RecordCount = recordCount,
            Records = records,
        };
    }
}