namespace MartialHeroes.Client.Application.World;

/// <summary>
/// Port for loading a terrain cell's bytes by grid coordinate. The concrete adapter lives at the
/// composition root (Godot / Infrastructure) and is backed by the VFS / Assets.Parsers; this layer
/// must not reference Assets.* (DAG), so the dependency is inverted through this interface.
/// spec: Docs/RE/formats/terrain.md (per-cell files keyed by <c>(mapX, mapZ)</c>).
/// </summary>
/// <remarks>
/// <para>
/// A "sector" here is one streaming cell of exactly 1024×1024 world units identified by its biased
/// <c>(mapX, mapZ)</c> coordinate (spec: terrain.md §Overview / §1.3). The adapter resolves the
/// per-cell base path, opens the VFS blob(s), and returns the raw bytes; the streaming policy
/// (which cells, when to evict) is owned by <see cref="SectorStreamingService"/> using the Domain
/// <see cref="MartialHeroes.Client.Domain.Simulation.SectorGrid"/> math.
/// </para>
/// <para>
/// The returned payload is a neutral <see cref="ReadOnlyMemory{T}"/> handle — Application never parses
/// it (that is Assets.Parsers). It is forwarded to the presentation layer via the load event.
/// </para>
/// </remarks>
public interface ITerrainSectorSource
{
    /// <summary>
    /// Loads the bytes for the cell at biased grid coordinate <c>(mapX, mapZ)</c>. Returns an empty
    /// memory when the cell is not present in the area manifest (spec: terrain.md §1.2 — a coordinate
    /// absent from the <c>.lst</c> manifest is never loaded). Implementations should not throw for an
    /// absent cell; they return empty so the service can record "no resident data" without faulting.
    /// </summary>
    /// <param name="mapX">Biased sector X (world origin bias 10000). spec: terrain.md §Overview.</param>
    /// <param name="mapZ">Biased sector Z (world origin bias 10000). spec: terrain.md §Overview.</param>
    /// <param name="cancellationToken">Cancellation for clean shutdown.</param>
    ValueTask<ReadOnlyMemory<byte>> LoadSectorAsync(
        int mapX,
        int mapZ,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebinds this source to a different area, reloading its per-area <c>.lst</c> manifest so
    /// subsequent <see cref="LoadSectorAsync"/> calls resolve cells in <paramref name="areaId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The streaming policy is area-agnostic (it works in biased <c>(mapX, mapZ)</c> space), but the
    /// concrete VFS adapter must know which area's path family and manifest to use. This method is the
    /// seam the composition root uses to point streaming at the area the player actually entered
    /// (spec: terrain.md §1.1 — per-area path tag; §1.2 — per-area manifest).
    /// </para>
    /// <para>
    /// The default implementation is a no-op so area-agnostic / test fakes need not implement it.
    /// Implementations should not throw for an absent manifest; an absent area simply yields empty
    /// loads (spec: terrain.md §1.2 — absent key → never loaded).
    /// </para>
    /// </remarks>
    /// <param name="areaId">The target area identifier. spec: terrain.md §1.1.</param>
    void SetArea(int areaId)
    {
        // Default no-op: area-agnostic sources ignore the rebind.
    }
}