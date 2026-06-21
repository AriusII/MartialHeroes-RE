using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Texture.Models;

namespace MartialHeroes.Assets.Parsers.Texture;

/// <summary>
///     Parsers for miscellaneous binary data files:
///     <c>mobinfo.mi</c> (4B header + 28B records),
///     <c>*.tol</c> (16B header + W×H tile bytes),
///     <c>discript.sc</c> (68B records, CP949).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mi.md — mobinfo.mi container + per-record layout (SAMPLE-VERIFIED).
///     spec: Docs/RE/formats/misc_data.md §3–§5 — .tol + discript.sc layouts.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class MiscParser
{
    // =========================================================================
    // mobinfo.mi — Monster info table (4B header + 28B records)
    // =========================================================================

    // Record stride: 28 bytes (7 × u32le). SAMPLE-VERIFIED (4 + 21 × 28 = 592 bytes).
    // spec: Docs/RE/formats/mi.md §Container layout — "fixed-stride 28 bytes per record: SAMPLE-VERIFIED".
    private const int MobInfoRecordStride = 28;

    // =========================================================================
    // *.tol — Terrain Tile Obstacle layer (16B header + W×H bytes)
    // =========================================================================

    // Header size: 16 bytes (4 × u32le). CONFIRMED.
    // spec: Docs/RE/formats/misc_data.md §3 — "16-byte header (4 × u32le): HIGH".
    private const int TolHeaderSize = 16;

    // =========================================================================
    // discript.sc — UI Descriptor Script Table (stride: 68 bytes)
    // =========================================================================

    // Stride: 68 bytes (0x44). CONFIRMED.
    // spec: Docs/RE/formats/misc_data.md §5 — "stride 68 bytes (0x44): HIGH".
    private const int DescriptorStride = 68;

    // display_name field: 30 bytes CP949 @ +8. HIGH.
    // spec: Docs/RE/formats/misc_data.md §5 — "display_name char[30] CP949 @ +8: HIGH".
    private const int DisplayNameOffset = 8;
    private const int DisplayNameLength = 30;

    // keyboard_shortcut field: 3 bytes ASCII @ +38. HIGH.
    // spec: Docs/RE/formats/misc_data.md §5 — "keyboard_shortcut char[3] @ +38: HIGH".
    private const int KeyboardShortcutOffset = 38;
    private const int KeyboardShortcutLength = 3;

    // reserved field: 27 bytes @ +41. LOW.
    // spec: Docs/RE/formats/misc_data.md §5 — "reserved u8[27] @ +41: LOW".
    private const int ReservedOffset = 41;
    private const int ReservedLength = 27;

    /// <summary>
    ///     Parses <c>data/ui/mobinfo.mi</c> — monster info table.
    ///     Header: count u32le @ offset 0. Records: count × 28 bytes.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/mi.md §Container layout — SAMPLE-VERIFIED (4B header + 28B stride).
    ///     File size = 4 + count × 28. SAMPLE-VERIFIED (4 + 21 × 28 = 592 bytes on disk).
    ///     <para>
    ///         IMPORTANT: the shipping client has NO loader for this file (CONFIRMED NOT READ in build 263bd994).
    ///         This parser is provided for archival/interoperability completeness only.
    ///         spec: Docs/RE/formats/mi.md §Loader — "no path literal, not in boot corpus, not compiled in".
    ///     </para>
    /// </remarks>
    public static MobInfoRecord[] ParseMobInfoMi(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 4)
            throw new InvalidDataException(
                $"mobinfo.mi parse error: buffer too short for 4-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/mi.md §Container layout.");

        // count u32le @ +0. SAMPLE-VERIFIED.
        // spec: Docs/RE/formats/mi.md §Container layout — "count u32le @ 0: SAMPLE-VERIFIED".
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var expectedSize = 4 + (long)count * MobInfoRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"mobinfo.mi parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/mi.md §Container layout.");

        var results = new MobInfoRecord[(int)count];
        for (var i = 0; i < (int)count; i++)
        {
            var offset = 4 + i * MobInfoRecordStride;
            var rec = span.Slice(offset, MobInfoRecordStride);

            // All 7 fields are u32le; field widths are SAMPLE-VERIFIED; semantics SINGLE-SAMPLE.
            // spec: Docs/RE/formats/mi.md §Per-record layout — "7 × u32le: SAMPLE-VERIFIED; field roles SINGLE-SAMPLE".
            // NOTE: portrait_res_3 label WITHDRAWN per CYCLE 7 — field +24 renamed to aux_field.
            // spec: Docs/RE/formats/mi.md §Per-record layout — "any portrait_res_3 label WITHDRAWN".
            results[i] = new MobInfoRecord
            {
                EntryId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]), // entry_id @ +0:  dense 101..121 (row key)
                CaptionMsgId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]), // caption_msg_id @ +4
                DescriptionMsgId =
                    BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]), // description_msg_id @ +8; 0xFFFFFFFF = absent
                SmallParam = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]), // small_param @ +12
                PackedCodeA =
                    BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]), // packed_code_a @ +16 (decimal-packed)
                PackedCodeB =
                    BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]), // packed_code_b @ +20 (decimal-packed)
                AuxField = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]) // aux_field @ +24; 0xFFFFFFFF = none
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses a <c>.tol</c> terrain tile walkability bitmap.
    ///     Header: 4 × u32le. Body: width_tiles × height_tiles bytes.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/misc_data.md §3: sample_verified (header layout + tile-grid stride).
    ///     File size = 16 + width_tiles × height_tiles. CONFIRMED (2048×2048=4,194,320 B and 256×256=65,552 B).
    /// </remarks>
    public static TolMapData ParseTol(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < TolHeaderSize)
            throw new InvalidDataException(
                $".tol parse error: buffer too short for 16-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/misc_data.md §3.");

        // Header fields.
        // spec: Docs/RE/formats/misc_data.md §3 — "world_origin_x u32 @ 0: PARTIAL; world_origin_y @ 4: PARTIAL; width_tiles @ 8: HIGH; height_tiles @ 12: HIGH".
        var worldOriginX = BinaryPrimitives.ReadUInt32LittleEndian(span[..]);
        var worldOriginY = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var widthTiles = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        var heightTiles = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);

        var tileBytes = (long)widthTiles * heightTiles;
        var expectedSize = TolHeaderSize + tileBytes;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".tol parse error: expected {expectedSize} bytes ({widthTiles}×{heightTiles} grid), " +
                $"got {span.Length}. spec: Docs/RE/formats/misc_data.md §3.");

        // Tile grid: row-major, 0=walkable, 1=blocked.
        // spec: Docs/RE/formats/misc_data.md §3 — "tile_grid u8[W×H] @ 16, row-major: HIGH".
        var tileGrid = data.Slice(TolHeaderSize, (int)tileBytes);

        return new TolMapData
        {
            WorldOriginX = worldOriginX,
            WorldOriginY = worldOriginY,
            WidthTiles = widthTiles,
            HeightTiles = heightTiles,
            TileGrid = tileGrid
        };
    }

    /// <summary>
    ///     Parses <c>data/script/discript.sc</c> — UI descriptor table.
    ///     Record count = file_size / 68 (must be exact multiple). Encoding: CP949.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/misc_data.md §5: sample_verified true.
    /// </remarks>
    public static DescriptorRecord[] ParseDescriptSc(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length % DescriptorStride != 0)
            throw new InvalidDataException(
                $"discript.sc parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {DescriptorStride}. spec: Docs/RE/formats/misc_data.md §5.");

        var count = span.Length / DescriptorStride;
        var results = new DescriptorRecord[count];

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        for (var i = 0; i < count; i++)
        {
            var offset = i * DescriptorStride;
            var rec = span.Slice(offset, DescriptorStride);

            // descriptor_id u32le @ +0. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "descriptor_id u32 @ 0: HIGH".
            var descriptorId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // category u32le @ +4. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "category u32 @ 4: HIGH".
            var category = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // display_name char[30] CP949 @ +8. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "display_name char[30] CP949 @ +8: HIGH".
            var nameBytes = rec.Slice(DisplayNameOffset, DisplayNameLength);
            var nameEnd = nameBytes.IndexOf((byte)0);
            var displayName = nameEnd < 0
                ? cp949.GetString(nameBytes)
                : nameEnd == 0
                    ? string.Empty
                    : cp949.GetString(nameBytes[..nameEnd]);

            // keyboard_shortcut char[3] ASCII @ +38. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "keyboard_shortcut char[3] ASCII @ +38: HIGH".
            var shortcutBytes = rec.Slice(KeyboardShortcutOffset, KeyboardShortcutLength);
            var scEnd = shortcutBytes.IndexOf((byte)0);
            var keyboardShortcut = scEnd < 0
                ? Encoding.ASCII.GetString(shortcutBytes)
                : scEnd == 0
                    ? string.Empty
                    : Encoding.ASCII.GetString(shortcutBytes[..scEnd]);

            // reserved u8[27] @ +41. LOW.
            // spec: Docs/RE/formats/misc_data.md §5 — "reserved u8[27] @ +41: LOW".
            var reserved = data.Slice(offset + ReservedOffset, ReservedLength);

            results[i] = new DescriptorRecord
            {
                DescriptorId = descriptorId,
                Category = category,
                DisplayName = displayName,
                KeyboardShortcut = keyboardShortcut,
                Reserved = reserved
            };
        }

        return results;
    }
}