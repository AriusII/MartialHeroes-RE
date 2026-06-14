using MartialHeroes.Client.Domain.Simulation;

namespace MartialHeroes.Client.Application.World;

/// <summary>
/// Port for loading the per-area region grid and zone-type table from the VFS.
/// The concrete adapter (<c>VfsRegionSource</c>) lives in layer 05; this interface keeps
/// the Application layer engine-free and VFS-free (downward DAG only).
/// </summary>
/// <remarks>
/// <para>
/// Implementations load <c>region&lt;area&gt;.bin</c> (the 256-unit region-id grid) and
/// <c>regiontable&lt;area&gt;.bin</c> (the 32-record zone-type table) for a given map area and
/// construct a <see cref="RegionCatalog"/>. When either file is absent or fails to parse, the
/// method returns <see langword="null"/> so <see cref="RegionService"/> degrades gracefully.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies. The concrete adapter in layer 05 may use
/// <see cref="MartialHeroes.Assets.Parsers.RegionGridParser"/> /
/// <see cref="MartialHeroes.Assets.Parsers.RegionZoneTableParser"/> from layer 03.
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.2.
/// </para>
/// </remarks>
public interface IRegionSource
{
    /// <summary>
    /// Loads the region catalog for <paramref name="areaId"/>.
    /// Returns <see langword="null"/> when the VFS is unavailable, the files are absent, or
    /// a parse error occurs — never throws for file-not-found / parse failures.
    /// </summary>
    /// <param name="areaId">
    /// The map area identifier. Path patterns:
    /// <c>data/map{area:D3}/region{area:D3}.bin</c> and
    /// <c>data/map{area:D3}/regiontable{area:D3}.bin</c>.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.2.
    /// </param>
    /// <param name="cancellationToken">Cancellation for clean shutdown.</param>
    ValueTask<RegionCatalog?> LoadRegionCatalogAsync(
        int areaId,
        CancellationToken cancellationToken = default);
}