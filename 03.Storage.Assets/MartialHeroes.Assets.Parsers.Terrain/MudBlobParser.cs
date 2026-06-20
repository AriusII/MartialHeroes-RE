using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Assets.Parsers.Terrain;

/// <summary>
///     Parser for <c>.mud</c> ambient-sound tile blob files.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §6. Ambient-sound tile blob — .mud
///     <para>
///         Fixed size: 32 768 bytes (0x8000) = 64 × 64 × 8 B. CONFIRMED (three samples).
///         Grid: 64 columns (X axis) × 64 rows (Z axis). Row-major (row = Z, col = X). CONFIRMED.
///         Record stride: 8 bytes. All fields: single bytes. CONFIRMED.
///         spec: Docs/RE/formats/terrain.md §6.1 Grid dimensions: CONFIRMED.
///         spec: Docs/RE/formats/terrain.md §6.2 Record layout: VERIFIED (all 3 samples, 12 288 tile observations).
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
        // spec: Docs/RE/formats/terrain.md §6 — "Total file size: exactly 32 768 bytes (0x8000)": CONFIRMED.
        if (span.Length != MudBlob.FixedSize)
            throw new InvalidDataException(
                $".mud parse error: expected exactly {MudBlob.FixedSize} bytes, got {span.Length}. " +
                "spec: Docs/RE/formats/terrain.md §6.");

        var totalTiles = MudBlob.GridRows * MudBlob.GridCols; // 4096
        var tiles = new MudTileRecord[totalTiles];

        for (var t = 0; t < totalTiles; t++)
        {
            var offset = t * MudBlob.RecordStride;
            // spec: Docs/RE/formats/terrain.md §6.2 Record layout (8 bytes):
            //   pad0 u8 @ +0: VERIFIED. pad1 u8 @ +1: VERIFIED.
            //   music_group u8 @ +2: VERIFIED. ambient_idx_0 u8 @ +3: VERIFIED.
            //   ambient_idx_1 u8 @ +4: VERIFIED. effect_idx_0 u8 @ +5: VERIFIED.
            //   effect_idx_1 u8 @ +6: VERIFIED. effect_idx_2 u8 @ +7: VERIFIED (limited).
            tiles[t] = new MudTileRecord(
                span[offset + 0], // pad0
                span[offset + 1], // pad1
                span[offset + 2], // music_group
                span[offset + 3], // ambient_idx_0
                span[offset + 4], // ambient_idx_1
                span[offset + 5], // effect_idx_0
                span[offset + 6], // effect_idx_1
                span[offset + 7] // effect_idx_2
            );
        }

        return new MudBlob { Tiles = tiles };
    }
}