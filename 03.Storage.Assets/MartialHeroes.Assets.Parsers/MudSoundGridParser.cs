using System.Runtime.InteropServices;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

// spec: Docs/RE/formats/mud.md

/// <summary>
/// Parser for <c>.mud</c> ambient-sound zone grid files.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.mud</c> file is a headerless raw blob — no magic, no version, no length prefix.
/// Fixed size: exactly 32 768 bytes (0x8000) = 64 × 64 × 8 B.
/// spec: Docs/RE/formats/mud.md §Identification — "Magic: none. File size: fixed 32768 bytes": CONFIRMED.
/// </para>
/// <para>
/// Grid: 64 columns (X axis) × 64 rows (Z axis) of 8-byte tiles, row-major.
/// spec: Docs/RE/formats/mud.md §Grid geometry — "Grid dimensions: 64 × 64 tiles. Tile stride: 8 bytes": CONFIRMED.
/// </para>
/// <para>
/// Each tile is reinterpreted directly as a <see cref="MudSoundTile"/> struct via
/// <see cref="MemoryMarshal.Cast{TFrom,TTo}"/> — zero field-by-field copying.
/// Valid because the struct is <c>[StructLayout(LayoutKind.Sequential, Pack = 1)]</c>, all fields
/// are u8, and the file is explicitly little-endian with no endianness dependency on single-byte values.
/// spec: Docs/RE/formats/mud.md §Identification — "Endianness: little-endian (single-byte fields only)": CONFIRMED.
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class MudSoundGridParser
{
    // Fixed file size: 64 × 64 × 8 = 32 768 bytes (0x8000).
    // spec: Docs/RE/formats/mud.md §Identification — "File size: fixed 32768 bytes (0x8000)": CONFIRMED.
    private const int ExpectedFileSize = MudSoundGrid.FixedFileSize; // 32768

    /// <summary>
    /// Parses the raw bytes of a <c>.mud</c> file into a <see cref="MudSoundGrid"/>.
    /// </summary>
    /// <param name="data">Raw file content from the VFS. Must be exactly 32 768 bytes.</param>
    /// <returns>The decoded 64×64 ambient-sound tile grid.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when <paramref name="data"/> is not exactly 32 768 bytes.
    /// spec: Docs/RE/formats/mud.md §Identification — fixed file size validation.
    /// </exception>
    public static MudSoundGrid Parse(ReadOnlyMemory<byte> data) => Parse(data.Span);

    /// <summary>
    /// Parses a span of bytes into a <see cref="MudSoundGrid"/>.
    /// </summary>
    /// <param name="span">Raw bytes from the VFS. Must be exactly 32 768 bytes.</param>
    /// <returns>The decoded 64×64 ambient-sound tile grid.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when <paramref name="span"/> length is not exactly 32 768 bytes.
    /// spec: Docs/RE/formats/mud.md §Identification — "File size: fixed 32768 bytes (0x8000)": CONFIRMED.
    /// </exception>
    public static MudSoundGrid Parse(ReadOnlySpan<byte> span)
    {
        // Validate fixed size: 64 × 64 × 8 = 32768 bytes.
        // spec: Docs/RE/formats/mud.md §Identification — "the loader allocates exactly 0x8000 bytes and reads exactly 0x8000 bytes": CONFIRMED.
        if (span.Length != ExpectedFileSize)
            throw new InvalidDataException(
                $".mud parse error: expected exactly {ExpectedFileSize} bytes (0x{ExpectedFileSize:X4}), " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/mud.md §Identification.");

        // Reinterpret the entire span as a flat array of MudSoundTile structs — zero-copy.
        // MudSoundTile is [StructLayout(LayoutKind.Sequential, Pack = 1)], 8 bytes, all u8.
        // spec: Docs/RE/formats/mud.md §Tile layout (8 bytes): CONFIRMED.
        ReadOnlySpan<MudSoundTile> rawTiles = MemoryMarshal.Cast<byte, MudSoundTile>(span);

        // 64 × 64 = 4096 tiles.
        // spec: Docs/RE/formats/mud.md §Grid geometry — "64 × 64 = 4096 tiles": CONFIRMED.
        int tileCount = MudSoundGrid.TileCount; // 4096

        var tiles = new MudSoundTile[tileCount];
        rawTiles.CopyTo(tiles);

        return new MudSoundGrid { Tiles = tiles };
    }
}