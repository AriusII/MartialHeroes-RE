using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Application.World;

/// <summary>
/// Application-layer service that maintains the per-area <see cref="RegionCatalog"/>, resolves
/// the local player's world position to a <see cref="ZoneType"/> each time
/// <see cref="UpdatePosition"/> is called, and publishes a <see cref="ZoneChangedEvent"/> on the
/// <see cref="IHudEventHub"/> only when the zone changes — not on every call.
/// </summary>
/// <remarks>
/// <para>
/// This service is <b>engine-free</b> (no <c>using Godot;</c>, no rendering dependency). It sits
/// at layer 04 (Application) and depends downward on layer 03 data (via the
/// <see cref="IRegionSource"/> port) and the Domain <see cref="RegionCatalog"/>.
/// </para>
/// <para>
/// <b>Lifecycle:</b>
/// <list type="bullet">
///   <item>Call <see cref="LoadAreaAsync"/> when the active map area is set (alongside terrain / environment load).</item>
///   <item>Call <see cref="UpdatePosition"/> each frame (or on significant player movement) with
///         the player's current legacy world XZ position.</item>
///   <item>The service publishes <see cref="ZoneChangedEvent"/> only when the resolved
///         <see cref="ZoneType"/> differs from the last-published value — never on identical
///         consecutive positions.</item>
/// </list>
/// </para>
/// <para>
/// <b>Graceful degradation:</b> if the region files are absent for an area (VFS offline, file
/// missing, parse error), <see cref="LoadAreaAsync"/> clears the catalog to null; every subsequent
/// <see cref="UpdatePosition"/> call resolves to <see cref="ZoneType.Safe"/> (the "no catalog
/// loaded / default" value, per §16.3 "anything else = safe") and fires one
/// <see cref="ZoneChangedEvent"/> to inform the HUD.
/// </para>
/// <para>
/// <b>Threading:</b> intended to be driven from the Godot main thread (or the single logical
/// game-loop owner). Not internally locked; do not call concurrently.
/// </para>
/// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.3.
/// </remarks>
public sealed class RegionService
{
    // ── dependencies ──────────────────────────────────────────────────────────

    private readonly IRegionSource _source;
    private readonly IHudEventHub _hub;

    // ── state ─────────────────────────────────────────────────────────────────

    // The catalog for the active area, or null when region files are absent.
    private RegionCatalog? _catalog;

    // The last zone type published so we can skip no-op updates.
    private ZoneType _lastPublished;

    // True after the first successful publish; false means "never published this area yet".
    // Reset by LoadAreaAsync so the first UpdatePosition after an area change always fires.
    private bool _everPublished;

    // ── construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="RegionService"/> backed by the given VFS region source and publishing
    /// zone changes onto the given HUD event hub.
    /// </summary>
    /// <param name="source">
    /// Port that loads <c>region&lt;area&gt;.bin</c> and <c>regiontable&lt;area&gt;.bin</c> bytes.
    /// The concrete adapter lives in layer 05 (<see cref="MartialHeroes.Client.Godot.Adapters"/>).
    /// </param>
    /// <param name="hub">
    /// The HUD event hub on which <see cref="ZoneChangedEvent"/> is published.
    /// </param>
    public RegionService(IRegionSource source, IHudEventHub hub)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the region grid and zone-type table for <paramref name="areaId"/> from the VFS,
    /// builds a <see cref="RegionCatalog"/>, and resets the last-published zone so the next
    /// <see cref="UpdatePosition"/> always fires a <see cref="ZoneChangedEvent"/>.
    /// </summary>
    /// <remarks>
    /// If either file is absent or fails to parse, the catalog is set to null and the next
    /// <see cref="UpdatePosition"/> will publish <see cref="ZoneType.Safe"/> (the no-catalog default).
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1–§16.2.
    /// </remarks>
    /// <param name="areaId">The map area to load region data for.</param>
    /// <param name="cancellationToken">Cancellation for clean shutdown.</param>
    public async ValueTask LoadAreaAsync(int areaId, CancellationToken cancellationToken = default)
    {
        _catalog = null;
        _everPublished = false; // reset so next UpdatePosition always fires

        try
        {
            RegionCatalog? catalog =
                await _source.LoadRegionCatalogAsync(areaId, cancellationToken).ConfigureAwait(false);

            _catalog = catalog; // null means "files absent / parse error" → Safe zone (no-catalog default)
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Any other error: keep _catalog null → Safe zone (no-catalog default) on next UpdatePosition.
        }
    }

    /// <summary>
    /// Resolves <paramref name="worldX"/> / <paramref name="worldZ"/> to a <see cref="ZoneType"/>
    /// using the loaded catalog and publishes a <see cref="ZoneChangedEvent"/> on
    /// <see cref="IHudEventHub.ZoneChanges"/> only when the zone changes.
    /// </summary>
    /// <remarks>
    /// No event is published when the resolved zone equals the last-published value.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.1 (256-unit grid lookup) and §16.3 (enum).
    /// </remarks>
    /// <param name="worldX">Legacy world-space X coordinate.</param>
    /// <param name="worldZ">Legacy world-space Z coordinate.</param>
    public void UpdatePosition(float worldX, float worldZ)
    {
        ZoneType zone = _catalog?.Resolve(worldX, worldZ) ?? ZoneType.Safe;
        // No catalog loaded → Safe (the §16.3 default: "anything else = safe").
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — ZoneType values.

        // Skip publish if zone is unchanged since last publish AND we have published at least once
        // this area cycle (first UpdatePosition after LoadAreaAsync always fires).
        if (_everPublished && zone == _lastPublished) return;

        _everPublished = true;
        _lastPublished = zone;
        _hub.PublishZoneChanged(new ZoneChangedEvent(zone));
    }

    /// <summary>
    /// The most recently resolved zone type, or <see cref="ZoneType.Safe"/> (the default) before the
    /// first <see cref="UpdatePosition"/> call after a <see cref="LoadAreaAsync"/>.
    /// </summary>
    public ZoneType CurrentZone => _everPublished ? _lastPublished : ZoneType.Safe;
}