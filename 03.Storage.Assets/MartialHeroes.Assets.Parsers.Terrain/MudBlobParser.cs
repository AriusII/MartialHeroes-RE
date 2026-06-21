using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

/// <summary>
///     Parser for <c>.mud</c> ambient-sound tile blob files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mud.md — canonical source for .mud format.
///     <para>
///         Fixed size: 32 768 bytes (0x8000) = 64 × 64 × 8 B. CONFIRMED.
///         Grid: 64 columns (X axis) × 64 rows (Z axis). Row-major (row = Z, col = X). CONFIRMED.
///         Record stride: 8 bytes. All fields: single bytes. CONFIRMED.
///         tile_index = col + (row &lt;&lt; 6). spec: Docs/RE/formats/mud.md §Indexing: CONFIRMED.
///         spec: Docs/RE/formats/mud.md §Grid geometry: CONFIRMED.
///         spec: Docs/RE/formats/mud.md §Tile record layout: CONFIRMED (all fields).
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public static class MudBlobParser
{
    /// <summary>
    ///     Parses a <c>.mud</c> file into a decoded <see cref="MudBlob" /> tile grid.
    /// </summary>
    /// <param name="data">Raw file content from the VFS (must be exactly 32 768 bytes).</param>
    /// <returns>Decoded MudBlob with a 64×64 typed tile grid.</returns>
    /// <exception cref="InvalidDataException">Thrown when the buffer is not exactly 32 768 bytes.</exception>
    public static MudBlob Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span);
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})" />
    public static MudBlob Parse(ReadOnlySpan<byte> span)
    {
        // Validate fixed size: 64 × 64 × 8 = 32768.
        // spec: Docs/RE/formats/mud.md §Fixed size — "exactly 32 768 bytes (0x8000)": CONFIRMED.
        if (span.Length != MudBlob.FixedSize)
            throw new InvalidDataException(
                $".mud parse error: expected exactly {MudBlob.FixedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/mud.md §Fixed size.");

        var totalTiles = MudBlob.GridRows * MudBlob.GridCols; // 4096
        var tiles = new MudTileRecord[totalTiles];

        for (var t = 0; t < totalTiles; t++)
        {
            var offset = t * MudBlob.RecordStride;
            // spec: Docs/RE/formats/mud.md §Tile record layout (8-byte stride):
            //   unread0 u8 @ +0 (never read — always zero): CONFIRMED.
            //   unread1 u8 @ +1 (never read — always zero): CONFIRMED.
            //   bgmZoneId u8 @ +2: CONFIRMED. bgeAmbientId0 u8 @ +3: CONFIRMED.
            //   bgeAmbientId1 u8 @ +4: CONFIRMED. effId0 u8 @ +5: CONFIRMED.
            //   effId1 u8 @ +6: CONFIRMED. effId2 u8 @ +7: CONFIRMED.
            tiles[t] = new MudTileRecord(
                span[offset + 0], // unread0 (pad0)
                span[offset + 1], // unread1 (pad1)
                span[offset + 2], // bgmZoneId
                span[offset + 3], // bgeAmbientId0
                span[offset + 4], // bgeAmbientId1
                span[offset + 5], // effId0
                span[offset + 6], // effId1
                span[offset + 7] // effId2
            );
        }

        return new MudBlob { Tiles = tiles };
    }
}