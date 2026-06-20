using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Application.World;

/// <summary>
///     Orchestrates the streaming terrain ring around the player: from a world position it computes the
///     required resident sectors (3×3 or 5×5 by <see cref="StreamQuality" />), loads the new ones through
///     the <see cref="ITerrainSectorSource" /> port, evicts the ones that drifted out of range, and
///     publishes load/unload events on the <see cref="IClientEventBus" />. spec:
///     Docs/RE/formats/terrain.md §9 (cell streaming policy); §2 (world→cell mapping).
/// </summary>
/// <remarks>
///     <para>
///         All grid math is delegated to the Domain <see cref="SectorGrid" /> (world→sector, required ring,
///         eviction predicate) — this service only sequences the I/O and event publishing. No magic terrain
///         constant is hard-coded here; they live in <see cref="SectorGrid" /> with their spec citations.
///     </para>
///     <para>
///         <b>No double-loading.</b> A set of resident sector keys is maintained; a required sector already
///         resident (or in-flight) is never requested again. spec: terrain.md §9 (background thread reuses
///         evictable slots; the runtime keeps an active set).
///     </para>
///     <para>
///         <b>Threading.</b> Intended to be driven by the single network-reader / loop owner. The resident set
///         is not internally locked; do not call <see cref="UpdateCenterAsync" /> concurrently.
///     </para>
/// </remarks>
public sealed class SectorStreamingService
{
    private readonly IClientEventBus _eventBus;

    // Resident (loaded) sector keys. A sector is added on a successful load and removed on eviction.
    private readonly HashSet<(int MapX, int MapZ)> _resident = new();

    // Scratch buffer for the required-ring computation. Sized for the largest documented ring (5×5).
    private readonly (int MapX, int MapZ)[] _ringScratch =
        new (int MapX, int MapZ)[SectorGrid.RequiredSectorCount(2)];

    private readonly ITerrainSectorSource _source;
    private (int MapX, int MapZ) _center;

    private bool _hasCenter;

    /// <summary>
    ///     Creates a streaming service at <paramref name="quality" /> (default
    ///     <see cref="StreamQuality.Medium" />, a 3×3 ring). spec: terrain.md §9.2.
    /// </summary>
    public SectorStreamingService(
        ITerrainSectorSource source,
        IClientEventBus eventBus,
        StreamQuality quality = StreamQuality.Medium)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Quality = quality;
    }

    /// <summary>The streaming quality (selects the 3×3 vs 5×5 ring). spec: terrain.md §9.2.</summary>
    public StreamQuality Quality { get; set; }

    /// <summary>The number of sectors currently resident.</summary>
    public int ResidentCount => _resident.Count;

    /// <summary>The current centre sector, or <see langword="null" /> before the first update.</summary>
    public (int MapX, int MapZ)? Center => _hasCenter ? _center : null;

    /// <summary>
    ///     Rebinds the underlying sector source to a different area (reloading its <c>.lst</c> manifest)
    ///     and clears the resident ring so the next <see cref="UpdateCenterAsync" /> streams the new area
    ///     from scratch. Every previously-resident sector is published as a
    ///     <see cref="SectorUnloadedEvent" /> so the presentation tears down the old area's terrain nodes.
    /// </summary>
    /// <remarks>
    ///     Area changes happen on entering a new map. The biased <c>(mapX, mapZ)</c> ranges of two areas
    ///     can overlap, so the resident set (which is keyed only by <c>(mapX, mapZ)</c>) MUST be cleared on
    ///     an area switch — otherwise a same-coordinate cell from the old area would be treated as already
    ///     resident and never reloaded. spec: terrain.md §1.1 (per-area paths) + §9 (active set).
    /// </remarks>
    /// <param name="areaId">The target area identifier. spec: terrain.md §1.1.</param>
    public void SetArea(int areaId)
    {
        _source.SetArea(areaId);

        // Drop the old area's residents and notify the presentation to unload them.
        foreach (var sector in _resident) _eventBus.Publish(new SectorUnloadedEvent(sector.MapX, sector.MapZ));

        _resident.Clear();

        // Force the next UpdateCenterAsync to treat its centre as new (no early-out on unchanged centre).
        _hasCenter = false;
    }

    /// <summary>True when <paramref name="sector" /> is currently resident.</summary>
    public bool IsResident((int MapX, int MapZ) sector)
    {
        return _resident.Contains(sector);
    }

    /// <summary>
    ///     Updates the streaming ring for a player at world position <paramref name="position" /> (Q16.16).
    ///     Converts to a sector coordinate via <see cref="SectorGrid.WorldToSector" />, then delegates to
    ///     <see cref="UpdateCenterAsync" />. The world Y component is ignored (terrain is XZ). spec:
    ///     terrain.md §2 (world→cell mapping).
    /// </summary>
    public ValueTask UpdateForPositionAsync(Vector3Fixed position, CancellationToken cancellationToken = default)
    {
        var (worldX, _, worldZ) = position.ToVector3Float();
        var (mapX, mapZ) = SectorGrid.WorldToSector(worldX, worldZ);
        return UpdateCenterAsync(mapX, mapZ, cancellationToken);
    }

    /// <summary>
    ///     Updates the streaming ring around centre sector <c>(centerMapX, centerMapZ)</c>: evicts cells
    ///     that fell out of range (Chebyshev distance &gt; 2), then loads every required ring cell that is
    ///     not already resident. Publishes a <see cref="SectorUnloadedEvent" /> per eviction and a
    ///     <see cref="SectorLoadedEvent" /> per newly-loaded cell. Idempotent when the centre and resident
    ///     set are unchanged (no duplicate loads). spec: terrain.md §9.2 / §9.3.
    /// </summary>
    public async ValueTask UpdateCenterAsync(
        int centerMapX,
        int centerMapZ,
        CancellationToken cancellationToken = default)
    {
        _center = (centerMapX, centerMapZ);
        _hasCenter = true;

        EvictOutOfRange(centerMapX, centerMapZ);

        // Compute the required ring for the current quality. spec: terrain.md §9.2.
        var ringRadius = SectorGrid.RingRadiusFor(Quality);
        var required = SectorGrid.RequiredSectors(centerMapX, centerMapZ, ringRadius, _ringScratch);

        for (var i = 0; i < required; i++)
        {
            var sector = _ringScratch[i];
            if (_resident.Contains(sector))
                continue; // already resident: never double-load. spec: terrain.md §9 (active set).

            // Mark resident BEFORE awaiting so a re-entrant/overlapping required cell is not requested
            // twice within this pass; the set is the single source of truth for "in flight or loaded".
            _resident.Add(sector);

            var payload =
                await _source.LoadSectorAsync(sector.MapX, sector.MapZ, cancellationToken)
                    .ConfigureAwait(false);

            _eventBus.Publish(new SectorLoadedEvent(sector.MapX, sector.MapZ, payload));
        }
    }

    /// <summary>Evicts every resident sector whose Chebyshev distance from the centre exceeds 2. spec: terrain.md §9.3.</summary>
    private void EvictOutOfRange(int centerMapX, int centerMapZ)
    {
        // Collect first to avoid mutating the set while enumerating it.
        List<(int MapX, int MapZ)>? toEvict = null;
        foreach (var sector in _resident)
            if (SectorGrid.ShouldEvict(centerMapX, centerMapZ, sector.MapX, sector.MapZ))
                (toEvict ??= new List<(int MapX, int MapZ)>()).Add(sector);

        if (toEvict is null) return;

        foreach (var sector in toEvict)
        {
            _resident.Remove(sector);
            _eventBus.Publish(new SectorUnloadedEvent(sector.MapX, sector.MapZ));
        }
    }
}