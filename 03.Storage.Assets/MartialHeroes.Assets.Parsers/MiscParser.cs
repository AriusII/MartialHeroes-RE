using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for miscellaneous binary data files:
/// <c>mobinfo.mi</c> (4B header + 28B records),
/// <c>*.tol</c> (16B header + W×H tile bytes),
/// <c>discript.sc</c> (68B records, CP949).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §2–§5.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class MiscParser
{
    // =========================================================================
    // mobinfo.mi — Monster info table (4B header + 28B records)
    // =========================================================================

    // Record stride: 28 bytes (7 × u32le). CONFIRMED (parser and stride).
    // spec: Docs/RE/formats/misc_data.md §2 — "stride 28 bytes: HIGH".
    private const int MobInfoRecordStride = 28;

    /// <summary>
    /// Parses <c>data/ui/mobinfo.mi</c> — monster info table.
    /// Header: count u32. Records: count × 28 bytes.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §2: sample_verified (header + stride).
    /// File size = 4 + count × 28. CONFIRMED (4 + 21 × 28 = 592 bytes).
    /// </remarks>
    public static MobInfoRecord[] ParseMobInfoMi(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 4)
            throw new InvalidDataException(
                $"mobinfo.mi parse error: buffer too short for 4-byte header (got {span.Length}). " +
                "spec: Docs/RE/formats/misc_data.md §2.");

        // count u32le @ +0. HIGH.
        // spec: Docs/RE/formats/misc_data.md §2 — "count u32 @ 0: HIGH".
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        long expectedSize = 4 + (long)count * MobInfoRecordStride;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $"mobinfo.mi parse error: expected {expectedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/misc_data.md §2.");

        var results = new MobInfoRecord[(int)count];
        for (int i = 0; i < (int)count; i++)
        {
            int offset = 4 + i * MobInfoRecordStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, MobInfoRecordStride);

            // All 7 fields: u32le × 7.
            // spec: Docs/RE/formats/misc_data.md §2 — "7 × u32le: HIGH (layout); field semantics PARTIAL".
            results[i] = new MobInfoRecord
            {
                MobClassId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]), // mob_class_id @ +0
                NameStrId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]), // name_str_id @ +4
                AltNameStrId = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]), // alt_name_str_id @ +8
                IconIndex = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]), // icon_index @ +12
                PortraitRes1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]), // portrait_res_1 @ +16
                PortraitRes2 = BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]), // portrait_res_2 @ +20
                PortraitRes3 = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]), // portrait_res_3 @ +24
            };
        }

        return results;
    }

    // =========================================================================
    // *.tol — Terrain Tile Obstacle layer (16B header + W×H bytes)
    // =========================================================================

    // Header size: 16 bytes (4 × u32le). CONFIRMED.
    // spec: Docs/RE/formats/misc_data.md §3 — "16-byte header (4 × u32le): HIGH".
    private const int TolHeaderSize = 16;

    /// <summary>
    /// Parses a <c>.tol</c> terrain tile walkability bitmap.
    /// Header: 4 × u32le. Body: width_tiles × height_tiles bytes.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §3: sample_verified (header layout + tile-grid stride).
    /// File size = 16 + width_tiles × height_tiles. CONFIRMED (2048×2048=4,194,320 B and 256×256=65,552 B).
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
        uint worldOriginX = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        uint worldOriginY = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        uint widthTiles = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        uint heightTiles = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);

        long tileBytes = (long)widthTiles * heightTiles;
        long expectedSize = TolHeaderSize + tileBytes;
        if (span.Length < expectedSize)
            throw new InvalidDataException(
                $".tol parse error: expected {expectedSize} bytes ({widthTiles}×{heightTiles} grid), " +
                $"got {span.Length}. spec: Docs/RE/formats/misc_data.md §3.");

        // Tile grid: row-major, 0=walkable, 1=blocked.
        // spec: Docs/RE/formats/misc_data.md §3 — "tile_grid u8[W×H] @ 16, row-major: HIGH".
        ReadOnlyMemory<byte> tileGrid = data.Slice(TolHeaderSize, (int)tileBytes);

        return new TolMapData
        {
            WorldOriginX = worldOriginX,
            WorldOriginY = worldOriginY,
            WidthTiles = widthTiles,
            HeightTiles = heightTiles,
            TileGrid = tileGrid,
        };
    }

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
    /// Parses <c>data/script/discript.sc</c> — UI descriptor table.
    /// Record count = file_size / 68 (must be exact multiple). Encoding: CP949.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §5: sample_verified true.
    /// </remarks>
    public static DescriptorRecord[] ParseDescriptSc(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length % DescriptorStride != 0)
            throw new InvalidDataException(
                $"discript.sc parse error: buffer length {span.Length} is not a multiple of " +
                $"stride {DescriptorStride}. spec: Docs/RE/formats/misc_data.md §5.");

        int count = span.Length / DescriptorStride;
        var results = new DescriptorRecord[count];

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        for (int i = 0; i < count; i++)
        {
            int offset = i * DescriptorStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, DescriptorStride);

            // descriptor_id u32le @ +0. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "descriptor_id u32 @ 0: HIGH".
            uint descriptorId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // category u32le @ +4. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "category u32 @ 4: HIGH".
            uint category = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // display_name char[30] CP949 @ +8. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "display_name char[30] CP949 @ +8: HIGH".
            ReadOnlySpan<byte> nameBytes = rec.Slice(DisplayNameOffset, DisplayNameLength);
            int nameEnd = nameBytes.IndexOf((byte)0);
            string displayName = nameEnd < 0
                ? cp949.GetString(nameBytes)
                : nameEnd == 0
                    ? string.Empty
                    : cp949.GetString(nameBytes[..nameEnd]);

            // keyboard_shortcut char[3] ASCII @ +38. HIGH.
            // spec: Docs/RE/formats/misc_data.md §5 — "keyboard_shortcut char[3] ASCII @ +38: HIGH".
            ReadOnlySpan<byte> shortcutBytes = rec.Slice(KeyboardShortcutOffset, KeyboardShortcutLength);
            int scEnd = shortcutBytes.IndexOf((byte)0);
            string keyboardShortcut = scEnd < 0
                ? Encoding.ASCII.GetString(shortcutBytes)
                : scEnd == 0
                    ? string.Empty
                    : Encoding.ASCII.GetString(shortcutBytes[..scEnd]);

            // reserved u8[27] @ +41. LOW.
            // spec: Docs/RE/formats/misc_data.md §5 — "reserved u8[27] @ +41: LOW".
            ReadOnlyMemory<byte> reserved = data.Slice(offset + ReservedOffset, ReservedLength);

            results[i] = new DescriptorRecord
            {
                DescriptorId = descriptorId,
                Category = category,
                DisplayName = displayName,
                KeyboardShortcut = keyboardShortcut,
                Reserved = reserved,
            };
        }

        return results;
    }
}