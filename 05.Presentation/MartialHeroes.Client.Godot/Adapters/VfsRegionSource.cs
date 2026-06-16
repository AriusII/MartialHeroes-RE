using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
/// Implements <see cref="IRegionSource"/> by loading <c>region&lt;area&gt;.bin</c> and
/// <c>regiontable&lt;area&gt;.bin</c> for a given map area from the VFS archive, parsing them
/// with <see cref="RegionGridParser"/> and <see cref="RegionZoneTableParser"/>, and constructing
/// a <see cref="RegionCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Graceful degradation:</b> when the VFS archive is <see langword="null"/> (offline mode), or
/// either file is absent from the archive, or a parse error occurs, <see cref="LoadRegionCatalogAsync"/>
/// returns <see langword="null"/>. <see cref="RegionService"/> then treats all positions as
/// <see cref="MartialHeroes.Shared.Kernel.Enums.ZoneType.Safe"/> (the §16.3 no-catalog default).
/// </para>
/// <para>
/// <b>VFS path patterns</b> (spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.2):
/// <code>
///   region grid:     data/map{area:D3}/region{area:D3}.bin
///   zone-type table: data/map{area:D3}/regiontable{area:D3}.bin
/// </code>
/// These match the same area-tag convention as the terrain files
/// (<c>data/map{d0d1d2}/dat/d{d0d1d2}x…</c> — spec: Docs/RE/formats/terrain.md §1.1).
/// </para>
/// <para>
/// ZERO Godot types. This class is layer 05 (it uses VFS from layer 03) but carries no engine
/// dependency — safe to unit-test without a Godot host.
/// </para>
/// </remarks>
internal sealed class VfsRegionSource : IRegionSource
{
    // The VFS archive; null = offline / no assets.
    private readonly MappedVfsArchive? _vfs;

    // ── VFS path format strings ───────────────────────────────────────────────
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — "region<area>.bin": CONFIRMED.
    private const string RegionGridPathFmt = "data/map{0:D3}/region{0:D3}.bin";

    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "regiontable<area>.bin": CONFIRMED.
    private const string RegionTablePathFmt = "data/map{0:D3}/regiontable{0:D3}.bin";

    /// <summary>
    /// Creates a <see cref="VfsRegionSource"/> backed by the given archive.
    /// Pass <see langword="null"/> for offline / no-VFS mode; all loads return null.
    /// </summary>
    public VfsRegionSource(MappedVfsArchive? vfs)
    {
        _vfs = vfs;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="null"/> when:
    /// <list type="bullet">
    ///   <item>no VFS is mounted (offline mode),</item>
    ///   <item>either file is absent from the archive,</item>
    ///   <item>either file fails to parse (e.g. truncated / corrupt),</item>
    ///   <item>any unexpected I/O error occurs.</item>
    /// </list>
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.2.
    /// </remarks>
    public ValueTask<RegionCatalog?> LoadRegionCatalogAsync(
        int areaId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_vfs is null)
            return ValueTask.FromResult<RegionCatalog?>(null);

        try
        {
            // ── Load region grid ──────────────────────────────────────────────
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — file: region<area>.bin.
            string gridPath = string.Format(RegionGridPathFmt, areaId);
            if (!_vfs.Contains(gridPath))
                return ValueTask.FromResult<RegionCatalog?>(null);

            ReadOnlyMemory<byte> gridBytes = _vfs.GetFileContent(gridPath);
            if (gridBytes.IsEmpty)
                return ValueTask.FromResult<RegionCatalog?>(null);

            RegionGridData grid = RegionGridParser.Parse(gridBytes);
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 — CONFIRMED layout.

            // ── Load zone-type table ──────────────────────────────────────────
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — file: regiontable<area>.bin.
            string tablePath = string.Format(RegionTablePathFmt, areaId);
            if (!_vfs.Contains(tablePath))
                return ValueTask.FromResult<RegionCatalog?>(null);

            ReadOnlyMemory<byte> tableBytes = _vfs.GetFileContent(tablePath);
            if (tableBytes.IsEmpty)
                return ValueTask.FromResult<RegionCatalog?>(null);

            RegionZoneRecord[] records = RegionZoneTableParser.Parse(tableBytes);
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — 32 records × 48 bytes: CONFIRMED.

            // ── Build raw zone-type array ─────────────────────────────────────
            // RegionCatalog expects an array of exactly 32 raw u32 zone-type values.
            // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.2 — "zone type u32 @ +40": CONFIRMED.
            var rawZoneTypes = new uint[RegionZoneTableParser.RecordCount]; // 32
            for (int i = 0; i < records.Length && i < rawZoneTypes.Length; i++)
                rawZoneTypes[i] = records[i].ZoneTypeRaw;

            // ── Construct the catalog ─────────────────────────────────────────
            // Origins are SIGNED i32 (region<area>.bin stores them as i32le); RegionCatalog now
            // accepts int origins so negative world extents address correctly — no cast needed.
            // spec: Docs/RE/formats/region_grid.md §Layout A — "originX i32 signed / originZ i32 signed": CONFIRMED.
            var catalog = new RegionCatalog(
                width: grid.Width,
                height: grid.Height,
                cells: grid.Cells.Span,
                originX: grid.OriginX,
                originZ: grid.OriginZ,
                rawZoneTypes: rawZoneTypes);

            return ValueTask.FromResult<RegionCatalog?>(catalog);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // File absent, truncated, or corrupt → degrade gracefully.
            return ValueTask.FromResult<RegionCatalog?>(null);
        }
    }
}