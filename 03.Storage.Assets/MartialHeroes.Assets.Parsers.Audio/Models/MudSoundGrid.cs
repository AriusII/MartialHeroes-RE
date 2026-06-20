using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Audio.Models;

// spec: Docs/RE/formats/mud.md

/// <summary>
///     One raw tile record in the <c>.mud</c> ambient-sound zone grid.
///     Fixed 8-byte record; all fields are single unsigned bytes.
///     Renamed from MudTile to MudSoundTile to avoid conflict with the MudTile semantic view
///     in TerrainCell.cs (which wraps MudTileRecord for high-level consumers).
/// </summary>
/// <remarks>
///     Field layout (per-tile byte order):
///     <list type="table">
///         <item>
///             <term>+0</term>
///             <description>
///                 WlkSoundIndex (PLAUSIBLE) — walk-footstep zone index into the area's <c>.wlk</c> sound table.
///                 Was previously labelled <c>reserved0</c>. Not read by the analysed ambient-update path;
///                 observed 0 in available samples. The pairing is PLAUSIBLE (exactly two unaccounted tile bytes
///                 match exactly two unaccounted table types: .wlk and .run), but unverified.
///                 spec: Docs/RE/formats/mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE (was MED/unused).
///             </description>
///         </item>
///         <item>
///             <term>+1</term>
///             <description>
///                 RunSoundIndex (PLAUSIBLE) — run-footstep zone index into the area's <c>.run</c> sound table.
///                 Was previously labelled <c>reserved1</c>. Not read by the analysed ambient-update path;
///                 observed 0 in available samples. PLAUSIBLE — see bytes-0/1 hypothesis in mud.md.
///                 spec: Docs/RE/formats/mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE (was MED/unused).
///             </description>
///         </item>
///         <item>
///             <term>+2</term>
///             <description>
///                 bgmZoneId — background-music zone index; 0 = silence. spec: Docs/RE/formats/mud.md §Tile
///                 layout offset 2 — bgmZoneId u8: CONFIRMED.
///             </description>
///         </item>
///         <item>
///             <term>+3</term>
///             <description>
///                 bgeAmbientId0 — background-environment ambient layer 0; 0 = silence. spec:
///                 Docs/RE/formats/mud.md §Tile layout offset 3 — bgeAmbientId0 u8: CONFIRMED.
///             </description>
///         </item>
///         <item>
///             <term>+4</term>
///             <description>
///                 bgeAmbientId1 — background-environment ambient layer 1; 0 = silence. spec:
///                 Docs/RE/formats/mud.md §Tile layout offset 4 — bgeAmbientId1 u8: CONFIRMED.
///             </description>
///         </item>
///         <item>
///             <term>+5</term>
///             <description>
///                 effId0 — 3D positional sound effect slot 0; 0 = silence. spec: Docs/RE/formats/mud.md §Tile
///                 layout offset 5 — effId0 u8: CONFIRMED.
///             </description>
///         </item>
///         <item>
///             <term>+6</term>
///             <description>
///                 effId1 — 3D positional sound effect slot 1; 0 = silence. spec: Docs/RE/formats/mud.md §Tile
///                 layout offset 6 — effId1 u8: CONFIRMED.
///             </description>
///         </item>
///         <item>
///             <term>+7</term>
///             <description>
///                 effId2 — 3D positional sound effect slot 2; 0 = silence. spec: Docs/RE/formats/mud.md §Tile
///                 layout offset 7 — effId2 u8: CONFIRMED.
///             </description>
///         </item>
///     </list>
///     spec: Docs/RE/formats/mud.md §Tile layout (8 bytes).
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct MudSoundTile
{
    // spec: Docs/RE/formats/mud.md §Tile layout — record stride 8 bytes: CONFIRMED.
    // MemoryMarshal.Cast is valid because Pack=1 and all fields are u8 (no endianness concern).

    /// <summary>
    ///     Walk-footstep zone index (PLAUSIBLE); selects a record in the area's <c>.wlk</c> sound table.
    ///     Observed zero in available samples. Not read by the analysed ambient-update path.
    ///     Was previously labelled <c>Reserved0</c>; renamed per spec update 2026-06-14.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE (was MED/unused).
    ///     spec: Docs/RE/formats/mud.md §Bytes 0 and 1 — walk/run footstep hypothesis: PLAUSIBLE — needs verify.
    ///     Do not treat as confirmed footstep index; do not treat as hard "reserved" either.
    /// </remarks>
    public readonly byte WlkSoundIndex; // PLAUSIBLE — spec: mud.md §Tile layout offset 0 — wlkZoneId?

    /// <summary>
    ///     Run-footstep zone index (PLAUSIBLE); selects a record in the area's <c>.run</c> sound table.
    ///     Observed zero in available samples. Not read by the analysed ambient-update path.
    ///     Was previously labelled <c>Reserved1</c>; renamed per spec update 2026-06-14.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE (was MED/unused).
    ///     spec: Docs/RE/formats/mud.md §Bytes 0 and 1 — walk/run footstep hypothesis: PLAUSIBLE — needs verify.
    ///     Do not treat as confirmed footstep index; do not treat as hard "reserved" either.
    /// </remarks>
    public readonly byte RunSoundIndex; // PLAUSIBLE — spec: mud.md §Tile layout offset 1 — runZoneId?

    /// <summary>
    ///     Background-music zone index (→ BGM table). Sentinel value 0 = silence.
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 2 — bgmZoneId u8: CONFIRMED.
    /// </summary>
    public readonly byte BgmZoneId;

    /// <summary>
    ///     Background-environment ambient index, layer 0 (→ BGE table). Sentinel 0 = silence.
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 3 — bgeAmbientId0 u8: CONFIRMED.
    /// </summary>
    public readonly byte BgeAmbientId0;

    /// <summary>
    ///     Background-environment ambient index, layer 1 (→ BGE table). Sentinel 0 = silence.
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 4 — bgeAmbientId1 u8: CONFIRMED.
    /// </summary>
    public readonly byte BgeAmbientId1;

    /// <summary>
    ///     3D positional sound-effect index, slot 0 (→ EFF table). Sentinel 0 = silence.
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 5 — effId0 u8: CONFIRMED.
    /// </summary>
    public readonly byte EffId0;

    /// <summary>
    ///     3D positional sound-effect index, slot 1 (→ EFF table). Sentinel 0 = silence.
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 6 — effId1 u8: CONFIRMED.
    /// </summary>
    public readonly byte EffId1;

    /// <summary>
    ///     3D positional sound-effect index, slot 2 (→ EFF table). Sentinel 0 = silence.
    ///     spec: Docs/RE/formats/mud.md §Tile layout offset 7 — effId2 u8: CONFIRMED.
    /// </summary>
    public readonly byte EffId2;
}

/// <summary>
///     The decoded 64 × 64 ambient-sound zone grid for one terrain cell, loaded from a <c>.mud</c> file.
/// </summary>
/// <remarks>
///     <para>
///         Grid dimensions: 64 columns (X axis) × 64 rows (Z axis) = 4 096 tiles.
///         spec: Docs/RE/formats/mud.md §Grid geometry — "64 × 64 tiles": CONFIRMED.
///     </para>
///     <para>
///         Tile world size: 16 world units per tile (1024 / 64).
///         spec: Docs/RE/formats/mud.md §Grid geometry — "Tile world size: 16 world units": CONFIRMED.
///     </para>
///     <para>
///         World position → tile lookup:
///         <code>
///   col        = ((worldX - cellOriginX) / 16) &amp; 0x3F   // 0..63
///   row        = ((worldZ - cellOriginZ) / 16) &amp; 0x3F   // 0..63
///   tileIndex  = col + (row &lt;&lt; 6)                        // row stride = 64 tiles
///   tileOffset = tileIndex * 8                            // byte offset into blob
/// </code>
///         spec: Docs/RE/formats/mud.md §Indexing (world → tile): CONFIRMED.
///     </para>
///     <para>
///         The cell-origin biasing convention (mapX − 10000) × 1024 is shared with the terrain system —
///         see <c>terrain.md</c> for the full cell coordinate model.
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class MudSoundGrid
{
    // ── Grid dimension constants ──────────────────────────────────────────────

    /// <summary>
    ///     Number of columns (X axis) in the grid.
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — "Grid dimensions: 64 × 64 tiles": CONFIRMED.
    /// </summary>
    public const int Cols = 64; // spec: Docs/RE/formats/mud.md §Grid geometry

    /// <summary>
    ///     Number of rows (Z axis) in the grid.
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — "Grid dimensions: 64 × 64 tiles": CONFIRMED.
    /// </summary>
    public const int Rows = 64; // spec: Docs/RE/formats/mud.md §Grid geometry

    /// <summary>
    ///     World-unit size of each tile side.
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — "Tile world size: 16 world units": CONFIRMED.
    /// </summary>
    public const int TileWorldSize = 16; // spec: Docs/RE/formats/mud.md §Grid geometry

    /// <summary>
    ///     Expected fixed file size in bytes (64 × 64 × 8 = 32 768).
    ///     spec: Docs/RE/formats/mud.md §Identification — "File size: fixed 32768 bytes (0x8000)": CONFIRMED.
    /// </summary>
    public const int FixedFileSize = Cols * Rows * 8; // 32768 — spec: Docs/RE/formats/mud.md §Identification

    /// <summary>
    ///     Total tile count (64 × 64 = 4 096).
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — "64 × 64 tiles" → 4096: CONFIRMED.
    /// </summary>
    public const int TileCount = Cols * Rows; // 4096

    // ── Payload ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Flat tile array, row-major (row = Z axis, col = X axis).
    ///     Length = 4 096 (<see cref="TileCount" />). Index = col + (row × <see cref="Cols" />).
    ///     spec: Docs/RE/formats/mud.md §Grid geometry — row stride = 64 tiles, col + (row &lt;&lt; 6): CONFIRMED.
    /// </summary>
    public required MudSoundTile[] Tiles { get; init; }

    // ── World-position lookup helper ──────────────────────────────────────────

    /// <summary>
    ///     Returns the tile at the given world-space position relative to this cell's south-west origin.
    /// </summary>
    /// <param name="localX">World X minus the cell's X origin.</param>
    /// <param name="localZ">World Z minus the cell's Z origin.</param>
    /// <returns>The <see cref="MudSoundTile" /> that covers (localX, localZ).</returns>
    /// <remarks>
    ///     Formula: col = (localX / 16) &amp; 0x3F; row = (localZ / 16) &amp; 0x3F; index = col + (row &lt;&lt; 6).
    ///     spec: Docs/RE/formats/mud.md §Indexing (world → tile) — col/row/index formula: CONFIRMED.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MudSoundTile GetTile(int localX, int localZ)
    {
        // col + (row << 6) — spec: Docs/RE/formats/mud.md §Indexing (world → tile): CONFIRMED.
        var col = (localX / TileWorldSize) &
                  0x3F; // spec: Docs/RE/formats/mud.md §Indexing — col = (local_x / 16) & 0x3F: CONFIRMED.
        var row = (localZ / TileWorldSize) &
                  0x3F; // spec: Docs/RE/formats/mud.md §Indexing — row = (local_z / 16) & 0x3F: CONFIRMED.
        var index = col +
                    (row << 6); // spec: Docs/RE/formats/mud.md §Indexing — tile_index = col + (row << 6): CONFIRMED.
        return Tiles[index];
    }

    /// <summary>
    ///     Returns the resolved sound-table indices for the tile at the given local position.
    ///     This is a convenience wrapper over <see cref="GetTile" /> that unpacks the 8 byte fields into
    ///     a named structure aligned with the resolution chain in <c>mud.md</c> and <c>sound_tables.md</c>.
    /// </summary>
    /// <param name="localX">World X minus the cell's X origin.</param>
    /// <param name="localZ">World Z minus the cell's Z origin.</param>
    /// <returns>
    ///     A <see cref="TileSoundIndices" /> struct with the five index families (wlk PLAUSIBLE, run
    ///     PLAUSIBLE, bgm CONFIRMED, bge CONFIRMED, eff CONFIRMED).
    /// </returns>
    /// <remarks>
    ///     spec: Docs/RE/formats/mud.md §Resolution chain — mud tile byte → sound table → leaf audio.
    ///     spec: Docs/RE/formats/sound_tables.md §Resolution chain — .mud tile → soundtable record → leaf audio.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TileSoundIndices ResolveSoundIndices(int localX, int localZ)
    {
        var tile = GetTile(localX, localZ);
        return new TileSoundIndices
        {
            WlkIndex = tile.WlkSoundIndex, // PLAUSIBLE — spec: mud.md §Tile layout offset 0
            RunIndex = tile.RunSoundIndex, // PLAUSIBLE — spec: mud.md §Tile layout offset 1
            BgmIndex = tile.BgmZoneId, // CONFIRMED — spec: mud.md §Tile layout offset 2
            BgeIndices = (tile.BgeAmbientId0, tile.BgeAmbientId1), // CONFIRMED — spec: mud.md offset 3/4
            EffIndices = (tile.EffId0, tile.EffId1, tile.EffId2) // CONFIRMED — spec: mud.md offset 5/6/7
        };
    }

    // ── Sound resolver helper ─────────────────────────────────────────────────

    /// <summary>
    ///     Describes the resolved sound indices for a given tile, extracted from <see cref="MudSoundTile" />.
    ///     Provides a structured view of the five sound-table index families plus the PLAUSIBLE footstep
    ///     indices, without caller needing to know the mud byte semantics.
    ///     spec: Docs/RE/formats/mud.md §Resolution chain — tile byte → sound table → leaf audio.
    /// </summary>
    public readonly struct TileSoundIndices
    {
        /// <summary>
        ///     Walk-footstep zone index (PLAUSIBLE). Selects record in <c>.wlk</c> table.
        ///     0 = null/silence.
        ///     spec: Docs/RE/formats/mud.md §Tile layout offset 0 — wlkZoneId?: PLAUSIBLE.
        /// </summary>
        public byte WlkIndex { get; init; }

        /// <summary>
        ///     Run-footstep zone index (PLAUSIBLE). Selects record in <c>.run</c> table.
        ///     0 = null/silence.
        ///     spec: Docs/RE/formats/mud.md §Tile layout offset 1 — runZoneId?: PLAUSIBLE.
        /// </summary>
        public byte RunIndex { get; init; }

        /// <summary>
        ///     Background-music zone index. Selects record in <c>.bgm</c> table.
        ///     0 = null/silence.
        ///     spec: Docs/RE/formats/mud.md §Tile layout offset 2 — bgmZoneId u8: CONFIRMED.
        /// </summary>
        public byte BgmIndex { get; init; }

        /// <summary>
        ///     Background-environment ambient indices (up to 2 simultaneous layers). Selects records in <c>.bge</c> table.
        ///     Zero entry = null/silence.
        ///     spec: Docs/RE/formats/mud.md §Tile layout offset 3/4 — bgeAmbientId0/1 u8: CONFIRMED.
        /// </summary>
        public (byte Slot0, byte Slot1) BgeIndices { get; init; }

        /// <summary>
        ///     3D positional sound-effect indices (up to 3 simultaneous). Selects records in <c>.eff</c> table.
        ///     Zero entry = null/silence.
        ///     spec: Docs/RE/formats/mud.md §Tile layout offset 5/6/7 — effId0/1/2 u8: CONFIRMED.
        /// </summary>
        public (byte Slot0, byte Slot1, byte Slot2) EffIndices { get; init; }
    }
}