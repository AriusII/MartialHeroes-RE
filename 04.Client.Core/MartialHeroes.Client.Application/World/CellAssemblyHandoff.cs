using MartialHeroes.Client.Application.Events;

namespace MartialHeroes.Client.Application.World;

// =============================================================================
// CellAssemblyHandoff — the AreaComposer → streaming handoff seam (layer-04 side).
//
// CYCLE 1 Phase 5 Stage C, Deliverable 3. The SectorStreamingService stays
// byte-oriented: it owns the streaming-radius lifecycle (a resident set keyed by
// (mapX, mapZ)) and publishes SectorLoadedEvent(mapX, mapZ, payload-bytes) per
// newly-resident cell. The layer-03 AreaComposer owns the 34-slot loader pool +
// 25-slot ring (the 5×5 moving view, live centre = ring slot 12) and turns raw
// cell bytes into an assembled cell. spec: Docs/RE/specs/assembly_graph.md §1/§4.
//
// THE HANDOFF (invoked by the layer-05 composition root, which references BOTH
// Assets.Mapping and Application):
//   on each SectorLoadedEvent  →  AreaComposer.ComposeCell(mapX, mapZ, payload)
//                              →  re-publish as CellAssembledEvent(IAssembledCellView).
//
// This adapter is the LAYER-04 side of that handoff: it subscribes to the
// SectorLoadedEvent stream and re-publishes a CellAssembledEvent, delegating the
// actual byte→cell bake to a caller-supplied composer callback. layer-04 never
// references layer-03 — the callback (AreaComposer.ComposeCell) is wired in by
// the layer-05 root. spec: Docs/RE/specs/assembly_graph.md §4 (AreaComposer
// contract: the assembled cell is consumed by layer-05 next-frame).
//
// RING/POOL RECONCILIATION NOTE (flagged, NOT merged this wave) — see the class
// remarks below.
// =============================================================================

/// <summary>
/// Re-bakes each streamed sector into an assembled cell and re-publishes it as a
/// <see cref="CellAssembledEvent"/>. The byte→cell bake itself is delegated to a caller-supplied
/// callback (the layer-05 composition root binds it to the layer-03 <c>AreaComposer.ComposeCell</c>),
/// so this layer-04 seam never references <c>Assets.Mapping</c>. spec:
/// Docs/RE/specs/assembly_graph.md §1/§4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> The layer-05 root drains the <see cref="IClientEventBus.Reader"/> stream once per
/// frame; for each <see cref="SectorLoadedEvent"/> it calls <see cref="OnSectorLoaded"/>, which invokes
/// the composer callback and publishes the resulting <see cref="CellAssembledEvent"/>. A callback that
/// returns <see langword="null"/> (an empty / unresolved cell) publishes nothing — faithfully empty,
/// never synthesised. spec: Docs/RE/specs/assembly_graph.md §1 (the <c>.map</c> parse fans the
/// sub-assets; an absent cell has no payload).
/// </para>
/// <para>
/// <b>RING/POOL RECONCILIATION (flagged).</b> Two distinct resident-set models coexist by design and
/// are NOT merged this wave:
/// <list type="bullet">
/// <item><see cref="SectorStreamingService"/> keeps a flat <c>HashSet&lt;(mapX, mapZ)&gt;</c> resident
/// set sized by the <c>StreamQuality</c> ring (3×3 / 5×5) — its job is "which sectors' BYTES are
/// resident, load/evict the deltas". spec: terrain.md §9 (active set).</item>
/// <item>The layer-03 <c>AreaComposer</c> owns the legacy <b>34-slot loader pool</b> (owner/recycle)
/// + the <b>25-slot manager ring</b> (a borrowed-pointer moving 5×5 view, live centre = ring slot 12)
/// — its job is "which assembled CELLS are live, recycle pool slots". spec:
/// Docs/RE/specs/assembly_graph.md §1/§4 (AreaComposer contract).</item>
/// </list>
/// These overlap conceptually (both are a resident-cell working set) but operate at different layers
/// and granularities (raw bytes vs assembled cells; 3×3/5×5 quality ring vs the legacy fixed 34/25
/// pool/ring). Forcing them into one model would either move the 34/25 ring math out of
/// <c>AreaComposer</c> (a layer-03 concern) or push the quality ring into layer-03 — neither is
/// warranted here. This handoff keeps them decoupled: streaming decides WHEN bytes arrive;
/// <c>AreaComposer</c> decides how its pool/ring absorbs them. A genuine unification (if the resident
/// sets are ever shown to fight in practice) is a deferred reconciliation, not this Stage-C wave's job.
/// </para>
/// <para>
/// <b>Threading.</b> Single-owner: invoked by the same logical owner that drains the bus. The
/// composer callback is expected to be pure/deterministic per the AreaComposer contract.
/// </para>
/// </remarks>
public sealed class CellAssemblyHandoff
{
    /// <summary>
    /// The byte→assembled-cell bake the layer-05 root binds to <c>AreaComposer.ComposeCell</c>. Returns
    /// <see langword="null"/> for an empty / unresolved cell (nothing is published). The
    /// <see cref="IAssembledCellView"/> is a layer-04 abstraction the layer-03 model implements (or the
    /// root adapts). spec: Docs/RE/specs/assembly_graph.md §1/§4.
    /// </summary>
    /// <param name="MapX">Biased sector X (from the <see cref="SectorLoadedEvent"/>).</param>
    /// <param name="MapZ">Biased sector Z (from the <see cref="SectorLoadedEvent"/>).</param>
    /// <param name="Payload">The raw cell bytes the streaming service handed off.</param>
    /// <returns>The assembled-cell view, or <see langword="null"/> for an empty cell.</returns>
    public delegate IAssembledCellView? CellBake(int MapX, int MapZ, ReadOnlyMemory<byte> Payload);

    private readonly IClientEventBus _eventBus;
    private readonly CellBake _bake;

    /// <summary>
    /// Creates the handoff over the outbound bus and the caller-supplied byte→cell bake (bound by the
    /// layer-05 root to <c>AreaComposer.ComposeCell</c>).
    /// </summary>
    public CellAssemblyHandoff(IClientEventBus eventBus, CellBake bake)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(bake);
        _eventBus = eventBus;
        _bake = bake;
    }

    /// <summary>
    /// Handles one <see cref="SectorLoadedEvent"/>: invokes the composer bake on the sector's raw bytes
    /// and, when it yields a resolved cell, publishes a <see cref="CellAssembledEvent"/>. An empty /
    /// unresolved cell (a <see langword="null"/> bake result) publishes nothing. spec:
    /// Docs/RE/specs/assembly_graph.md §1/§4.
    /// </summary>
    /// <param name="loaded">The streamed-sector event the <see cref="SectorStreamingService"/> published.</param>
    /// <returns><see langword="true"/> if a <see cref="CellAssembledEvent"/> was published.</returns>
    public bool OnSectorLoaded(SectorLoadedEvent loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        IAssembledCellView? cell = _bake(loaded.MapX, loaded.MapZ, loaded.Payload);
        if (cell is null)
        {
            return false; // empty / unresolved cell → faithfully nothing published. spec: assembly_graph.md §1
        }

        return _eventBus.Publish(new CellAssembledEvent(cell));
    }
}
